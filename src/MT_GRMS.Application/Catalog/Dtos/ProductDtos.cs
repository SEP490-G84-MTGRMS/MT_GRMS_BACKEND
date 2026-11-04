using System.Text.Json.Serialization;

namespace MT_GRMS.Application.Catalog.Dtos;

public record ProductResponse(
    Guid Id,
    string ProductCode,
    string? Barcode,
    string Name,
    Guid CategoryId,
    string CategoryName,
    string BaseUnit,
    decimal SellingPrice,
    decimal? CostPrice,
    string Status,
    string? ImageUrl,
    string? Description,
    string? InternalNote,
    bool IsFavorite,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    List<string>? ImageUrls = null,
    bool HasOrders = false,
    List<ProductUnitResponse>? Units = null,
    int? TotalUnits = null
)
{
    // Backward compatibility aliases
    public string SKU => ProductCode;
    public string ProductName => Name;
    public decimal BasePrice => SellingPrice;
    public string UnitName => BaseUnit;
    public bool IsActive => Status == "Active";
    public bool HasTransactions => HasOrders;
    public List<ProductUnitResponse>? UomConversions => Units;
}

[method: JsonConstructor]
public record ProductCreateRequest(
    string Name,
    Guid CategoryId,
    decimal SellingPrice,
    string? ProductCode = null,
    string? Barcode = null,
    string BaseUnit = "Cái",
    decimal? CostPrice = null,
    string Status = "Active",
    string? ImageUrl = null,
    string? Description = null,
    string? InternalNote = null,
    bool IsFavorite = false,
    List<string>? ImageUrls = null
)
{
    // Backward compatibility constructor
    public ProductCreateRequest(
        string SKU,
        string? Barcode,
        string ProductName,
        Guid? CategoryId,
        decimal BasePrice,
        decimal CostPrice,
        string UnitName) : this(
            ProductName,
            CategoryId ?? Guid.Empty,
            BasePrice,
            SKU,
            Barcode,
            string.IsNullOrWhiteSpace(UnitName) ? "Cái" : UnitName,
            CostPrice)
    {
    }
}

public record ProductUpdateRequest(
    string Name,
    Guid CategoryId,
    decimal SellingPrice,
    string? Barcode = null,
    string BaseUnit = "Cái",
    decimal? CostPrice = null,
    string Status = "Active",
    string? ImageUrl = null,
    string? Description = null,
    string? InternalNote = null,
    bool IsFavorite = false,
    List<string>? ImageUrls = null
);

public class ProductQueryFilter
{
    public string? SearchTerm { get; set; }
    public Guid? CategoryId { get; set; }
    public List<Guid>? CategoryIds { get; set; }
    public string? Status { get; set; }
    public int PageIndex { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
