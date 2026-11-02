using MT_GRMS.Domain.Common;

namespace MT_GRMS.Domain.Entities.Supplier;

public class SupplierDeliveryConfig : BaseTenantEntity
{
    public Guid SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    public int LeadTimeHours { get; set; } = 24;
    public int LeadTimeDays { get; set; } = 1;
    public int CutoffDayOffset { get; set; } = 1;
    public string CutoffTime { get; set; } = "17:00";
    public string OrderCutoffTime { get; set; } = "17:00";
    public int DeliveryWeekdaysMask { get; set; } = 127; // 7 days (binary 1111111)
    public string DeliveryDaysJson { get; set; } = "[\"Mon\",\"Tue\",\"Wed\",\"Thu\",\"Fri\",\"Sat\"]";

    public int MinOrderQuantity { get; set; } = 0;

    public bool AllowUrgent { get; set; } = false;
    public int UrgentLeadTimeHours { get; set; } = 4;
    public decimal UrgentFeePercent { get; set; } = 0;

    public ICollection<SupplierBlackoutDate> BlackoutDates { get; set; } = new List<SupplierBlackoutDate>();
}
