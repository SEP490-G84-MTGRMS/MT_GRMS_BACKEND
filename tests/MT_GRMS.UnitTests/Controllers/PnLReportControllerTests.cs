using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Moq;
using MT_GRMS.Api.Controllers;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Reports;
using MT_GRMS.Application.Reports.Dtos;
using Xunit;

namespace MT_GRMS.UnitTests.Controllers;

public class PnLReportControllerTests
{
    private readonly Mock<IPnLReportService> _mockPnLService;
    private readonly PnLReportController _controller;

    public PnLReportControllerTests()
    {
        _mockPnLService = new Mock<IPnLReportService>();
        _controller = new PnLReportController(_mockPnLService.Object);
    }

    private static PnLSummaryResponse CreateTestSummary(
        decimal grossRevenue = 100_000_000m,
        decimal cogs = 60_000_000m,
        decimal netProfit = 30_000_000m)
    {
        return new PnLSummaryResponse(
            Period: new PnLPeriod(DateTime.UtcNow.AddDays(-7), DateTime.UtcNow),
            BranchId: Guid.NewGuid(),
            BranchName: "Branch 1",
            GrossRevenue: grossRevenue,
            Discounts: 0m,
            SalesReturns: 0m,
            NetRevenue: grossRevenue,
            Cogs: cogs,
            GrossProfit: grossRevenue - cogs,
            GrossMarginPercentage: 40.0m,
            OperatingExpenses: new OperatingExpenseSummary(10_000_000m, new List<CategoryExpenseBreakdown>()),
            BadDebtExpense: 0m,
            NetProfit: netProfit,
            NetMarginPercentage: 30.0m);
    }

    [Fact]
    public async Task GetSummary_ShouldReturnOk()
    {
        var summary = CreateTestSummary(100_000_000m, 60_000_000m, 30_000_000m);

        _mockPnLService
            .Setup(s => s.GetPnLSummaryAsync(It.IsAny<PnLFilterRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(summary);

        var actionResult = await _controller.GetSummary(null, DateTime.UtcNow.AddDays(-7), DateTime.UtcNow, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<PnLSummaryResponse>>(okResult.Value);
        Assert.True(apiResponse.Success);
        Assert.Equal(100_000_000m, apiResponse.Data?.GrossRevenue);
    }

    [Fact]
    public async Task GetExpenseBreakdown_ShouldReturnOk()
    {
        var breakdown = new OperatingExpenseSummary(
            TotalAmount: 5_000_000m,
            Categories: new List<CategoryExpenseBreakdown>());

        _mockPnLService
            .Setup(s => s.GetExpenseBreakdownAsync(It.IsAny<PnLFilterRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(breakdown);

        var actionResult = await _controller.GetExpenseBreakdown(null, null, null, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<OperatingExpenseSummary>>(okResult.Value);
        Assert.True(apiResponse.Success);
        Assert.Equal(5_000_000m, apiResponse.Data?.TotalAmount);
    }

    [Fact]
    public async Task GetComparison_ShouldReturnOk()
    {
        var current = CreateTestSummary(100_000_000m, 60_000_000m, 30_000_000m);
        var previous = CreateTestSummary(80_000_000m, 50_000_000m, 20_000_000m);
        var comparison = new PnLComparisonResponse(
            CurrentPeriod: current,
            PreviousPeriod: previous,
            RevenueGrowthPercentage: 25.0m,
            GrossProfitGrowthPercentage: 33.3m,
            NetProfitGrowthPercentage: 50.0m);

        _mockPnLService
            .Setup(s => s.GetComparisonAsync(It.IsAny<PnLFilterRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(comparison);

        var actionResult = await _controller.GetComparison(null, null, null, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<PnLComparisonResponse>>(okResult.Value);
        Assert.True(apiResponse.Success);
        Assert.Equal(25.0m, apiResponse.Data?.RevenueGrowthPercentage);
    }

    [Fact]
    public async Task GetTrend_ShouldReturnOk()
    {
        var trend = new List<PnLTrendPointResponse>
        {
            new(DateTime.UtcNow.Date, 10_000_000m, 6_000_000m, 1_000_000m, 3_000_000m)
        };

        _mockPnLService
            .Setup(s => s.GetTrendAsync(It.IsAny<PnLFilterRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(trend);

        var actionResult = await _controller.GetTrend(null, null, null, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var apiResponse = Assert.IsType<ApiResponse<List<PnLTrendPointResponse>>>(okResult.Value);
        Assert.True(apiResponse.Success);
        Assert.Single(apiResponse.Data!);
    }
}
