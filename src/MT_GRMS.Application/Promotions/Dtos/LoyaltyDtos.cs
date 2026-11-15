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

public record LoyaltyLedgerDto(
    string Id,
    string TransactionCode,
    Guid CustomerId,
    string CustomerName,
    string CustomerPhone,
    string Type,
    int PointsChanged,
    int BalanceAfter,
    string? OrderCode,
    string Reason,
    DateTime CreatedAt,
    string CreatedBy
);

public record AdjustLoyaltyPointsRequest(
    string CustomerPhone,
    string AdjustmentType,
    int Points,
    string Reason
);

