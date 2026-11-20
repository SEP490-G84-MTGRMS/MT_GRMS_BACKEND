using MT_GRMS.Domain.Common;
using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Domain.Entities.Inventory;

/// <summary>Append-only audit trail for every transfer state transition.</summary>
public sealed class TransferStatusHistory : BaseTenantEntity
{
    public Guid TransferId { get; set; }
    public TransferStatus? FromStatus { get; set; }
    public TransferStatus ToStatus { get; set; }
    public Guid ActorUserId { get; set; }
    public Guid? ActorBranchId { get; set; }
    public string ActorRole { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public Transfer? Transfer { get; set; }
}
