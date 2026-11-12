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

        // Tạo mã đơn 6 chữ số ngẫu nhiên không trùng lặp
        var orderCode = $"{Random.Shared.Next(100000, 999999)}";
        var transferContent = $"WAL {orderCode}";

        var bankCode = !string.IsNullOrWhiteSpace(_sepaySettings.BankCode) ? _sepaySettings.BankCode : "MBBank";
        var bankName = !string.IsNullOrWhiteSpace(_sepaySettings.BankName) ? _sepaySettings.BankName : "MB Bank (Ngân hàng Quân Đội)";
        var accountNumber = !string.IsNullOrWhiteSpace(_sepaySettings.AccountNumber) ? _sepaySettings.AccountNumber : "190065228888";
        var accountName = !string.IsNullOrWhiteSpace(_sepaySettings.AccountName) ? _sepaySettings.AccountName : "CONG TY TNHH PHAN MEM MT-GRMS";

        var qrUrl = $"https://qr.sepay.vn/img?acc={accountNumber}&bank={bankCode}&amount={(long)request.Amount}&des={Uri.EscapeDataString(transferContent)}";
        var expiresAt = DateTime.UtcNow.AddMinutes(15);

        var order = new WalletTopupOrder
        {
            TenantId = tenant.Id,
            OrderCode = orderCode,
            Amount = request.Amount,
            TransferContent = transferContent,
            BankCode = bankCode,
            BankName = bankName,
            AccountNumber = accountNumber,
            AccountName = accountName,
            QrUrl = qrUrl,
            Status = "PENDING",
            ExpiresAt = expiresAt
        };

        _context.WalletTopupOrders.Add(order);
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
        var query = _context.WalletTopupOrders.AsQueryable();

        if (tenant != null)
        {
            query = query.Where(o => o.TenantId == tenant.Id && (o.OrderCode == orderCode || o.TransferContent.Contains(orderCode)));
        }
        else
        {
            query = query.Where(o => o.OrderCode == orderCode || o.TransferContent.Contains(orderCode));
        }

        var order = await query.FirstOrDefaultAsync(cancellationToken);
        if (order == null)
        {
            return null;
        }

        // Tự động đánh dấu hết hạn nếu quá hạn 15 phút
        if (order.Status == "PENDING" && order.ExpiresAt < DateTime.UtcNow)
        {
            order.Status = "EXPIRED";
            await _context.SaveChangesAsync(cancellationToken);
        }

        var currentBalance = tenant?.WalletBalance;
        return new TopupOrderStatusResponse(
            OrderCode: order.OrderCode,
            Status: order.Status,
            Amount: order.Amount,
            NewBalance: currentBalance,
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
            .Where(t => t.TenantId == tenant.Id);

        if (!string.IsNullOrWhiteSpace(type) && type != "ALL")
        {
            query = query.Where(t => t.Type == type);
        }

        return await query
            .OrderByDescending(t => t.CreatedAt)
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
                t.CreatedAt
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

        // 1. Kiểm tra Idempotency để chống cộng tiền 2 lần
        var refCode = !string.IsNullOrWhiteSpace(payload.ReferenceCode)
            ? payload.ReferenceCode
            : $"SEPAY-{payload.Id}";

        var existingTx = await _context.WalletTransactions
            .FirstOrDefaultAsync(t =>
                t.ReferenceCode == refCode ||
                t.TransactionCode == $"WAL-{payload.Id}" ||
                t.TransactionCode.EndsWith($"-{payload.Id}"),
                cancellationToken);

        if (existingTx != null)
        {
            return new SepayWebhookResult(true, "Giao dịch đã được xử lý trước đó (Idempotent)", null, existingTx.TenantId, existingTx.Amount);
        }

        // 2. Trích xuất mã đơn từ nội dung chuyển khoản
        var content = payload.Content ?? string.Empty;
        var match = Regex.Match(content, @"(?:WAL|wal)\s*([0-9A-Za-z_-]+)", RegexOptions.IgnoreCase);
        var candidateCode = match.Success ? match.Groups[1].Value.Trim() : string.Empty;

        WalletTopupOrder? order = null;
        Tenant? tenant = null;

        if (!string.IsNullOrWhiteSpace(candidateCode))
        {
            order = await _context.WalletTopupOrders
                .FirstOrDefaultAsync(o => o.OrderCode == candidateCode || o.TransferContent.Contains(candidateCode), cancellationToken);

            if (order != null)
            {
                tenant = await _context.Tenants.FirstOrDefaultAsync(t => t.Id == order.TenantId, cancellationToken);
            }
            else
            {
                // Fallback: Tìm theo tenant code trực tiếp
                tenant = await _context.Tenants.FirstOrDefaultAsync(t =>
                    t.Code == candidateCode || t.Subdomain == candidateCode || t.Id == candidateCode,
                    cancellationToken);
            }
        }

        // Nếu vẫn chưa tìm thấy tenant, fallback tìm tenant đầu tiên nếu có một tenant duy nhất trong hệ thống
        if (tenant == null)
        {
            tenant = await _context.Tenants.FirstOrDefaultAsync(cancellationToken);
        }

        if (tenant == null)
        {
            return new SepayWebhookResult(false, $"Không xác định được gian hàng từ nội dung: {content}", candidateCode, null, payload.TransferAmount);
        }

        // 3. Thực thi cập nhật số dư trong DB Transaction an toàn qua ExecutionStrategy của Npgsql
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                var balanceBefore = tenant.WalletBalance;
                tenant.WalletBalance += payload.TransferAmount;
                var balanceAfter = tenant.WalletBalance;

                var txCode = $"WAL-{DateTime.UtcNow:yyyyMMdd}-{payload.Id}";
                var walletTx = new WalletTransaction
                {
                    TenantId = tenant.Id,
                    TransactionCode = txCode,
                    Type = "TOPUP",
                    Amount = payload.TransferAmount,
                    BalanceBefore = balanceBefore,
                    BalanceAfter = balanceAfter,
                    Description = $"Nạp tiền ví gian hàng qua VietQR SePay ({payload.Gateway ?? "MBBank"})",
                    PaymentMethod = "VietQR",
                    Status = "SUCCESS",
                    ReferenceCode = refCode,
                    Metadata = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        payload.Gateway,
                        payload.AccountNumber,
                        payload.TransactionDate,
                        payload.Content,
                        payload.TransferAmount
                    })
                };

                _context.WalletTransactions.Add(walletTx);

                if (order != null)
                {
                    order.Status = "COMPLETED";
                    order.CompletedAt = DateTime.UtcNow;
                    order.ReferenceCode = refCode;
                }

                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                return new SepayWebhookResult(true, "Cộng tiền ví gian hàng thành công.", order?.OrderCode, tenant.Id, payload.TransferAmount);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new SepayWebhookResult(false, $"Lỗi xử lý giao dịch: {ex.Message}", candidateCode, tenant.Id, payload.TransferAmount);
            }
        });
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
        if (string.IsNullOrWhiteSpace(tenantId) || tenantId == "DEFAULT")
        {
            return await _context.Tenants.FirstOrDefaultAsync(cancellationToken);
        }

        return await _context.Tenants.FirstOrDefaultAsync(
            t => t.Id == tenantId || t.Subdomain == tenantId || t.Code == tenantId,
            cancellationToken);
    }
}

