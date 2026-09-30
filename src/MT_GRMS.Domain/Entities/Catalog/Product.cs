using MT_GRMS.Domain.Common;

namespace MT_GRMS.Domain.Entities.Catalog;

public class Product : BaseTenantEntity
{
    public string SKU { get; set; } = string.Empty;
    public string Barcode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public Guid? CategoryId { get; set; }
    public decimal BasePrice { get; set; }
    public decimal CostPrice { get; set; }
    public string UnitName { get; set; } = "Cái";
    public bool HasBatches { get; set; } = false;
    public bool IsActive { get; set; } = true;
    public Category? Category { get; set; }
}

public class Category : BaseTenantEntity
{
    public string CategoryCode { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public Guid? ParentCategoryId { get; set; }
    public ICollection<Product> Products { get; set; } = new List<Product>();
}
