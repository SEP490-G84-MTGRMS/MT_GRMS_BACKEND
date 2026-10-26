using MT_GRMS.Domain.Common;

namespace MT_GRMS.Domain.Entities.Finance;

public class CustomerDebtAllocation : BaseTenantEntity
{
    public Guid DebtEntryId { get; set; }
    public Guid DebtInvoiceId { get; set; }
    public decimal AllocatedAmount { get; set; }
    public string? Notes { get; set; }
}
