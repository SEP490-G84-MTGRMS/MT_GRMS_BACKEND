using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Entities.Inventory;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Domain.Entities.Sales;

namespace MT_GRMS.Application.PosOrders;

public interface IOrderRepository : IRepository<Order>
{
    Task<Branch?> GetBranchByIdAsync(Guid branchId, CancellationToken cancellationToken = default);
    Task<Customer?> GetCustomerByIdAsync(Guid customerId, CancellationToken cancellationToken = default);
    Task<Dictionary<Guid, Product>> GetProductsByIdsAsync(IEnumerable<Guid> productIds, CancellationToken cancellationToken = default);
    Task<Dictionary<Guid, BranchProduct>> GetBranchProductsByProductIdsAsync(Guid branchId, IEnumerable<Guid> productIds, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BranchStock>> GetBranchStocksForProductFefoAsync(Guid branchId, Guid productId, CancellationToken cancellationToken = default);
    Task<BranchStock?> GetBranchStockByIdAsync(Guid branchId, Guid productId, Guid batchId, CancellationToken cancellationToken = default);

    Task<Order?> GetOrderWithDetailsAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Order>> GetOrdersWithFilterAsync(
        Guid? branchId,
        Guid? customerId,
        string? paymentMethod,
        string? status,
        DateTime? fromDate,
        DateTime? toDate,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default);
    Task<int> CountOrdersWithFilterAsync(
        Guid? branchId,
        Guid? customerId,
        string? paymentMethod,
        string? status,
        DateTime? fromDate,
        DateTime? toDate,
        CancellationToken cancellationToken = default);

    Task<BranchStock?> GetBranchStockAsync(Guid branchId, Guid productId, string? batchCode, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Order>> GetAllOrdersForSummaryAsync(Guid? branchId, DateTime? fromDate, DateTime? toDate, CancellationToken cancellationToken = default);
}
