using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Reports;
using MT_GRMS.Application.Reports.Dtos;
using MT_GRMS.Application.Tax;
using MT_GRMS.Domain.Constants;

namespace MT_GRMS.Api.Controllers;

[Route("api/finance/reports/pnl")]
[Authorize(Roles = RoleConstants.StoreOwnerOrAdmin)]
public class PnLReportController : BaseApiController
{
    private readonly IPnLReportService _pnlService;

    public PnLReportController(IPnLReportService pnlService)
    {
        _pnlService = pnlService;
    }

    private static (DateTime FromUtc, DateTime ToUtc) ResolveDateRange(DateTime? fromDate, DateTime? toDate, int defaultDaysIfNull = 0)
    {
        var vnTz = HouseholdTaxService.GetVietnamTimeZone();
        var nowVn = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, vnTz);

        DateTime fromUtc;
        if (fromDate.HasValue)
        {
            fromUtc = fromDate.Value.Kind == DateTimeKind.Utc
                ? fromDate.Value
                : DateTime.SpecifyKind(fromDate.Value, DateTimeKind.Utc);
        }
        else if (defaultDaysIfNull > 0)
        {
            var startVn = nowVn.Date.AddDays(-defaultDaysIfNull);
            fromUtc = TimeZoneInfo.ConvertTimeToUtc(new DateTime(startVn.Year, startVn.Month, startVn.Day, 0, 0, 0, DateTimeKind.Unspecified), vnTz);
        }
        else
        {
            var startOfMonthVn = new DateTime(nowVn.Year, nowVn.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);
            fromUtc = TimeZoneInfo.ConvertTimeToUtc(startOfMonthVn, vnTz);
        }

        DateTime toUtc = toDate.HasValue
            ? (toDate.Value.Kind == DateTimeKind.Utc ? toDate.Value : DateTime.SpecifyKind(toDate.Value, DateTimeKind.Utc))
            : DateTime.UtcNow;

        return (fromUtc, toUtc);
    }

    [HttpGet("summary")]
    public async Task<ActionResult<ApiResponse<PnLSummaryResponse>>> GetSummary(
        [FromQuery] Guid? branchId,
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate,
        CancellationToken ct)
    {
        var (from, to) = ResolveDateRange(fromDate, toDate);
        var result = await _pnlService.GetPnLSummaryAsync(new PnLFilterRequest(branchId, from, to), ct);
        return Ok(ApiResponse<PnLSummaryResponse>.Ok(result));
    }

    [HttpGet("breakdown/expenses")]
    public async Task<ActionResult<ApiResponse<OperatingExpenseSummary>>> GetExpenseBreakdown(
        [FromQuery] Guid? branchId,
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate,
        CancellationToken ct)
    {
        var (from, to) = ResolveDateRange(fromDate, toDate);
        var result = await _pnlService.GetExpenseBreakdownAsync(new PnLFilterRequest(branchId, from, to), ct);
        return Ok(ApiResponse<OperatingExpenseSummary>.Ok(result));
    }

    [HttpGet("comparison")]
    public async Task<ActionResult<ApiResponse<PnLComparisonResponse>>> GetComparison(
        [FromQuery] Guid? branchId,
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate,
        CancellationToken ct)
    {
        var (from, to) = ResolveDateRange(fromDate, toDate);
        var result = await _pnlService.GetComparisonAsync(new PnLFilterRequest(branchId, from, to), ct);
        return Ok(ApiResponse<PnLComparisonResponse>.Ok(result));
    }

    [HttpGet("trend")]
    public async Task<ActionResult<ApiResponse<List<PnLTrendPointResponse>>>> GetTrend(
        [FromQuery] Guid? branchId,
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate,
        CancellationToken ct)
    {
        var (from, to) = ResolveDateRange(fromDate, toDate, defaultDaysIfNull: 30);
        var result = await _pnlService.GetTrendAsync(new PnLFilterRequest(branchId, from, to), ct);
        return Ok(ApiResponse<List<PnLTrendPointResponse>>.Ok(result));
    }
}
