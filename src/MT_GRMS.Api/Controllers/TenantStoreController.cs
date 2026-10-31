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
}

