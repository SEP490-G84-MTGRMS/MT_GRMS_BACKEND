using System;
using System.Collections.Generic;
using MT_GRMS.Application.Debt.Dtos;
using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Application.PosOrders.Dtos;

public record OrderCreateRequest(
    Guid? BranchId,
    Guid? CustomerId,
    string PaymentMethod,
    List<OrderItemCreateDto> Items,
    decimal? CashTendered = null,
    decimal? DiscountAmount = null,
    string? ManagerBypassCode = null,
    string? Note = null,
    string? CreatorRole = null
)
{
    // Hỗ trợ tương thích ngược cho StoreId
    public Guid StoreId => BranchId ?? Guid.Empty;
}

public record OrderItemCreateDto(
    Guid ProductId,
    decimal Quantity,
    decimal UnitPrice,
    decimal? DiscountAmount = null,
    string? ProductName = null
);

public record OrderResponse(
    Guid Id,
    string OrderCode,
    Guid BranchId,
    string? BranchName,
    Guid? CustomerId,
    string? CustomerName,
    DateTime OrderDate,
    decimal TotalAmount,
    decimal DiscountAmount,
    decimal FinalAmount,
    decimal CashTendered,
    decimal ChangeAmount,
    string PaymentMethod,
    string PaymentStatus,
    string Status,
    List<OrderItemResponse> Items,
    string? CustomerPhone = null,
    decimal DepositAmount = 0m,
    string? Note = null,
    CustomerDebtInvoiceResponse? DebtInvoice = null
)
{
    public Guid StoreId => BranchId;
}

public record OrderItemResponse(
    Guid Id,
    Guid ProductId,
    string ProductName,
    string? Barcode,
    string? UnitName,
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountAmount,
    decimal ActualPrice,
    decimal CostPriceAtSale,
    decimal TotalLineAmount,
    decimal AppliedVatRate = 0m,
    decimal VatAmount = 0m,
    string? BatchCode = null
);

public record OrderFilterRequest(
    Guid? BranchId = null,
    Guid? CustomerId = null,
    string? PaymentMethod = null,
    string? Status = null,
    DateTime? FromDate = null,
    DateTime? ToDate = null,
    int PageNumber = 1,
    int PageSize = 20
);

public record PrintOrderRequest(
    string? PrinterName = "POS-58",
    decimal? CashTendered = null
);

public record PreOrderCreateRequest(
    Guid BranchId,
    Guid CustomerId,
    List<OrderItemCreateDto> Items,
    decimal DepositAmount,
    DateTime ExpectedPickupDate,
    string PaymentMethod = "CASH",
    string? Note = null
);

public record PreOrderFulfillRequest(
    string PaymentMethod,
    decimal AmountPaid,
    string? Note = null
);

public record CancelOrderRequest(
    string Reason,
    string? Note = null,
    string? UserRole = null
);

public record ReturnExchangeRequest(
    List<ReturnItemDto> ReturnedItems,
    List<OrderItemCreateDto> NewItems,
    string SettlementMethod = "CASH",
    string? Note = null
);

public record ReturnItemDto(
    Guid ProductId,
    decimal Quantity,
    decimal UnitPrice
);

public record OrderSummaryResponse(
    int TotalOrders,
    decimal TotalRevenue,
    decimal CashRevenue,
    decimal TransferRevenue,
    int PendingPreOrdersCount,
    int CompletedOrdersCount,
    int CancelledOrdersCount
);
