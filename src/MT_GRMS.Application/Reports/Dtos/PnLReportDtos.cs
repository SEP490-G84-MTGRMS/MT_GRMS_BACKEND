namespace MT_GRMS.Application.Reports.Dtos;

public record CategoryExpenseBreakdown(
    string Category,
    decimal Amount,
    decimal PercentageOfRevenue
);

public record OperatingExpenseSummary(
    decimal TotalAmount,
    List<CategoryExpenseBreakdown> Categories
);

public record PnLPeriod(
    DateTime FromDate,
    DateTime ToDate
);

public record CogsValuationComparisonDto(
    decimal WeightedAverageCogs,
    decimal FefoSpecificCogs,
    decimal CogsVariance,
    decimal WeightedAverageGrossProfit,
    decimal FefoSpecificGrossProfit,
    decimal WeightedAverageGrossMarginPct,
    decimal FefoSpecificGrossMarginPct,
    decimal WeightedAverageNetProfit,
    decimal FefoSpecificNetProfit,
    string AppliedTaxMethod = "WEIGHTED_AVERAGE_TT152"
);

public record TopProductPnLDto(
    Guid ProductId,
    string ProductCode,
    string ProductName,
    decimal QuantitySold,
    decimal Revenue,
    decimal Cogs,
    decimal Profit,
    decimal MarginPct
);

public record PnLSummaryResponse(
    PnLPeriod Period,
    Guid? BranchId,
    string BranchName,
    decimal GrossRevenue,
    decimal Discounts,
    decimal SalesReturns,
    decimal NetRevenue,
    decimal Cogs,
    decimal GrossProfit,
    decimal GrossMarginPercentage,
    OperatingExpenseSummary OperatingExpenses,
    decimal BadDebtExpense,
    decimal NetProfit,
    decimal NetMarginPercentage,
    string CogsSource = "ACTUAL",
    decimal CogsEstimatedPercentage = 0m,
    string? CogsWarningNotice = null,
    int TotalOrders = 0,
    decimal AverageOrderValue = 0m,
    CogsValuationComparisonDto? CogsComparison = null,
    List<TopProductPnLDto>? TopProducts = null
);

public record PnLComparisonResponse(
    PnLSummaryResponse CurrentPeriod,
    PnLSummaryResponse PreviousPeriod,
    decimal RevenueGrowthPercentage,
    decimal GrossProfitGrowthPercentage,
    decimal NetProfitGrowthPercentage
);

public record PnLTrendPointResponse(
    DateTime Date,
    decimal Revenue,
    decimal Cogs,
    decimal Expenses,
    decimal NetProfit
);

public record PnLFilterRequest(
    Guid? BranchId,
    DateTime FromDate,
    DateTime ToDate
);
