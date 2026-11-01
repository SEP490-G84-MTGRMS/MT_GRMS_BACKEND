using MT_GRMS.Domain.Common;
using MT_GRMS.Domain.Entities.Catalog;

namespace MT_GRMS.Domain.Entities.Sales;

/// <summary>
/// Bảng PromotionConditions (Hiện thực hóa quan hệ eligible_for M:N giữa Promotion và Product)
/// </summary>
public class PromotionCondition : BaseTenantEntity
{
    public Guid PromotionId { get; set; }
    public Guid ProductId { get; set; }
    public string ProductRole { get; set; } = "BUY"; // BUY, GIFT, DISCOUNT
    public decimal MinQty { get; set; } = 1.00m;
    public decimal? DiscountValue { get; set; }
    public string? Note { get; set; }

    // Navigations
    public Promotion? Promotion { get; set; }
    public Product? Product { get; set; }
}

