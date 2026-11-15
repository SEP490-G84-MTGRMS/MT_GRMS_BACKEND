using MT_GRMS.Domain.Common;
using MT_GRMS.Domain.Entities.Catalog;

namespace MT_GRMS.Domain.Entities.Supplier;

/// <summary>
/// Tỷ lệ chia sẻ chi phí đổi hàng theo nhóm sản phẩm (Category) trong chính sách trả/đổi của NCC.
/// </summary>
public class SupplierReturnPolicyCategoryCostShare : BaseTenantEntity
{
    public Guid PolicyId { get; set; }
    public Guid CategoryId { get; set; }

    public decimal StoreCostSharePercent { get; set; }
    public decimal SupplierCostSharePercent { get; set; }

    public SupplierReturnPolicy? Policy { get; set; }
    public Category? Category { get; set; }
}
