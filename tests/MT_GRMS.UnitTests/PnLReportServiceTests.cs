using MT_GRMS.Application.Reports;
using MT_GRMS.Application.Reports.Dtos;
using Xunit;

namespace MT_GRMS.UnitTests;

public class PnLReportServiceTests
{
    private class FakePnLReportRepository : IPnLReportRepository
    {
        public RevenueAggregation Revenue { get; set; } = new(100_000_000m, 5_000_000m, 2_000_000m);
        public CogsAggregation Cogs { get; set; } = new(60_000_000m);
        public List<CategoryExpenseItem> Expenses { get; set; } = new()
        {
            new("Mặt bằng", 6_000_000m),
            new("Điện nước", 4_000_000m)
        };
        public decimal BadDebt { get; set; } = 1_000_000m;

        public Task<RevenueAggregation> GetRevenueAsync(Guid? branchId, DateTime fromDate, DateTime toDate, CancellationToken ct = default) =>
            Task.FromResult(Revenue);

        public Task<CogsAggregation> GetCogsAsync(Guid? branchId, DateTime fromDate, DateTime toDate, CancellationToken ct = default) =>
            Task.FromResult(Cogs);

        public Task<List<CategoryExpenseItem>> GetOperatingExpensesAsync(Guid? branchId, DateTime fromDate, DateTime toDate, CancellationToken ct = default) =>
            Task.FromResult(Expenses);

        public Task<decimal> GetBadDebtExpenseAsync(Guid? branchId, DateTime fromDate, DateTime toDate, CancellationToken ct = default) =>
            Task.FromResult(BadDebt);
    }

    private readonly FakePnLReportRepository _fakeRepo = new();
    private readonly PnLReportService _service;

    public PnLReportServiceTests()
    {
        _service = new PnLReportService(_fakeRepo);
    }

    [Fact]
    public async Task GetPnLSummaryAsync_StandardCase_ShouldCalculateCorrectMetrics()
    {
        // GIVEN
        var request = new PnLFilterRequest(null, new DateTime(2026, 10, 1), new DateTime(2026, 10, 31));

        // WHEN
        var response = await _service.GetPnLSummaryAsync(request);

        // THEN
        // NetRevenue = 100tr - 5tr - 2tr = 93tr
        Assert.Equal(93_000_000m, response.NetRevenue);
        // COGS = 60tr
        Assert.Equal(60_000_000m, response.Cogs);
        // GrossProfit = 93tr - 60tr = 33tr
        Assert.Equal(33_000_000m, response.GrossProfit);
        // GrossMargin % = (33tr / 93tr) * 100 = 35.48%
        Assert.Equal(35.48m, response.GrossMarginPercentage);

        // OPEX = 6tr + 4tr = 10tr
        Assert.Equal(10_000_000m, response.OperatingExpenses.TotalAmount);
        Assert.Equal(2, response.OperatingExpenses.Categories.Count);

        // NetProfit = 33tr - 10tr (OPEX) - 1tr (BadDebt) = 22tr
        Assert.Equal(22_000_000m, response.NetProfit);
        // NetMargin % = (22tr / 93tr) * 100 = 23.66%
        Assert.Equal(23.66m, response.NetMarginPercentage);

        Assert.Equal("Toàn chuỗi (Consolidated)", response.BranchName);
    }

    [Fact]
    public async Task GetPnLSummaryAsync_WhenNetRevenueIsZero_ShouldReturnZeroMarginsWithoutDividingByZero()
    {
        // GIVEN: Không có đơn hàng bán ra
        _fakeRepo.Revenue = new RevenueAggregation(0, 0, 0);
        _fakeRepo.Cogs = new CogsAggregation(0);
        _fakeRepo.Expenses = new List<CategoryExpenseItem> { new("Điện", 500_000m) };
        _fakeRepo.BadDebt = 0;

        var request = new PnLFilterRequest(null, new DateTime(2026, 10, 1), new DateTime(2026, 10, 31));

        // WHEN
        var response = await _service.GetPnLSummaryAsync(request);

        // THEN
        Assert.Equal(0, response.NetRevenue);
        Assert.Equal(0, response.GrossMarginPercentage);
        Assert.Equal(0, response.NetMarginPercentage);
        Assert.Equal(-500_000m, response.NetProfit); // Lỗ 500k chi phí điện
    }

    [Fact]
    public async Task GetPnLSummaryAsync_WithSpecificBranch_ShouldIncludeBranchName()
    {
        var branchId = Guid.NewGuid();
        var request = new PnLFilterRequest(branchId, new DateTime(2026, 10, 1), new DateTime(2026, 10, 31));

        var response = await _service.GetPnLSummaryAsync(request);

        Assert.Equal(branchId, response.BranchId);
        Assert.Contains(branchId.ToString()[..8], response.BranchName);
    }

    [Fact]
    public async Task GetExpenseBreakdownAsync_ShouldReturnCategoryRatiosCorrectly()
    {
        var request = new PnLFilterRequest(null, new DateTime(2026, 10, 1), new DateTime(2026, 10, 31));

        var breakdown = await _service.GetExpenseBreakdownAsync(request);

        Assert.Equal(10_000_000m, breakdown.TotalAmount);
        var rent = breakdown.Categories.First(c => c.Category == "Mặt bằng");
        // (6tr / 93tr) * 100 = 6.45%
        Assert.Equal(6.45m, rent.PercentageOfRevenue);
    }

    [Fact]
    public async Task GetComparisonAsync_ShouldCalculateGrowthPercentages()
    {
        var request = new PnLFilterRequest(null, new DateTime(2026, 10, 1), new DateTime(2026, 10, 10));

        var comparison = await _service.GetComparisonAsync(request);

        Assert.NotNull(comparison.CurrentPeriod);
        Assert.NotNull(comparison.PreviousPeriod);
        Assert.Equal(0, comparison.RevenueGrowthPercentage); // Vì repo trả về hằng số giống nhau nên tăng trưởng = 0%
    }

    [Fact]
    public async Task GetTrendAsync_ShouldReturnDataPointsForDateRange()
    {
        var from = new DateTime(2026, 10, 1);
        var to = new DateTime(2026, 10, 3);
        var request = new PnLFilterRequest(null, from, to);

        var trend = await _service.GetTrendAsync(request);

        Assert.Equal(3, trend.Count); // 3 ngày: mùng 1, 2, 3
        Assert.Equal(from.Date, trend[0].Date);
        Assert.Equal(93_000_000m, trend[0].Revenue);
    }
}
