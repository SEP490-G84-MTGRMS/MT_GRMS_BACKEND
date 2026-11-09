using System.Text.Json.Serialization;

namespace MT_GRMS.Application.Promotions.Dtos;

public record CartItemDto(
    Guid ProductId,
    string ProductName,
    decimal UnitPrice,
    decimal Quantity,
    decimal ConversionRate = 1.00m
);

public record EvaluateCartRequest(
    List<CartItemDto> Items,
    decimal SubTotal,
    string? CouponCode = null,
    string? BranchId = null,
    string? CustomerId = null
)
{
    public EvaluateCartRequest(
        List<CartItemDto> items,
        decimal subTotal,
        string? couponCode,
        Guid? branchId,
        Guid? customerId
    ) : this(items, subTotal, couponCode, branchId?.ToString(), customerId?.ToString())
    {
    }
}

public record CartGiftItemDto(
    Guid ProductId,
    string ProductName,
    decimal Quantity,
    string Reason,
    Guid? PromotionId = null,
    string? UnitName = null,
    decimal EquivalentValue = 0m
);

public record CartItemDiscountDto(
    Guid ProductId,
    decimal DiscountAmount
);

public record EvaluateCartResponse(
    decimal SubTotal,
    decimal TotalDiscountAmount,
    decimal FinalAmount,
    List<string> AppliedPromotions,
    List<CartGiftItemDto> GiftItems,
    ValidateVoucherResponse? VoucherResult = null,
    List<CartItemDiscountDto>? ItemDiscounts = null,
    Guid? AppliedPromotionId = null
);

