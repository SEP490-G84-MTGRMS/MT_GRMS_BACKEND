using MT_GRMS.Domain.Common;

namespace MT_GRMS.Domain.Entities.Sales;

public class LoyaltyPointAdjustment : BaseTenantEntity
{
    public Guid CustomerId { get; set; }
    public string TransactionCode { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public int PointsChanged { get; set; }
    public int BalanceAfter { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string PerformedBy { get; set; } = string.Empty;
    public Customer Customer { get; set; } = null!;
}
