using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Application.PosOrders.Dtos;

public record OrderCreateRequest(
    Guid StoreId,
    PaymentMethod PaymentMethod,
    List<OrderItemCreateDto> Items,
    string? Note
);

public record OrderItemCreateDto(
    Guid ProductId,
    string ProductName,
    decimal UnitPrice,
    decimal Quantity
);

public record OrderResponse(
    Guid Id,
    string OrderCode,
    Guid StoreId,
    decimal TotalAmount,
    PaymentStatus PaymentStatus,
    DateTime CreatedAt,
    List<OrderItemResponse> Items
);

public record OrderItemResponse(
    Guid Id,
    Guid ProductId,
    string ProductName,
    decimal UnitPrice,
    decimal Quantity,
    decimal TotalLineAmount
);
