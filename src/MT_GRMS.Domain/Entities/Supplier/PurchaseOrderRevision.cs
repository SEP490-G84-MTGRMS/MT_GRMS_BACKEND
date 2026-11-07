using MT_GRMS.Domain.Common;

namespace MT_GRMS.Domain.Entities.Supplier;

/// <summary>
/// Lịch sử amend PO — snapshot trước/sau, không cho sửa.
/// </summary>
public class PurchaseOrderRevision : BaseTenantEntity
{
    public Guid PurchaseOrderId { get; set; }
    public int RevisionNumber { get; set; }
    public string? PreviousDataSnapshotJson { get; set; }
    public string? NewDataSnapshotJson { get; set; }
    public string? ChangedByUserId { get; set; }
    public DateTime ChangedAtUtc { get; set; }
    public string? ChangeReason { get; set; }

    public PurchaseOrder? PurchaseOrder { get; set; }
}
