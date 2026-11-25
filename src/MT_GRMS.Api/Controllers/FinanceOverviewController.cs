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
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        CancellationToken ct = default)
    {
        var nowUtc = DateTime.UtcNow;
        var nowVn = nowUtc.AddHours(7);
        var defaultFromUtc = new DateTime(nowVn.Year, nowVn.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddHours(-7);

        var resolvedFrom = NormalizeUtc(fromDate, defaultFromUtc, endOfDayIfDateOnly: false);
        var resolvedTo = NormalizeUtc(toDate, nowUtc, endOfDayIfDateOnly: true);

        var result = await _service.GetOverviewAsync(branchId, resolvedFrom, resolvedTo, ct);
        return Ok(ApiResponse<FinanceOverviewResponse>.Ok(result));
    }

    private static DateTime NormalizeUtc(DateTime? input, DateTime fallbackUtc, bool endOfDayIfDateOnly)
    {
        if (!input.HasValue || input.Value == default)
            return fallbackUtc;

        var dt = input.Value;
        if (dt.Kind == DateTimeKind.Utc)
            return dt;
        if (dt.Kind == DateTimeKind.Local)
            return dt.ToUniversalTime();

        var vnDt = endOfDayIfDateOnly && dt.TimeOfDay == TimeSpan.Zero
            ? dt.Date.AddDays(1).AddTicks(-1)
            : dt;
        return DateTime.SpecifyKind(vnDt.AddHours(-7), DateTimeKind.Utc);
    }
}
