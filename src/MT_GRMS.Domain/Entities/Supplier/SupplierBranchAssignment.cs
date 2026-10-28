using MT_GRMS.Domain.Common;
using MT_GRMS.Domain.Entities.SaaS;

namespace MT_GRMS.Domain.Entities.Supplier;

public class SupplierBranchAssignment : BaseTenantEntity
{
    public Guid SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    public Guid BranchId { get; set; }
    public Branch? Branch { get; set; }
}
