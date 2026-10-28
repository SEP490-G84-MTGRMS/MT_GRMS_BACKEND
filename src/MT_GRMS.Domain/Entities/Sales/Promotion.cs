using MT_GRMS.Domain.Common;

namespace MT_GRMS.Domain.Entities.Sales;

/// <summary>
/// Bảng Promotions (Chương trình khuyến mại & Voucher)
/// </summary>
public class Promotion : BaseTenantEntity
{
    public string PromotionCode { get; set; } = string.Empty;
    public string PromotionName { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public decimal DiscountValue { get; set; } = 0.00m;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public bool IsActive { get; set; } = true;
}
