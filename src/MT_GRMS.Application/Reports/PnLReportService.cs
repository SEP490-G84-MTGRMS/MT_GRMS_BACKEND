using MT_GRMS.Application.Reports.Dtos;

namespace MT_GRMS.Application.Reports;

public class PnLReportService : IPnLReportService
{
    private readonly IPnLReportRepository _pnlRepo;

    public PnLReportService(IPnLReportRepository pnlRepo)
    {
        _pnlRepo = pnlRepo;
    }

    public async Task<PnLSummaryResponse> GetPnLSummaryAsync(PnLFilterRequest request, CancellationToken ct = default)
    {
        var revenue = await _pnlRepo.GetRevenueAsync(request.BranchId, request.FromDate, request.ToDate, ct);
        var cogsData = await _pnlRepo.GetCogsAsync(request.BranchId, request.FromDate, request.ToDate, ct);
        var expenseItems = await _pnlRepo.GetOperatingExpensesAsync(request.BranchId, request.FromDate, request.ToDate, ct);
        var badDebt = await _pnlRepo.GetBadDebtExpenseAsync(request.BranchId, request.FromDate, request.ToDate, ct);

        var netRevenue = Math.Max(0, revenue.GrossRevenue - revenue.Discounts - revenue.SalesReturns);
        var cogs = cogsData.TotalCogs;
        var grossProfit = netRevenue - cogs;
        var grossMarginPct = netRevenue > 0
            ? Math.Round((grossProfit / netRevenue) * 100m, 2, MidpointRounding.AwayFromZero)
            : 0;

        var totalOpex = expenseItems.Sum(x => x.Amount);
        var categories = expenseItems.Select(e => new CategoryExpenseBreakdown(
            e.Category,
            e.Amount,
            netRevenue > 0 ? Math.Round((e.Amount / netRevenue) * 100m, 2, MidpointRounding.AwayFromZero) : 0
        )).ToList();

        var netProfit = grossProfit - totalOpex - badDebt;
        var netMarginPct = netRevenue > 0
            ? Math.Round((netProfit / netRevenue) * 100m, 2, MidpointRounding.AwayFromZero)
            : 0;

        var branchName = request.BranchId.HasValue ? $"Chi nhánh {request.BranchId.Value.ToString()[..8]}" : "Toàn chuỗi (Consolidated)";

        return new PnLSummaryResponse(
            new PnLPeriod(request.FromDate, request.ToDate),
            request.BranchId,
            branchName,
            revenue.GrossRevenue,
            revenue.Discounts,
            revenue.SalesReturns,
            netRevenue,
            cogs,
            grossProfit,
            grossMarginPct,
            new OperatingExpenseSummary(totalOpex, categories),
            badDebt,
            netProfit,
            netMarginPct
        );
    }

    public async Task<OperatingExpenseSummary> GetExpenseBreakdownAsync(PnLFilterRequest request, CancellationToken ct = default)
    {
        var summary = await GetPnLSummaryAsync(request, ct);
        return summary.OperatingExpenses;
    }

    public async Task<PnLComparisonResponse> GetComparisonAsync(PnLFilterRequest request, CancellationToken ct = default)
    {
        var currentSummary = await GetPnLSummaryAsync(request, ct);

        // Tính toán khoảng thời gian kỳ trước có độ dài tương đương
        var duration = request.ToDate - request.FromDate;
        var prevToDate = request.FromDate.AddSeconds(-1);
        var prevFromDate = prevToDate - duration;

        var prevSummary = await GetPnLSummaryAsync(new PnLFilterRequest(request.BranchId, prevFromDate, prevToDate), ct);

        var revGrowth = prevSummary.NetRevenue > 0
            ? Math.Round(((currentSummary.NetRevenue - prevSummary.NetRevenue) / prevSummary.NetRevenue) * 100m, 2, MidpointRounding.AwayFromZero)
            : (currentSummary.NetRevenue > 0 ? 100m : 0);

        var gpGrowth = prevSummary.GrossProfit > 0
            ? Math.Round(((currentSummary.GrossProfit - prevSummary.GrossProfit) / prevSummary.GrossProfit) * 100m, 2, MidpointRounding.AwayFromZero)
            : (currentSummary.GrossProfit > 0 ? 100m : 0);

        var npGrowth = prevSummary.NetProfit > 0
            ? Math.Round(((currentSummary.NetProfit - prevSummary.NetProfit) / prevSummary.NetProfit) * 100m, 2, MidpointRounding.AwayFromZero)
            : (currentSummary.NetProfit > 0 ? 100m : 0);

        return new PnLComparisonResponse(currentSummary, prevSummary, revGrowth, gpGrowth, npGrowth);
    }

    public async Task<List<PnLTrendPointResponse>> GetTrendAsync(PnLFilterRequest request, CancellationToken ct = default)
    {
        var trendPoints = new List<PnLTrendPointResponse>();
        var currentStart = request.FromDate.Date;
        var end = request.ToDate.Date;

        // Nếu khoảng thời gian <= 60 ngày thì phân tích theo từng ngày
        while (currentStart <= end)
        {
            var dayEnd = currentStart.AddDays(1).AddSeconds(-1);
            var daySummary = await GetPnLSummaryAsync(new PnLFilterRequest(request.BranchId, currentStart, dayEnd), ct);

            trendPoints.Add(new PnLTrendPointResponse(
                currentStart,
                daySummary.NetRevenue,
                daySummary.Cogs,
                daySummary.OperatingExpenses.TotalAmount,
                daySummary.NetProfit
            ));

            currentStart = currentStart.AddDays(1);
        }

        return trendPoints;
    }
}
