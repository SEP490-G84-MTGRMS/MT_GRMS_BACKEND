using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.GoodsReceipts.Dtos;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Entities.Inventory;
using MT_GRMS.Domain.Entities.Supplier;

namespace MT_GRMS.Application.GoodsReceipts;

public interface IGoodsReceiptRepository
{
    Task<PagedResult<GoodsReceiptDto>> GetPagedAsync(string tenantId, GoodsReceiptFilterQuery query, CancellationToken cancellationToken = default);
    Task<GoodsReceipt?> GetByIdAsync(string tenantId, Guid id, CancellationToken cancellationToken = default);
    Task<GoodsReceipt> AddAsync(GoodsReceipt goodsReceipt, CancellationToken cancellationToken = default);
    Task UpdateAsync(GoodsReceipt goodsReceipt, CancellationToken cancellationToken = default);
    Task<string> GenerateCodeAsync(string tenantId, CancellationToken cancellationToken = default);
    Task<List<PurchaseOrder>> GetEligiblePurchaseOrdersAsync(string tenantId, Guid? branchId, CancellationToken cancellationToken = default);
    Task<PurchaseOrder?> GetPurchaseOrderWithDetailsAsync(string tenantId, Guid poId, CancellationToken cancellationToken = default);
    Task<PurchaseOrder?> GetPurchaseOrderByCodeWithDetailsAsync(string tenantId, string poCode, CancellationToken cancellationToken = default);
    Task<GoodsReceipt?> GetActiveReceiptByPoIdAsync(string tenantId, Guid poId, CancellationToken cancellationToken = default);
    Task<BranchStock?> GetBranchStockAsync(string tenantId, Guid branchId, Guid productId, string? batchCode, CancellationToken cancellationToken = default);
    Task AddBranchStockAsync(BranchStock stock, CancellationToken cancellationToken = default);
    Task AddBatchAsync(Batch batch, CancellationToken cancellationToken = default);
    Task<Product?> GetProductByIdAsync(string tenantId, Guid productId, CancellationToken cancellationToken = default);
    Task<decimal> GetTotalProductStockAsync(string tenantId, Guid productId, CancellationToken cancellationToken = default);
    Task<Supplier?> GetSupplierByIdAsync(string tenantId, Guid supplierId, CancellationToken cancellationToken = default);
    Task AddSupplierLedgerAsync(SupplierAPLedger ledger, CancellationToken cancellationToken = default);
}
