using MT_GRMS.Application.Tax.Dtos;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Application.Tax;

public interface IHouseholdTaxRepository
{
    Task<decimal> GetAnnualAccumulatedRevenueAsync(int year, DateTime upToDateUtc, CancellationToken cancellationToken = default);
    Task<(decimal GrossRevenue, decimal DiscountAmount, decimal ReturnAmount)> GetPeriodRevenueAsync(DateTime fromDateUtc, DateTime toDateUtc, Guid? branchId, CancellationToken cancellationToken = default);
    Task<(decimal ReducedVatRevenue, decimal StandardVatRevenue)> GetPeriodRevenueSplitAsync(DateTime fromDateUtc, DateTime toDateUtc, Guid? branchId, CancellationToken cancellationToken = default);
    Task<decimal> GetPeriodDeductibleExpensesAsync(DateTime fromDateUtc, DateTime toDateUtc, Guid? branchId, CancellationToken cancellationToken = default);
    Task<List<ExpenseVoucher>> GetDeductibleExpenseVouchersAsync(DateTime fromDateUtc, DateTime toDateUtc, Guid? branchId, CancellationToken cancellationToken = default);
    Task<(decimal TotalCogs, bool IsEstimated)> GetPeriodCogsAsync(DateTime fromDateUtc, DateTime toDateUtc, Guid? branchId, CancellationToken cancellationToken = default);
    Task<(decimal DocumentedCogs, bool HasMissingCostItems, decimal EstimatedMissingCogsWarning)> GetPeriodCogsDetailedAsync(DateTime fromDateUtc, DateTime toDateUtc, Guid? branchId, CancellationToken cancellationToken = default)
        => GetPeriodCogsAsync(fromDateUtc, toDateUtc, branchId, cancellationToken)
            .ContinueWith(t => (t.Result.TotalCogs, t.Result.IsEstimated, 0m), cancellationToken);
    Task<decimal> GetPeriodOpexAsync(DateTime fromDateUtc, DateTime toDateUtc, Guid? branchId, CancellationToken cancellationToken = default);
    Task<TaxExpenseComplianceSummaryDto> GetExpenseComplianceSummaryAsync(DateTime fromDateUtc, DateTime toDateUtc, Guid? branchId, decimal cashlessThreshold = 5_000_000m, CancellationToken cancellationToken = default)
        => Task.FromResult(new TaxExpenseComplianceSummaryDto());
    Task<List<AccountingBookEntryDto>> GetBookEntriesAsync(DateTime fromDateUtc, DateTime toDateUtc, Guid? branchId, HouseholdTaxGroup group, TaxCalculationMethod method, AccountingBookType? bookType, CancellationToken cancellationToken = default);
    Task<HouseholdTaxSnapshot?> GetSnapshotAsync(int year, TaxPeriodType periodType, int periodNumber, Guid? branchId, CancellationToken cancellationToken = default);
    Task<HouseholdTaxSnapshot?> GetSnapshotByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<HouseholdTaxSnapshot?> GetSnapshotForUpdateAsync(Guid id, CancellationToken cancellationToken = default);
    Task<List<HouseholdTaxSnapshot>> GetSnapshotsAsync(int? year, Guid? branchId, CancellationToken cancellationToken = default);
    Task<HouseholdTaxSnapshot> AddSnapshotAsync(HouseholdTaxSnapshot snapshot, CancellationToken cancellationToken = default);
    Task UpdateSnapshotAsync(HouseholdTaxSnapshot snapshot, CancellationToken cancellationToken = default);
    Task<HouseholdTaxpayerProfileDto> GetTaxpayerProfileAsync(Guid? branchId, CancellationToken cancellationToken = default);
}
