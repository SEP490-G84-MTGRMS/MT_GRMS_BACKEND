using Microsoft.AspNetCore.Mvc;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Reports;
using MT_GRMS.Application.Reports.Dtos;

namespace MT_GRMS.Api.Controllers;

[Route("api/finance/reports/pnl")]
public class PnLReportController : BaseApiController
{
    private readonly IPnLReportService _pnlService;

    public PnLReportController(IPnLReportService pnlService)
    {
        _pnlService = pnlService;
    }

    [HttpGet("summary")]
    public async Task<ActionResult<ApiResponse<PnLSummaryResponse>>> GetSummary(
        [FromQuery] Guid? branchId,
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate,
        CancellationToken ct)
    {
        var from = fromDate ?? new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = toDate ?? DateTime.UtcNow;

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
        var from = fromDate ?? new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = toDate ?? DateTime.UtcNow;

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
        var from = fromDate ?? new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = toDate ?? DateTime.UtcNow;

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
        var from = fromDate ?? DateTime.UtcNow.AddDays(-30);
        var to = toDate ?? DateTime.UtcNow;

        var result = await _pnlService.GetTrendAsync(new PnLFilterRequest(branchId, from, to), ct);
        return Ok(ApiResponse<List<PnLTrendPointResponse>>.Ok(result));
    }
}
