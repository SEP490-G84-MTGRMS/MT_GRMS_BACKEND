namespace MT_GRMS.Application.Promotions.Dtos;

public record CartItemDto(
    Guid ProductId,
    string ProductName,
    decimal UnitPrice,
    decimal Quantity
);

public record EvaluateCartRequest(
    List<CartItemDto> Items,
    decimal SubTotal,
    string? CouponCode = null,
    Guid? BranchId = null,
    Guid? CustomerId = null
);

public record CartGiftItemDto(
    Guid ProductId,
    string ProductName,
    decimal Quantity,
    string Reason
);

public record EvaluateCartResponse(
    decimal SubTotal,
    decimal TotalDiscountAmount,
    decimal FinalAmount,
    List<string> AppliedPromotions,
    List<CartGiftItemDto> GiftItems,
    ValidateVoucherResponse? VoucherResult = null
);

