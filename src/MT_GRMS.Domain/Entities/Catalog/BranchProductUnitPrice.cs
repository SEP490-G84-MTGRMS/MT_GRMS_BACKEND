using MT_GRMS.Domain.Common;

namespace MT_GRMS.Domain.Entities.Catalog;

/// <summary>
/// Override giá bán theo từng ĐVT tại một BranchProduct.
/// Không có dòng = dùng giá Master ProductUnits.Price.
/// </summary>
public class BranchProductUnitPrice : BaseTenantEntity
{
    public Guid BranchProductId { get; set; }
    public Guid ProductUnitId { get; set; }
    public decimal SellingPriceOverride { get; set; }

    public BranchProduct? BranchProduct { get; set; }
    public ProductUnit? ProductUnit { get; set; }
}
