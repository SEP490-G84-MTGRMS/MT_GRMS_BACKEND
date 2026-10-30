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
    public int PageSize { get; init; } = 10;
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
    string Unit,
    decimal CostPrice,
    decimal TotalValuation,
    string Status, // Expired, Critical, Warning, Normal
    string? ZoneArea,
    string? ShelfLocation,
    string? OriginalGrnNo
);

public record BatchPagedResultDto(
    IReadOnlyList<BatchListItemDto> Items,
    int TotalCount,
    int PageIndex,
    int PageSize,
    BatchKpiSummaryDto KpiSummary
);

