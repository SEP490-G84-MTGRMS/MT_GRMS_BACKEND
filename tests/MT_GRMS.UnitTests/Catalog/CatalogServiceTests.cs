using System.Linq.Expressions;
using MT_GRMS.Application.Catalog;
using MT_GRMS.Application.Catalog.Dtos;
using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Exceptions;

namespace MT_GRMS.UnitTests.Catalog;

public class CatalogServiceTests
{
    private class FakeUnitOfWork : IUnitOfWork
    {
        public bool SaveChangesCalled { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveChangesCalled = true;
            return Task.FromResult(1);
        }
    }

    private class FakeTenantContext : ITenantContext
    {
        public string CurrentTenantId { get; set; } = "tenant-test";
        public Guid CurrentUserId { get; set; } = Guid.NewGuid();
        public Guid? CurrentBranchId { get; set; } = Guid.NewGuid();
        public string CurrentRole { get; set; } = "STORE_OWNER";

        public void SetContext(string tenantId, Guid userId, Guid? branchId)
        {
            CurrentTenantId = tenantId;
            CurrentUserId = userId;
            CurrentBranchId = branchId;
        }

        public void SetRole(string? role)
        {
            CurrentRole = role ?? "STAFF";
        }
    }

    private class FakePriceHistoryRepository : IProductPriceHistoryRepository
    {
        public List<ProductPriceHistory> Histories { get; } = new();

        public Task AddAsync(ProductPriceHistory history, CancellationToken cancellationToken = default)
        {
            Histories.Add(history);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ProductPriceHistory>> GetByProductIdAsync(Guid productId, Guid? branchId = null, CancellationToken cancellationToken = default)
        {
            var query = Histories.Where(h => h.ProductId == productId);
            if (branchId.HasValue)
            {
                query = query.Where(h => h.BranchId == branchId.Value);
            }
            return Task.FromResult<IReadOnlyList<ProductPriceHistory>>(query.ToList());
        }
    }

    private class FakeProductRepository : IProductRepository
    {
        public List<Product> Products { get; } = new();
        public HashSet<Guid> OrderedProductIds { get; } = new();

        public Task<(IReadOnlyList<Product> Items, int TotalCount)> GetPagedListAsync(
            ProductQueryFilter filter,
            CancellationToken cancellationToken = default)
        {
            var query = Products.Where(p => !p.IsDeleted);

            if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
            {
                var term = filter.SearchTerm.Trim().ToLower();
                query = query.Where(p =>
                    p.Name.ToLower().Contains(term) ||
                    p.ProductCode.ToLower().Contains(term) ||
                    (p.Barcode != null && p.Barcode.ToLower().Contains(term)));
            }

            if (filter.CategoryId.HasValue)
            {
                query = query.Where(p => p.CategoryId == filter.CategoryId.Value);
            }

            var total = query.Count();
            var items = query
                .Skip((filter.PageIndex - 1) * filter.PageSize)
                .Take(filter.PageSize)
                .ToList();

            return Task.FromResult<(IReadOnlyList<Product> Items, int TotalCount)>((items, total));
        }

        public Task<Product?> GetByIdWithCategoryAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Products.FirstOrDefault(p => p.Id == id && !p.IsDeleted));
        }

        public Task<bool> ExistsByCodeAsync(string code, Guid? excludeId = null, CancellationToken cancellationToken = default)
        {
            var exists = Products.Any(p =>
                !p.IsDeleted &&
                p.ProductCode.Equals(code.Trim(), StringComparison.OrdinalIgnoreCase) &&
                (!excludeId.HasValue || p.Id != excludeId.Value));
            return Task.FromResult(exists);
        }

        public Task<bool> ExistsByBarcodeAsync(string barcode, Guid? excludeId = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(barcode)) return Task.FromResult(false);
            var exists = Products.Any(p =>
                !p.IsDeleted &&
                p.Barcode != null &&
                p.Barcode.Equals(barcode.Trim(), StringComparison.OrdinalIgnoreCase) &&
                (!excludeId.HasValue || p.Id != excludeId.Value));
            return Task.FromResult(exists);
        }

        public Task<string?> GetMaxSequentialCodeAsync(string prefix, int totalLength, CancellationToken cancellationToken = default)
        {
            var max = Products
                .Where(p => !p.IsDeleted && p.ProductCode.ToUpper().StartsWith(prefix.ToUpper()) && p.ProductCode.Length == totalLength)
                .OrderByDescending(p => p.ProductCode)
                .Select(p => p.ProductCode)
                .FirstOrDefault();
            return Task.FromResult(max);
        }

        public Task<Product?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Products.FirstOrDefault(p => p.Id == id && !p.IsDeleted));
        }

        public Task<IReadOnlyList<Product>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<Product>>(Products.Where(p => !p.IsDeleted).ToList());
        }

        public Task<IReadOnlyList<Product>> FindAsync(Expression<Func<Product, bool>> predicate, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<Product>>(Products.AsQueryable().Where(predicate).ToList());
        }

        public Task AddAsync(Product entity, CancellationToken cancellationToken = default)
        {
            Products.Add(entity);
            return Task.CompletedTask;
        }

        public void Update(Product entity)
        {
            var idx = Products.FindIndex(p => p.Id == entity.Id);
            if (idx >= 0) Products[idx] = entity;
        }

        public void Delete(Product entity)
        {
            entity.IsDeleted = true;
        }

        public Task<bool> HasOrdersAsync(Guid productId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OrderedProductIds.Contains(productId));
        }

        public Task<HashSet<Guid>> GetProductIdsWithOrdersAsync(IEnumerable<Guid> productIds, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(productIds.Where(id => OrderedProductIds.Contains(id)).ToHashSet());
        }

        public Task DeleteWithUnitsAsync(Product product, CancellationToken cancellationToken = default)
        {
            product.IsDeleted = true;
            foreach (var unit in product.Units)
            {
                unit.IsDeleted = true;
            }
            return Task.CompletedTask;
        }
    }

    private class FakeCategoryRepo : ICategoryRepository
    {
        public List<Category> Categories { get; } = new();

        public Task<Category?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Categories.FirstOrDefault(c => c.Id == id && !c.IsDeleted));
        }

        public Task<IReadOnlyList<CategoryResponse>> GetAllWithProductCountAsync(CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<CategoryResponse?> GetByIdWithProductCountAsync(Guid id, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<bool> ExistsByNameAsync(string name, Guid? excludeId = null, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<bool> ExistsByCodeAsync(string code, Guid? excludeId = null, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<string?> GetMaxSequentialCodeAsync(string prefix, int totalLength, CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);
        public Task<int> GetProductCountAsync(Guid categoryId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<IReadOnlyList<Category>> GetAllAsync(CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<IReadOnlyList<Category>> FindAsync(Expression<Func<Category, bool>> predicate, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task AddAsync(Category entity, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public void Update(Category entity) { }
        public void Delete(Category entity) { }
    }

    private static (CatalogService Service, FakeProductRepository ProdRepo, FakeCategoryRepo CatRepo, FakePriceHistoryRepository HistoryRepo, FakeUnitOfWork Uow, FakeTenantContext Tenant, Guid CategoryId) CreateSut(string role = "STORE_OWNER")
    {
        var prodRepo = new FakeProductRepository();
        var catRepo = new FakeCategoryRepo();
        var historyRepo = new FakePriceHistoryRepository();
        var uow = new FakeUnitOfWork();
        var tenant = new FakeTenantContext { CurrentRole = role };

        var catId = Guid.NewGuid();
        catRepo.Categories.Add(new Category { Id = catId, Name = "Bánh kẹo", CategoryCode = "CAT000001" });

        var service = new CatalogService(prodRepo, catRepo, historyRepo, uow, tenant);
        return (service, prodRepo, catRepo, historyRepo, uow, tenant, catId);
    }

    [Fact]
    public async Task GetProductsPagedAsync_SearchFilter_ReturnsCorrectResult()
    {
        var (service, prodRepo, _, _, _, _, catId) = CreateSut();

        prodRepo.Products.Add(new Product { Id = Guid.NewGuid(), Name = "Bánh quy Danisa", CategoryId = catId, ProductCode = "SP-01", Barcode = "111" });
        prodRepo.Products.Add(new Product { Id = Guid.NewGuid(), Name = "Kẹo mút Chupa Chups", CategoryId = catId, ProductCode = "SP-02", Barcode = "222" });
        prodRepo.Products.Add(new Product { Id = Guid.NewGuid(), Name = "Sữa tươi TH True Milk", CategoryId = Guid.NewGuid(), ProductCode = "SP-03", Barcode = "333" });

        var result = await service.GetProductsPagedAsync(new ProductQueryFilter { SearchTerm = "Bánh", PageIndex = 1, PageSize = 10 });

        Assert.Single(result.Items);
        Assert.Equal("Bánh quy Danisa", result.Items[0].Name);
        Assert.Equal(1, result.TotalItems);
    }

    [Fact]
    public async Task CreateProductAsync_ValidRequest_AutoGeneratesSkuAndDefaultBaseUnit()
    {
        var (service, prodRepo, _, _, uow, _, catId) = CreateSut();

        var request = new ProductCreateRequest(
            Name: "  Coca Cola 320ml  ",
            CategoryId: catId,
            SellingPrice: 10000,
            CostPrice: 7500,
            BaseUnit: "  Lon  ",
            IsBaseUnitPurchaseUnit: true
        );

        var result = await service.CreateProductAsync(request);

        Assert.NotNull(result);
        Assert.Equal("SP000001", result.ProductCode);
        Assert.Null(result.Barcode);
        Assert.Equal("Coca Cola 320ml", result.Name);
        Assert.Equal("Lon", result.BaseUnit);
        Assert.True(result.IsBaseUnitPurchaseUnit);
        Assert.Equal("Lon", result.PurchaseUnit);
        Assert.Single(prodRepo.Products[0].Units);
        Assert.True(prodRepo.Products[0].Units.First().IsBaseUnit);
        Assert.True(prodRepo.Products[0].Units.First().IsPurchaseUnit);
        Assert.True(uow.SaveChangesCalled);
    }

    [Fact]
    public async Task CreateProductAsync_WhenProductCodeIsEmptyAndPreviousExists_GeneratesNextCode_SP000002()
    {
        var (service, prodRepo, _, _, _, _, catId) = CreateSut();
        prodRepo.Products.Add(new Product { Id = Guid.NewGuid(), Name = "Sản phẩm 1", ProductCode = "SP000001" });

        var request = new ProductCreateRequest("Bánh AFC", catId, 15000m, ProductCode: null, Barcode: "");

        var result = await service.CreateProductAsync(request);

        Assert.Equal("SP000002", result.ProductCode);
        Assert.Null(result.Barcode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateProductAsync_EmptyName_ThrowsArgumentException(string invalidName)
    {
        var (service, _, _, _, _, _, catId) = CreateSut();
        var request = new ProductCreateRequest(invalidName, catId, 10000m);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.CreateProductAsync(request));
        Assert.Contains("Tên sản phẩm không được để trống", ex.Message);
    }

    [Fact]
    public async Task CreateProductAsync_NameExceeds200Chars_ThrowsArgumentException()
    {
        var (service, _, _, _, _, _, catId) = CreateSut();
        var request = new ProductCreateRequest(new string('A', 201), catId, 10000m);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.CreateProductAsync(request));
        Assert.Contains("không được vượt quá 200 ký tự", ex.Message);
    }

    [Fact]
    public async Task CreateProductAsync_BaseUnitExceeds30Chars_ThrowsArgumentException()
    {
        var (service, _, _, _, _, _, catId) = CreateSut();
        var request = new ProductCreateRequest("Bánh quy", catId, 10000m, BaseUnit: new string('U', 31));

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.CreateProductAsync(request));
        Assert.Contains("Tên đơn vị cơ bản không được vượt quá 30 ký tự", ex.Message);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1_000_000_001)]
    public async Task CreateProductAsync_InvalidSellingPrice_ThrowsArgumentException(decimal invalidSellingPrice)
    {
        var (service, _, _, _, _, _, catId) = CreateSut();
        var request = new ProductCreateRequest("Bánh quy", catId, invalidSellingPrice);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.CreateProductAsync(request));
        Assert.Contains("Giá bán", ex.Message);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1_000_000_001)]
    public async Task CreateProductAsync_InvalidCostPrice_ThrowsArgumentException(decimal invalidCostPrice)
    {
        var (service, _, _, _, _, _, catId) = CreateSut();
        var request = new ProductCreateRequest("Bánh quy", catId, 20000m, CostPrice: invalidCostPrice);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.CreateProductAsync(request));
        Assert.Contains("Giá vốn", ex.Message);
    }

    [Theory]
    [InlineData(-5, 100)]
    [InlineData(50, 20)]
    public async Task CreateProductAsync_InvalidStockThresholds_ThrowsArgumentException(decimal minStock, decimal maxStock)
    {
        var (service, _, _, _, _, _, catId) = CreateSut();
        var request = new ProductCreateRequest("Bánh quy", catId, 20000m, MinStock: minStock, MaxStock: maxStock);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.CreateProductAsync(request));
        Assert.Contains("Định mức tồn kho không hợp lệ", ex.Message);
    }

    [Fact]
    public async Task CreateProductAsync_DescriptionOrInternalNoteTooLong_ThrowsArgumentException()
    {
        var (service, _, _, _, _, _, catId) = CreateSut();

        var reqLongDesc = new ProductCreateRequest("Bánh quy", catId, 20000m, Description: new string('D', 1001));
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateProductAsync(reqLongDesc));

        var reqLongNote = new ProductCreateRequest("Bánh quy", catId, 20000m, InternalNote: new string('N', 501));
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateProductAsync(reqLongNote));
    }

    [Theory]
    [InlineData("SP 001")]
    [InlineData("SP@001")]
    [InlineData("MÃ_CÓ_DẤU")]
    public async Task CreateProductAsync_InvalidProductCodeFormat_ThrowsArgumentException(string invalidCode)
    {
        var (service, _, _, _, _, _, catId) = CreateSut();
        var request = new ProductCreateRequest("Bánh quy", catId, 20000m, ProductCode: invalidCode);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.CreateProductAsync(request));
        Assert.Contains("Mã hàng", ex.Message);
    }

    [Fact]
    public async Task CreateProductAsync_DuplicateProductCode_ThrowsBusinessException()
    {
        var (service, prodRepo, _, _, _, _, catId) = CreateSut();
        prodRepo.Products.Add(new Product { Id = Guid.NewGuid(), Name = "Pepsi", ProductCode = "SP-01", CategoryId = catId });

        var request = new ProductCreateRequest("Coca Cola", catId, 10000m, ProductCode: "sp-01");

        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.CreateProductAsync(request));
        Assert.Equal("PRODUCT_CODE_EXISTS", ex.ErrorCode);
    }

    [Theory]
    [InlineData("893 123 456")]
    [InlineData("123456789012345678901234567890123456789012345678901")] // 51 chars
    public async Task CreateProductAsync_InvalidBarcodeFormatOrLength_ThrowsArgumentException(string invalidBarcode)
    {
        var (service, _, _, _, _, _, catId) = CreateSut();
        var request = new ProductCreateRequest("Bánh quy", catId, 20000m, Barcode: invalidBarcode);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.CreateProductAsync(request));
        Assert.Contains("Mã vạch không hợp lệ", ex.Message);
    }

    [Fact]
    public async Task CreateProductAsync_DuplicateBarcode_ThrowsBusinessException()
    {
        var (service, prodRepo, _, _, _, _, catId) = CreateSut();
        prodRepo.Products.Add(new Product { Id = Guid.NewGuid(), Name = "Pepsi 320ml", Barcode = "893111222", ProductCode = "SP-01", CategoryId = catId });

        var request = new ProductCreateRequest(
            Name: "Coca Cola 320ml",
            CategoryId: catId,
            SellingPrice: 10000,
            Barcode: "893111222"
        );

        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.CreateProductAsync(request));
        Assert.Equal("PRODUCT_BARCODE_EXISTS", ex.ErrorCode);
    }

    [Fact]
    public async Task CreateProductAsync_EmptyCategoryIdOrNonExistentCategory_ThrowsException()
    {
        var (service, _, _, _, _, _, _) = CreateSut();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateProductAsync(new ProductCreateRequest("SP", Guid.Empty, 10000m)));

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            service.CreateProductAsync(new ProductCreateRequest("SP", Guid.NewGuid(), 10000m)));
        Assert.Equal("CATEGORY_NOT_FOUND", ex.ErrorCode);
    }

    [Fact]
    public async Task BR_SEC_01_CashierRole_ConcealsCostPrice()
    {
        var (service, prodRepo, _, _, _, _, _) = CreateSut(role: "CASHIER");

        var prodId = Guid.NewGuid();
        prodRepo.Products.Add(new Product
        {
            Id = prodId,
            Name = "Bia Heineken",
            ProductCode = "SP-BEER",
            Barcode = "999888",
            CostPrice = 18000,
            SellingPrice = 22000
        });

        var result = await service.GetByIdAsync(prodId);

        Assert.NotNull(result);
        Assert.Null(result.CostPrice);
        Assert.Equal(22000, result.SellingPrice);
    }

    [Fact]
    public async Task BR_SEC_01_StoreOwnerRole_RevealsCostPrice()
    {
        var (service, prodRepo, _, _, _, _, _) = CreateSut(role: "STORE_OWNER");

        var prodId = Guid.NewGuid();
        prodRepo.Products.Add(new Product
        {
            Id = prodId,
            Name = "Bia Heineken",
            ProductCode = "SP-BEER",
            Barcode = "999888",
            CostPrice = 18000,
            SellingPrice = 22000
        });

        var result = await service.GetByIdAsync(prodId);

        Assert.NotNull(result);
        Assert.Equal(18000, result.CostPrice);
        Assert.Equal(22000, result.SellingPrice);
    }

    [Fact]
    public async Task UpdateProductAsync_PriceChanged_RecordsPriceHistoryAndUpdatesBaseUnit()
    {
        var (service, prodRepo, _, historyRepo, uow, _, catId) = CreateSut();

        var prodId = Guid.NewGuid();
        var product = new Product
        {
            Id = prodId,
            Name = "Bánh Oreo",
            ProductCode = "SP000001",
            CategoryId = catId,
            BaseUnit = "Gói",
            CostPrice = 15000m,
            SellingPrice = 20000m
        };
        product.Units.Add(new ProductUnit(prodId, "Gói", 1m, 20000m, isBaseUnit: true, isPurchaseUnit: false));
        product.Units.Add(new ProductUnit(prodId, "Thùng", 24m, 450000m, isBaseUnit: false, isPurchaseUnit: true));
        prodRepo.Products.Add(product);

        var updateReq = new ProductUpdateRequest(
            Name: "Bánh Oreo Vani",
            CategoryId: catId,
            SellingPrice: 12000m, // Dưới giá vốn 15000m
            CostPrice: 15000m,
            BaseUnit: "Hộp",
            IsBaseUnitPurchaseUnit: true
        );

        var response = await service.UpdateProductAsync(prodId, updateReq);

        Assert.Equal("Bánh Oreo Vani", response.Name);
        Assert.Equal("Hộp", response.BaseUnit);
        Assert.True(response.IsBaseUnitPurchaseUnit);
        Assert.Equal("Hộp", response.PurchaseUnit);
        Assert.Single(historyRepo.Histories);
        Assert.Equal(20000m, historyRepo.Histories[0].OldPrice);
        Assert.Equal(12000m, historyRepo.Histories[0].NewPrice);
        Assert.True(historyRepo.Histories[0].IsBelowCost);

        var secondaryUnit = product.Units.First(u => !u.IsBaseUnit);
        Assert.False(secondaryUnit.IsPurchaseUnit);
        Assert.True(uow.SaveChangesCalled);
    }

    [Fact]
    public async Task DeleteProductAsync_ExistingProduct_SoftDeletesProductAndUnits()
    {
        var (service, prodRepo, _, _, uow, _, _) = CreateSut();

        var prodId = Guid.NewGuid();
        var product = new Product { Id = prodId, Name = "Sản phẩm test", ProductCode = "SP-DEL", Barcode = "DEL123" };
        var unit = new ProductUnit(prodId, "Thùng", 10, 100000, "BARCODE-THUNG");
        product.Units.Add(unit);
        prodRepo.Products.Add(product);

        await service.DeleteProductAsync(prodId);

        Assert.True(product.IsDeleted);
        Assert.True(unit.IsDeleted);
        Assert.True(uow.SaveChangesCalled);
    }

    [Fact]
    public async Task DeleteProductAsync_WhenProductHasOrders_ThrowsBusinessException()
    {
        var (service, prodRepo, _, _, _, _, _) = CreateSut();

        var prodId = Guid.NewGuid();
        var product = new Product { Id = prodId, Name = "Sản phẩm đã có đơn hàng", ProductCode = "SP-ORDERED" };
        prodRepo.Products.Add(product);
        prodRepo.OrderedProductIds.Add(prodId);

        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.DeleteProductAsync(prodId));
        Assert.Equal("PRODUCT_HAS_ORDERS", ex.ErrorCode);
        Assert.False(product.IsDeleted);
    }

    [Fact]
    public async Task CreateProduct_WithMultipleImageUrls_StoresListAndSetsAvatarAsFirstImage()
    {
        var (service, prodRepo, _, _, _, _, catId) = CreateSut();

        var request = new ProductCreateRequest(
            Name: "Nước tăng lực RedBull",
            CategoryId: catId,
            SellingPrice: 15000m,
            ImageUrls: new List<string> { "https://res.cloudinary.com/img1.png", "https://res.cloudinary.com/img2.png" }
        );

        var response = await service.CreateProductAsync(request);

        Assert.Equal(2, response.ImageUrls?.Count);
        Assert.Equal("https://res.cloudinary.com/img1.png", response.ImageUrl);
        Assert.Single(prodRepo.Products);
        Assert.Equal(2, prodRepo.Products[0].ImageUrls.Count);
        Assert.Equal("https://res.cloudinary.com/img1.png", prodRepo.Products[0].ImageUrl);
    }
}

