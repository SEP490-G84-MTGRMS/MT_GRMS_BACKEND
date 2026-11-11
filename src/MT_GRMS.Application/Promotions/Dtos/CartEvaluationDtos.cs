using System.Text.Json.Serialization;

namespace MT_GRMS.Application.Promotions.Dtos;

public record CartItemDto
{
    public Guid ProductId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public decimal UnitPrice { get; init; }
    public decimal Quantity { get; init; }
    public decimal ConversionRate { get; init; } = 1.00m;
    public string? Unit { get; init; }

    public CartItemDto() { }

    public CartItemDto(
        Guid productId,
        string productName,
        decimal unitPrice,
        decimal quantity,
        decimal conversionRate = 1.00m,
        string? unit = null)
    {
        ProductId = productId;
        ProductName = productName;
        UnitPrice = unitPrice;
        Quantity = quantity;
        ConversionRate = conversionRate;
        Unit = unit;
    }
}

public record EvaluateCartRequest(
    List<CartItemDto> Items,
    decimal SubTotal,
    string? CouponCode = null,
    string? BranchId = null,
    string? CustomerId = null
);

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

