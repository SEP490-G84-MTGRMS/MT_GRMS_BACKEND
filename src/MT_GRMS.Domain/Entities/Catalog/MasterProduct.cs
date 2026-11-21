using MT_GRMS.Domain.Common;

namespace MT_GRMS.Domain.Entities.Catalog;

/// <summary>
/// Thực thể Sản phẩm mẫu toàn cầu (Global FMCG Master Product)
/// Dữ liệu toàn cục của nền tảng (Platform-scoped), không gắn TenantId.
/// </summary>
public class MasterProduct : BaseEntity
{
    public string Barcode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Brand { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public string BaseUnit { get; set; } = "Cái";
    public decimal? SuggestedRetailPrice { get; set; }
    public decimal VatRate { get; set; } = 8.00m;
    public string TaxCategory { get; set; } = "STANDARD_1PCT";
    public string? ImageUrl { get; set; }
    public string? Description { get; set; }
    public int UsageCount { get; set; } = 0;
    public bool IsActive { get; set; } = true;

    // Navigation
    public ICollection<MasterProductUnit> Units { get; set; } = new List<MasterProductUnit>();
}
