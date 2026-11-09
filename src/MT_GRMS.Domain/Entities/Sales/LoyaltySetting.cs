using MT_GRMS.Domain.Common;

namespace MT_GRMS.Domain.Entities.Sales;

/// <summary>
/// Bảng LoyaltySettings (Cấu hình hệ thống tích điểm và tiêu điểm theo Tenant)
/// </summary>
public class LoyaltySetting : BaseTenantEntity
{
    public bool IsEnabled { get; set; } = true;
    public decimal EarningRateAmount { get; set; } = 10000.00m; // 10.000 VNĐ = EarningPoints
    public int EarningPoints { get; set; } = 1;
    public int RedemptionRatePoints { get; set; } = 1; // 1 điểm = RedemptionRateAmount VNĐ
    public decimal RedemptionRateAmount { get; set; } = 1000.00m;
    public int MinRedemptionPoints { get; set; } = 20; // Tối thiểu 20 điểm mới được tiêu
    public bool AllowPointsWithPromotions { get; set; } = true;
}

