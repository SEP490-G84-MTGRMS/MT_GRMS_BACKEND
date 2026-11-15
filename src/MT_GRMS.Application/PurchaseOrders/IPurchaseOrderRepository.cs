using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MT_GRMS.Application.PurchaseOrders.Dtos;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Domain.Entities.Supplier;

namespace MT_GRMS.Application.PurchaseOrders;

public interface IPurchaseOrderRepository
{
    Task<Branch?> GetBranchByIdAsync(Guid branchId, CancellationToken cancellationToken = default);

    Task<Domain.Entities.Supplier.Supplier?> GetSupplierByIdAsync(Guid supplierId, CancellationToken cancellationToken = default);

    Task<Product?> GetProductByIdAsync(Guid productId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderMatrixProductDto>> GetMatrixProductsAsync(
        Guid branchId,
        Guid? supplierId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderMatrixSupplierDeliveryConfigDto>> GetMatrixSupplierConfigsAsync(
        Guid branchId,
        Guid? supplierId,
        CancellationToken cancellationToken = default);

    Task<PurchaseOrder?> GetDraftMatrixPoAsync(
        Guid branchId,
        Guid? supplierId,
        DateTime viewFrom,
        CancellationToken cancellationToken = default);

    /// <summary>Tất cả draft matrix của chi nhánh (dùng khi view ALL — mỗi NCC 1 draft).</summary>
    Task<IReadOnlyList<PurchaseOrder>> GetDraftMatrixPosAsync(
        Guid branchId,
        CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<PurchaseOrder> Items, int TotalCount)> GetPagedPurchaseOrdersAsync(
        Guid? branchId,
        Guid? supplierId,
        string? status,
        DateTime? dateFrom,
        DateTime? dateTo,
        int pageIndex,
        int pageSize,
        string? keyword,
        CancellationToken cancellationToken = default);

    Task<PurchaseOrder?> GetPurchaseOrderByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task AddPurchaseOrderAsync(
        PurchaseOrder purchaseOrder,
        CancellationToken cancellationToken = default);

    Task<string> GeneratePoCodeAsync(
        DateTime deliveryDate,
        CancellationToken cancellationToken = default);

    Task<bool> UserExistsAsync(
        string userId,
        CancellationToken cancellationToken = default);

    void AddPurchaseOrderDetail(PurchaseOrderDetail detail);

    void AddPurchaseOrderRevision(PurchaseOrderRevision revision);

    Task<PurchaseOrder?> FindCommittedPoAsync(
        Guid branchId,
        Guid supplierId,
        DateTime deliveryDate,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PurchaseOrder>> GetCommittedPosInRangeAsync(
        Guid branchId,
        Guid? supplierId,
        DateTime viewFrom,
        int durationDays,
        CancellationToken cancellationToken = default);

    Task<SupplierProduct?> GetSupplierProductAsync(
        Guid supplierId,
        Guid productId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Branch>> GetActiveBranchesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Guid>> GetActiveBranchIdsForSupplierAsync(
        Guid supplierId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy phiếu nhập kho DRAFT/COMPLETED đang gắn các PO (1 PO tối đa 1 phiếu active).
    /// </summary>
    Task<IReadOnlyDictionary<Guid, (Guid ReceiptId, string Code, string Status)>> GetActiveGoodsReceiptsByPoIdsAsync(
        IReadOnlyCollection<Guid> purchaseOrderIds,
        CancellationToken cancellationToken = default);
}


