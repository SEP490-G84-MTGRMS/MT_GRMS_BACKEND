using System.ComponentModel.DataAnnotations.Schema;
using MT_GRMS.Domain.Common;

namespace MT_GRMS.Domain.Entities.Catalog;

/// <summary>
/// Bảng Products (Mặt hàng sản phẩm chuẩn 3NF)
/// </summary>
public class Product : BaseTenantEntity
{
    public Guid CategoryId { get; set; }
    public string ProductCode { get; set; } = string.Empty;
    public string Barcode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string BaseUnit { get; set; } = "Cái";
    public decimal? CostPrice { get; set; }
    public decimal SellingPrice { get; set; } = 0.00m;
    public decimal MinStock { get; set; } = 0.00m;
    public decimal MaxStock { get; set; } = 1000.00m;
    public decimal VatRate { get; set; } = 0.00m;
    public string ProductType { get; set; } = "STANDARD";
    public string Status { get; set; } = "Active";
    public string? ImageUrl { get; set; }
    public string? Description { get; set; }
    public string? InternalNote { get; set; }
    public bool HasBatches { get; set; } = false;
    public bool IsFavorite { get; set; } = false;

    // Backward compatibility aliases
    [NotMapped]
    public string SKU { get => ProductCode; set => ProductCode = value; }
    [NotMapped]
    public string ProductName { get => Name; set => Name = value; }
    [NotMapped]
    public decimal BasePrice { get => SellingPrice; set => SellingPrice = value; }
    [NotMapped]
    public string UnitName { get => BaseUnit; set => BaseUnit = value; }
    [NotMapped]
    public bool IsActive { get => Status == "Active"; set => Status = value ? "Active" : "Inactive"; }

    // Navigations
    public Category? Category { get; set; }
    public ICollection<ProductUnit> Units { get; set; } = new List<ProductUnit>();
}
