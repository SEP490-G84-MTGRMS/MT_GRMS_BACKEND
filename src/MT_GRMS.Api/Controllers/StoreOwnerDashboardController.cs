using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Reports;
using MT_GRMS.Application.Reports.Dtos;

namespace MT_GRMS.Api.Controllers;

[ApiController]
[Route("api/store-owner/dashboard")]
[Authorize(Roles = "StoreOwner,STORE_OWNER")]
public class StoreOwnerDashboardController : BaseApiController
{
    private static readonly HashSet<string> Periods = new(StringComparer.Ordinal)
    {
        "today", "yesterday", "last7days", "thisMonth"
    };

    private readonly IStoreOwnerDashboardService _dashboard;
    private readonly ITenantContext _tenant;

    public StoreOwnerDashboardController(IStoreOwnerDashboardService dashboard, ITenantContext tenant)
    {
        _dashboard = dashboard;
        _tenant = tenant;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<StoreOwnerDashboardResponse>>> Get(
        [FromQuery] string period = "today", [FromQuery] Guid? branchId = null, CancellationToken ct = default)
    {
        if (!Periods.Contains(period))
            return BadRequest(new ProblemDetails { Status = 400, Title = "Kỳ xem không hợp lệ." });

        var tokenTenantId = User.FindFirst("tenant_id")?.Value;
        if (string.IsNullOrWhiteSpace(tokenTenantId) || tokenTenantId != _tenant.CurrentTenantId)
            return Forbid();

        var result = await _dashboard.GetAsync(period, branchId, ct);
        if (result is null) return Forbid();
        return Ok(ApiResponse<StoreOwnerDashboardResponse>.Ok(result));
    }
}
