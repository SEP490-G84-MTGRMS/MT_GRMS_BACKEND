using Microsoft.Extensions.Configuration;
using MT_GRMS.Application.Reports.Dtos;
using MT_GRMS.Application.Tax;

namespace MT_GRMS.Application.Reports;

public class PnLReportService : IPnLReportService
{
    private readonly IPnLReportRepository _pnlRepo;
    private readonly MT_GRMS.Application.Common.ITenantContext _tenantContext;
    private readonly TaxPolicy _taxPolicy;

    public PnLReportService(
        IPnLReportRepository pnlRepo,
        MT_GRMS.Application.Common.ITenantContext tenantContext,
        IConfiguration? configuration = null)
    {
        _pnlRepo = pnlRepo;
        _tenantContext = tenantContext;
        _taxPolicy = TaxPolicy.Load(configuration);
    }

    public async Task<PnLSummaryResponse> GetPnLSummaryAsync(PnLFilterRequest request, CancellationToken ct = default)
    {
        RequireOwner();
        string branchName;
        if (request.BranchId.HasValue)
        {
            branchName = await _pnlRepo.GetBranchNameAsync(request.BranchId.Value, ct)
                ?? throw new MT_GRMS.Domain.Exceptions.NotFoundException($"Không tìm thấy chi nhánh với mã {request.BranchId.Value}.");
        }
        else
        {
            branchName = "Toàn chuỗi (Consolidated)";
        }

        var revenue = await _pnlRepo.GetRevenueAsync(request.BranchId, request.FromDate, request.ToDate, ct);
        var cogsData = await _pnlRepo.GetCogsAsync(request.BranchId, request.FromDate, request.ToDate, ct);
        var expenseItems = await _pnlRepo.GetOperatingExpensesAsync(request.BranchId, request.FromDate, request.ToDate, ct);
        var topProductItems = await _pnlRepo.GetTopProductsAsync(request.BranchId, request.FromDate, request.ToDate, 10, ct);
        var shrinkageLoss = await _pnlRepo.GetShrinkageLossAsync(request.BranchId, request.FromDate, request.ToDate, ct);

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

        var netProfit = grossProfit - totalOpex - shrinkageLoss;
        var netMarginPct = netRevenue > 0
            ? Math.Round((netProfit / netRevenue) * 100m, 2, MidpointRounding.AwayFromZero)
            : 0m;

        var estimatedTaxAmount = await CalculatePolicyDrivenEstimatedTaxAsync(request.ToDate, netRevenue, ct);

        var totalOrders = revenue.TotalOrders;
        var aov = totalOrders > 0 ? Math.Round(netRevenue / totalOrders, 0, MidpointRounding.AwayFromZero) : 0m;

        var weightedAvgCogs = cogsData.WeightedAverageCogs > 0 ? cogsData.WeightedAverageCogs : cogs;
        var fefoSpecificCogs = cogsData.FefoSpecificCogs > 0 ? cogsData.FefoSpecificCogs : cogs;
        var cogsVariance = fefoSpecificCogs - weightedAvgCogs;
        var weightedGp = netRevenue - weightedAvgCogs;
        var fefoGp = netRevenue - fefoSpecificCogs;
        var weightedGpMarginPct = netRevenue > 0 ? Math.Round((weightedGp / netRevenue) * 100m, 2, MidpointRounding.AwayFromZero) : 0m;
        var fefoGpMarginPct = netRevenue > 0 ? Math.Round((fefoGp / netRevenue) * 100m, 2, MidpointRounding.AwayFromZero) : 0m;
        var weightedNp = weightedGp - totalOpex - shrinkageLoss;
        var fefoNp = fefoGp - totalOpex - shrinkageLoss;

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
            netProfit,
            netMarginPct,
            cogsData.Source,
            cogsData.EstimatedPercentage,
            cogsData.WarningNotice,
            totalOrders,
            aov,
            cogsComparison,
            topProducts,
            shrinkageLoss,
            estimatedTaxAmount
        );
    }

    public async Task<OperatingExpenseSummary> GetExpenseBreakdownAsync(PnLFilterRequest request, CancellationToken ct = default)
    {
        RequireOwner();
        var summary = await GetPnLSummaryAsync(request, ct);
        return summary.OperatingExpenses;
    }

    public async Task<PnLComparisonResponse> GetComparisonAsync(PnLFilterRequest request, CancellationToken ct = default)
    {
        RequireOwner();
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
        RequireOwner();
        if (request.BranchId.HasValue)
        {
            _ = await _pnlRepo.GetBranchNameAsync(request.BranchId.Value, ct)
                ?? throw new MT_GRMS.Domain.Exceptions.NotFoundException($"Không tìm thấy chi nhánh với mã {request.BranchId.Value}.");
        }

        // Tối ưu hóa: Thay thế vòng lặp N+1 queries bằng 1 lần gom toàn kỳ qua GetDailyTrendAsync
        var dailyItems = await _pnlRepo.GetDailyTrendAsync(request.BranchId, request.FromDate, request.ToDate, ct);

        return dailyItems.Select(d => new PnLTrendPointResponse(
            d.Date,
            d.NetRevenue,
            d.Cogs,
            d.Expenses,
            d.NetRevenue - d.Cogs - d.Expenses
        )).ToList();
    }

    /// <summary>
    /// Computes policy-driven estimated retail household tax (Decree 68/2026 &amp; Decree 398/2026):
    /// 0 VND when annual YTD net revenue &lt;= 1B; otherwise VAT on full period revenue + PIT on excess over 1B (with 30% PIT relief when &lt;= 10B in 2026-2027).
    /// </summary>
    private async Task<decimal> CalculatePolicyDrivenEstimatedTaxAsync(DateTime periodToDate, decimal periodNetRevenue, CancellationToken ct)
    {
        if (periodNetRevenue <= 0m) return 0m;

        var vnTz = HouseholdTaxService.GetVietnamTimeZone();
        var toDateUtc = periodToDate.Kind == DateTimeKind.Local
            ? periodToDate.ToUniversalTime()
            : DateTime.SpecifyKind(periodToDate, DateTimeKind.Utc);
        var toDateVn = TimeZoneInfo.ConvertTimeFromUtc(toDateUtc, vnTz);
        var periodYear = toDateVn.Year;
        var startOfYearUtc = TimeZoneInfo.ConvertTimeToUtc(
            new DateTime(periodYear, 1, 1, 0, 0, 0, DateTimeKind.Unspecified),
            vnTz);

        var ytdRevenue = await _pnlRepo.GetRevenueAsync(null, startOfYearUtc, toDateUtc, ct);
        var ytdNetRevenue = Math.Max(
            periodNetRevenue,
            Math.Max(0m, ytdRevenue.GrossRevenue - ytdRevenue.Discounts - ytdRevenue.SalesReturns));

        if (ytdNetRevenue <= _taxPolicy.ExemptionThreshold) return 0m;

        var vatAmount = Math.Round(
            periodNetRevenue * (HouseholdTaxService.RetailVatRate / 100m),
            0,
            MidpointRounding.AwayFromZero);

        var pitTaxableInPeriod = Math.Min(
            periodNetRevenue,
            Math.Max(0m, ytdNetRevenue - _taxPolicy.ExemptionThreshold));
        var pitBeforeReduction = Math.Round(
            pitTaxableInPeriod * (HouseholdTaxService.RetailPitRate / 100m),
            0,
            MidpointRounding.AwayFromZero);
        var pitReduction = _taxPolicy.IsPitReductionEligible(periodYear, ytdNetRevenue)
            ? Math.Round(
                pitBeforeReduction * (_taxPolicy.EffectivePitReductionPercentage / 100m),
                0,
                MidpointRounding.AwayFromZero)
            : 0m;
        var pitAmount = Math.Max(0m, pitBeforeReduction - pitReduction);

        return vatAmount + pitAmount;
    }

    private void RequireOwner() => MT_GRMS.Application.Common.TenantRoleGuard.RequireStoreOwner(_tenantContext, "báo cáo lãi lỗ");
}
