using System;
using System.Collections.Generic;

namespace MT_GRMS.Application.Repacks.Dtos;

public record PackageLineRequest(
    decimal NetWeight,
    decimal SellingPrice,
    DateTime? ExpiryDate = null,
    string? Barcode = null,
    DateTime? ManufactureDate = null
);

public record CreateRepackOrderRequest(
    Guid BranchStockId,
    string RepackType,
    decimal InputWeight,
    IReadOnlyList<PackageLineRequest> Packages,
    decimal WasteWeight = 0m,
    string? WasteReason = null,
    string? WasteNote = null,
    decimal? DefaultUnitPricePerKg = null
);

public record PackageDto(
    Guid Id,
    Guid BranchStockId,
    Guid ProductId,
    Guid BranchId,
    string InternalCode,
    string? Barcode,
    decimal NetWeight,
    decimal SellingPrice,
    DateTime? ManufactureDate,
    DateTime? ExpiryDate,
    string RepackType,
    string Status,
    Guid? RepackOrderId,
    /// <summary>Mã phiếu chế biến (đợt/ca tạo khay), vd RP-20261114-001.</summary>
    string? RepackOrderCode,
    DateTime? SoldAt,
    DateTime CreatedAt
);

public record RepackOrderDto(
    Guid Id,
    string Code,
    Guid BranchId,
    Guid ProductId,
    Guid BranchStockId,
    string RepackType,
    decimal InputWeight,
    decimal WasteWeight,
    string? WasteReason,
    string? WasteNote,
    decimal UnitCostSnapshot,
    decimal WasteCostAmount,
    decimal DefaultUnitPricePerKg,
    string Status,
    DateTime? CompletedAt,
    IReadOnlyList<PackageDto> Packages
);

public record BranchStockPackageSummaryDto(
    Guid BranchStockId,
    Guid ProductId,
    string ProductName,
    string BatchCode,
    string Unit,
    bool IsWeightProduct,
    bool AllowRepack,
    decimal QuantityOnHand,
    decimal QuantityReserved,
    decimal QuantityQuarantined,
    decimal PackagedQty,
    int PackageCount,
    decimal RawSellableQty,
    DateTime? ExpiryDate,
    decimal LandedCostPerUnit,
    decimal? SuggestPricePerKg,
    IReadOnlyList<PackageDto> Packages
);

public record UpdatePackagePriceRequest(decimal SellingPrice);

public record VoidPackageRequest(string Mode, string? Reason = null);
