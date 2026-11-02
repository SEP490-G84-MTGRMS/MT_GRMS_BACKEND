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

    /// <summary>
    /// Constructor mặc định cho EF Core và Object Initializer
    /// </summary>
    public ProductUnit() { }

    /// <summary>
    /// Constructor tiện ích để khởi tạo đơn vị tính quy đổi
    /// </summary>
    public ProductUnit(
        Guid productId,
        string unitName,
        decimal conversionRate,
        decimal price,
        string? barcode = null,
        bool isBaseUnit = false)
    {
        ProductId = productId;
        UnitName = unitName;
        ConversionRate = isBaseUnit ? 1.00m : conversionRate;
        Price = price;
        Barcode = barcode;
        IsBaseUnit = isBaseUnit;
    }
}
