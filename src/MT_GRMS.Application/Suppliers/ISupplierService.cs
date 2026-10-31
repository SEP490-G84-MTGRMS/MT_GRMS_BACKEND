using MT_GRMS.Application.Common;
using MT_GRMS.Application.Suppliers.Dtos;

namespace MT_GRMS.Application.Suppliers;

public interface ISupplierService
{
    Task<PagedResult<SupplierSummaryDto>> GetSuppliersAsync(SupplierQueryFilter filter, CancellationToken cancellationToken = default);
    Task<SupplierDetailDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<SupplierDetailDto> CreateSupplierAsync(CreateSupplierCommand command, CancellationToken cancellationToken = default);
    Task<SupplierDetailDto> UpdateSupplierAsync(Guid id, UpdateSupplierCommand command, CancellationToken cancellationToken = default);
    Task<SupplierDetailDto> UpdateStatusAsync(Guid id, UpdateSupplierStatusRequest request, CancellationToken cancellationToken = default);
    Task DeleteSupplierAsync(Guid id, CancellationToken cancellationToken = default);

    Task<SupplierDeliveryConfigDto> GetDeliveryConfigAsync(Guid supplierId, CancellationToken cancellationToken = default);
    Task<SupplierDeliveryConfigDto> UpdateDeliveryConfigAsync(Guid supplierId, UpdateSupplierDeliveryConfigCommand command, CancellationToken cancellationToken = default);

    Task<PagedResult<SupplierAPLedgerDto>> GetAPLedgerAsync(Guid supplierId, DateTime? fromDate, DateTime? toDate, int pageIndex = 1, int pageSize = 20, CancellationToken cancellationToken = default);
    Task<SupplierAPLedgerDto> RecordAPTransactionAsync(RecordAPTransactionCommand command, CancellationToken cancellationToken = default);
    Task<SupplierApSummaryDto> GetApSummaryAsync(Guid supplierId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SupplierBranchAssignmentDto>> GetAssignedBranchesAsync(Guid supplierId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SupplierBranchAssignmentDto>> ReplaceAssignedBranchesAsync(Guid supplierId, ReplaceSupplierBranchesCommand command, CancellationToken cancellationToken = default);
    Task<PagedResult<SupplierBranchAssignmentDetailDto>> GetBranchAssignmentsAsync(SupplierBranchAssignmentQueryFilter filter, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SupplierBranchAssignmentDetailDto>> AssignSupplierToBranchesAsync(AssignSupplierToBranchesCommand command, CancellationToken cancellationToken = default);
    Task UnassignSupplierBranchAsync(Guid supplierId, Guid branchId, CancellationToken cancellationToken = default);
    Task<SupplierBranchAssignmentDetailDto> UpdateBranchAssignmentStatusAsync(Guid supplierId, Guid branchId, UpdateSupplierBranchAssignmentStatusRequest request, CancellationToken cancellationToken = default);
    Task<SupplierSearchSuggestionsDto> GetSearchSuggestionsAsync(string query, int limit = 10, CancellationToken cancellationToken = default);
    Task<PagedResult<SupplierTransactionDto>> GetTransactionsAsync(Guid supplierId, int pageIndex = 1, int pageSize = 20, CancellationToken cancellationToken = default);
    Task<SupplierDetailDto> LinkCustomerAsync(Guid supplierId, LinkCustomerRequest request, CancellationToken cancellationToken = default);

    // Supplier Product Management (REQ-SP01 đến REQ-SP06)
    Task<PagedResult<SupplierProductDto>> GetSupplierProductsAsync(Guid supplierId, SupplierProductQueryFilter filter, CancellationToken cancellationToken = default);
    Task<SupplierProductDto> AssignSupplierProductAsync(Guid supplierId, AssignSupplierProductCommand command, CancellationToken cancellationToken = default);
    Task<SupplierProductDto> UpdateSupplierProductAsync(Guid supplierId, Guid productId, UpdateSupplierProductCommand command, CancellationToken cancellationToken = default);
    Task<SupplierProductDto> ToggleSupplierProductStatusAsync(Guid supplierId, Guid productId, ToggleSupplierProductStatusRequest request, CancellationToken cancellationToken = default);
    Task<SupplierProductDto> SetPrimarySupplierAsync(Guid supplierId, Guid productId, SetPrimarySupplierRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProductSupplierMappingDto>> GetSuppliersForProductAsync(Guid productId, bool onlyActive = false, CancellationToken cancellationToken = default);
}

