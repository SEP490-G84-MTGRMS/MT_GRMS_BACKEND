using MT_GRMS.Domain.Common;
using MT_GRMS.Domain.Entities.Identity;
using MT_GRMS.Domain.Entities.SaaS;

namespace MT_GRMS.Domain.Entities.Supplier;

/// <summary>
/// Bảng PurchaseOrders (Đơn đặt hàng nhập kho - PO)
/// </summary>
public class PurchaseOrder : BaseTenantEntity
{
    public string Code { get; set; } = string.Empty;
    public Guid SupplierId { get; set; }
    public Guid BranchId { get; set; }
    public DateTime? DeliveryDate { get; set; }
    public DateTime? ViewFromDate { get; set; }
    public DateTime? ViewToDate { get; set; }
    public string Status { get; set; } = "DRAFT";
    public DateTime? OrderedAtUtc { get; set; }
    public decimal TotalQty { get; set; } = 0.000m;
    public decimal TotalAmount { get; set; } = 0.00m;
    public string? Note { get; set; }
    public Guid? ParentDraftId { get; set; }
    public DateTime? DispatchedAtUtc { get; set; }
    public DateTime? ConfirmedBySupplierAtUtc { get; set; }
    public string? CreatedByRole { get; set; }
    public string? SubmittedByUserId { get; set; }
    public DateTime? SubmittedAtUtc { get; set; }
    public string? ApprovedByUserId { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public string? RejectedByUserId { get; set; }
    public DateTime? RejectedAtUtc { get; set; }
    public string? RejectionReason { get; set; }
    public int RowVersion { get; set; } = 1;

    // Navigations
    public Supplier? Supplier { get; set; }
    public Branch? Branch { get; set; }
    public PurchaseOrder? ParentDraft { get; set; }
    public User? SubmittedByUser { get; set; }
    public User? ApprovedByUser { get; set; }
    public User? RejectedByUser { get; set; }
    public ICollection<PurchaseOrderDetail> Details { get; set; } = new List<PurchaseOrderDetail>();
    public ICollection<GoodsReceipt> GoodsReceipts { get; set; } = new List<GoodsReceipt>();
}
