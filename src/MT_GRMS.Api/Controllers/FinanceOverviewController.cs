using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.FinanceOverview;
using MT_GRMS.Application.FinanceOverview.Dtos;
using MT_GRMS.Domain.Constants;

namespace MT_GRMS.Api.Controllers;

[Route("api/finance/overview")]
[Authorize(Roles = SystemRoles.StoreOwner)]
public sealed class FinanceOverviewController : BaseApiController
{
    private readonly IFinanceOverviewService _service;

    public FinanceOverviewController(IFinanceOverviewService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<FinanceOverviewResponse>>> GetOverview(
        [FromQuery] Guid? branchId,
        [FromQuery] DateTime fromDate,
        [FromQuery] DateTime toDate,
        CancellationToken ct)
    {
        var result = await _service.GetOverviewAsync(branchId, fromDate, toDate, ct);
        return Ok(ApiResponse<FinanceOverviewResponse>.Ok(result));
    }
}
