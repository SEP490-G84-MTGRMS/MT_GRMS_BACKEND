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
    decimal MinStock,
    decimal MaxStock,
    decimal VatRate,
    string ProductType,
    string Status,
    string? ImageUrl,
    string? Description,
    string? InternalNote,
    bool HasBatches,
    bool IsFavorite,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    List<string>? ImageUrls = null,
    bool HasOrders = false,
    string TaxCategory = "STANDARD_1PCT",
    bool EligibleForVatReduction = false,
    bool IsBaseUnitPurchaseUnit = true,
    string? PurchaseUnit = null,
    List<ProductUnitResponse>? UnitPrices = null,
    bool AllowRepack = false,
    bool SellRawByWeight = true
)
{
    // Backward compatibility aliases
    public string SKU => ProductCode;
    public string ProductName => Name;
    public decimal BasePrice => SellingPrice;
    public string UnitName => BaseUnit;
    public bool IsActive => Status == "Active";
    public bool HasTransactions => HasOrders;
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
    decimal MinStock = 0,
    decimal MaxStock = 1000,
    decimal VatRate = 0,
    string ProductType = "STANDARD",
    string Status = "Active",
    string? ImageUrl = null,
    string? Description = null,
    string? InternalNote = null,
    bool HasBatches = false,
    bool IsFavorite = false,
    List<string>? ImageUrls = null,
    bool EligibleForVatReduction = false,
    string TaxCategory = "STANDARD_1PCT",
    bool IsBaseUnitPurchaseUnit = true,
    bool AllowRepack = false,
    bool SellRawByWeight = true
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
        string UnitName,
        bool HasBatches,
        bool EligibleForVatReduction = false) : this(
            ProductName,
            CategoryId ?? Guid.Empty,
            BasePrice,
            SKU,
            Barcode,
            string.IsNullOrWhiteSpace(UnitName) ? "Cái" : UnitName,
            CostPrice,
            HasBatches: HasBatches,
            EligibleForVatReduction: EligibleForVatReduction)
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
    decimal MinStock = 0,
    decimal MaxStock = 1000,
    decimal VatRate = 0,
    string Status = "Active",
    string? ImageUrl = null,
    string? Description = null,
    string? InternalNote = null,
    bool HasBatches = false,
    bool IsFavorite = false,
    List<string>? ImageUrls = null,
    bool EligibleForVatReduction = false,
    string TaxCategory = "STANDARD_1PCT",
    bool? IsBaseUnitPurchaseUnit = null,
    bool AllowRepack = false,
    bool SellRawByWeight = true
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

public record ProductPriceHistoryDto(
    Guid Id,
    Guid ProductId,
    Guid? BranchId,
    string? BranchName,
    string PriceScope,
    string PriceType,
    decimal OldPrice,
    decimal NewPrice,
    decimal? CostPriceSnapshot,
    bool IsBelowCost,
    string? ManagerApprovedBy,
    string? ManagerApprovalReason,
    string ChangedByUserId,
    string ChangedByUserName,
    string Reason,
    DateTime CreatedAt
);
