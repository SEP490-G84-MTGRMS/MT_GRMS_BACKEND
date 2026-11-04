using MT_GRMS.Domain.Common;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Domain.Entities.Supplier;

/// <summary>
/// Bảng SupplierProducts (Chính sách giá nhập sỉ & MOQ của nhà cung cấp)
/// </summary>
public class SupplierProduct : BaseTenantEntity
{
    public Guid SupplierId { get; set; }
    public Domain.Entities.Supplier.Supplier Supplier { get; set; } = null!;

    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public string? SupplierProductCode { get; set; }
    public string DeliveryUoM { get; set; } = "Cái";
    public decimal AgreedWholesalePrice { get; set; }
    public int MinimumOrderQuantity { get; set; } = 1;
    public Guid? DeliveryUomId { get; set; }
    public decimal ConversionRate { get; set; } = 1.000m;
    public bool IsPrimarySupplier { get; set; } = false;
    public SupplierProductStatus Status { get; set; } = SupplierProductStatus.Active;
    public int RowVersion { get; set; } = 1;

    // Navigation
    public ProductUnit? DeliveryUnit { get; set; }
}
