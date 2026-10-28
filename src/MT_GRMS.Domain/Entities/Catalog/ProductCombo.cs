using MT_GRMS.Domain.Common;

namespace MT_GRMS.Domain.Entities.Catalog;

/// <summary>
/// Bảng ProductCombos (Định mức Combo BOM chuẩn 3NF)
/// </summary>
public class ProductCombo : BaseTenantEntity
{
    public Guid ComboProductId { get; set; }
    public Guid ComponentProductId { get; set; }
    public decimal Quantity { get; set; } = 1.00m;
    public string? Note { get; set; }

    // Navigations
    public Product? ComboProduct { get; set; }
    public Product? ComponentProduct { get; set; }
}
