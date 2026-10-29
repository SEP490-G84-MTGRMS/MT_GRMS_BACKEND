using System.Linq.Expressions;
using MT_GRMS.Application.Catalog;
using MT_GRMS.Application.Catalog.Dtos;
using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Exceptions;

namespace MT_GRMS.UnitTests;

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

    private class FakeProductRepository : IProductRepository
    {
        public List<Product> Products { get; } = new();

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
                    p.Barcode.ToLower().Contains(term));
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
            var exists = Products.Any(p =>
                !p.IsDeleted &&
                p.Barcode.Equals(barcode.Trim(), StringComparison.OrdinalIgnoreCase) &&
                (!excludeId.HasValue || p.Id != excludeId.Value));
            return Task.FromResult(exists);
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

    [Fact]
    public async Task GetProductsPagedAsync_SearchFilter_ReturnsCorrectResult()
    {
        // Arrange
        var fakeProdRepo = new FakeProductRepository();
        var fakeCatRepo = new FakeCategoryRepo();
        var fakeUow = new FakeUnitOfWork();
        var fakeTenantContext = new FakeTenantContext();

        var catId = Guid.NewGuid();
        fakeCatRepo.Categories.Add(new Category { Id = catId, Name = "Bánh kẹo" });

        fakeProdRepo.Products.Add(new Product { Id = Guid.NewGuid(), Name = "Bánh quy Danisa", CategoryId = catId, ProductCode = "SP-01", Barcode = "111" });
        fakeProdRepo.Products.Add(new Product { Id = Guid.NewGuid(), Name = "Kẹo mút Chupa Chups", CategoryId = catId, ProductCode = "SP-02", Barcode = "222" });
        fakeProdRepo.Products.Add(new Product { Id = Guid.NewGuid(), Name = "Sữa tươi TH True Milk", CategoryId = Guid.NewGuid(), ProductCode = "SP-03", Barcode = "333" });

        var service = new CatalogService(fakeProdRepo, fakeCatRepo, fakeUow, fakeTenantContext);

        // Act
        var result = await service.GetProductsPagedAsync(new ProductQueryFilter { SearchTerm = "Bánh", PageIndex = 1, PageSize = 10 });

        // Assert
        Assert.Single(result.Items);
        Assert.Equal("Bánh quy Danisa", result.Items[0].Name);
        Assert.Equal(1, result.TotalItems);
    }

    [Fact]
    public async Task CreateProductAsync_ValidRequest_AutoGeneratesSkuAndBarcode()
    {
        // Arrange
        var fakeProdRepo = new FakeProductRepository();
        var fakeCatRepo = new FakeCategoryRepo();
        var fakeUow = new FakeUnitOfWork();
        var fakeTenantContext = new FakeTenantContext();

        var catId = Guid.NewGuid();
        fakeCatRepo.Categories.Add(new Category { Id = catId, Name = "Nước ngọt" });

        var service = new CatalogService(fakeProdRepo, fakeCatRepo, fakeUow, fakeTenantContext);

        var request = new ProductCreateRequest(
            Name: "Coca Cola 320ml",
            CategoryId: catId,
            SellingPrice: 10000,
            CostPrice: 7500
        );

        // Act
        var result = await service.CreateProductAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.StartsWith("SP-", result.ProductCode);
        Assert.Equal(result.ProductCode, result.Barcode);
        Assert.Equal("Coca Cola 320ml", result.Name);
        Assert.True(fakeUow.SaveChangesCalled);
    }

    [Fact]
    public async Task CreateProductAsync_DuplicateBarcode_ThrowsBusinessException()
    {
        // Arrange
        var fakeProdRepo = new FakeProductRepository();
        var fakeCatRepo = new FakeCategoryRepo();
        var fakeUow = new FakeUnitOfWork();
        var fakeTenantContext = new FakeTenantContext();

        var catId = Guid.NewGuid();
        fakeCatRepo.Categories.Add(new Category { Id = catId, Name = "Nước ngọt" });
        fakeProdRepo.Products.Add(new Product { Id = Guid.NewGuid(), Name = "Pepsi 320ml", Barcode = "893111222", ProductCode = "SP-01", CategoryId = catId });

        var service = new CatalogService(fakeProdRepo, fakeCatRepo, fakeUow, fakeTenantContext);

        var request = new ProductCreateRequest(
            Name: "Coca Cola 320ml",
            CategoryId: catId,
            SellingPrice: 10000,
            Barcode: "893111222"
        );

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.CreateProductAsync(request));
        Assert.Equal("PRODUCT_BARCODE_EXISTS", ex.ErrorCode);
    }

    [Fact]
    public async Task BR_SEC_01_CashierRole_ConcealsCostPrice()
    {
        // Arrange
        var fakeProdRepo = new FakeProductRepository();
        var fakeCatRepo = new FakeCategoryRepo();
        var fakeUow = new FakeUnitOfWork();
        var fakeTenantContext = new FakeTenantContext { CurrentRole = "CASHIER" };

        var prodId = Guid.NewGuid();
        fakeProdRepo.Products.Add(new Product
        {
            Id = prodId,
            Name = "Bia Heineken",
            ProductCode = "SP-BEER",
            Barcode = "999888",
            CostPrice = 18000,
            SellingPrice = 22000
        });

        var service = new CatalogService(fakeProdRepo, fakeCatRepo, fakeUow, fakeTenantContext);

        // Act
        var result = await service.GetByIdAsync(prodId);

        // Assert
        Assert.NotNull(result);
        Assert.Null(result.CostPrice); // Phải bị che giấu thành null theo BR-SEC-01
        Assert.Equal(22000, result.SellingPrice);
    }

    [Fact]
    public async Task BR_SEC_01_StoreOwnerRole_RevealsCostPrice()
    {
        // Arrange
        var fakeProdRepo = new FakeProductRepository();
        var fakeCatRepo = new FakeCategoryRepo();
        var fakeUow = new FakeUnitOfWork();
        var fakeTenantContext = new FakeTenantContext { CurrentRole = "STORE_OWNER" };

        var prodId = Guid.NewGuid();
        fakeProdRepo.Products.Add(new Product
        {
            Id = prodId,
            Name = "Bia Heineken",
            ProductCode = "SP-BEER",
            Barcode = "999888",
            CostPrice = 18000,
            SellingPrice = 22000
        });

        var service = new CatalogService(fakeProdRepo, fakeCatRepo, fakeUow, fakeTenantContext);

        // Act
        var result = await service.GetByIdAsync(prodId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(18000, result.CostPrice); // StoreOwner được thấy giá vốn
        Assert.Equal(22000, result.SellingPrice);
    }

    [Fact]
    public async Task DeleteProductAsync_ExistingProduct_SoftDeletes()
    {
        // Arrange
        var fakeProdRepo = new FakeProductRepository();
        var fakeCatRepo = new FakeCategoryRepo();
        var fakeUow = new FakeUnitOfWork();
        var fakeTenantContext = new FakeTenantContext();

        var prodId = Guid.NewGuid();
        var product = new Product { Id = prodId, Name = "Sản phẩm test", ProductCode = "SP-DEL", Barcode = "DEL123" };
        fakeProdRepo.Products.Add(product);

        var service = new CatalogService(fakeProdRepo, fakeCatRepo, fakeUow, fakeTenantContext);

        // Act
        await service.DeleteProductAsync(prodId);

        // Assert
        Assert.True(product.IsDeleted);
        Assert.True(fakeUow.SaveChangesCalled);
    }
}

