namespace MT_GRMS.Application.Catalog.Dtos;

public record ProductCreateRequest(
    string SKU,
    string Barcode,
    string ProductName,
    Guid? CategoryId,
    decimal BasePrice,
    decimal CostPrice,
    string UnitName,
    bool HasBatches
);

public record ProductResponse(
    Guid Id,
    string SKU,
    string Barcode,
    string ProductName,
    Guid? CategoryId,
    string? CategoryName,
    decimal BasePrice,
    decimal CostPrice,
    string UnitName,
    bool HasBatches,
    bool IsActive,
    string TaxCategory = "STANDARD_1PCT"
);
