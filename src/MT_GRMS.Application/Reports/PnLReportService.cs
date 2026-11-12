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
        var topProductItems = await _pnlRepo.GetTopProductsAsync(request.BranchId, request.FromDate, request.ToDate, 10, ct);

        var netRevenue = Math.Max(0, revenue.GrossRevenue - revenue.Discounts - revenue.SalesReturns);
        var cogs = cogsData.TotalCogs;
        var grossProfit = netRevenue - cogs;
        var grossMarginPct = netRevenue > 0
            ? Math.Round((grossProfit / netRevenue) * 100m, 2, MidpointRounding.AwayFromZero)
            : 0m;

        var totalOpex = expenseItems.Sum(x => x.Amount);
        var categories = expenseItems.Select(e => new CategoryExpenseBreakdown(
            e.Category,
            e.Amount,
            netRevenue > 0 ? Math.Round((e.Amount / netRevenue) * 100m, 2, MidpointRounding.AwayFromZero) : 0m
        )).ToList();

        var netProfit = grossProfit - totalOpex - badDebt;
        var netMarginPct = netRevenue > 0
            ? Math.Round((netProfit / netRevenue) * 100m, 2, MidpointRounding.AwayFromZero)
            : 0m;

        var totalOrders = revenue.TotalOrders;
        var aov = totalOrders > 0 ? Math.Round(netRevenue / totalOrders, 0, MidpointRounding.AwayFromZero) : 0m;

        var weightedAvgCogs = cogsData.WeightedAverageCogs > 0 ? cogsData.WeightedAverageCogs : cogs;
        var fefoSpecificCogs = cogsData.FefoSpecificCogs > 0 ? cogsData.FefoSpecificCogs : cogs;
        var cogsVariance = fefoSpecificCogs - weightedAvgCogs;
        var weightedGp = netRevenue - weightedAvgCogs;
        var fefoGp = netRevenue - fefoSpecificCogs;
        var weightedGpMarginPct = netRevenue > 0 ? Math.Round((weightedGp / netRevenue) * 100m, 2, MidpointRounding.AwayFromZero) : 0m;
        var fefoGpMarginPct = netRevenue > 0 ? Math.Round((fefoGp / netRevenue) * 100m, 2, MidpointRounding.AwayFromZero) : 0m;
        var weightedNp = weightedGp - totalOpex - badDebt;
        var fefoNp = fefoGp - totalOpex - badDebt;

        var cogsComparison = new CogsValuationComparisonDto(
            weightedAvgCogs,
            fefoSpecificCogs,
            cogsVariance,
            weightedGp,
            fefoGp,
            weightedGpMarginPct,
            fefoGpMarginPct,
            weightedNp,
            fefoNp,
            "WEIGHTED_AVERAGE_TT152"
        );

        var topProducts = topProductItems.Select(p => new TopProductPnLDto(
            p.ProductId,
            p.ProductCode,
            p.ProductName,
            p.QuantitySold,
            p.Revenue,
            p.Cogs,
            p.Profit,
            p.MarginPct
        )).ToList();

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
            netMarginPct,
            cogsData.Source,
            cogsData.EstimatedPercentage,
            cogsData.WarningNotice,
            totalOrders,
            aov,
            cogsComparison,
            topProducts
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
            : (currentSummary.NetRevenue > 0 ? 100m : 0m);

        var gpGrowth = prevSummary.GrossProfit > 0
            ? Math.Round(((currentSummary.GrossProfit - prevSummary.GrossProfit) / prevSummary.GrossProfit) * 100m, 2, MidpointRounding.AwayFromZero)
            : (currentSummary.GrossProfit > 0 ? 100m : 0m);

        var npGrowth = prevSummary.NetProfit > 0
            ? Math.Round(((currentSummary.NetProfit - prevSummary.NetProfit) / prevSummary.NetProfit) * 100m, 2, MidpointRounding.AwayFromZero)
            : (currentSummary.NetProfit > 0 ? 100m : 0m);

        return new PnLComparisonResponse(currentSummary, prevSummary, revGrowth, gpGrowth, npGrowth);
    }

    public async Task<List<PnLTrendPointResponse>> GetTrendAsync(PnLFilterRequest request, CancellationToken ct = default)
    {
        // Tối ưu hóa: Thay thế vòng lặp N+1 queries bằng 1 lần gom toàn kỳ qua GetDailyTrendAsync
        var dailyItems = await _pnlRepo.GetDailyTrendAsync(request.BranchId, request.FromDate, request.ToDate, ct);

        return dailyItems.Select(d => new PnLTrendPointResponse(
            d.Date,
            d.NetRevenue,
            d.Cogs,
            d.Expenses,
            d.NetRevenue - d.Cogs - d.Expenses - d.BadDebt
        )).ToList();
    }
}
