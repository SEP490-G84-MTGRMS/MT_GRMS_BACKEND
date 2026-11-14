using System;
using System.Collections.Generic;
using MT_GRMS.Application.Common;

namespace MT_GRMS.Application.Batches.Dtos;

public record BatchCreateRequest(
    Guid ProductId,
    Guid BranchId,
    string BatchCode,
    DateTime ManufactureDate,
    DateTime ExpiryDate,
    decimal InitialQuantity,
    decimal LandedCostPerUnit
);

public record BatchResponse(
    Guid Id,
    Guid ProductId,
    Guid BranchId,
    string BatchCode,
    DateTime ManufactureDate,
    DateTime ExpiryDate,
    decimal InitialQuantity,
    decimal LandedCostPerUnit,
    int DaysUntilExpiry,
    bool IsExpired
);

public record ExpiryAlertDto(
    Guid BatchId,
    string BatchCode,
    string ProductName,
    DateTime ExpiryDate,
    int DaysRemaining,
    decimal RemainingQuantity
);

public record BatchFilterQuery
{
    public string? Keyword { get; init; }
    public Guid? BranchId { get; init; }
    public string? Status { get; init; } // ALL, Expired, Critical, Warning, Normal
    public int PageIndex { get; init; } = 1;
    public int PageSize { get; init; } = 20;
    public string? SortBy { get; init; }
    public string? SortOrder { get; init; }
}

public record BatchKpiSummaryDto(
    int TotalBatches,
    int ExpiredCount,
    int CriticalCount,
    int WarningCount,
    int NormalCount,
    decimal ExpiredValue
);

public record BatchListItemDto(
    Guid Id,
    string BatchCode,
    Guid ProductId,
    string Sku,
    string ProductName,
    string CategoryName,
    Guid BranchId,
    string BranchName,
    Guid? SupplierId,
    string SupplierName,
    DateTime MfgDate,
    DateTime ExpiryDate,
    int DaysLeft,
    decimal QtyInitial,
    decimal QtyCurrent,
    /// <summary>Đơn vị nhập (purchase unit).</summary>
    string Unit,
    /// <summary>Đơn vị tồn / gốc (base unit) — số tồn đang tính theo đơn vị này.</summary>
    string StockUnit = "",
    /// <summary>1 đơn vị nhập = bao nhiêu đơn vị tồn. Ví dụ Thùng→Chai = 24.</summary>
    decimal PurchaseToStockRate = 1m,
    decimal CostPrice = 0m,
    decimal TotalValuation = 0m,
    string Status = "Normal", // Expired, Critical, Warning, Normal
    string? ZoneArea = null,
    string? ShelfLocation = null,
    string? OriginalGrnNo = null,
    decimal QtyReserved = 0m,
    decimal QtyQuarantined = 0m,
    decimal QtyAvailable = 0m,
    Guid? OriginalGrnId = null,
    bool IsWeightProduct = false,
    bool AllowRepack = false,
    decimal PackagedQty = 0m,
    int PackageCount = 0,
    decimal RawSellableQty = 0m
);

public record BatchPagedResultDto(
    IReadOnlyList<BatchListItemDto> Items,
    int TotalCount,
    int PageIndex,
    int PageSize,
    BatchKpiSummaryDto KpiSummary
);

