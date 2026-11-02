using MT_GRMS.Application.Debt.Dtos;
using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Application.PosOrders.Dtos;

public record OrderCreateRequest(
    Guid? BranchId,
    Guid? CustomerId,
    string PaymentMethod, // "CASH", "BANK", "CREDIT"
    decimal DiscountAmount,
    List<OrderItemCreateDto> Items,
    string? ManagerBypassCode,
    string? Note
);

public record OrderItemCreateDto(
    Guid ProductId,
    string ProductName,
    decimal UnitPrice,
    decimal Quantity,
    decimal DiscountAmount = 0m
);

public record OrderResponse(
    Guid Id,
    string OrderCode,
    Guid BranchId,
    Guid? CustomerId,
    string? CustomerName,
    decimal TotalAmount,
    decimal DiscountAmount,
    decimal FinalAmount,
    string PaymentMethod,
    string PaymentStatus,
    string Status,
    DateTime OrderDate,
    List<OrderItemResponse> Items,
    CustomerDebtInvoiceResponse? DebtInvoice
);

public record OrderItemResponse(
    Guid Id,
    Guid ProductId,
    string ProductName,
    decimal UnitPrice,
    decimal Quantity,
    decimal DiscountAmount,
    decimal ActualPrice,
    decimal CostPriceAtSale,
    decimal TotalLineAmount,
    decimal AppliedVatRate,
    decimal VatAmount
);
