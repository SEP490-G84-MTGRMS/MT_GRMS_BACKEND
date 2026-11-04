using MT_GRMS.Domain.Common;
using MT_GRMS.Domain.Entities.SaaS;

namespace MT_GRMS.Domain.Entities.Catalog;

/// <summary>
/// Catalog kinh doanh tại chi nhánh: gán Product ↔ Branch + override giá bán / trạng thái.
/// Tồn kho theo lô nằm ở <see cref="Inventory.BranchStock"/>.
/// </summary>
public class BranchProduct : BaseTenantEntity
{
    public Guid BranchId { get; set; }
    public Guid ProductId { get; set; }

    /// <summary>NULL = dùng giá Master Product.SellingPrice.</summary>
    public decimal? SellingPriceOverride { get; set; }

    public string Status { get; set; } = "Active";
    public string? ShelfLocation { get; set; }

    public Branch? Branch { get; set; }
    public Product? Product { get; set; }
    public ICollection<BranchProductUnitPrice> UnitPriceOverrides { get; set; } = new List<BranchProductUnitPrice>();
}
