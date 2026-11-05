using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Admin.Dtos;
using MT_GRMS.Application.Admin.Services;
using MT_GRMS.Application.Common;
using MT_GRMS.Infrastructure.Persistence;

namespace MT_GRMS.Api.Controllers;

[ApiController]
[Route("api/v1/tenant")]
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

    /// <summary>
    /// Lấy thông tin chi tiết gian hàng và gói cước hiện tại của khách thuê đang đăng nhập
    /// </summary>
    [HttpGet("store-info")]
    public async Task<IActionResult> GetStoreInfo(CancellationToken cancellationToken)
    {
        var tenantId = User.FindFirst("tenant_id")?.Value;

        // Nếu không có tenantId hoặc là DEFAULT, fallback lấy tenant đầu tiên trong DB
        var tenant = !string.IsNullOrWhiteSpace(tenantId) && tenantId != "DEFAULT"
            ? await _context.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId || t.Subdomain == tenantId, cancellationToken)
            : await _context.Tenants.FirstOrDefaultAsync(cancellationToken);

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
        var tenantId = User.FindFirst("tenant_id")?.Value;

        var tenant = !string.IsNullOrWhiteSpace(tenantId) && tenantId != "DEFAULT"
            ? await _context.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId || t.Subdomain == tenantId, cancellationToken)
            : await _context.Tenants.FirstOrDefaultAsync(cancellationToken);

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
        return Ok(ApiResponse<IReadOnlyList<PlanSummaryResponse>>.Ok(plans));
    }

    /// <summary>
    /// Đặt mua hoặc nâng cấp / gia hạn gói dịch vụ SaaS cho gian hàng
    /// </summary>
    [HttpPost("subscriptions/purchase")]
    public async Task<IActionResult> PurchaseSubscription(
        [FromBody] PurchaseSubscriptionRequest request,
        CancellationToken cancellationToken)
    {
        var tenantId = User.FindFirst("tenant_id")?.Value;

        var tenant = !string.IsNullOrWhiteSpace(tenantId) && tenantId != "DEFAULT"
            ? await _context.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId || t.Subdomain == tenantId, cancellationToken)
            : await _context.Tenants.FirstOrDefaultAsync(cancellationToken);

        if (tenant == null)
        {
            return NotFound(ApiResponse<PurchaseSubscriptionResponse>.Fail("TENANT_NOT_FOUND", "Không tìm thấy thông tin gian hàng trong hệ thống."));
        }

        // Tìm gói cước theo PlanCode hoặc Id
        var plan = await _context.SubscriptionPlans.FirstOrDefaultAsync(
            p => (p.PlanCode == request.PlanCode || p.Id == request.PlanCode) && p.IsActive,
            cancellationToken);

        if (plan == null)
        {
            // Fallback tìm gói bất kỳ đang active hoặc tạo gói tương ứng
            plan = await _context.SubscriptionPlans.FirstOrDefaultAsync(p => p.IsActive, cancellationToken);
            if (plan == null)
            {
                return BadRequest(ApiResponse<PurchaseSubscriptionResponse>.Fail("PLAN_NOT_FOUND", "Gói dịch vụ không hợp lệ hoặc đã tạm ngưng."));
            }
        }

        var months = request.Months <= 0 ? 12 : request.Months;

        // 1. Tính toán chi phí
        var basePrice = plan.PriceMonthly * months;
        var branchAddonPrice = request.ExtraBranches * 270000m * months;
        var warehouseAddonPrice = request.ExtraWarehouses * 150000m * months;
        var subTotal = basePrice + branchAddonPrice + warehouseAddonPrice;
        var vatAmount = Math.Round(subTotal * 0.1m, 0);
        var grandTotal = subTotal + vatAmount;

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

        // 2. Tính số tháng gia hạn (Khuyến mãi thêm tháng)
        var bonusMonths = months >= 24 ? 3 : (months >= 12 ? 1 : 0);
        var totalMonthsAdded = months + bonusMonths;

        // 3. Gia hạn thời gian sử dụng
        var baseDate = (tenant.SubscriptionExpiresAt.HasValue && tenant.SubscriptionExpiresAt.Value > DateTime.UtcNow)
            ? tenant.SubscriptionExpiresAt.Value
            : DateTime.UtcNow;

        var newExpiresAt = baseDate.AddMonths(totalMonthsAdded);

        tenant.CurrentPlan = plan.PlanCode;
        tenant.SubscriptionExpiresAt = newExpiresAt;
        tenant.Status = "ACTIVE";

        await _context.SaveChangesAsync(cancellationToken);

        var daysRemaining = (int)Math.Max(0, Math.Ceiling((newExpiresAt - DateTime.UtcNow).TotalDays));

        var response = new PurchaseSubscriptionResponse(
            PlanCode: plan.PlanCode,
            PlanName: plan.PlanName,
            SubscriptionExpiresAt: newExpiresAt,
            DaysRemaining: daysRemaining,
            TotalAmount: grandTotal,
            PaymentMethod: request.PaymentMethod,
            Message: $"Gia hạn thành công {plan.PlanName} thêm {totalMonthsAdded} tháng."
        );

        return Ok(ApiResponse<PurchaseSubscriptionResponse>.Ok(response, "Thanh toán và gia hạn gói dịch vụ thành công."));
    }
}

