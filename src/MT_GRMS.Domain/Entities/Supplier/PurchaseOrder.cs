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

    /// <summary>Số revision hiện tại. PO mới = 1; mỗi lần amend có thay đổi +1.</summary>
    public int RevisionNumber { get; set; } = 1;
    public DateTime? LastAmendedAtUtc { get; set; }
    public string? LastAmendedByUserId { get; set; }

    /// <summary>Gói dispatch/email chứa PO này (nullable — PO cũ chưa có package).</summary>
    public Guid? DispatchPackageId { get; set; }
    public Guid? DispatchBatchId { get; set; }

    // Snapshots tại thời điểm chốt — không ghi đè sau này
    public string? SupplierCodeSnapshot { get; set; }
    public string? SupplierNameSnapshot { get; set; }
    public string? SupplierEmailSnapshot { get; set; }
    public string? StoreCodeSnapshot { get; set; }
    public string? StoreNameSnapshot { get; set; }
    public string? StoreAddressSnapshot { get; set; }
    public string? StorePhoneSnapshot { get; set; }

    // Navigations
    public Supplier? Supplier { get; set; }
    public Branch? Branch { get; set; }
    public PurchaseOrder? ParentDraft { get; set; }
    public User? SubmittedByUser { get; set; }
    public User? ApprovedByUser { get; set; }
    public User? RejectedByUser { get; set; }
    public SupplierDispatchPackage? DispatchPackage { get; set; }
    public ICollection<PurchaseOrderDetail> Details { get; set; } = new List<PurchaseOrderDetail>();
    public ICollection<GoodsReceipt> GoodsReceipts { get; set; } = new List<GoodsReceipt>();
}
