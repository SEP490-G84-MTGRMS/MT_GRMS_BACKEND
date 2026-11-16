using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.SaaS.Dtos;
using MT_GRMS.Application.SaaS.Services;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Infrastructure.Persistence;

namespace MT_GRMS.Infrastructure.Services;

public class WalletService : IWalletService
{
    private readonly AppDbContext _context;
    private readonly SePaySettings _sepaySettings;

    public WalletService(AppDbContext context, IOptions<SePaySettings> sepayOptions)
    {
        _context = context;
        _sepaySettings = sepayOptions.Value;
    }

    public async Task<TopupOrderResponse> CreateTopupOrderAsync(
        string tenantId,
        CreateTopupOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Amount < 50000)
        {
            throw new ArgumentException("Số tiền nạp tối thiểu là 50.000 đ");
        }

        var tenant = await ResolveTenantAsync(tenantId, cancellationToken);
        if (tenant == null)
        {
            throw new KeyNotFoundException("Không tìm thấy thông tin gian hàng.");
        }

        var orderCode = Guid.NewGuid().ToString("N")[..16].ToUpperInvariant();
        var transferContent = $"WAL {orderCode}";

        var platformSetting = await _context.PlatformSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        var bankCode = !string.IsNullOrWhiteSpace(platformSetting?.MasterBankCode)
            ? platformSetting.MasterBankCode
            : (!string.IsNullOrWhiteSpace(_sepaySettings.BankCode) ? _sepaySettings.BankCode : "MBBank");
        var bankName = !string.IsNullOrWhiteSpace(platformSetting?.MasterBankName)
            ? platformSetting.MasterBankName
            : (!string.IsNullOrWhiteSpace(_sepaySettings.BankName) ? _sepaySettings.BankName : "MB Bank (Ngân hàng Quân Đội)");
        var accountNumber = !string.IsNullOrWhiteSpace(platformSetting?.MasterAccountNumber)
            ? platformSetting.MasterAccountNumber
            : (!string.IsNullOrWhiteSpace(_sepaySettings.AccountNumber) ? _sepaySettings.AccountNumber : "190065228888");
        var accountName = !string.IsNullOrWhiteSpace(platformSetting?.MasterAccountName)
            ? platformSetting.MasterAccountName
            : (!string.IsNullOrWhiteSpace(_sepaySettings.AccountName) ? _sepaySettings.AccountName : "CONG TY TNHH PHAN MEM MT-GRMS");

        var qrUrl = $"https://qr.sepay.vn/img?acc={accountNumber}&bank={bankCode}&amount={(long)request.Amount}&des={Uri.EscapeDataString(transferContent)}";
        var expiresAt = DateTime.UtcNow.AddMinutes(15);

        var order = new WalletTransaction
        {
            TenantId = tenant.Id,
            TransactionCode = $"WAL-ORDER-{orderCode}",
            OrderCode = orderCode,
            Type = "TOPUP",
            Amount = request.Amount,
            RequestedAmount = request.Amount,
            TransferContent = transferContent,
            TopupDetails = JsonSerializer.Serialize(new { bankCode, bankName, accountNumber, accountName, qrUrl }),
            Status = "PENDING",
            ExpiresAt = expiresAt
        };

        _context.WalletTransactions.Add(order);
        await _context.SaveChangesAsync(cancellationToken);

        return new TopupOrderResponse(
            OrderCode: orderCode,
            Amount: request.Amount,
            BankCode: bankCode,
            BankName: bankName,
            AccountNumber: accountNumber,
            AccountName: accountName,
            TransferContent: transferContent,
            QrUrl: qrUrl,
            ExpiresAt: expiresAt
        );
    }

    public async Task<TopupOrderStatusResponse?> GetTopupOrderStatusAsync(
        string tenantId,
        string orderCode,
        CancellationToken cancellationToken = default)
    {
        var tenant = await ResolveTenantAsync(tenantId, cancellationToken);
        if (tenant == null)
        {
            return null;
        }

        var order = await _context.WalletTransactions.AsNoTracking()
            .SingleOrDefaultAsync(t => t.TenantId == tenant.Id && t.Type == "TOPUP"
                && t.OrderCode != null && t.OrderCode == orderCode, cancellationToken);
        if (order == null)
        {
            return null;
        }

        var status = order.Status switch
        {
            "SUCCESS" => "COMPLETED",
            "PENDING" when order.ExpiresAt <= DateTime.UtcNow => "EXPIRED",
            _ => order.Status
        };
        return new TopupOrderStatusResponse(
            OrderCode: order.OrderCode!,
            Status: status,
            Amount: status == "COMPLETED" ? order.Amount : order.RequestedAmount ?? order.Amount,
            NewBalance: tenant.WalletBalance,
            CompletedAt: order.CompletedAt
        );
    }

    public async Task<WalletBalanceResponse> GetWalletBalanceAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        var tenant = await ResolveTenantAsync(tenantId, cancellationToken);
        if (tenant == null)
        {
            return new WalletBalanceResponse(0, 0, 0);
        }

        var transactions = await _context.WalletTransactions
            .Where(t => t.TenantId == tenant.Id && t.Status == "SUCCESS")
            .ToListAsync(cancellationToken);

        var totalTopup = transactions
            .Where(t => t.Type == "TOPUP")
            .Sum(t => t.Amount);

        var totalSpent = transactions
            .Where(t => t.Type == "PURCHASE_PLAN" || t.Type == "PURCHASE_ADDON")
            .Sum(t => t.Amount);

        return new WalletBalanceResponse(
            WalletBalance: tenant.WalletBalance,
            TotalTopup: totalTopup,
            TotalSpent: totalSpent
        );
    }

    public async Task<IReadOnlyList<WalletTransactionDto>> GetWalletTransactionsAsync(
        string tenantId,
        string? type = null,
        CancellationToken cancellationToken = default)
    {
        var tenant = await ResolveTenantAsync(tenantId, cancellationToken);
        if (tenant == null)
        {
            return Array.Empty<WalletTransactionDto>();
        }

        var query = _context.WalletTransactions
            .Where(t => t.TenantId == tenant.Id && t.Status == "SUCCESS");

        if (!string.IsNullOrWhiteSpace(type) && type != "ALL")
        {
            query = query.Where(t => t.Type == type);
        }

        return await query
            .OrderByDescending(t => t.CompletedAt ?? t.CreatedAt)
            .Select(t => new WalletTransactionDto(
                t.Id,
                t.TransactionCode,
                t.Type,
                t.Amount,
                t.BalanceBefore,
                t.BalanceAfter,
                t.Description,
                t.PaymentMethod,
                t.Status,
                t.ReferenceCode,
                t.CompletedAt ?? t.CreatedAt
            ))
            .ToListAsync(cancellationToken);
    }

    public async Task<SepayWebhookResult> ProcessSepayWebhookAsync(
        SepayWebhookPayload payload,
        CancellationToken cancellationToken = default)
    {
        if (payload.TransferType != null && !payload.TransferType.Equals("in", StringComparison.OrdinalIgnoreCase))
        {
            return new SepayWebhookResult(true, "Bỏ qua giao dịch tiền ra", null, null, 0);
        }

        if (payload.TransferAmount <= 0 || (payload.Id <= 0 && string.IsNullOrWhiteSpace(payload.ReferenceCode)))
        {
            return new SepayWebhookResult(false, "Thiếu thông tin giao dịch chuyển khoản hợp lệ.", null, null, 0);
        }

        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    if (!_context.Database.IsRelational())
                    {
                        return await RecordSepayTransferAsync(payload, cancellationToken);
                    }

                    // Serialize webhook decisions across API instances. Other wallet writers
                    // are protected by the concurrency token on Tenant.WalletBalance.
                    _context.ChangeTracker.Clear();
                    await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
                    await _context.Database.ExecuteSqlRawAsync(
                        "SELECT pg_advisory_xact_lock(164711, 1)", cancellationToken);
                    var result = await RecordSepayTransferAsync(payload, cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    return result;
                }
                catch (DbUpdateConcurrencyException) when (attempt < 2 && _context.Database.IsRelational())
                {
                    // Retry the entire operation with the newly committed balance.
                }
            }
        });
    }

    private async Task<SepayWebhookResult> RecordSepayTransferAsync(
        SepayWebhookPayload payload, CancellationToken cancellationToken)
    {
        long? sepayId = payload.Id > 0 ? payload.Id : null;
        var reference = string.IsNullOrWhiteSpace(payload.ReferenceCode)
            ? $"SEPAY-{payload.Id}"
            : payload.ReferenceCode.Trim();
        var existing = await _context.WalletTransactions.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(t => t.Type == "TOPUP" && (t.Status == "SUCCESS" || t.Status == "REVIEW")
                && (t.ReferenceCode == reference || (sepayId != null && t.SepayTransactionId == sepayId)),
                cancellationToken);
        if (existing != null)
        {
            return new SepayWebhookResult(true, "Giao dịch đã được xử lý trước đó (Idempotent)",
                existing.OrderCode ?? existing.ReconciliationOrderCode, existing.TenantId, existing.Amount);
        }

        var match = Regex.Match(payload.Content ?? string.Empty,
            @"\bWAL\s*([0-9A-Za-z_-]+)", RegexOptions.IgnoreCase);
        var code = match.Groups[1].Value.ToUpperInvariant();
        var transferContent = $"WAL {code}";
        var order = await _context.WalletTransactions
            .SingleOrDefaultAsync(t => t.Type == "TOPUP" && t.OrderCode != null
                && (t.OrderCode == code || t.TransferContent == transferContent), cancellationToken);
        var tenant = order == null
            ? await _context.Tenants.SingleOrDefaultAsync(t => t.Code.ToUpper() == code && code != "", cancellationToken)
            : await _context.Tenants.FindAsync(new object[] { order.TenantId }, cancellationToken);
        if (tenant == null)
        {
            return new SepayWebhookResult(false, "Không xác định được gian hàng từ nội dung chuyển khoản.",
                code, null, payload.TransferAmount);
        }

        var needsReview = order != null && order.Status != "PENDING" && order.Status != "EXPIRED";
        var walletTx = needsReview ? null : order;
        if (walletTx == null)
        {
            walletTx = new WalletTransaction
            {
                TenantId = tenant.Id,
                TransactionCode = $"WAL-{Guid.NewGuid():N}",
                Type = "TOPUP",
                ReconciliationOrderCode = needsReview ? order!.OrderCode : null
            };
            _context.WalletTransactions.Add(walletTx);
        }

        walletTx.Amount = payload.TransferAmount;
        walletTx.BalanceBefore = tenant.WalletBalance;
        if (!needsReview)
        {
            tenant.WalletBalance += payload.TransferAmount;
            walletTx.CompletedAt = DateTime.UtcNow;
        }

        walletTx.BalanceAfter = tenant.WalletBalance;
        walletTx.Status = needsReview ? "REVIEW" : "SUCCESS";
        walletTx.Description = needsReview
            ? "Khoản nạp dùng mã đơn đã hoàn tất hoặc đã hủy, chờ đối soát"
            : "Nạp tiền ví gian hàng qua VietQR SePay";
        walletTx.PaymentMethod = "VietQR";
        walletTx.ReferenceCode = reference;
        walletTx.SepayTransactionId = sepayId;
        walletTx.Metadata = JsonSerializer.Serialize(new
        {
            payload.Gateway,
            payload.AccountNumber,
            payload.TransactionDate,
            payload.Content,
            payload.TransferAmount
        });

        await _context.SaveChangesAsync(cancellationToken);
        return new SepayWebhookResult(true, needsReview ? "Đã lưu khoản chuyển chờ đối soát." : "Đã cộng tiền vào ví.",
            order?.OrderCode, tenant.Id, payload.TransferAmount);
    }

    public async Task DeductWalletAsync(
        string tenantId,
        decimal amount,
        string description,
        string referenceCode,
        CancellationToken cancellationToken = default)
    {
        var tenant = await ResolveTenantAsync(tenantId, cancellationToken);
        if (tenant == null)
        {
            throw new KeyNotFoundException("Không tìm thấy thông tin gian hàng.");
        }

        if (tenant.WalletBalance < amount)
        {
            throw new InvalidOperationException("INSUFFICIENT_WALLET_BALANCE");
        }

        var balanceBefore = tenant.WalletBalance;
        tenant.WalletBalance -= amount;
        var balanceAfter = tenant.WalletBalance;

        var txCode = $"SUB-{DateTime.UtcNow:yyyyMMdd}-{Random.Shared.Next(1000, 9999)}";
        var walletTx = new WalletTransaction
        {
            TenantId = tenant.Id,
            TransactionCode = txCode,
            Type = "PURCHASE_PLAN",
            Amount = amount,
            BalanceBefore = balanceBefore,
            BalanceAfter = balanceAfter,
            Description = description,
            PaymentMethod = "WALLET",
            Status = "SUCCESS",
            ReferenceCode = referenceCode
        };

        _context.WalletTransactions.Add(walletTx);
        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task<Tenant?> ResolveTenantAsync(string tenantId, CancellationToken cancellationToken)
    {
        return await _context.Tenants.FirstOrDefaultAsync(
            t => tenantId != "" && tenantId != "DEFAULT"
                && (t.Id == tenantId || t.Subdomain == tenantId || t.Code == tenantId),
            cancellationToken);
    }
}

