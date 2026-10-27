using MT_GRMS.Domain.Common;
using MT_GRMS.Domain.Entities.Catalog;

namespace MT_GRMS.Domain.Entities.Supplier;

/// <summary>
/// Bảng PurchaseOrderDetails (Chi tiết đặt mua PO)
/// </summary>
public class PurchaseOrderDetail : BaseTenantEntity
{
    public Guid PurchaseOrderId { get; set; }
    public Guid ProductId { get; set; }
    public DateTime? DeliveryDate { get; set; }
    public decimal? EmployeeQty { get; set; }
    public decimal? StoreQty { get; set; }
    public decimal? FinalQty { get; set; }
    public decimal? OrderedQty { get; set; }
    public decimal ReceivedQty { get; set; } = 0.000m;
    public decimal UnitPrice { get; set; } = 0.00m;
    public string DeliveryUoM { get; set; } = string.Empty;
    public decimal LineAmount { get; set; } = 0.00m;
    public string? RemainOption { get; set; }
    public bool IsClosed { get; set; } = false;

    // Navigations
    public PurchaseOrder? PurchaseOrder { get; set; }
    public Product? Product { get; set; }
}
