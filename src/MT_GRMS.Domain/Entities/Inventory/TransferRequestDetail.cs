using MT_GRMS.Domain.Common;

namespace MT_GRMS.Domain.Entities.Inventory;

public sealed class TransferRequestDetail : BaseTenantEntity
{
    public Guid TransferRequestId { get; set; }
    public Guid ProductId { get; set; }
    public decimal RequestedQuantity { get; set; }
    public decimal AllocatedQuantity { get; set; }
    public decimal ReceivedQuantity { get; set; }
    public string Unit { get; set; } = string.Empty;
    public TransferRequest? TransferRequest { get; set; }
}
