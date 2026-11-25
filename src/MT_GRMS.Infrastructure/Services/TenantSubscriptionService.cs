using System.Data;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Admin.Dtos;
using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Constants;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Infrastructure.Persistence;
using Npgsql;

namespace MT_GRMS.Infrastructure.Services;

public sealed class TenantSubscriptionService(AppDbContext context)
{
    private const decimal StaffAddonMonthlyPrice = 30000m;
    private readonly AppDbContext _context = context;

    public readonly record struct Actor(string? TenantId, string? UserId);

    private async Task<Tenant?> FindTenantAsync(Actor actor, CancellationToken cancellationToken, bool requireOwner = false)
    {
        var (tenantId, userId) = actor;
        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(userId) || tenantId == "DEFAULT")
            return null;

        var hasMembership = await _context.TenantUsers.AnyAsync(x =>
            x.TenantId == tenantId && x.UserId == userId && x.IsActive &&
            (!requireOwner || x.Role == SystemRoles.StoreOwner || x.Role == "StoreOwner"), cancellationToken);

        return hasMembership
            ? await _context.Tenants.Include(t => t.SubscriptionPlan)
                .FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken)
            : null;
    }

    public readonly record struct OperationResult(int StatusCode, object? Body);

    private static OperationResult Ok<T>(ApiResponse<T> body) => new(200, body);
    private static OperationResult BadRequest<T>(ApiResponse<T> body) => new(400, body);
    private static OperationResult Forbid() => new(403, null);
    private static OperationResult NotFound<T>(ApiResponse<T> body) => new(404, body);
    private static OperationResult Conflict<T>(ApiResponse<T> body) => new(409, body);

    public async Task<OperationResult> GetStaffAddonQuoteAsync(Actor actor, CancellationToken cancellationToken)
    {
        var tenant = await FindTenantAsync(actor, cancellationToken, requireOwner: true);
        if (tenant == null) return Forbid();

        var now = DateTime.UtcNow;
        return tenant.CanPurchaseStaffManagementAddon(now)
            ? Ok(ApiResponse<StaffAddonQuote>.Ok(CreateStaffAddonQuote(tenant.SubscriptionExpiresAt!.Value, now)))
            : StaffAddonUnavailable();
    }

    public async Task<OperationResult> PurchaseStaffAddonAsync(Actor actor, StaffAddonPurchaseRequest request, CancellationToken cancellationToken)
    {
        if (request.RequestId == Guid.Empty || request.Total <= 0 || request.ExpiresAt == default)
            return BadRequest(ApiResponse<object>.Fail("INVALID_STAFF_ADDON_PURCHASE", "Yêu cầu mua tiện ích không hợp lệ."));

        try
        {
            await using var transaction = _context.Database.IsRelational()
                ? await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
                : null;
            var tenant = await FindTenantAsync(actor, cancellationToken, requireOwner: true);
            if (tenant == null) return Forbid();

            var code = $"STAFF-{request.RequestId:N}";
            var alreadyPurchased = await _context.WalletTransactions.AsNoTracking().AnyAsync(
                x => x.TenantId == tenant.Id && x.TransactionCode == code && x.Status == "SUCCESS", cancellationToken);
            if (alreadyPurchased)
                return Ok(ApiResponse<StaffAddonPurchaseResponse>.Ok(new(tenant.WalletBalance)));

            var now = DateTime.UtcNow;
            if (!tenant.CanPurchaseStaffManagementAddon(now)) return StaffAddonUnavailable();

            var quote = CreateStaffAddonQuote(tenant.SubscriptionExpiresAt!.Value, now);
            if (quote.Total != request.Total || quote.ExpiresAt != request.ExpiresAt)
                return Conflict(ApiResponse<object>.Fail("STAFF_ADDON_QUOTE_CHANGED", "Báo giá đã thay đổi. Vui lòng xem lại báo giá.", 409));
            if (tenant.WalletBalance < quote.Total)
                return BadRequest(ApiResponse<object>.Fail("INSUFFICIENT_WALLET_BALANCE", "Số dư Ví gian hàng không đủ. Vui lòng nạp thêm tiền."));

            var balanceBefore = tenant.WalletBalance;
            tenant.WalletBalance -= quote.Total;
            tenant.HasStaffManagementAddon = true;
            _context.WalletTransactions.Add(new WalletTransaction
            {
                TenantId = tenant.Id,
                TransactionCode = code,
                Type = "PURCHASE_ADDON",
                Amount = quote.Total,
                BalanceBefore = balanceBefore,
                BalanceAfter = tenant.WalletBalance,
                PaymentMethod = "WALLET",
                Status = "SUCCESS",
                Description = $"Mua thêm Quản lý nhân viên đến {quote.ExpiresAt:dd/MM/yyyy}",
                CreatedAt = now,
                Metadata = JsonSerializer.Serialize(new PaidPeriod("STAFF", now, quote.ExpiresAt, quote.Total))
            });

            await _context.SaveChangesAsync(cancellationToken);
            if (transaction != null) await transaction.CommitAsync(cancellationToken);
            return Ok(ApiResponse<StaffAddonPurchaseResponse>.Ok(new(tenant.WalletBalance)));
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.SerializationFailure)
        {
            return StaffAddonPurchaseConflict();
        }
        catch (DbUpdateConcurrencyException)
        {
            return StaffAddonPurchaseConflict();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.UniqueViolation })
        {
            return StaffAddonPurchaseConflict();
        }
    }

    private static StaffAddonQuote CreateStaffAddonQuote(DateTime expiry, DateTime now)
    {
        var days = (decimal)Math.Ceiling((expiry - now).TotalDays);
        var subtotal = StaffAddonMonthlyPrice * days / 30m;
        var vat = Math.Round(subtotal * 0.1m, 0, MidpointRounding.AwayFromZero);
        return new StaffAddonQuote(subtotal, vat, subtotal + vat, expiry);
    }

    private static OperationResult StaffAddonUnavailable() => BadRequest(ApiResponse<object>.Fail(
        "STAFF_ADDON_UNAVAILABLE", "Chỉ Gói Thường còn hiệu lực và chưa có tiện ích Quản lý nhân viên mới có thể mua thêm."));

    private static OperationResult StaffAddonPurchaseConflict() => Conflict(ApiResponse<object>.Fail(
        "STAFF_ADDON_PURCHASE_CONFLICT", "Ví hoặc gói dịch vụ vừa thay đổi. Vui lòng thử xác nhận lại.", 409));

    public Task<OperationResult> QuoteProUpgradeAsync(Actor actor, PurchaseSubscriptionRequest request, CancellationToken cancellationToken)
        => ProcessSubscriptionAsync(actor, request with { PlanCode = "PRO" }, true, cancellationToken);

    public async Task<OperationResult> PurchaseSubscriptionAsync(
        Actor actor, PurchaseSubscriptionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ProcessSubscriptionAsync(actor, request, false, cancellationToken);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.SerializationFailure)
        {
            return UpgradeQuoteChanged();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.UniqueViolation })
        {
            return UpgradeQuoteChanged();
        }
    }

    private static OperationResult UpgradeQuoteChanged() => Conflict(ApiResponse<object>.Fail(
        "SUBSCRIPTION_QUOTE_CHANGED", "Báo giá đã thay đổi. Vui lòng xem lại trước khi thanh toán.", 409));

    private async Task<OperationResult> ProcessSubscriptionAsync(
        Actor actor, PurchaseSubscriptionRequest request, bool preview, CancellationToken cancellationToken)
    {
        await using var transaction = !preview && _context.Database.IsRelational()
            ? await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken) : null;
        if (request.ExtraStorageGb is < 0 or > 100)
            return BadRequest(ApiResponse<PurchaseSubscriptionResponse>.Fail("INVALID_STORAGE_GB", "Số GB mua thêm phải từ 0 đến 100."));
        var tenant = await FindTenantAsync(actor, cancellationToken);
        if (tenant == null)
        {
            return NotFound(ApiResponse<PurchaseSubscriptionResponse>.Fail("TENANT_NOT_FOUND", "Không tìm thấy thông tin gian hàng trong hệ thống."));
        }

        var idempotencyCode = !preview && request.RequestId.HasValue ? $"SUB-{request.RequestId.Value:N}" : null;
        if (idempotencyCode != null)
        {
            var completed = await _context.WalletTransactions.AsNoTracking().SingleOrDefaultAsync(
                transaction => transaction.TenantId == tenant.Id && transaction.TransactionCode == idempotencyCode && transaction.Status == "SUCCESS",
                cancellationToken);
            if (completed != null && tenant.SubscriptionExpiresAt.HasValue)
            {
                var period = ReadPaidPeriod(completed);
                var expiry = period?.End ?? tenant.SubscriptionExpiresAt.Value;
                var existingDays = (int)Math.Max(0, Math.Ceiling((expiry - DateTime.UtcNow).TotalDays));
                var existingResponse = new PurchaseSubscriptionResponse(
                    period?.Code ?? tenant.CurrentPlan ?? "STANDARD",
                    period?.Code == "PRO" ? "Gói Pro" : tenant.SubscriptionPlan?.PlanName ?? tenant.CurrentPlan ?? "Gói dịch vụ",
                    expiry,
                    existingDays,
                    completed.Amount,
                    completed.PaymentMethod,
                    "Giao dịch đã được xử lý trước đó.");
                return Ok(ApiResponse<PurchaseSubscriptionResponse>.Ok(existingResponse, "Giao dịch đã được xử lý trước đó."));
            }
        }

        // Trial có endpoint riêng để đảm bảo one-time eligibility; purchase chỉ dành cho gói trả phí.
        var targetCode = (request.PlanCode ?? "").Trim().ToUpperInvariant();
        var currentPlanCode = (tenant.CurrentPlan ?? "").Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(targetCode))
            return BadRequest(ApiResponse<PurchaseSubscriptionResponse>.Fail("INVALID_PLAN_CODE", "Mã gói dịch vụ không hợp lệ."));
        if (targetCode.Contains("TRIAL") || (request.Months == 0 && targetCode != "PRO") ||
            string.Equals(request.PaymentMethod, "TRIAL", StringComparison.OrdinalIgnoreCase))
            return BadRequest(ApiResponse<PurchaseSubscriptionResponse>.Fail("TRIAL_ENDPOINT_REQUIRED", "Vui lòng dùng chức năng Bắt đầu dùng thử."));

        if (targetCode == "STANDARD" &&
            string.Equals(tenant.CurrentPlan, "PRO", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(tenant.SubscriptionStatus, "NO_PLAN", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(ApiResponse<PurchaseSubscriptionResponse>.Fail(
                "DOWNGRADE_NOT_ALLOWED",
                "Gian hàng đang sử dụng Gói Pro, không hỗ trợ hạ cấp xuống Gói Thường."));
        }

        if (!string.Equals(request.PaymentMethod, "WALLET", StringComparison.OrdinalIgnoreCase) &&
            request.PaymentMethod?.Contains("Ví", StringComparison.OrdinalIgnoreCase) != true)
            return BadRequest(ApiResponse<PurchaseSubscriptionResponse>.Fail(
                "INVALID_SUBSCRIPTION_PAYMENT", "Vui lòng thanh toán bằng Ví gian hàng."));
        if (request.ExtraBranches < 0)
            return BadRequest(ApiResponse<PurchaseSubscriptionResponse>.Fail(
                "INVALID_PLAN_ADDON", "Số chi nhánh mua thêm không được âm."));

        // Tìm gói cước theo PlanCode hoặc Id
        var plan = await _context.SubscriptionPlans.FirstOrDefaultAsync(
            p => (p.PlanCode == targetCode || p.Id == request.PlanCode) && !p.IsDeleted,
            cancellationToken);

        if (plan != null && !plan.IsActive)
        {
            return BadRequest(ApiResponse<PurchaseSubscriptionResponse>.Fail(
                "PLAN_INACTIVE", "Gói dịch vụ này hiện đang tạm ngừng mở bán."));
        }

        if (plan == null)
        {
            if (targetCode != "STANDARD" && targetCode != "PRO")
            {
                return BadRequest(ApiResponse<PurchaseSubscriptionResponse>.Fail(
                    "INVALID_PLAN_CODE", "Mã gói dịch vụ không hợp lệ hoặc không tồn tại."));
            }

            plan = new SubscriptionPlan
            {
                Id = targetCode == "PRO" ? "plan_pro" : "plan_standard",
                PlanCode = targetCode,
                PlanName = targetCode == "PRO" ? "Gói Pro (Chuỗi Cửa Hàng)" : "Gói Thường (Cửa Hàng Độc Lập)",
                PriceMonthly = targetCode == "PRO" ? 249000.00m : 99000.00m,
                PriceYearly = targetCode == "PRO" ? 2988000.00m : 1188000.00m,
                MaxBranches = targetCode == "PRO" ? 2 : 1,
                MaxStaff = targetCode == "PRO" ? 100 : 1,
                EnabledModulesJson = targetCode == "PRO"
                    ? "[\"Catalog\",\"Pos\",\"Inventory\",\"Supplier\",\"Finance\",\"HR\",\"Tax\",\"MultiBranch\"]"
                    : "[\"Catalog\",\"Pos\",\"Inventory\",\"Finance\"]",
                IsActive = true
            };
            if (!preview) _context.SubscriptionPlans.Add(plan);
        }

        var now = DateTime.UtcNow;
        var isUpgrade = targetCode == "PRO" && currentPlanCode == "STANDARD"
            && tenant.SubscriptionStatus == "ACTIVE" && tenant.SubscriptionExpiresAt > now;
        if ((preview || request.QuoteAt.HasValue || request.Months == 0) && !isUpgrade)
            return UpgradeQuoteChanged();
        var months = request.Months < 0 ? 12 : request.Months;

        // Validation nghiệp vụ theo gói
        if (plan.PlanCode == "STANDARD" && request.ExtraBranches > 0)
        {
            return BadRequest(ApiResponse<PurchaseSubscriptionResponse>.Fail(
                "INVALID_PLAN_ADDON",
                "Gói Thường cố định 1 chi nhánh và không hỗ trợ mua thêm chi nhánh. Vui lòng nâng cấp lên Gói Pro để mở rộng chuỗi cửa hàng."));
        }

        var branchAddonPrice = plan.PlanCode == "PRO" ? request.ExtraBranches * 80000m : 0m;
        var staffAddonPrice = plan.PlanCode == "STANDARD" && request.HasStaffManagementAddon ? StaffAddonMonthlyPrice : 0m;
        var subtotal = (plan.PriceMonthly + branchAddonPrice + staffAddonPrice + request.ExtraStorageGb * 15000m) * months;
        var grandTotal = subtotal + Math.Round(subtotal * 0.1m, 0);
        var bonusMonths = months >= 24 ? 3 : (months >= 12 ? 1 : 0);
        var totalMonthsAdded = months + bonusMonths;
        var baseDate = tenant.SubscriptionExpiresAt > now && (targetCode != "PRO" || isUpgrade
            || (currentPlanCode == "PRO" && tenant.SubscriptionStatus == "ACTIVE"))
            ? tenant.SubscriptionExpiresAt.Value : now;
        var newExpiresAt = baseDate.AddMonths(totalMonthsAdded);

        if (isUpgrade)
        {
            if (request.Months is not (0 or 3 or 6 or 12) || request.ExtraStorageGb != 0)
                return BadRequest(ApiResponse<object>.Fail("INVALID_UPGRADE_OPTIONS", "Tùy chọn nâng cấp không hợp lệ."));
            var at = preview ? now : request.QuoteAt;
            if (!at.HasValue || at > now || now - at.Value > TimeSpan.FromMinutes(5)
                || (!preview && (request.ExpectedExpiry != baseDate || !request.RequestId.HasValue || request.RequestId == Guid.Empty)))
                return UpgradeQuoteChanged();

            var remainingMonths = (baseDate - at.Value).Ticks / (decimal)TimeSpan.FromDays(30).Ticks;
            var remainingPro = decimal.Round(plan.PriceMonthly * remainingMonths * 1.1m);
            var credit = await GetUpgradeCreditAsync(tenant, at.Value, cancellationToken);
            var branches = decimal.Round(branchAddonPrice * remainingMonths * 1.1m);
            var total = Math.Max(0m, remainingPro - credit) + grandTotal + branches;
            if (preview)
                return Ok(ApiResponse<ProUpgradeQuote>.Ok(new(at.Value, baseDate, newExpiresAt,
                    remainingPro, credit, grandTotal, branches, total)));
            if (request.ExpectedTotal != total) return UpgradeQuoteChanged();
            grandTotal = total;
        }

        var balanceBefore = tenant.WalletBalance;
        if (balanceBefore < grandTotal)
            return BadRequest(ApiResponse<PurchaseSubscriptionResponse>.Fail(
                "INSUFFICIENT_WALLET_BALANCE",
                $"Số dư ví không đủ (hiện có {balanceBefore:N0} đ, cần {grandTotal:N0} đ). Vui lòng nạp thêm tiền."));

        tenant.WalletBalance -= grandTotal;
        _context.WalletTransactions.Add(new WalletTransaction
        {
            TenantId = tenant.Id,
            TransactionCode = idempotencyCode ?? $"SUB-{now:yyyyMMdd}-{Guid.NewGuid():N}",
            Type = isUpgrade ? "UPGRADE_PLAN" : "PURCHASE_PLAN",
            Amount = grandTotal,
            BalanceBefore = balanceBefore,
            BalanceAfter = tenant.WalletBalance,
            Description = isUpgrade ? $"Nâng cấp {plan.PlanName}" : $"Thanh toán gói dịch vụ {plan.PlanName} ({months} tháng)",
            PaymentMethod = "WALLET",
            Status = "SUCCESS",
            CreatedAt = now,
            Metadata = JsonSerializer.Serialize(new PaidPeriod(targetCode, isUpgrade ? now : baseDate, newExpiresAt,
                targetCode == "STANDARD" ? decimal.Round((plan.PriceMonthly + staffAddonPrice) * months * 1.1m) : 0m))
        });

        var hasActiveStandardAddon = currentPlanCode == "STANDARD" && tenant.HasStaffManagementAddon && baseDate > now;

        tenant.CurrentPlan = plan.PlanCode;
        tenant.SubscriptionPlan = plan;
        tenant.FirstPaidAt ??= now;
        tenant.SubscriptionExpiresAt = newExpiresAt;
        tenant.SubscriptionStatus = "ACTIVE";
        tenant.Status = "ACTIVE";
        tenant.HasStaffManagementAddon = plan.PlanCode != "STANDARD" || request.HasStaffManagementAddon || hasActiveStandardAddon;
        if (plan.PlanCode != "STANDARD") tenant.ExtraBranches += request.ExtraBranches;
        tenant.ExtraStorageGb = request.ExtraStorageGb;
        tenant.StorageAddonExpiresAt = request.ExtraStorageGb > 0 ? newExpiresAt : null;

        await _context.SaveChangesAsync(cancellationToken);
        if (transaction != null) await transaction.CommitAsync(cancellationToken);

        var daysRemaining = (int)Math.Max(0, Math.Ceiling((newExpiresAt - DateTime.UtcNow).TotalDays));

        var response = new PurchaseSubscriptionResponse(
            PlanCode: plan.PlanCode,
            PlanName: plan.PlanName,
            SubscriptionExpiresAt: newExpiresAt,
            DaysRemaining: daysRemaining,
            TotalAmount: grandTotal,
            PaymentMethod: request.PaymentMethod,
            Message: isUpgrade ? $"Nâng cấp thành công {plan.PlanName}." : $"Gia hạn thành công {plan.PlanName} thêm {totalMonthsAdded} tháng."
        );

        return Ok(ApiResponse<PurchaseSubscriptionResponse>.Ok(response, "Thanh toán và gia hạn gói dịch vụ thành công."));
    }

    private sealed record PaidPeriod(string Code, DateTime Start, DateTime End, decimal Credit);

    private static PaidPeriod? ReadPaidPeriod(WalletTransaction payment)
    {
        if (string.IsNullOrEmpty(payment.Metadata)) return null;
        try { return JsonSerializer.Deserialize<PaidPeriod>(payment.Metadata); }
        catch (JsonException) { return null; }
    }

    private static PaidPeriod? GetPaidPeriod(WalletTransaction payment, ref DateTime standardExpiry)
    {
        var period = ReadPaidPeriod(payment);
        if (period == null && standardExpiry > payment.CreatedAt
            && payment.TransactionCode.StartsWith("STAFF-", StringComparison.Ordinal))
            return new("STAFF", payment.CreatedAt, standardExpiry, payment.Amount);

        if (period == null && payment.Type == "PURCHASE_PLAN"
            && payment.Description.StartsWith("Thanh toán gói dịch vụ Gói Thường", StringComparison.Ordinal))
        {
            var match = Regex.Match(payment.Description, @"[(]([0-9]{1,3}) tháng[)]$");
            if (!match.Success) return null;
            var months = int.Parse(match.Groups[1].Value);
            var start = standardExpiry > payment.CreatedAt ? standardExpiry : payment.CreatedAt;
            period = new("STANDARD", start, start.AddMonths(months + (months >= 24 ? 3 : months >= 12 ? 1 : 0)), payment.Amount);
        }

        if (period?.Code == "STANDARD") standardExpiry = period.End;
        return period;
    }

    private async Task<decimal> GetUpgradeCreditAsync(Tenant tenant, DateTime at, CancellationToken cancellationToken)
    {
        var payments = await _context.WalletTransactions.AsNoTracking()
            .Where(p => p.TenantId == tenant.Id && p.Status == "SUCCESS" && p.Amount > 0
                && (p.Type == "PURCHASE_PLAN" || p.TransactionCode.StartsWith("STAFF-")))
            .OrderBy(p => p.CreatedAt).ThenBy(p => p.TransactionCode).ToListAsync(cancellationToken);
        var cursor = tenant.TrialExpiresAt ?? DateTime.MinValue;
        var expiry = tenant.SubscriptionExpiresAt!.Value;
        decimal credit = 0;
        foreach (var payment in payments)
        {
            var period = GetPaidPeriod(payment, ref cursor);
            if (period == null || period.Code is not ("STANDARD" or "STAFF") || period.End <= period.Start) continue;
            var from = at > period.Start ? at : period.Start;
            var to = expiry < period.End ? expiry : period.End;
            credit += period.Credit * Math.Max(0, (to - from).Ticks) / (period.End - period.Start).Ticks;
        }
        return decimal.Round(credit);
    }

    public async Task<OperationResult> GetSubscriptionOrderHistoryAsync(Actor actor, CancellationToken cancellationToken)
    {
        var tenant = await FindTenantAsync(actor, cancellationToken);
        if (tenant == null)
        {
            return NotFound(ApiResponse<IReadOnlyList<SubscriptionOrderHistoryDto>>.Fail("TENANT_NOT_FOUND", "Không tìm thấy thông tin gian hàng trong hệ thống."));
        }

        var list = new List<SubscriptionOrderHistoryDto>();

        // 1. Lấy các giao dịch mua gói dịch vụ từ WalletTransactions
        var paidOrders = await _context.WalletTransactions
            .Where(w => w.TenantId == tenant.Id && (w.Type == "PURCHASE_PLAN" || w.Type == "UPGRADE_PLAN" || w.Type == "PURCHASE_ADDON"))
            .OrderBy(w => w.CreatedAt).ThenBy(w => w.TransactionCode)
            .ToListAsync(cancellationToken);

        var cursor = tenant.TrialExpiresAt ?? DateTime.MinValue;
        var upgradeAt = paidOrders.LastOrDefault(w => w.Type == "UPGRADE_PLAN" && w.Status == "SUCCESS")?.CreatedAt;
        foreach (var order in paidOrders)
        {
            var period = GetPaidPeriod(order, ref cursor);
            var replaced = period is { Code: "STANDARD" or "STAFF" }
                && upgradeAt.HasValue && order.CreatedAt <= upgradeAt.Value && period.End > upgradeAt.Value;
            var isPlanActive = (period?.End ?? tenant.SubscriptionExpiresAt) >= DateTime.UtcNow;
            list.Add(new SubscriptionOrderHistoryDto(
                Code: order.TransactionCode,
                PlanName: order.Description.Replace("Thanh toán gói dịch vụ ", ""),
                StartDate: (period?.Start ?? order.CreatedAt).ToString("dd/MM/yyyy"),
                EndDate: (period?.End ?? tenant.SubscriptionExpiresAt)?.ToString("dd/MM/yyyy") ?? order.CreatedAt.AddYears(1).ToString("dd/MM/yyyy"),
                TotalAmount: order.Amount,
                PaymentMethod: order.PaymentMethod == "WALLET" ? "Ví MT-GRMS" : order.PaymentMethod,
                Status: replaced ? "Đã chuyển sang Pro" : isPlanActive ? "Đang hiệu lực" : "Hết hạn",
                CreatedAt: order.CreatedAt
            ));
        }
        list.Reverse();

        // 2. Chỉ bổ sung bản ghi gói dùng thử nếu tenant thực sự có dấu Trial
        if (tenant.TrialStartedAt.HasValue || string.Equals(tenant.CurrentPlan, "TRIAL", StringComparison.OrdinalIgnoreCase))
        {
            var initPlanName = tenant.SubscriptionPlan?.PlanName ?? "Gói Dùng Thử Trải Nghiệm";
            var initEndDate = tenant.TrialExpiresAt ?? tenant.SubscriptionExpiresAt ?? tenant.CreatedAt.AddHours(72);
            var isInitActive = list.Count == 0 && initEndDate >= DateTime.UtcNow;

            list.Add(new SubscriptionOrderHistoryDto(
                Code: $"SUB-{tenant.CreatedAt:yyyyMMdd}-001",
                PlanName: initPlanName,
                StartDate: (tenant.TrialStartedAt ?? tenant.CreatedAt).ToString("dd/MM/yyyy"),
                EndDate: initEndDate.ToString("dd/MM/yyyy"),
                TotalAmount: 0m,
                PaymentMethod: "Gói hệ thống cấp",
                Status: isInitActive ? "Đang hiệu lực" : "Hết hạn",
                CreatedAt: tenant.TrialStartedAt ?? tenant.CreatedAt
            ));
        }

        return Ok(ApiResponse<IReadOnlyList<SubscriptionOrderHistoryDto>>.Ok(list));
    }
}
