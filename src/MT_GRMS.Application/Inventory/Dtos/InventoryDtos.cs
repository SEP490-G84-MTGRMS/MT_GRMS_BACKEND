using System;
using System.Collections.Generic;

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

public record InventoryFilterQuery
{
    public string? Keyword { get; init; }
    public Guid? BranchId { get; init; }
    public string? Category { get; init; }
    public string? Status { get; init; } // ALL, OK, LOW_STOCK, OUT_OF_STOCK, NEAR_EXPIRY
    public int PageIndex { get; init; } = 1;
    public int PageSize { get; init; } = 10;
}

public record InventoryBatchSummaryDto(
    Guid BatchId,
    string BatchCode,
    DateTime MfgDate,
    DateTime ExpiryDate,
    int DaysLeft,
    decimal AvailableQty,
    string Status
);

public record InventoryStockItemDto(
    Guid ProductId,
    string Sku,
    string Barcode,
    string ProductName,
    string CategoryName,
    Guid BranchId,
    string BranchName,
    string Unit,
    decimal PhysicalQty,
    decimal AvailableQty,
    decimal PendingReturnQty,
    decimal ExpiredQty,
    decimal DamagedQty,
    decimal MinStock,
    decimal AvgCost,
    decimal TotalValue,
    string Status, // OK, LOW_STOCK, OUT_OF_STOCK, NEAR_EXPIRY
    IReadOnlyList<InventoryBatchSummaryDto> Batches
);

public record InventoryKpiSummaryDto(
    int TotalSkus,
    decimal TotalValue,
    int LowStockCount,
    int OutOfStockCount,
    int ExpiryWarningCount
);

public record InventoryPagedResultDto(
    IReadOnlyList<InventoryStockItemDto> Items,
    int TotalCount,
    int PageIndex,
    int PageSize,
    InventoryKpiSummaryDto KpiSummary
);

