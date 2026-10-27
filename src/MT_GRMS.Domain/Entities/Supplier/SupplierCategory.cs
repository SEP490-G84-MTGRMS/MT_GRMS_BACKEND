using System.ComponentModel.DataAnnotations.Schema;
using MT_GRMS.Domain.Common;

namespace MT_GRMS.Domain.Entities.Supplier;

/// <summary>
/// Bảng SupplierCategories (Nhóm ngành hàng nhà cung cấp)
/// </summary>
public class SupplierCategory : BaseTenantEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    // Backward compatibility aliases
    [NotMapped]
    public string CategoryCode { get => Code; set => Code = value; }
    [NotMapped]
    public string CategoryName { get => Name; set => Name = value; }

    public ICollection<Supplier> Suppliers { get; set; } = new List<Supplier>();
}
