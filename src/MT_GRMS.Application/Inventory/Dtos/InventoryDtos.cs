namespace MT_GRMS.Application.Inventory.Dtos;

public record StockAdjustmentRequest(
    Guid BranchId,
    Guid ProductId,
    Guid? BatchId,
    decimal ActualQuantity,
    string Reason
);

public record StockOnHandResponse(
    Guid Id,
    Guid BranchId,
    Guid ProductId,
    string ProductName,
    string SKU,
    Guid? BatchId,
    string? BatchCode,
    decimal Quantity,
    decimal ReservedQuantity,
    decimal AvailableQuantity,
    bool IsBelowThreshold
);
