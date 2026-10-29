using System.Linq.Expressions;
using MT_GRMS.Application.Catalog;
using MT_GRMS.Application.Catalog.Dtos;
using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Exceptions;

namespace MT_GRMS.UnitTests;

public class CategoryServiceTests
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

    private class FakeCategoryRepository : ICategoryRepository
    {
        public List<Category> Categories { get; } = new();
        public Dictionary<Guid, int> ProductCounts { get; } = new();

        public Task<IReadOnlyList<CategoryResponse>> GetAllWithProductCountAsync(CancellationToken cancellationToken = default)
        {
            var list = Categories
                .Where(c => !c.IsDeleted)
                .OrderBy(c => c.DisplayOrder)
                .ThenBy(c => c.Name)
                .Select(c => new CategoryResponse(
                    c.Id,
                    c.CategoryCode,
                    c.Name,
                    c.Description,
                    c.DisplayOrder,
                    ProductCounts.GetValueOrDefault(c.Id, 0)
                ))
                .ToList();
            return Task.FromResult<IReadOnlyList<CategoryResponse>>(list);
        }

        public Task<CategoryResponse?> GetByIdWithProductCountAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var c = Categories.FirstOrDefault(x => x.Id == id && !x.IsDeleted);
            if (c == null) return Task.FromResult<CategoryResponse?>(null);
            var res = new CategoryResponse(c.Id, c.CategoryCode, c.Name, c.Description, c.DisplayOrder, ProductCounts.GetValueOrDefault(c.Id, 0));
            return Task.FromResult<CategoryResponse?>(res);
        }

        public Task<bool> ExistsByNameAsync(string name, Guid? excludeId = null, CancellationToken cancellationToken = default)
        {
            var exists = Categories.Any(c =>
                !c.IsDeleted &&
                c.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase) &&
                (!excludeId.HasValue || c.Id != excludeId.Value));
            return Task.FromResult(exists);
        }

        public Task<bool> ExistsByCodeAsync(string code, Guid? excludeId = null, CancellationToken cancellationToken = default)
        {
            var exists = Categories.Any(c =>
                !c.IsDeleted &&
                c.CategoryCode.Equals(code.Trim(), StringComparison.OrdinalIgnoreCase) &&
                (!excludeId.HasValue || c.Id != excludeId.Value));
            return Task.FromResult(exists);
        }

        public Task<int> GetProductCountAsync(Guid categoryId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ProductCounts.GetValueOrDefault(categoryId, 0));
        }

        public Task<Category?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Categories.FirstOrDefault(c => c.Id == id && !c.IsDeleted));
        }

        public Task<IReadOnlyList<Category>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<Category>>(Categories.Where(c => !c.IsDeleted).ToList());
        }

        public Task<IReadOnlyList<Category>> FindAsync(Expression<Func<Category, bool>> predicate, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<Category>>(Categories.AsQueryable().Where(predicate).ToList());
        }

        public Task AddAsync(Category entity, CancellationToken cancellationToken = default)
        {
            Categories.Add(entity);
            return Task.CompletedTask;
        }

        public void Update(Category entity)
        {
            var idx = Categories.FindIndex(c => c.Id == entity.Id);
            if (idx >= 0) Categories[idx] = entity;
        }

        public void Delete(Category entity)
        {
            entity.IsDeleted = true;
        }
    }

    [Fact]
    public async Task GetAllAsync_ReturnsCategoriesWithAccurateProductCount()
    {
        // Arrange
        var fakeRepo = new FakeCategoryRepository();
        var fakeUow = new FakeUnitOfWork();
        var catId = Guid.NewGuid();

        fakeRepo.Categories.Add(new Category
        {
            Id = catId,
            CategoryCode = "CAT-001",
            Name = "Bánh kẹo",
            DisplayOrder = 1
        });
        fakeRepo.ProductCounts[catId] = 12;

        var service = new CategoryService(fakeRepo, fakeUow);

        // Act
        var result = await service.GetAllAsync();

        // Assert
        Assert.Single(result);
        Assert.Equal("Bánh kẹo", result[0].Name);
        Assert.Equal(12, result[0].ProductCount);
    }

    [Fact]
    public async Task CreateAsync_ValidRequest_CreatesSuccessfully()
    {
        // Arrange
        var fakeRepo = new FakeCategoryRepository();
        var fakeUow = new FakeUnitOfWork();
        var service = new CategoryService(fakeRepo, fakeUow);

        var request = new CategoryCreateRequest("Đồ uống", "BEV", "Nước khoáng, trà", 1);

        // Act
        var result = await service.CreateAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Đồ uống", result.Name);
        Assert.Equal("BEV", result.Code);
        Assert.Equal(0, result.ProductCount);
        Assert.True(fakeUow.SaveChangesCalled);
    }

    [Fact]
    public async Task CreateAsync_EmptyName_ThrowsArgumentException()
    {
        // Arrange
        var fakeRepo = new FakeCategoryRepository();
        var fakeUow = new FakeUnitOfWork();
        var service = new CategoryService(fakeRepo, fakeUow);

        var request = new CategoryCreateRequest("   ");

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(request));
    }

    [Fact]
    public async Task CreateAsync_DuplicateName_ThrowsBusinessException()
    {
        // Arrange
        var fakeRepo = new FakeCategoryRepository();
        var fakeUow = new FakeUnitOfWork();
        fakeRepo.Categories.Add(new Category { Id = Guid.NewGuid(), Name = "Bánh kẹo", CategoryCode = "CAT-01" });
        var service = new CategoryService(fakeRepo, fakeUow);

        var request = new CategoryCreateRequest("bánh kẹo");

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.CreateAsync(request));
        Assert.Equal("CATEGORY_NAME_EXISTS", ex.ErrorCode);
    }

    [Fact]
    public async Task DeleteAsync_CategoryWithProducts_ThrowsBusinessException()
    {
        // Arrange
        var fakeRepo = new FakeCategoryRepository();
        var fakeUow = new FakeUnitOfWork();
        var catId = Guid.NewGuid();
        fakeRepo.Categories.Add(new Category { Id = catId, Name = "Bánh kẹo", CategoryCode = "CAT-01" });
        fakeRepo.ProductCounts[catId] = 5;

        var service = new CategoryService(fakeRepo, fakeUow);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.DeleteAsync(catId));
        Assert.Equal("CATEGORY_HAS_PRODUCTS", ex.ErrorCode);
    }

    [Fact]
    public async Task DeleteAsync_EmptyCategory_DeletesSuccessfully()
    {
        // Arrange
        var fakeRepo = new FakeCategoryRepository();
        var fakeUow = new FakeUnitOfWork();
        var catId = Guid.NewGuid();
        var category = new Category { Id = catId, Name = "Gia vị", CategoryCode = "CAT-SPICE" };
        fakeRepo.Categories.Add(category);
        fakeRepo.ProductCounts[catId] = 0;

        var service = new CategoryService(fakeRepo, fakeUow);

        // Act
        await service.DeleteAsync(catId);

        // Assert
        Assert.True(category.IsDeleted);
        Assert.True(fakeUow.SaveChangesCalled);
    }
}

