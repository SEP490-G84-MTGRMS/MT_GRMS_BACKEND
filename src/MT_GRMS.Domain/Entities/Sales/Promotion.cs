using MT_GRMS.Domain.Common;

namespace MT_GRMS.Domain.Entities.Sales;

/// <summary>
/// Bảng Promotions (Chương trình khuyến mại & Chiến dịch Voucher chuẩn 3NF)
/// </summary>
public class Promotion : BaseTenantEntity
{
    public string PromotionCode { get; set; } = string.Empty;
    public string PromotionName { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // ORDER_DISCOUNT, FIXED_AMOUNT, PERCENT, BUY_X_GET_Y, VOUCHER
    public decimal DiscountValue { get; set; } = 0.00m;
    public decimal MinOrderValue { get; set; } = 0.00m;
    public decimal? MaxDiscountAmount { get; set; }
    public int? UsageLimit { get; set; }
    public int UsedCount { get; set; } = 0;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Description { get; set; }
    public string TargetBranches { get; set; } = "ALL";
    public decimal? BudgetLimit { get; set; }
    public decimal TotalDiscountGiven { get; set; } = 0.00m;
    public string? VoucherPrefix { get; set; }
    public int TotalVoucherQuantity { get; set; } = 0;

    // Navigations
    public ICollection<PromotionCondition> Conditions { get; set; } = new List<PromotionCondition>();
    public ICollection<VoucherCode> VoucherCodes { get; set; } = new List<VoucherCode>();
    public ICollection<OrderDetail> OrderDetails { get; set; } = new List<OrderDetail>();
}
