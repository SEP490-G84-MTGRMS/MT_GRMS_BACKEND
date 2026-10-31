using MT_GRMS.Application.Common;
using MT_GRMS.Application.Suppliers.Dtos;
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
        string? code = null,
        string? name = null,
        string? contactPerson = null,
        string? phone = null,
        List<Guid>? supplierIds = null,
        List<Guid>? productIds = null,
        List<string>? contactPersons = null,
        List<string>? phones = null,
        List<string>? codes = null,
        List<string>? names = null,
        CancellationToken cancellationToken = default);

    Task<SupplierSearchSuggestionsDto> GetSearchSuggestionsAsync(
        string query,
        int limit = 10,
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

    // Supplier Product Management (REQ-SP01 đến REQ-SP06)
    Task<SupplierProduct?> GetSupplierProductAsync(Guid supplierId, Guid productId, CancellationToken cancellationToken = default);
    Task<(IReadOnlyList<SupplierProduct> Items, int TotalCount)> GetPagedSupplierProductsAsync(
        Guid supplierId,
        string? searchTerm,
        Guid? categoryId,
        SupplierProductStatus? status,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken = default);
    Task AddSupplierProductAsync(SupplierProduct supplierProduct, CancellationToken cancellationToken = default);
    Task ClearPrimarySupplierFlagAsync(Guid productId, Guid? excludeSupplierId = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SupplierProduct>> GetSuppliersForProductAsync(Guid productId, bool onlyActive = false, CancellationToken cancellationToken = default);
    Task<bool> ProductExistsAsync(Guid productId, CancellationToken cancellationToken = default);
    Task<Domain.Entities.Catalog.Product?> GetProductByIdAsync(Guid productId, CancellationToken cancellationToken = default);

    // Supplier Branch Assignment (REQ-SBA01 đến REQ-SBA03)
    Task<(IReadOnlyList<SupplierBranchAssignment> Items, int TotalCount)> GetPagedBranchAssignmentsAsync(
        string? supplierSearch,
        string? branchSearch,
        IReadOnlyList<string>? supplierCodes,
        IReadOnlyList<string>? supplierNames,
        IReadOnlyList<string>? branchCodes,
        IReadOnlyList<string>? branchNames,
        Guid? supplierId,
        Guid? branchId,
        SupplierStatus? status,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken = default);
    Task<SupplierBranchAssignment?> GetBranchAssignmentAsync(Guid supplierId, Guid branchId, CancellationToken cancellationToken = default);
    Task<bool> BranchAssignmentExistsAsync(Guid supplierId, Guid branchId, CancellationToken cancellationToken = default);
    Task AddBranchAssignmentAsync(SupplierBranchAssignment assignment, CancellationToken cancellationToken = default);
    Task RemoveBranchAssignmentAsync(Guid supplierId, Guid branchId, CancellationToken cancellationToken = default);
    Task<bool> BranchExistsAsync(Guid branchId, CancellationToken cancellationToken = default);
    Task<MT_GRMS.Domain.Entities.SaaS.Branch?> GetBranchByIdAsync(Guid branchId, CancellationToken cancellationToken = default);
}

