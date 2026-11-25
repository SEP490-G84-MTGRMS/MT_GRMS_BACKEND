using System.Text.Json.Serialization;

namespace MT_GRMS.Application.Admin.Dtos;

public record MasterProductFilterRequest(
    string? Keyword = null,
    string? Brand = null,
    string? CategoryName = null,
    int PageIndex = 1,
    int PageSize = 15
);

public record MasterProductUnitDto(
    Guid Id,
    string UnitName,
    decimal ConversionRate,
    string? Barcode,
    bool IsPurchaseUnit
);

public record MasterProductDto(
    Guid Id,
    string Barcode,
    string Name,
    string Brand,
    string CategoryName,
    string BaseUnit,
    decimal? SuggestedRetailPrice,
    decimal VatRate,
    string TaxCategory,
    string? ImageUrl,
    int UsageCount,
    bool IsActive,
    List<MasterProductUnitDto> Units,
    string? Description = null
);

public record MasterProductDetailDto(
    Guid Id,
    string Barcode,
    string Name,
    string Brand,
    string CategoryName,
    string BaseUnit,
    decimal? SuggestedRetailPrice,
    decimal VatRate,
    string TaxCategory,
    string? ImageUrl,
    string? Description,
    int UsageCount,
    bool IsActive,
    DateTime CreatedAt,
    List<MasterProductUnitDto> Units
);

public record CreateMasterProductUnitRequest(
    string UnitName,
    decimal ConversionRate,
    string? Barcode,
    bool IsPurchaseUnit = true
);

public interface IMasterProductFields
{
    string Name { get; }
    string Brand { get; }
    string CategoryName { get; }
    string BaseUnit { get; }
    decimal? SuggestedRetailPrice { get; }
    decimal VatRate { get; }
    string TaxCategory { get; }
    string? ImageUrl { get; }
    string? Description { get; }
    List<CreateMasterProductUnitRequest>? Units { get; }
}

public record CreateMasterProductRequest(
    string Barcode,
    string Name,
    string Brand,
    string CategoryName,
    string BaseUnit,
    decimal? SuggestedRetailPrice,
    decimal VatRate = 8.00m,
    string TaxCategory = "STANDARD_1PCT",
    string? ImageUrl = null,
    string? Description = null,
    List<CreateMasterProductUnitRequest>? Units = null
) : IMasterProductFields;

public record UpdateMasterProductRequest(
    string Name,
    string Brand,
    string CategoryName,
    string BaseUnit,
    decimal? SuggestedRetailPrice,
    [property: JsonRequired] decimal VatRate,
    string TaxCategory,
    string? ImageUrl,
    string? Description,
    [property: JsonRequired] bool IsActive,
    List<CreateMasterProductUnitRequest>? Units = null
) : IMasterProductFields;

public record MasterCatalogKpiDto(
    int TotalProducts,
    int TotalBrands,
    int TotalUsage,
    decimal ImageCoveragePercent
);

public record MasterProductExcelImportRow(
    int RowNumber,
    string Barcode,
    string Name,
    string Brand,
    string CategoryName,
    string BaseUnit,
    decimal? SuggestedPrice,
    decimal VatRate,
    string? ImageUrl,
    string? PackagingSpecs,
    bool IsSuccess,
    string? ErrorMessage
);

public record MasterProductExcelImportResult(
    int TotalRows,
    int SuccessCount,
    int FailedCount,
    List<string> ErrorMessages,
    List<MasterProductExcelImportRow> Rows
);

public record MasterProductSuggestionDto(
    Guid MasterProductId,
    string Barcode,
    string Name,
    string Brand,
    string CategoryName,
    string BaseUnit,
    decimal? SuggestedRetailPrice,
    decimal VatRate,
    string TaxCategory,
    string? ImageUrl,
    List<MasterProductUnitDto> Units
);
