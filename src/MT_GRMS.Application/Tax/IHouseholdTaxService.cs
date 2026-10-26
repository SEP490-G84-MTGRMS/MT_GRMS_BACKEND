using MT_GRMS.Application.Tax.Dtos;
using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Application.Tax;

public interface IHouseholdTaxService
{
    Task<HouseholdTaxSummaryDto> GetTaxSummaryAsync(HouseholdTaxFilterDto filter, CancellationToken cancellationToken = default);
    Task<List<AccountingBookEntryDto>> GetBookDetailsAsync(int year, TaxPeriodType periodType, int periodNumber, Guid? branchId, AccountingBookType? bookType, CancellationToken cancellationToken = default);
    Task<HouseholdTaxSnapshotDto> FinalizeTaxSnapshotAsync(CreateTaxSnapshotRequest request, string finalizedBy, CancellationToken cancellationToken = default);
    Task<List<HouseholdTaxSnapshotDto>> GetSnapshotsAsync(int? year, Guid? branchId, CancellationToken cancellationToken = default);
    Task<HouseholdTaxSnapshotDto?> GetSnapshotByIdAsync(Guid id, CancellationToken cancellationToken = default);
}
