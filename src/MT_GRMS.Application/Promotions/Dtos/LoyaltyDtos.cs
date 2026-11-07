namespace MT_GRMS.Application.Promotions.Dtos;

public record LoyaltyConfigDto(
    bool IsEnabled,
    decimal EarningRateAmount,
    int EarningPoints,
    int RedemptionRatePoints,
    decimal RedemptionRateAmount,
    int MinRedemptionPoints,
    bool AllowPointsWithPromotions
);

public record UpdateLoyaltyConfigRequest(
    bool IsEnabled,
    decimal EarningRateAmount,
    int EarningPoints,
    int RedemptionRatePoints,
    decimal RedemptionRateAmount,
    int MinRedemptionPoints,
    bool AllowPointsWithPromotions
);

