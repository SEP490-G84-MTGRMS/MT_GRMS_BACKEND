using MT_GRMS.Domain.Common;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Domain.Entities.Supplier;

public class SupplierBranchAssignment : BaseTenantEntity
{
    public Guid SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    public Guid BranchId { get; set; }
    public Branch? Branch { get; set; }

    public SupplierStatus Status { get; set; } = SupplierStatus.Active;
    public string? InactiveReason { get; set; }
}
