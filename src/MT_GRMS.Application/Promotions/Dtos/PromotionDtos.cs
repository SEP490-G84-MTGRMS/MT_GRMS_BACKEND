namespace MT_GRMS.Application.Promotions.Dtos;

public record PromotionConditionDto(
    Guid? Id,
    Guid ProductId,
    string? ProductName,
    string ProductRole, // BUY, GIFT, DISCOUNT
    decimal MinQty,
    decimal? DiscountValue,
    string? Note
);

public record PromotionCreateRequest(
    string PromotionCode,
    string PromotionName,
    string Type, // ORDER_DISCOUNT, FIXED_AMOUNT, PERCENT, BUY_X_GET_Y, VOUCHER
    decimal DiscountValue,
    decimal MinOrderValue,
    decimal? MaxDiscountAmount,
    int? UsageLimit,
    DateTime StartDate,
    DateTime EndDate,
    bool IsActive = true,
    string? Description = null,
    string TargetBranches = "ALL",
    decimal? BudgetLimit = null,
    List<PromotionConditionDto>? Conditions = null
);

public record PromotionUpdateRequest(
    string PromotionName,
    string Type,
    decimal DiscountValue,
    decimal MinOrderValue,
    decimal? MaxDiscountAmount,
    int? UsageLimit,
    DateTime StartDate,
    DateTime EndDate,
    bool IsActive,
    string? Description = null,
    string TargetBranches = "ALL",
    decimal? BudgetLimit = null,
    List<PromotionConditionDto>? Conditions = null
);

public record PromotionResponse(
    Guid Id,
    string PromotionCode,
    string PromotionName,
    string Type,
    decimal DiscountValue,
    decimal MinOrderValue,
    decimal? MaxDiscountAmount,
    int? UsageLimit,
    int UsedCount,
    DateTime StartDate,
    DateTime EndDate,
    bool IsActive,
    string? Description,
    string TargetBranches,
    decimal? BudgetLimit,
    decimal TotalDiscountGiven,
    DateTime CreatedAt,
    List<PromotionConditionDto> Conditions
);

public class PromotionFilterQuery
{
    public string? SearchTerm { get; set; }
    public string? Type { get; set; } // ALL, ORDER_DISCOUNT, BUY_X_GET_Y, etc.
    public string? Status { get; set; } // ALL, ACTIVE, UPCOMING, EXPIRED, PAUSED
    public int PageIndex { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

