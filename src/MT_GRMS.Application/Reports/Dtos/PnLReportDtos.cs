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
    decimal NetMarginPercentage
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
