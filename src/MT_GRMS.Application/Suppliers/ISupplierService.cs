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
    Task<SupplierDetailDto> LinkCustomerAsync(Guid supplierId, LinkCustomerRequest request, CancellationToken cancellationToken = default);
}
