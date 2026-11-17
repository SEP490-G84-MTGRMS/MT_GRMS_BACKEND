using MT_GRMS.Domain.Common;
using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Domain.Entities.Inventory;

/// <summary>A branch demand that may be fulfilled by one or more physical transfers.</summary>
public sealed class TransferRequest : BaseTenantEntity
{
    public string RequestCode { get; set; } = string.Empty;
    public Guid ToBranchId { get; set; }
    public TransferRequestStatus Status { get; set; } = TransferRequestStatus.Open;
    public TransferPriority Priority { get; set; } = TransferPriority.Urgent;
    public Guid? RelatedOrderId { get; set; }
    public DateTime? RequiredAt { get; set; }
    public string? Note { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancelReason { get; set; }
    public ICollection<TransferRequestDetail> Details { get; set; } = new List<TransferRequestDetail>();
    public ICollection<Transfer> Transfers { get; set; } = new List<Transfer>();
}
