using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Admin.Dtos;
using MT_GRMS.Application.Admin.Services;
using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Constants;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Infrastructure.Persistence;
using MT_GRMS.Api.Filters;

namespace MT_GRMS.Api.Controllers;

[ApiController]
[Route("api/tenant")]
[Authorize]
public class TenantStoreController : BaseApiController
{
    private readonly ITenantService _tenantService;
    private readonly ISubscriptionPlanService _planService;
    private readonly AppDbContext _context;

    public TenantStoreController(
        ITenantService tenantService,
        ISubscriptionPlanService planService,
        AppDbContext context)
    {
        _tenantService = tenantService;
        _planService = planService;
        _context = context;
    }

    private async Task<Tenant?> ResolveCurrentTenantAsync(CancellationToken cancellationToken)
    {
        var tenantId = User.FindFirst("tenant_id")?.Value;
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(userId) || tenantId == "DEFAULT") return null;

        var hasMembership = await _context.TenantUsers.AnyAsync(x =>
            x.TenantId == tenantId && x.UserId == userId && x.IsActive, cancellationToken);
        if (!hasMembership) return null;

        return await _context.Tenants.Include(t => t.SubscriptionPlan)
            .FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);
    }

    /// <summary>
    /// Lấy thông tin chi tiết gian hàng và gói cước hiện tại của khách thuê đang đăng nhập
    /// </summary>
    [HttpGet("store-info")]
    public async Task<IActionResult> GetStoreInfo(CancellationToken cancellationToken)
    {
        var tenant = await ResolveCurrentTenantAsync(cancellationToken);
        if (tenant == null)
        {
            return NotFound(ApiResponse<TenantDetailResponse>.Fail("TENANT_NOT_FOUND", "Không tìm thấy thông tin gian hàng trong hệ thống."));
        }

        var detail = await _tenantService.GetByIdAsync(tenant.Id, cancellationToken);
        return Ok(ApiResponse<TenantDetailResponse>.Ok(detail));
    }

    /// <summary>
    /// Cập nhật thông tin gian hàng của khách thuê (Tên chuỗi, MST, Hotline, Địa chỉ, Thành phố)
    /// </summary>
    [HttpPut("store-info")]
    [Authorize(Roles = RoleConstants.StoreOwnerOrAdmin)]
    public async Task<IActionResult> UpdateStoreInfo([FromBody] UpdateTenantRequest request, CancellationToken cancellationToken)
    {
        var tenant = await ResolveCurrentTenantAsync(cancellationToken);
        if (tenant == null)
        {
            return NotFound(ApiResponse<TenantDetailResponse>.Fail("TENANT_NOT_FOUND", "Không tìm thấy thông tin gian hàng để cập nhật."));
        }

        var updated = await _tenantService.UpdateTenantAsync(tenant.Id, request, cancellationToken);
        return Ok(ApiResponse<TenantDetailResponse>.Ok(updated, "Cập nhật thông tin gian hàng thành công."));
    }

    /// <summary>
    /// Lấy danh sách các gói cước dịch vụ đang kích hoạt để chủ cửa hàng xem, so sánh và đặt mua
    /// </summary>
    [HttpGet("pricing-plans")]
    public async Task<IActionResult> GetPricingPlans(CancellationToken cancellationToken)
    {
        var plans = await _planService.GetPlansAsync(new PlanFilterRequest(Status: "ACTIVE"), cancellationToken);

        // Lấy 3 gói cước chính thức của hệ thống SaaS bán lẻ: TRIAL (Trải nghiệm 0đ 3 ngày), STANDARD (Gói Thường 99k) và PRO (Gói Pro 249k)
        var officialPlans = plans
            .Where(p => string.Equals(p.Code, "TRIAL", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(p.Code, "STANDARD", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(p.Code, "PRO", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.MonthlyPrice)
            .ToList();

        // Đảm bảo luôn có gói TRIAL ở đầu danh sách nếu CSDL chưa nạp
        if (!officialPlans.Any(p => string.Equals(p.Code, "TRIAL", StringComparison.OrdinalIgnoreCase)))
        {
            var defaultTrial = new PlanSummaryResponse(
                Id: "plan_trial",
                Code: "TRIAL",
                Name: "Gói Dùng Thử (Trải Nghiệm)",
                Description: "Trải nghiệm toàn diện các tính năng cao cấp của MT-GRMS hoàn toàn miễn phí trong 3 ngày.",
                MonthlyPrice: 0m,
                YearlyPrice: 0m,
                Status: "ACTIVE",
                ActiveTenantCount: 0,
                Quotas: new PlanQuotaDto(MaxBranches: 1, MaxEmployees: 5, MaxStorageGb: 5, BackupFrequency: "Daily"),
                Features: new PlanFeatureDto(FefoExpiryAlert: true, MultiUnitConversion: true, VietQrDynamic: true, DebtZaloBook: false, BarcodeScanning: true, FinancialReporting: false),
                CreatedAt: DateTime.UtcNow,
                UpdatedAt: null
            );
            officialPlans.Insert(0, defaultTrial);
        }

        if (!officialPlans.Any(p => string.Equals(p.Code, "STANDARD", StringComparison.OrdinalIgnoreCase)))
        {
            var defaultStandard = new PlanSummaryResponse(
                Id: "plan_standard",
                Code: "STANDARD",
                Name: "Gói Thường (Cửa Hàng Độc Lập)",
                Description: "Dành cho tiệm tạp hóa, siêu thị mini độc lập.",
                MonthlyPrice: 99000m,
                YearlyPrice: 1188000m,
                Status: "ACTIVE",
                ActiveTenantCount: 0,
                Quotas: new PlanQuotaDto(MaxBranches: 1, MaxEmployees: 1, MaxStorageGb: 2, BackupFrequency: "Daily"),
                Features: new PlanFeatureDto(FefoExpiryAlert: true, MultiUnitConversion: true, VietQrDynamic: true, DebtZaloBook: false, BarcodeScanning: true, FinancialReporting: false),
                CreatedAt: DateTime.UtcNow,
                UpdatedAt: null
            );
            officialPlans.Add(defaultStandard);
        }

        if (!officialPlans.Any(p => string.Equals(p.Code, "PRO", StringComparison.OrdinalIgnoreCase)))
        {
            var defaultPro = new PlanSummaryResponse(
                Id: "plan_pro",
                Code: "PRO",
                Name: "Gói Pro (Chuỗi Cửa Hàng)",
                Description: "Dành cho chuỗi siêu thị bán lẻ, chuyên môn hóa quy trình và điều chuyển kho chuỗi.",
                MonthlyPrice: 249000m,
                YearlyPrice: 2988000m,
                Status: "ACTIVE",
                ActiveTenantCount: 0,
                Quotas: new PlanQuotaDto(MaxBranches: 2, MaxEmployees: 100, MaxStorageGb: 2, BackupFrequency: "Hourly"),
                Features: new PlanFeatureDto(FefoExpiryAlert: true, MultiUnitConversion: true, VietQrDynamic: true, DebtZaloBook: true, BarcodeScanning: true, FinancialReporting: true),
                CreatedAt: DateTime.UtcNow,
                UpdatedAt: null
            );
            officialPlans.Add(defaultPro);
        }

        // Sắp xếp thứ tự: TRIAL (1, trái) -> STANDARD (2, giữa) -> PRO (3, phải)
        var orderMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["TRIAL"] = 1,
            ["STANDARD"] = 2,
            ["PRO"] = 3
        };
        officialPlans = officialPlans.OrderBy(p => orderMap.TryGetValue(p.Code, out var o) ? o : 99).ToList();

        return Ok(ApiResponse<IReadOnlyList<PlanSummaryResponse>>.Ok(officialPlans));
    }

    /// <summary>
    /// Đặt mua hoặc nâng cấp / gia hạn gói dịch vụ SaaS cho gian hàng
    /// </summary>
    [HttpPost("subscriptions/purchase")]
    [Authorize(Roles = RoleConstants.StoreOwnerOrAdmin)]
    public async Task<IActionResult> PurchaseSubscription(
        [FromBody] PurchaseSubscriptionRequest request,
        CancellationToken cancellationToken)
    {
        if (request.ExtraStorageGb is < 0 or > 100)
            return BadRequest(ApiResponse<PurchaseSubscriptionResponse>.Fail("INVALID_STORAGE_GB", "Số GB mua thêm phải từ 0 đến 100."));
        var tenant = await ResolveCurrentTenantAsync(cancellationToken);
        if (tenant == null)
        {
            return NotFound(ApiResponse<PurchaseSubscriptionResponse>.Fail("TENANT_NOT_FOUND", "Không tìm thấy thông tin gian hàng trong hệ thống."));
        }

        var idempotencyCode = request.RequestId.HasValue ? $"SUB-{request.RequestId.Value:N}" : null;
        if (idempotencyCode != null)
        {
            var completed = await _context.WalletTransactions.AsNoTracking().SingleOrDefaultAsync(
                transaction => transaction.TenantId == tenant.Id && transaction.TransactionCode == idempotencyCode && transaction.Status == "SUCCESS",
                cancellationToken);
            if (completed != null && tenant.SubscriptionExpiresAt.HasValue)
            {
                var existingDays = (int)Math.Max(0, Math.Ceiling((tenant.SubscriptionExpiresAt.Value - DateTime.UtcNow).TotalDays));
                var existingResponse = new PurchaseSubscriptionResponse(
                    tenant.CurrentPlan ?? "STANDARD",
                    tenant.SubscriptionPlan?.PlanName ?? tenant.CurrentPlan ?? "Gói dịch vụ",
                    tenant.SubscriptionExpiresAt.Value,
                    existingDays,
                    completed.Amount,
                    completed.PaymentMethod,
                    "Giao dịch đã được xử lý trước đó.");
                return Ok(ApiResponse<PurchaseSubscriptionResponse>.Ok(existingResponse, "Giao dịch đã được xử lý trước đó."));
            }
        }

        // Trial có endpoint riêng để đảm bảo one-time eligibility; purchase chỉ dành cho gói trả phí.
        var targetCode = (request.PlanCode ?? "").Trim().ToUpperInvariant();
        if (targetCode.Contains("TRIAL"))
            return BadRequest(ApiResponse<PurchaseSubscriptionResponse>.Fail("TRIAL_ENDPOINT_REQUIRED", "Vui lòng dùng chức năng Bắt đầu dùng thử."));
        if (targetCode != "STANDARD" && targetCode != "PRO")
        {
            return BadRequest(ApiResponse<PurchaseSubscriptionResponse>.Fail("INVALID_PLAN_CODE", "Mã gói dịch vụ không hợp lệ. Vui lòng chọn STANDARD hoặc PRO."));
        }

        if (targetCode == "STANDARD" &&
            string.Equals(tenant.CurrentPlan, "PRO", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(tenant.SubscriptionStatus, "NO_PLAN", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(ApiResponse<PurchaseSubscriptionResponse>.Fail(
                "DOWNGRADE_NOT_ALLOWED",
                "Gian hàng đang sử dụng Gói Pro, không hỗ trợ hạ cấp xuống Gói Thường."));
        }

        // Tìm gói cước theo PlanCode
        var plan = await _context.SubscriptionPlans.FirstOrDefaultAsync(
            p => (p.PlanCode == targetCode || p.Id == request.PlanCode) && p.IsActive,
            cancellationToken);

        if (plan == null)
        {
            plan = await _context.SubscriptionPlans.FirstOrDefaultAsync(
                p => p.PlanCode == targetCode,
                cancellationToken);
        }

        if (plan == null)
        {
            plan = new SubscriptionPlan
            {
                Id = targetCode == "PRO" ? "plan_pro" : (targetCode == "TRIAL" ? "plan_trial" : "plan_standard"),
                PlanCode = targetCode,
                PlanName = targetCode == "PRO" ? "Gói Pro (Chuỗi Cửa Hàng)" : (targetCode == "TRIAL" ? "Gói Dùng Thử (Trải Nghiệm)" : "Gói Thường (Cửa Hàng Độc Lập)"),
                PriceMonthly = targetCode == "PRO" ? 249000.00m : (targetCode == "TRIAL" ? 0m : 99000.00m),
                PriceYearly = targetCode == "PRO" ? 2988000.00m : (targetCode == "TRIAL" ? 0m : 1188000.00m),
                MaxBranches = targetCode == "PRO" ? 2 : 1,
                MaxStaff = targetCode == "PRO" ? 100 : (targetCode == "TRIAL" ? 5 : 1),
                EnabledModulesJson = targetCode == "PRO"
                    ? "[\"Catalog\",\"Pos\",\"Inventory\",\"Supplier\",\"Finance\",\"HR\",\"Tax\",\"MultiBranch\"]"
                    : (targetCode == "TRIAL" ? "[\"Catalog\",\"Pos\",\"Inventory\",\"Finance\",\"HR\"]" : "[\"Catalog\",\"Pos\",\"Inventory\",\"Finance\"]"),
                IsActive = true
            };
            await _context.SubscriptionPlans.AddAsync(plan, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
        }

        var isTrial = request.Months == 0 || string.Equals(request.PaymentMethod, "TRIAL", StringComparison.OrdinalIgnoreCase);
        if (isTrial && request.ExtraStorageGb > 0)
            return BadRequest(ApiResponse<PurchaseSubscriptionResponse>.Fail("INVALID_TRIAL_ADDON", "Gói dùng thử không hỗ trợ mua thêm dung lượng."));
        var months = isTrial ? 0 : (request.Months <= 0 ? 12 : request.Months);

        // Validation nghiệp vụ theo gói
        if (plan.PlanCode == "STANDARD" && request.ExtraBranches > 0)
        {
            return BadRequest(ApiResponse<PurchaseSubscriptionResponse>.Fail(
                "INVALID_PLAN_ADDON",
                "Gói Thường cố định 1 chi nhánh và không hỗ trợ mua thêm chi nhánh. Vui lòng nâng cấp lên Gói Pro để mở rộng chuỗi cửa hàng."));
        }

        // 1. Tính toán chi phí chuẩn hóa
        decimal grandTotal = 0m;
        if (!isTrial)
        {
            var basePrice = plan.PriceMonthly * months;
            var branchAddonPrice = (plan.PlanCode == "PRO" ? request.ExtraBranches * 80000m : 0m) * months;
            var staffAddonPrice = (plan.PlanCode == "STANDARD" && request.HasStaffManagementAddon ? 30000m : 0m) * months;
            var storageAddonPrice = request.ExtraStorageGb * 15000m * months;
            var subTotal = basePrice + branchAddonPrice + staffAddonPrice + storageAddonPrice;
            var vatAmount = Math.Round(subTotal * 0.1m, 0);
            grandTotal = subTotal + vatAmount;

            // 2. Xử lý thanh toán nếu chọn phương thức Ví gian hàng
            var isWalletPayment = string.Equals(request.PaymentMethod, "WALLET", StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(request.PaymentMethod, "Wallet", StringComparison.OrdinalIgnoreCase) ||
                                  request.PaymentMethod.Contains("Ví", StringComparison.OrdinalIgnoreCase);

            if (isWalletPayment)
            {
                if (tenant.WalletBalance < grandTotal)
                {
                    return BadRequest(ApiResponse<PurchaseSubscriptionResponse>.Fail(
                        "INSUFFICIENT_WALLET_BALANCE",
                        $"Số dư ví không đủ (hiện có {tenant.WalletBalance:N0} đ, cần {grandTotal:N0} đ). Vui lòng nạp thêm tiền hoặc chọn Quét mã VietQR."));
                }

                var balanceBefore = tenant.WalletBalance;
                tenant.WalletBalance -= grandTotal;
                var balanceAfter = tenant.WalletBalance;

                var walletTx = new MT_GRMS.Domain.Entities.SaaS.WalletTransaction
                {
                    TenantId = tenant.Id,
                    TransactionCode = idempotencyCode ?? $"SUB-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid():N}",
                    Type = "PURCHASE_PLAN",
                    Amount = grandTotal,
                    BalanceBefore = balanceBefore,
                    BalanceAfter = balanceAfter,
                    Description = $"Thanh toán gói dịch vụ {plan.PlanName} ({months} tháng)",
                    PaymentMethod = "WALLET",
                    Status = "SUCCESS"
                };
                _context.WalletTransactions.Add(walletTx);
            }
        }

        DateTime newExpiresAt;
        int totalMonthsAdded = 0;
        string resultMessage;

        if (isTrial)
        {
            newExpiresAt = DateTime.UtcNow.AddDays(3);
            tenant.CurrentPlan = "TRIAL";
            tenant.SubscriptionStatus = "TRIAL";
            tenant.SubscriptionExpiresAt = newExpiresAt;
            tenant.HasStaffManagementAddon = true; // Gói Dùng thử mở khóa trải nghiệm quản lý nhân viên
            tenant.Status = "ACTIVE";
            resultMessage = "Kích hoạt thành công 3 ngày dùng thử miễn phí Gói Dùng Thử (Trải Nghiệm).";
        }
        else
        {
            // Tính số tháng gia hạn (Khuyến mãi thêm tháng)
            var bonusMonths = months >= 24 ? 3 : (months >= 12 ? 1 : 0);
            totalMonthsAdded = months + bonusMonths;

            // Gia hạn thời gian sử dụng
            var baseDate = (tenant.SubscriptionExpiresAt.HasValue && tenant.SubscriptionExpiresAt.Value > DateTime.UtcNow)
                ? tenant.SubscriptionExpiresAt.Value
                : DateTime.UtcNow;

            newExpiresAt = baseDate.AddMonths(totalMonthsAdded);

            tenant.CurrentPlan = plan.PlanCode;
            tenant.FirstPaidAt ??= DateTime.UtcNow;
            tenant.SubscriptionExpiresAt = newExpiresAt;
            tenant.SubscriptionStatus = "ACTIVE";
            tenant.Status = "ACTIVE";

            if (plan.PlanCode == "PRO")
            {
                tenant.ExtraBranches += request.ExtraBranches;
                tenant.HasStaffManagementAddon = true; // Gói Pro mặc định có sẵn tính năng quản lý nhân viên
            }
            else if (plan.PlanCode == "STANDARD" && request.HasStaffManagementAddon)
            {
                tenant.HasStaffManagementAddon = true;
            }
            tenant.ExtraStorageGb = request.ExtraStorageGb;
            tenant.StorageAddonExpiresAt = request.ExtraStorageGb > 0 ? newExpiresAt : null;

            resultMessage = $"Gia hạn thành công {plan.PlanName} thêm {totalMonthsAdded} tháng.";
        }

        await _context.SaveChangesAsync(cancellationToken);

        var daysRemaining = (int)Math.Max(0, Math.Ceiling((newExpiresAt - DateTime.UtcNow).TotalDays));

        var response = new PurchaseSubscriptionResponse(
            PlanCode: plan.PlanCode,
            PlanName: plan.PlanName,
            SubscriptionExpiresAt: newExpiresAt,
            DaysRemaining: daysRemaining,
            TotalAmount: grandTotal,
            PaymentMethod: isTrial ? "TRIAL" : request.PaymentMethod,
            Message: resultMessage
        );

        return Ok(ApiResponse<PurchaseSubscriptionResponse>.Ok(response, isTrial ? "Kích hoạt dùng thử thành công." : "Thanh toán và gia hạn gói dịch vụ thành công."));
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> TrialActivationLocks = new();

    /// <summary>
    /// Kích hoạt gói dùng thử 3 ngày miễn phí cho gian hàng
    /// </summary>
    [HttpPost("subscriptions/trial")]
    [RequireIdempotency(useProblemDetails: true)]
    [Authorize(Roles = RoleConstants.StoreOwnerOrAdmin)]
    public async Task<IActionResult> ActivateTrialSubscription(CancellationToken cancellationToken)
    {
        var tenantId = User.FindFirst("tenant_id")?.Value;
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(userId) || tenantId == "DEFAULT")
        {
            return ProblemResult(StatusCodes.Status403Forbidden, "FORBIDDEN", "Không có quyền truy cập", "Phiên đăng nhập không hợp lệ cho gian hàng.");
        }

        var hasMembership = await _context.TenantUsers.AsNoTracking().AnyAsync(x =>
            x.TenantId == tenantId && x.UserId == userId && x.IsActive &&
            (x.Role == MT_GRMS.Domain.Enums.UserRoleNames.StoreOwner || x.Role == "StoreOwner" || x.Role == "ADMIN"),
            cancellationToken);
        if (!hasMembership)
        {
            return ProblemResult(StatusCodes.Status403Forbidden, "FORBIDDEN", "Không có quyền truy cập", "Tài khoản không phải chủ sở hữu đang hoạt động của gian hàng.");
        }

        var lockGate = TrialActivationLocks.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
        await lockGate.WaitAsync(cancellationToken);
        try
        {
            await using var transaction = _context.Database.IsRelational()
                ? await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken)
                : null;

            var utcNow = DateTime.UtcNow;
            var tenant = await _context.Tenants.Include(t => t.SubscriptionPlan)
                .SingleOrDefaultAsync(t => t.Id == tenantId && !t.IsDeleted, cancellationToken);
            var user = await _context.Users
                .SingleOrDefaultAsync(x => x.Id == userId && !x.IsDeleted, cancellationToken);

            if (tenant == null || user == null || !user.IsEmailVerified)
            {
                return ProblemResult(StatusCodes.Status403Forbidden, "FORBIDDEN", "Không đủ điều kiện xác thực", "Tài khoản chưa xác thực email hoặc gian hàng không tồn tại.");
            }

            if (tenant.TrialStartedAt.HasValue)
            {
                if (tenant.TrialExpiresAt > utcNow)
                    return Ok(ApiResponse<object>.Ok(ToTrialStatus(tenant, user)));
                return ProblemResult(StatusCodes.Status409Conflict, "TRIAL_ALREADY_USED", "Đã hết lượt dùng thử", "Gian hàng đã sử dụng gói dùng thử.");
            }

            if (user.TrialConsumedAt.HasValue)
                return ProblemResult(StatusCodes.Status409Conflict, "TRIAL_ALREADY_USED", "Đã hết lượt dùng thử", "Tài khoản đã sử dụng gói dùng thử.");

            if (tenant.FirstPaidAt.HasValue || !string.Equals(tenant.SubscriptionStatus, "NO_PLAN", StringComparison.OrdinalIgnoreCase))
                return ProblemResult(StatusCodes.Status409Conflict, "TRIAL_NOT_ELIGIBLE", "Không đủ điều kiện dùng thử", "Gian hàng không đủ điều kiện dùng thử.");

            var idempotencyKey = Request?.Headers["Idempotency-Key"].FirstOrDefault() ?? $"trial_{Guid.NewGuid():N}";
            var startedAt = utcNow;
            var expiresAt = startedAt.AddHours(72);

            if (_context.Database.IsRelational())
            {
                var updatedUsers = await _context.Users
                    .Where(u => u.Id == userId && !u.IsDeleted && u.IsEmailVerified && u.TrialConsumedAt == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(u => u.TrialConsumedAt, startedAt), cancellationToken);
                if (updatedUsers != 1)
                {
                    if (transaction != null) await transaction.RollbackAsync(cancellationToken);
                    return ProblemResult(StatusCodes.Status409Conflict, "TRIAL_ALREADY_USED", "Đã hết lượt dùng thử", "Tài khoản đã sử dụng gói dùng thử.");
                }

                var updatedTenants = await _context.Tenants
                    .Where(t => t.Id == tenantId && !t.IsDeleted && t.TrialStartedAt == null && t.FirstPaidAt == null && t.SubscriptionStatus == "NO_PLAN")
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(t => t.TrialStartedAt, startedAt)
                        .SetProperty(t => t.TrialExpiresAt, expiresAt)
                        .SetProperty(t => t.SubscriptionExpiresAt, expiresAt)
                        .SetProperty(t => t.CurrentPlan, "TRIAL")
                        .SetProperty(t => t.SubscriptionStatus, "TRIAL")
                        .SetProperty(t => t.HasStaffManagementAddon, true), cancellationToken);
                if (updatedTenants != 1)
                {
                    if (transaction != null) await transaction.RollbackAsync(cancellationToken);
                    return ProblemResult(StatusCodes.Status409Conflict, "TRIAL_NOT_ELIGIBLE", "Không đủ điều kiện dùng thử", "Gian hàng không đủ điều kiện dùng thử.");
                }

                await _context.Entry(user).ReloadAsync(cancellationToken);
                await _context.Entry(tenant).ReloadAsync(cancellationToken);
            }
            else
            {
                user.TrialConsumedAt = startedAt;
                tenant.TrialStartedAt = startedAt;
                tenant.TrialExpiresAt = expiresAt;
                tenant.SubscriptionExpiresAt = expiresAt;
                tenant.CurrentPlan = "TRIAL";
                tenant.SubscriptionStatus = "TRIAL";
                tenant.HasStaffManagementAddon = true;
            }

            _context.AuditLogs.Add(new MT_GRMS.Domain.Entities.System.AuditLog
            {
                TenantId = tenant.Id,
                ActorId = user.Id,
                ActorName = user.FullName,
                ActorRole = MT_GRMS.Domain.Enums.UserRoleNames.StoreOwner,
                ActionType = "TRIAL_ACTIVATED",
                TargetEntity = "Tenant",
                TargetId = tenant.Id,
                IpAddress = HttpContext?.Connection?.RemoteIpAddress?.ToString(),
                NewValuesJson = System.Text.Json.JsonSerializer.Serialize(new
                {
                    idempotencyKey,
                    trialStartedAt = startedAt,
                    trialExpiresAt = expiresAt
                })
            });

            await _context.SaveChangesAsync(cancellationToken);
            if (transaction != null) await transaction.CommitAsync(cancellationToken);
            return Ok(ApiResponse<object>.Ok(ToTrialStatus(tenant, user), "Kích hoạt dùng thử 3 ngày thành công."));
        }
        finally
        {
            lockGate.Release();
        }
    }

    [HttpGet("subscriptions/trial-status")]
    [Authorize(Roles = RoleConstants.StoreOwnerOrAdmin)]
    public Task<IActionResult> GetTrialStatus(CancellationToken cancellationToken)
        => BuildSubscriptionAccessStatusAsync(cancellationToken);

    [HttpGet("subscriptions/access-status")]
    [Authorize]
    public Task<IActionResult> GetSubscriptionAccessStatus(CancellationToken cancellationToken)
        => BuildSubscriptionAccessStatusAsync(cancellationToken);

    private async Task<IActionResult> BuildSubscriptionAccessStatusAsync(CancellationToken cancellationToken)
    {
        var tenant = await ResolveCurrentTenantAsync(cancellationToken);
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;
        if (tenant == null || string.IsNullOrWhiteSpace(userId))
        {
            return ProblemResult(StatusCodes.Status403Forbidden, "FORBIDDEN", "Không có quyền truy cập", "Không xác minh được gian hàng hợp lệ.");
        }
        var user = await _context.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId, cancellationToken);
        if (user == null)
        {
            return ProblemResult(StatusCodes.Status403Forbidden, "FORBIDDEN", "Không có quyền truy cập", "Không tìm thấy tài khoản.");
        }
        return Ok(ApiResponse<object>.Ok(ToTrialStatus(tenant, user)));
    }

    private ObjectResult ProblemResult(int statusCode, string code, string title, string detail)
    {
        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Instance = HttpContext?.Request?.Path.Value
        };
        problem.Extensions["code"] = code;
        problem.Extensions["traceId"] = HttpContext?.TraceIdentifier ?? Guid.NewGuid().ToString("N");
        return new ObjectResult(problem) { StatusCode = statusCode };
    }

    private static object ToTrialStatus(Tenant tenant, MT_GRMS.Domain.Entities.Identity.User user)
    {
        var now = DateTime.UtcNow;
        var hasPaidRecord = tenant.FirstPaidAt.HasValue || string.Equals(tenant.SubscriptionStatus, "ACTIVE", StringComparison.OrdinalIgnoreCase);
        var isPaidExpired = hasPaidRecord && tenant.SubscriptionExpiresAt.HasValue && tenant.SubscriptionExpiresAt.Value <= now;
        var isExplicitExpired = string.Equals(tenant.SubscriptionStatus, "EXPIRED", StringComparison.OrdinalIgnoreCase);
        var trialExpiry = tenant.TrialExpiresAt ?? (string.Equals(tenant.SubscriptionStatus, "TRIAL", StringComparison.OrdinalIgnoreCase) ? tenant.SubscriptionExpiresAt : null);
        var isTrialExpired = !hasPaidRecord && trialExpiry.HasValue && trialExpiry.Value <= now;
        var isExpired = isExplicitExpired || isPaidExpired || isTrialExpired;

        var state = isExpired
            ? "EXPIRED"
            : hasPaidRecord
                ? "PAID"
                : tenant.TrialStartedAt.HasValue
                    ? "ACTIVE"
                    : !user.TrialConsumedAt.HasValue && string.Equals(tenant.SubscriptionStatus, "NO_PLAN", StringComparison.OrdinalIgnoreCase)
                        ? "ELIGIBLE"
                        : "REVIEW_REQUIRED";

        var isNoPlan = string.Equals(tenant.SubscriptionStatus, "NO_PLAN", StringComparison.OrdinalIgnoreCase);
        var isLocked = state == "EXPIRED" || state == "ELIGIBLE" || state == "REVIEW_REQUIRED";

        return new
        {
            state,
            planCode = tenant.CurrentPlan,
            subscriptionStatus = tenant.SubscriptionStatus,
            subscriptionExpiresAt = tenant.SubscriptionExpiresAt,
            trialStartedAt = tenant.TrialStartedAt,
            trialExpiresAt = tenant.TrialExpiresAt,
            isExpired,
            isNoPlan,
            isLocked,
            destination = state == "ACTIVE" || state == "PAID" ? "DASHBOARD" : state == "ELIGIBLE" ? "CONFIRM" : "SUBSCRIPTIONS"
        };
    }

    /// <summary>
    /// Lấy danh sách lịch sử mua hàng, đăng ký và gia hạn các gói dịch vụ của gian hàng
    /// </summary>
    [HttpGet("subscriptions/history")]
    public async Task<IActionResult> GetSubscriptionOrderHistory(CancellationToken cancellationToken)
    {
        var tenant = await ResolveCurrentTenantAsync(cancellationToken);
        if (tenant == null)
        {
            return NotFound(ApiResponse<IReadOnlyList<SubscriptionOrderHistoryDto>>.Fail("TENANT_NOT_FOUND", "Không tìm thấy thông tin gian hàng trong hệ thống."));
        }

        var list = new List<SubscriptionOrderHistoryDto>();

        // 1. Lấy các giao dịch mua gói dịch vụ từ WalletTransactions
        var paidOrders = await _context.WalletTransactions
            .Where(w => w.TenantId == tenant.Id && (w.Type == "PURCHASE_PLAN" || w.Type == "PURCHASE_ADDON"))
            .OrderByDescending(w => w.CreatedAt)
            .ToListAsync(cancellationToken);

        foreach (var order in paidOrders)
        {
            var isPlanActive = tenant.SubscriptionExpiresAt.HasValue && tenant.SubscriptionExpiresAt.Value >= DateTime.UtcNow;
            list.Add(new SubscriptionOrderHistoryDto(
                Code: order.TransactionCode,
                PlanName: order.Description.Replace("Thanh toán gói dịch vụ ", ""),
                StartDate: order.CreatedAt.ToString("dd/MM/yyyy"),
                EndDate: tenant.SubscriptionExpiresAt?.ToString("dd/MM/yyyy") ?? order.CreatedAt.AddYears(1).ToString("dd/MM/yyyy"),
                TotalAmount: order.Amount,
                PaymentMethod: order.PaymentMethod == "WALLET" ? "Ví MT-GRMS" : order.PaymentMethod,
                Status: isPlanActive ? "Đang hiệu lực" : "Hết hạn",
                CreatedAt: order.CreatedAt
            ));
        }

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

