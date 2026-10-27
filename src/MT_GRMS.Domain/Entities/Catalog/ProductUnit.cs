using MT_GRMS.Domain.Common;

namespace MT_GRMS.Domain.Entities.Catalog;

/// <summary>
/// Bảng ProductUnits (Đơn vị tính quy đổi)
/// </summary>
public class ProductUnit : BaseEntity
{
    public Guid ProductId { get; set; }
    public string UnitName { get; set; } = string.Empty;
    public decimal ConversionRate { get; set; } = 1.00m;
    public decimal Price { get; set; } = 0.00m;
    public string? Barcode { get; set; }
    public bool IsBaseUnit { get; set; } = false;

    // Navigation
    public Product? Product { get; set; }
}
