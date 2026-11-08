using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Admin.Dtos;
using MT_GRMS.Application.Admin.Services;
using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Infrastructure.Persistence;

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
        var headerTenant = Request.Headers["X-Tenant-Id"].FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(headerTenant) && headerTenant != "DEFAULT" && headerTenant != "tenant_mt_mart_01")
        {
            tenantId = headerTenant;
        }
        else if (string.IsNullOrWhiteSpace(tenantId) || tenantId == "DEFAULT")
        {
            if (!string.IsNullOrWhiteSpace(headerTenant) && headerTenant != "DEFAULT")
            {
                tenantId = headerTenant;
            }
        }

        return !string.IsNullOrWhiteSpace(tenantId) && tenantId != "DEFAULT"
            ? await _context.Tenants.Include(t => t.SubscriptionPlan).FirstOrDefaultAsync(t => t.Id == tenantId || t.Subdomain == tenantId, cancellationToken)
            : await _context.Tenants.Include(t => t.SubscriptionPlan).FirstOrDefaultAsync(cancellationToken);
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

        // Chỉ lấy 2 gói cước chính thức của hệ thống SaaS bán lẻ: STANDARD (Gói Thường 99k) và PRO (Gói Pro 249k)
        var officialPlans = plans
            .Where(p => string.Equals(p.Code, "STANDARD", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(p.Code, "PRO", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.MonthlyPrice)
            .ToList();

        if (officialPlans.Count == 0)
        {
            var defaultStandard = new PlanSummaryResponse(
                Id: "plan_standard",
                Code: "STANDARD",
                Name: "Gói Thường (Cửa Hàng Độc Lập)",
                Description: "Dành cho tiệm tạp hóa, siêu thị mini độc lập (1 điểm bán kiêm kho tại chỗ).",
                MonthlyPrice: 99000m,
                YearlyPrice: 1188000m,
                Status: "ACTIVE",
                ActiveTenantCount: 0,
                Quotas: new PlanQuotaDto(MaxBranches: 1, MaxEmployees: 1, MaxStorageGb: 5, BackupFrequency: "Daily"),
                Features: new PlanFeatureDto(FefoExpiryAlert: true, MultiUnitConversion: true, VietQrDynamic: true, DebtZaloBook: false, BarcodeScanning: true, FinancialReporting: false),
                CreatedAt: DateTime.UtcNow,
                UpdatedAt: null
            );

            var defaultPro = new PlanSummaryResponse(
                Id: "plan_pro",
                Code: "PRO",
                Name: "Gói Pro (Chuỗi Cửa Hàng)",
                Description: "Dành cho chuỗi siêu thị bán lẻ, chuyên môn hóa quy trình và điều chuyển kho chuỗi.",
                MonthlyPrice: 249000m,
                YearlyPrice: 2988000m,
                Status: "ACTIVE",
                ActiveTenantCount: 0,
                Quotas: new PlanQuotaDto(MaxBranches: 2, MaxEmployees: 100, MaxStorageGb: 50, BackupFrequency: "Hourly"),
                Features: new PlanFeatureDto(FefoExpiryAlert: true, MultiUnitConversion: true, VietQrDynamic: true, DebtZaloBook: true, BarcodeScanning: true, FinancialReporting: true),
                CreatedAt: DateTime.UtcNow,
                UpdatedAt: null
            );

            officialPlans = new List<PlanSummaryResponse> { defaultStandard, defaultPro };
        }

        return Ok(ApiResponse<IReadOnlyList<PlanSummaryResponse>>.Ok(officialPlans));
    }

    /// <summary>
    /// Đặt mua hoặc nâng cấp / gia hạn gói dịch vụ SaaS cho gian hàng
    /// </summary>
    [HttpPost("subscriptions/purchase")]
    public async Task<IActionResult> PurchaseSubscription(
        [FromBody] PurchaseSubscriptionRequest request,
        CancellationToken cancellationToken)
    {
        var tenant = await ResolveCurrentTenantAsync(cancellationToken);
        if (tenant == null)
        {
            return NotFound(ApiResponse<PurchaseSubscriptionResponse>.Fail("TENANT_NOT_FOUND", "Không tìm thấy thông tin gian hàng trong hệ thống."));
        }

        // Chuẩn hóa mã gói: chỉ chấp nhận STANDARD hoặc PRO
        var targetCode = (request.PlanCode ?? "").ToUpper();
        if (targetCode != "STANDARD" && targetCode != "PRO")
        {
            targetCode = targetCode.Contains("PRO") ? "PRO" : "STANDARD";
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
            await _context.SubscriptionPlans.AddAsync(plan, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
        }

        var isTrial = request.Months == 0 || string.Equals(request.PaymentMethod, "TRIAL", StringComparison.OrdinalIgnoreCase);
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
            var subTotal = basePrice + branchAddonPrice + staffAddonPrice;
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
                    TransactionCode = $"SUB-{DateTime.UtcNow:yyyyMMdd}-{Random.Shared.Next(1000, 9999)}",
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
            tenant.CurrentPlan = "PRO";
            tenant.SubscriptionStatus = "TRIAL";
            tenant.SubscriptionExpiresAt = newExpiresAt;
            tenant.HasStaffManagementAddon = true; // Gói Pro dùng thử mở khóa full nhân viên
            tenant.Status = "ACTIVE";
            resultMessage = "Kích hoạt thành công 3 ngày dùng thử miễn phí Full tính năng Gói Pro.";
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

    /// <summary>
    /// Kích hoạt gói dùng thử 3 ngày miễn phí Full tính năng Gói Pro cho gian hàng
    /// </summary>
    [HttpPost("subscriptions/trial")]
    public async Task<IActionResult> ActivateTrialSubscription(CancellationToken cancellationToken)
    {
        return await PurchaseSubscription(new PurchaseSubscriptionRequest(
            PlanCode: "PRO",
            Months: 0,
            ExtraBranches: 0,
            ExtraWarehouses: 0,
            PaymentMethod: "TRIAL",
            HasStaffManagementAddon: true
        ), cancellationToken);
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

        // 2. Luôn có bản ghi gói khởi tạo ban đầu (Gói miễn phí / dùng thử khi đăng ký tài khoản)
        var initPlanName = tenant.SubscriptionPlan?.PlanName ?? (tenant.CurrentPlan == "FREE" ? "Gói Cơ Bản (Miễn Phí) 01" : "Gói Dùng Thử Trải Nghiệm");
        var initEndDate = tenant.SubscriptionExpiresAt ?? tenant.CreatedAt.AddYears(1);
        var isInitActive = list.Count == 0 && initEndDate >= DateTime.UtcNow;

        list.Add(new SubscriptionOrderHistoryDto(
            Code: $"SUB-{tenant.CreatedAt:yyyyMMdd}-001",
            PlanName: initPlanName,
            StartDate: tenant.CreatedAt.ToString("dd/MM/yyyy"),
            EndDate: initEndDate.ToString("dd/MM/yyyy"),
            TotalAmount: 0m,
            PaymentMethod: "Gói hệ thống cấp",
            Status: isInitActive ? "Đang hiệu lực" : "Hết hạn",
            CreatedAt: tenant.CreatedAt
        ));

        return Ok(ApiResponse<IReadOnlyList<SubscriptionOrderHistoryDto>>.Ok(list));
    }
}

