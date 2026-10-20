using MT_GRMS.Domain.Common;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Domain.Entities.Supplier;

/// <summary>
/// Bảng liên kết N-N giữa Nhà Cung Cấp và Sản Phẩm (REQ-SP01 đến REQ-SP06)
/// Độc quyền Primary Supplier (BR-SP02), Chặn trùng lặp (BR-SP01), Cô lập đa Tenant (REQ-SP06).
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
    public bool IsPrimarySupplier { get; set; } = false;
    public SupplierProductStatus Status { get; set; } = SupplierProductStatus.Active;
}
