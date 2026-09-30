using MT_GRMS.Domain.Common;

namespace MT_GRMS.Domain.Entities.Inventory;

public class Batch : BaseTenantEntity
{
    public Guid ProductId { get; set; }
    public Guid BranchId { get; set; }
    public string BatchCode { get; set; } = string.Empty;
    public DateTime ManufactureDate { get; set; }
    public DateTime ExpiryDate { get; set; }
    public decimal InitialQuantity { get; set; }
    public decimal LandedCostPerUnit { get; set; }

    public int DaysUntilExpiry => (ExpiryDate.Date - DateTime.UtcNow.Date).Days;
    public bool IsExpired => DateTime.UtcNow.Date > ExpiryDate.Date;
}

public class StockOnHand : BaseTenantEntity
{
    public Guid BranchId { get; set; }
    public Guid ProductId { get; set; }
    public Guid? BatchId { get; set; }
    public decimal Quantity { get; set; }
    public decimal ReservedQuantity { get; set; }
    public decimal AvailableQuantity => Math.Max(0, Quantity - ReservedQuantity);
    public decimal MinAlertThreshold { get; set; } = 10;
}
