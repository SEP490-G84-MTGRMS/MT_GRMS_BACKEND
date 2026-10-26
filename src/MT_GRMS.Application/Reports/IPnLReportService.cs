using MT_GRMS.Application.Reports.Dtos;

namespace MT_GRMS.Application.Reports;

public interface IPnLReportService
{
    Task<PnLSummaryResponse> GetPnLSummaryAsync(PnLFilterRequest request, CancellationToken ct = default);
    Task<OperatingExpenseSummary> GetExpenseBreakdownAsync(PnLFilterRequest request, CancellationToken ct = default);
    Task<PnLComparisonResponse> GetComparisonAsync(PnLFilterRequest request, CancellationToken ct = default);
    Task<List<PnLTrendPointResponse>> GetTrendAsync(PnLFilterRequest request, CancellationToken ct = default);
}
