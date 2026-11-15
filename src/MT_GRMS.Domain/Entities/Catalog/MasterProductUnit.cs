using MT_GRMS.Domain.Common;

namespace MT_GRMS.Domain.Entities.Catalog;

/// <summary>
/// Thực thể Quy cách đóng gói / Đơn vị tính mẫu toàn cầu
/// </summary>
public class MasterProductUnit : BaseEntity
{
    public Guid MasterProductId { get; set; }
    public string UnitName { get; set; } = string.Empty;
    public decimal ConversionRate { get; set; } = 1.00m;
    public string? Barcode { get; set; }
    public bool IsPurchaseUnit { get; set; } = true;

    // Navigation
    public MasterProduct? MasterProduct { get; set; }
}
