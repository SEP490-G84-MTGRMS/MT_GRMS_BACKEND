using System.ComponentModel.DataAnnotations.Schema;
using MT_GRMS.Domain.Common;

namespace MT_GRMS.Domain.Entities.Catalog;

/// <summary>
/// Bảng Categories (Cây danh mục ngành hàng đa cấp)
/// </summary>
public class Category : BaseTenantEntity
{
    public string CategoryCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int DisplayOrder { get; set; } = 0;
    public Guid? ParentCategoryId { get; set; }

    // Backward compatibility alias
    [NotMapped]
    public string CategoryName { get => Name; set => Name = value; }

    // Navigations
    public Category? ParentCategory { get; set; }
    public ICollection<Category> SubCategories { get; set; } = new List<Category>();
    public ICollection<Product> Products { get; set; } = new List<Product>();
}
