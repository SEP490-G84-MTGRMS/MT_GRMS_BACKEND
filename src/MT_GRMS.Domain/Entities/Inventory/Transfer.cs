using MT_GRMS.Domain.Common;
using MT_GRMS.Domain.Entities.SaaS;

namespace MT_GRMS.Domain.Entities.Inventory;

/// <summary>
/// Bảng Transfers (Phiếu điều chuyển kho 2 pha giữa các chi nhánh)
/// </summary>
public class Transfer : BaseEntity
{
    public string TransferCode { get; set; } = string.Empty;
    public Guid FromBranchId { get; set; }
    public Guid ToBranchId { get; set; }
    public string Status { get; set; } = "Draft";
    public DateTime? DispatchedAt { get; set; }
    public DateTime? ReceivedAt { get; set; }
    public DateTime? ExpectedDeliveryDate { get; set; }
    public string CreatedByStaff { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; } = 0.00m;
    public string? Note { get; set; }
    public string? DiscrepancyNote { get; set; }
    public string? CancelReason { get; set; }
    public bool IsFavorite { get; set; } = false;

    // Navigations
    public Branch? FromBranch { get; set; }
    public Branch? ToBranch { get; set; }
    public ICollection<TransferDetail> Details { get; set; } = new List<TransferDetail>();
}
