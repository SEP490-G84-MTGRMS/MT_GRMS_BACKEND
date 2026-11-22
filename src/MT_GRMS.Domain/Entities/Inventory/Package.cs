using MT_GRMS.Domain.Common;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Entities.SaaS;

namespace MT_GRMS.Domain.Entities.Inventory;

/// <summary>
/// Khay bán sẵn gắn đúng 1 dòng tồn BranchStock (lô).
/// </summary>
public class Package : BaseTenantEntity
{
    public Guid BranchId { get; set; }
    public Guid ProductId { get; set; }
    public Guid BranchStockId { get; set; }

    public string InternalCode { get; set; } = string.Empty;
    /// <summary>Tên hiển thị khi bán / tìm kiếm (vd: Dưa hấu khay 1kg).</summary>
    public string? Name { get; set; }
    public string? Barcode { get; set; }
    public decimal NetWeight { get; set; } = 0.000m;
    public decimal SellingPrice { get; set; } = 0.00m;
    /// <summary>Ngày sản xuất / ngày đóng khay (mặc định = ngày tạo).</summary>
    public DateTime? ManufactureDate { get; set; }
    public DateTime? ExpiryDate { get; set; }

    /// <summary>PORTIONING | PROCESSING | SUPPLIER_TRAY</summary>
    public string RepackType { get; set; } = "PORTIONING";

    public Guid? RepackOrderId { get; set; }
    public Guid? GoodsReceiptLineId { get; set; }

    /// <summary>AVAILABLE | QUARANTINED | SOLD | RETURNED | VOID</summary>
    public string Status { get; set; } = "AVAILABLE";

    public string? VoidMode { get; set; }
    public string? VoidReason { get; set; }

    public Guid? SoldOrderId { get; set; }
    public Guid? SoldOrderDetailId { get; set; }
    public DateTime? SoldAt { get; set; }

    public string? PriceHistoryJson { get; set; }

    public Branch? Branch { get; set; }
    public Product? Product { get; set; }
    public BranchStock? BranchStock { get; set; }
    public RepackOrder? RepackOrder { get; set; }
}
