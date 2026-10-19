using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Entities.Supplier;
using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Application.Suppliers;

public interface ISupplierRepository : IRepository<Supplier>
{
    Task<Supplier?> GetByIdWithDetailsAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> ExistsByCodeAsync(string code, CancellationToken cancellationToken = default);
    Task<int> CountTodayAsync(CancellationToken cancellationToken = default);
    Task<(IReadOnlyList<Supplier> Items, int TotalCount)> GetPagedListAsync(
        string? searchTerm,
        SupplierType? supplierType,
        SupplierStatus? status,
        Guid? categoryId,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<bool> HasLedgerTransactionsAsync(Guid supplierId, APDocType docType, CancellationToken cancellationToken = default);
    Task<SupplierAPLedger?> GetLedgerByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default);
    Task AddLedgerAsync(SupplierAPLedger ledger, CancellationToken cancellationToken = default);
    Task<(IReadOnlyList<SupplierAPLedger> Items, int TotalCount)> GetPagedLedgerAsync(
        Guid supplierId,
        DateTime? fromDate,
        DateTime? toDate,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<SupplierDeliveryConfig?> GetDeliveryConfigAsync(Guid supplierId, CancellationToken cancellationToken = default);
    Task AddDeliveryConfigAsync(SupplierDeliveryConfig config, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SupplierBlackoutDate>> GetBlackoutDatesAsync(Guid supplierId, CancellationToken cancellationToken = default);
    Task ReplaceBlackoutDatesAsync(Guid supplierId, Guid configId, IEnumerable<SupplierBlackoutDate> blackoutDates, CancellationToken cancellationToken = default);
}
