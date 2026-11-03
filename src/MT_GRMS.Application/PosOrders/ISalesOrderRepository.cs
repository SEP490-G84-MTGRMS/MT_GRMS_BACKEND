using MT_GRMS.Domain.Entities.Sales;

namespace MT_GRMS.Application.PosOrders;

public interface ISalesOrderRepository
{
    Task<Order?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<Order>> GetListAsync(Guid? branchId = null, Guid? customerId = null, DateTime? fromDate = null, DateTime? toDate = null, CancellationToken ct = default);
    Task AddAsync(Order order, CancellationToken ct = default);
    Task<Guid> GetDefaultBranchIdAsync(CancellationToken ct = default);
    Task<Dictionary<Guid, decimal>> GetProductCostPricesAsync(List<Guid> productIds, CancellationToken ct = default);
}
