using MT_GRMS.Domain.Common;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Domain.Entities.Inventory;

/// <summary>
/// Bảng Transfers (Phiếu điều chuyển kho 2 pha giữa các chi nhánh)
/// </summary>
public class Transfer : BaseTenantEntity
{
    public string TransferCode { get; set; } = string.Empty;
    public Guid FromBranchId { get; set; }
    public Guid ToBranchId { get; set; }
    public TransferStatus Status { get; set; } = TransferStatus.Draft;
    public TransferCompletionResult? CompletionResult { get; set; }
    public TransferOrigin Origin { get; set; } = TransferOrigin.OwnerAllocation;
    public TransferPriority Priority { get; set; } = TransferPriority.Normal;
    public Guid? TransferRequestId { get; set; }
    public Guid? RelatedOrderId { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public Guid? SubmittedByUserId { get; set; }
    public DateTime? SourceConfirmedAt { get; set; }
    public Guid? SourceConfirmedByUserId { get; set; }
    public DateTime? DispatchedAt { get; set; }
    public Guid? DispatchedByUserId { get; set; }
    public DateTime? ReceivedAt { get; set; }
    public Guid? ReceivedByUserId { get; set; }
    public DateTime? ExpectedDeliveryDate { get; set; }
    public string CreatedByStaff { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; } = 0.00m;
    public string? Note { get; set; }
    public string? DiscrepancyNote { get; set; }
    public string? CancelReason { get; set; }
    public DateTime? CancelledAt { get; set; }
    public Guid? CancelledByUserId { get; set; }
    public bool HasDiscrepancy { get; set; }
    public bool IsStockReserved { get; set; }
    public bool IsFavorite { get; set; } = false;

    // Navigations
    public Branch? FromBranch { get; set; }
    public Branch? ToBranch { get; set; }
    public ICollection<TransferDetail> Details { get; set; } = new List<TransferDetail>();
    public ICollection<TransferStatusHistory> StatusHistory { get; set; } = new List<TransferStatusHistory>();
    public TransferRequest? TransferRequest { get; set; }
}
