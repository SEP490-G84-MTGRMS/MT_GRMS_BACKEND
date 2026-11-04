using MT_GRMS.Domain.Common;
using MT_GRMS.Domain.Entities.Identity;
using MT_GRMS.Domain.Entities.SaaS;

namespace MT_GRMS.Domain.Entities.Supplier;

/// <summary>
/// Bảng GoodsReceipts (Phiếu nhập kho GRN)
/// </summary>
public class GoodsReceipt : BaseTenantEntity
{
    public string Code { get; set; } = string.Empty;
    public Guid? PurchaseOrderId { get; set; }
    public Guid SupplierId { get; set; }
    public Guid BranchId { get; set; }
    public string Status { get; set; } = "DRAFT";
    public DateTime ReceivedAtUtc { get; set; } = DateTime.UtcNow;
    public string? InvoiceNo { get; set; }
    public string ReceiverUserId { get; set; } = string.Empty;
    public string? ConfirmedByUserId { get; set; }
    public DateTime? ConfirmedAtUtc { get; set; }
    public string? RejectedByUserId { get; set; }
    public DateTime? RejectedAtUtc { get; set; }
    public string? RejectionReason { get; set; }
    public decimal SubTotal { get; set; } = 0.00m;
    public decimal FreightFee { get; set; } = 0.00m;
    public decimal DiscountAmount { get; set; } = 0.00m;
    public decimal VatPercent { get; set; } = 0.00m;
    public decimal VatAmount { get; set; } = 0.00m;
    public decimal GrandTotal { get; set; } = 0.00m;
    public string PaymentMethod { get; set; } = "AP";
    public DateTime? PaymentDueDate { get; set; }
    public bool CreateApOnConfirm { get; set; } = true;
    public string? AttachmentUrlsJson { get; set; }
    public string? Note { get; set; }
    public int RowVersion { get; set; } = 1;

    // Navigations
    public PurchaseOrder? PurchaseOrder { get; set; }
    public Supplier? Supplier { get; set; }
    public Branch? Branch { get; set; }
    public User? ReceiverUser { get; set; }
    public User? ConfirmedByUser { get; set; }
    public User? RejectedByUser { get; set; }
    public ICollection<GoodsReceiptLine> Lines { get; set; } = new List<GoodsReceiptLine>();
}
