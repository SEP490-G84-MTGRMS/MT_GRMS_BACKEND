using System.ComponentModel.DataAnnotations.Schema;
using MT_GRMS.Domain.Common;

namespace MT_GRMS.Domain.Entities.Catalog;

/// <summary>
/// Bảng Categories (Danh mục ngành hàng dạng phẳng, không phân cấp cha con)
/// </summary>
public class Category : BaseTenantEntity
{
    public string CategoryCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    // Backward compatibility alias
    [NotMapped]
    public string CategoryName { get => Name; set => Name = value; }

    // Navigations
    public ICollection<Product> Products { get; set; } = new List<Product>();
}
