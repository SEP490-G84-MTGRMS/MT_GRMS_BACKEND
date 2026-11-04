using MT_GRMS.Domain.Common;

namespace MT_GRMS.Domain.Entities.Supplier;

public class SupplierBlackoutDate : BaseTenantEntity
{
    public Guid? SupplierDeliveryConfigId { get; set; }
    public SupplierDeliveryConfig? SupplierDeliveryConfig { get; set; }

    public Guid SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string Reason { get; set; } = string.Empty;
    public bool IsRecurringAnnual { get; set; } = false;
}
