namespace MT_GRMS.Application.Reports;

public record RevenueAggregation(
    decimal GrossRevenue,
    decimal Discounts,
    decimal SalesReturns,
    int TotalOrders = 0
);

public record CogsAggregation(
    decimal TotalCogs,
    string Source = "ACTUAL",
    decimal EstimatedPercentage = 0m,
    string? WarningNotice = null,
    decimal WeightedAverageCogs = 0m,
    decimal FefoSpecificCogs = 0m
);

public record CategoryExpenseItem(
    string Category,
    decimal Amount
);

public record DailyPnLItem(
    DateTime Date,
    decimal NetRevenue,
    decimal Cogs,
    decimal Expenses,
    decimal BadDebt
);

public record TopProductPnLItem(
    Guid ProductId,
    string ProductCode,
    string ProductName,
    decimal QuantitySold,
    decimal Revenue,
    decimal Cogs,
    decimal Profit,
    decimal MarginPct
);

public interface IPnLReportRepository
{
    Task<RevenueAggregation> GetRevenueAsync(Guid? branchId, DateTime fromDate, DateTime toDate, CancellationToken ct = default);
    Task<CogsAggregation> GetCogsAsync(Guid? branchId, DateTime fromDate, DateTime toDate, CancellationToken ct = default);
    Task<List<CategoryExpenseItem>> GetOperatingExpensesAsync(Guid? branchId, DateTime fromDate, DateTime toDate, CancellationToken ct = default);
    Task<decimal> GetBadDebtExpenseAsync(Guid? branchId, DateTime fromDate, DateTime toDate, CancellationToken ct = default);
    Task<List<DailyPnLItem>> GetDailyTrendAsync(Guid? branchId, DateTime fromDate, DateTime toDate, CancellationToken ct = default);
    Task<List<TopProductPnLItem>> GetTopProductsAsync(Guid? branchId, DateTime fromDate, DateTime toDate, int limit = 10, CancellationToken ct = default);
}
