using MT_GRMS.Domain.Common;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Entities.Inventory;

namespace MT_GRMS.Domain.Entities.Supplier;

/// <summary>
/// Bảng GoodsReceiptLines (Chi tiết kiểm đếm hàng nhập kho)
/// </summary>
public class GoodsReceiptLine : BaseTenantEntity
{
    public Guid GoodsReceiptId { get; set; }
    public Guid ProductId { get; set; }
    public Guid? PurchaseOrderDetailId { get; set; }
    public decimal OrderedQty { get; set; } = 0.000m;
    public decimal DeliveredQty { get; set; } = 0.000m;
    public decimal AcceptedQty { get; set; } = 0.000m;
    public decimal RejectedQty { get; set; } = 0.000m;
    public decimal UnitPrice { get; set; } = 0.00m;
    public decimal LineAmount { get; set; } = 0.00m;
    public string? BatchCode { get; set; }
    public DateTime? ManufactureDate { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public decimal FreightAllocated { get; set; } = 0.00m;
    public decimal DiscountAllocated { get; set; } = 0.00m;
    public decimal VatAllocated { get; set; } = 0.00m;
    public decimal LandedUnitCost { get; set; } = 0.00m;
    public string? RemainOption { get; set; }
    public string QualityStatus { get; set; } = "ACCEPTED";
    public string? RejectionReason { get; set; }
    public Guid? BranchStockId { get; set; }
    public string? Note { get; set; }

    // Navigations
    public GoodsReceipt? GoodsReceipt { get; set; }
    public Product? Product { get; set; }
    public PurchaseOrderDetail? PurchaseOrderDetail { get; set; }
    public BranchStock? BranchStock { get; set; }
}
