using MT_GRMS.Domain.Common;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Entities.SaaS;

namespace MT_GRMS.Domain.Entities.Inventory;

/// <summary>
/// Bảng BranchStocks (Tồn kho chi nhánh theo Lô - FEFO)
/// </summary>
public class BranchStock : BaseTenantEntity
{
    public Guid BranchId { get; set; }
    public Guid ProductId { get; set; }
    public Guid? SupplierId { get; set; }
    public string? BatchCode { get; set; }
    public DateTime? ManufactureDate { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public decimal QuantityOnHand { get; set; } = 0.000m;
    public decimal QuantityReserved { get; set; } = 0.000m;
    public decimal LandedCostPerUnit { get; set; } = 0.00m;
    public string? ShelfLocation { get; set; }
    public string? ZoneArea { get; set; }
    public int RowVersion { get; set; } = 1;

    // Navigations
    public Branch? Branch { get; set; }
    public Product? Product { get; set; }
    public Supplier.Supplier? Supplier { get; set; }
}
