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

    Task<(IReadOnlyList<PurchaseOrder> Items, int TotalCount)> GetPagedPurchaseOrdersAsync(
        Guid? branchId,
        Guid? supplierId,
        string? status,
        DateTime? dateFrom,
        DateTime? dateTo,
        int pageIndex,
        int pageSize,
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
}


