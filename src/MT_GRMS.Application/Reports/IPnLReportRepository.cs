namespace MT_GRMS.Application.Reports;

public record RevenueAggregation(
    decimal GrossRevenue,
    decimal Discounts,
    decimal SalesReturns
);

public record CogsAggregation(
    decimal TotalCogs
);

public record CategoryExpenseItem(
    string Category,
    decimal Amount
);

public interface IPnLReportRepository
{
    Task<RevenueAggregation> GetRevenueAsync(Guid? branchId, DateTime fromDate, DateTime toDate, CancellationToken ct = default);
    Task<CogsAggregation> GetCogsAsync(Guid? branchId, DateTime fromDate, DateTime toDate, CancellationToken ct = default);
    Task<List<CategoryExpenseItem>> GetOperatingExpensesAsync(Guid? branchId, DateTime fromDate, DateTime toDate, CancellationToken ct = default);
    Task<decimal> GetBadDebtExpenseAsync(Guid? branchId, DateTime fromDate, DateTime toDate, CancellationToken ct = default);
}
