using MT_GRMS.Domain.Entities.Supplier;

namespace MT_GRMS.Application.PurchaseOrders.Dispatch;

public interface ISupplierOrderExcelGenerator
{
    Task<byte[]> GenerateAsync(
        SupplierDispatchPackage package,
        IReadOnlyList<PurchaseOrder> purchaseOrders,
        CancellationToken cancellationToken = default);
}
