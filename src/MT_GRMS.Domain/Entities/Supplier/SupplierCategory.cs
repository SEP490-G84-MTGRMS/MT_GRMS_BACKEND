using MT_GRMS.Domain.Common;

namespace MT_GRMS.Domain.Entities.Supplier;

public class SupplierCategory : BaseTenantEntity
{
    public string CategoryCode { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public string? Description { get; set; }

    public ICollection<Supplier> Suppliers { get; set; } = new List<Supplier>();
}
