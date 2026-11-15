using MT_GRMS.Domain.Common;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Entities.SaaS;

namespace MT_GRMS.Domain.Entities.Inventory;

/// <summary>
/// Phiếu chế biến / chia khay từ tồn nguyên quả (BranchStock).
/// </summary>
public class RepackOrder : BaseTenantEntity
{
    public string Code { get; set; } = string.Empty;
    public Guid BranchId { get; set; }
    public Guid ProductId { get; set; }
    public Guid BranchStockId { get; set; }

    /// <summary>PORTIONING | PROCESSING</summary>
    public string RepackType { get; set; } = "PORTIONING";

    public decimal InputWeight { get; set; } = 0.000m;
    public decimal WasteWeight { get; set; } = 0.000m;
    public string? WasteReason { get; set; }
    public string? WasteNote { get; set; }

    public decimal UnitCostSnapshot { get; set; } = 0.00m;
    public decimal WasteCostAmount { get; set; } = 0.00m;
    public decimal DefaultUnitPricePerKg { get; set; } = 0.00m;

    /// <summary>DRAFT | COMPLETED | CANCELLED</summary>
    public string Status { get; set; } = "COMPLETED";
    public DateTime? CompletedAt { get; set; }

    public Branch? Branch { get; set; }
    public Product? Product { get; set; }
    public BranchStock? BranchStock { get; set; }
    public ICollection<Package> Packages { get; set; } = new List<Package>();
}
