using MT_GRMS.Application.Catalog.Dtos;
using MT_GRMS.Application.Catalog.Validators;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Enums;

namespace MT_GRMS.UnitTests.Catalog;

public class CatalogDomainAndValidatorTests
{
    private readonly ProductCreateRequestValidator _validator = new();

    // =========================================================================
    // 1. NHÓM TEST CHO VALIDATION (FluentValidation - ProductCreateRequestValidator)
    // =========================================================================

    [Fact]
    public void Validate_HappyPath_ValidProductCreateRequest_Passes()
    {
        var request = new ProductCreateRequest(
            Name: "Coca Cola 320ml",
            CategoryId: Guid.NewGuid(),
            SellingPrice: 10000m,
            CostPrice: 7500m,
            ProductCode: "SP-001",
            Barcode: "893123456789",
            BaseUnit: "Lon"
        );

        var result = _validator.Validate(request);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_MissingName_FailsWithNotEmpty(string emptyName)
    {
        var request = new ProductCreateRequest(
            Name: emptyName,
            CategoryId: Guid.NewGuid(),
            SellingPrice: 10000m
        );

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ProductCreateRequest.Name));
    }

    [Fact]
    public void Validate_MissingCategoryId_FailsWithNotEmpty()
    {
        var request = new ProductCreateRequest(
            Name: "Coca Cola",
            CategoryId: Guid.Empty,
            SellingPrice: 10000m
        );

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ProductCreateRequest.CategoryId));
    }

    [Fact]
    public void Validate_NameExceedsMaximumLength_FailsWithMaxLength()
    {
        var longName = new string('A', 256);
        var request = new ProductCreateRequest(
            Name: longName,
            CategoryId: Guid.NewGuid(),
            SellingPrice: 10000m
        );

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ProductCreateRequest.Name));
    }

    [Theory]
    [InlineData(-1, 5000)]
    [InlineData(10000, -5)]
    public void Validate_NegativePrices_FailsWithGreaterThanOrEqualToZero(decimal sellingPrice, decimal costPrice)
    {
        var request = new ProductCreateRequest(
            Name: "Coca Cola",
            CategoryId: Guid.NewGuid(),
            SellingPrice: sellingPrice,
            CostPrice: costPrice
        );

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_EmptyProductCode_PassesForAutoGeneration(string? emptyCode)
    {
        var request = new ProductCreateRequest(
            Name: "Coca Cola",
            CategoryId: Guid.NewGuid(),
            SellingPrice: 10000m,
            ProductCode: emptyCode
        );

        var result = _validator.Validate(request);

        Assert.True(result.IsValid);
    }

    // =========================================================================
    // 2. NHÓM TEST CHO LOGIC NỘI TẠI CỦA ENTITY (Domain Logic)
    // =========================================================================

    [Fact]
    public void Product_ImageUrls_WhenAssignedMultipleImages_AutomaticallySetsFirstAsAvatarImageUrl()
    {
        var product = new Product
        {
            ImageUrls = new List<string> { "img1.png", "img2.png" }
        };

        Assert.Equal("img1.png", product.ImageUrl);
        Assert.Equal(2, product.ImageUrls.Count);
    }

    [Fact]
    public void Product_ImageUrls_WhenLoadedFromDbWithSingleImageUrl_AutomaticallyCreatesList()
    {
        var product = new Product
        {
            ImageUrl = "old-image.jpg"
        };

        var urls = product.ImageUrls;

        Assert.Single(urls);
        Assert.Equal("old-image.jpg", urls[0]);
    }

    [Fact]
    public void Product_ImageUrls_SanitizesDirtyInputsAndTrimsWhitespace()
    {
        var product = new Product
        {
            ImageUrls = new List<string> { " img.png ", "", "   ", null! }
        };

        Assert.Single(product.ImageUrls);
        Assert.Equal("img.png", product.ImageUrls[0]);
        Assert.Equal("img.png", product.ImageUrl);
    }

    [Fact]
    public void ProductUnit_Constructor_WhenIsBaseUnitTrue_ForcesConversionRateToOne()
    {
        var productId = Guid.NewGuid();
        var unit = new ProductUnit(
            productId: productId,
            unitName: "Lon",
            conversionRate: 10m,
            price: 15000m,
            barcode: null,
            isBaseUnit: true,
            isPurchaseUnit: true);

        Assert.Equal(1.00m, unit.ConversionRate);
        Assert.True(unit.IsBaseUnit);
        Assert.True(unit.IsPurchaseUnit);
    }

    [Fact]
    public void ProductUnit_Constructor_WhenIsBaseUnitFalse_PreservesConversionRate()
    {
        var productId = Guid.NewGuid();
        var unit = new ProductUnit(
            productId: productId,
            unitName: "Thùng",
            conversionRate: 12m,
            price: 180000m,
            barcode: "8930001",
            isBaseUnit: false,
            isPurchaseUnit: true);

        Assert.Equal(12.00m, unit.ConversionRate);
        Assert.False(unit.IsBaseUnit);
        Assert.True(unit.IsPurchaseUnit);
    }

    // =========================================================================
    // 3. NHÓM TEST CHO BACKWARD COMPATIBILITY (Các thuộc tính Alias [NotMapped])
    // =========================================================================

    [Fact]
    public void Product_SKU_Alias_MapsBidirectionallyToProductCode()
    {
        var product = new Product();
        product.SKU = "SP001";

        Assert.Equal("SP001", product.ProductCode);
        Assert.Equal("SP001", product.SKU);
    }

    [Fact]
    public void Product_IsActive_WhenSetToFalse_SetsStatusToInactive()
    {
        var product = new Product { Status = ProductStatus.Active };
        product.IsActive = false;

        Assert.Equal(ProductStatus.Inactive, product.Status);
        Assert.False(product.IsActive);
    }

    [Fact]
    public void Product_IsActive_WhenSetToTrue_SetsStatusToActive()
    {
        var product = new Product { Status = ProductStatus.Inactive };
        product.IsActive = true;

        Assert.Equal(ProductStatus.Active, product.Status);
        Assert.True(product.IsActive);
    }

    [Fact]
    public void Product_UnitName_Alias_MapsBidirectionallyToBaseUnit()
    {
        var product = new Product();
        product.UnitName = "Hộp";

        Assert.Equal("Hộp", product.BaseUnit);
        Assert.Equal("Hộp", product.UnitName);
    }

    [Fact]
    public void Product_ProductNameAndBasePrice_Aliases_MapBidirectionally()
    {
        var product = new Product();
        product.ProductName = "Sữa tươi TH";
        product.BasePrice = 32000m;

        Assert.Equal("Sữa tươi TH", product.Name);
        Assert.Equal(32000m, product.SellingPrice);
        Assert.Equal("Sữa tươi TH", product.ProductName);
        Assert.Equal(32000m, product.BasePrice);
    }

    [Fact]
    public void Category_CategoryName_Alias_MapsBidirectionallyToName()
    {
        var category = new Category();
        category.CategoryName = "Đồ uống có gas";

        Assert.Equal("Đồ uống có gas", category.Name);
        Assert.Equal("Đồ uống có gas", category.CategoryName);
    }

    [Fact]
    public void Product_ImageUrls_WhenAssignedNullOrEmpty_ResetsToEmptyListAndNullImageUrl()
    {
        var product = new Product { ImageUrls = new List<string> { "https://img1.png" } };
        Assert.Equal("https://img1.png", product.ImageUrl);

        product.ImageUrls = null!;
        Assert.Empty(product.ImageUrls);
        Assert.Null(product.ImageUrl);

        product.ImageUrls = new List<string>();
        Assert.Empty(product.ImageUrls);
        Assert.Null(product.ImageUrl);
    }

    [Fact]
    public void ProductResponse_And_CategoryResponse_ComputedAliases_ReturnExpectedValues()
    {
        var prodId = Guid.NewGuid();
        var catId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var productResponse = new ProductResponse(
            Id: prodId,
            ProductCode: "SP000123",
            Barcode: "893000123",
            Name: "Nước khoáng Lavie",
            CategoryId: catId,
            CategoryName: "Nước uống",
            BaseUnit: "Chai",
            SellingPrice: 6000m,
            CostPrice: 4000m,
            MinStock: 10m,
            MaxStock: 500m,
            VatRate: 8m,
            ProductType: "STANDARD",
            Status: "Active",
            ImageUrl: "https://img.png",
            Description: "Mô tả",
            InternalNote: "Ghi chú",
            HasBatches: true,
            IsFavorite: true,
            CreatedAt: now,
            UpdatedAt: null,
            ImageUrls: new List<string> { "https://img.png" },
            HasOrders: true
        );

        Assert.Equal("SP000123", productResponse.SKU);
        Assert.Equal("Nước khoáng Lavie", productResponse.ProductName);
        Assert.Equal(6000m, productResponse.BasePrice);
        Assert.Equal("Chai", productResponse.UnitName);
        Assert.True(productResponse.IsActive);
        Assert.True(productResponse.HasTransactions);

        var categoryResponse = new CategoryResponse(catId, "CAT000001", "Nước uống", "Mô tả nhóm", 15);
        Assert.Equal("CAT000001", categoryResponse.CategoryCode);
    }

    [Fact]
    public void ProductCreateRequest_BackwardCompatibleConstructor_MapsFieldsAndDefaultsEmptyUnitName()
    {
        var catId = Guid.NewGuid();
        var request = new ProductCreateRequest(
            SKU: "SP-LEGACY-01",
            Barcode: "893111222333",
            ProductName: "Bánh ChocoPie",
            CategoryId: catId,
            BasePrice: 55000m,
            CostPrice: 42000m,
            UnitName: "   ",
            HasBatches: true,
            EligibleForVatReduction: true
        );

        Assert.Equal("Bánh ChocoPie", request.Name);
        Assert.Equal("SP-LEGACY-01", request.ProductCode);
        Assert.Equal("893111222333", request.Barcode);
        Assert.Equal(catId, request.CategoryId);
        Assert.Equal(55000m, request.SellingPrice);
        Assert.Equal(42000m, request.CostPrice);
        Assert.Equal("Cái", request.BaseUnit);
        Assert.True(request.HasBatches);
        Assert.True(request.EligibleForVatReduction);
    }

    [Theory]
    [InlineData("SP 001", "893123", "Lon", "Mô tả", "Ghi chú", nameof(ProductCreateRequest.ProductCode))]
    [InlineData("SP001", "893 123", "Lon", "Mô tả", "Ghi chú", nameof(ProductCreateRequest.Barcode))]
    public void Validate_InvalidCodeOrBarcodeWithSpaces_FailsValidation(
        string productCode,
        string barcode,
        string baseUnit,
        string desc,
        string note,
        string expectedErrorProperty)
    {
        var request = new ProductCreateRequest(
            Name: "Sản phẩm test",
            CategoryId: Guid.NewGuid(),
            SellingPrice: 15000m,
            ProductCode: productCode,
            Barcode: barcode,
            BaseUnit: baseUnit,
            Description: desc,
            InternalNote: note
        );

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == expectedErrorProperty);
    }

    [Fact]
    public void Validate_ExceedingMaxLengthsOnOptionalFields_FailsValidation()
    {
        var request = new ProductCreateRequest(
            Name: "Sản phẩm test",
            CategoryId: Guid.NewGuid(),
            SellingPrice: 1_500_000_000m, // > 1 tỷ
            CostPrice: 1_500_000_000m,    // > 1 tỷ
            ProductCode: new string('A', 51),
            Barcode: new string('1', 51),
            BaseUnit: new string('U', 31),
            Description: new string('D', 1001),
            InternalNote: new string('N', 501)
        );

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ProductCreateRequest.SellingPrice));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ProductCreateRequest.CostPrice));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ProductCreateRequest.ProductCode));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ProductCreateRequest.Barcode));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ProductCreateRequest.BaseUnit));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ProductCreateRequest.Description));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ProductCreateRequest.InternalNote));
    }
}

