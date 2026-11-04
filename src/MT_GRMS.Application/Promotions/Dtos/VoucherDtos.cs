namespace MT_GRMS.Application.Promotions.Dtos;

public record VoucherBatchCreateRequest(
    string BatchCode,
    string Name,
    string DiscountType, // FIXED_AMOUNT, PERCENT
    decimal DiscountValue,
    decimal MinOrderValue,
    decimal? MaxDiscountAmount,
    int TotalQuantity,
    DateTime StartDate,
    DateTime EndDate,
    string Prefix,
    string? Description = null
);

public record VoucherBatchResponse(
    Guid Id,
    string BatchCode,
    string Name,
    string DiscountType,
    decimal DiscountValue,
    decimal MinOrderValue,
    decimal? MaxDiscountAmount,
    int TotalQuantity,
    int UsedQuantity,
    DateTime StartDate,
    DateTime EndDate,
    string Status, // ACTIVE, EXPIRED, PAUSED, DEPLETED
    string Prefix,
    string? Description,
    DateTime CreatedAt
);

public record GenerateVoucherCodesRequest(
    int Quantity,
    string? Prefix = null
);

public record VoucherCodeDto(
    Guid Id,
    Guid PromotionId,
    string Code,
    string Status, // UNUSED, USED, EXPIRED, LOCKED
    Guid? CustomerId,
    string? CustomerName,
    string? CustomerPhone,
    Guid? OrderId,
    string? OrderCode,
    DateTime? UsedAt,
    DateTime CreatedAt
);

public record ValidateVoucherRequest(
    string Code,
    decimal CartTotal,
    Guid? CustomerId = null
);

public record ValidateVoucherResponse(
    bool IsValid,
    string Message,
    decimal DiscountAmount,
    string? Code = null,
    Guid? PromotionId = null,
    string? PromotionName = null,
    string? DiscountType = null,
    decimal? DiscountValue = null
);

public record RedeemVoucherRequest(
    string Code,
    Guid OrderId,
    string OrderCode,
    Guid? CustomerId = null,
    string? CustomerName = null,
    string? CustomerPhone = null
);

public record RedeemVoucherResponse(
    bool Success,
    string Message,
    string Code,
    DateTime? RedeemedAt = null
);

