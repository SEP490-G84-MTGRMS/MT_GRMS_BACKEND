using System.Linq.Expressions;
using MT_GRMS.Application.Catalog;
using MT_GRMS.Application.Catalog.Dtos;
using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Exceptions;

namespace MT_GRMS.UnitTests.Catalog;

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
                .OrderByDescending(c => c.CreatedAt)
                .ThenBy(c => c.Name)
                .Select(c => new CategoryResponse(
                    c.Id,
                    c.CategoryCode,
                    c.Name,
                    c.Description,
                    ProductCounts.GetValueOrDefault(c.Id, 0)
                ))
                .ToList();
            return Task.FromResult<IReadOnlyList<CategoryResponse>>(list);
        }

        public Task<CategoryResponse?> GetByIdWithProductCountAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var c = Categories.FirstOrDefault(x => x.Id == id && !x.IsDeleted);
            if (c == null) return Task.FromResult<CategoryResponse?>(null);
            var res = new CategoryResponse(c.Id, c.CategoryCode, c.Name, c.Description, ProductCounts.GetValueOrDefault(c.Id, 0));
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

        public Task<string?> GetMaxSequentialCodeAsync(string prefix, int totalLength, CancellationToken cancellationToken = default)
        {
            var max = Categories
                .Where(c => !c.IsDeleted && c.CategoryCode.ToUpper().StartsWith(prefix.ToUpper()) && c.CategoryCode.Length == totalLength)
                .OrderByDescending(c => c.CategoryCode)
                .Select(c => c.CategoryCode)
                .FirstOrDefault();
            return Task.FromResult(max);
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
        var fakeRepo = new FakeCategoryRepository();
        var fakeUow = new FakeUnitOfWork();
        var catId = Guid.NewGuid();

        fakeRepo.Categories.Add(new Category
        {
            Id = catId,
            CategoryCode = "CAT-001",
            Name = "Bánh kẹo"
        });
        fakeRepo.ProductCounts[catId] = 12;

        var service = new CategoryService(fakeRepo, fakeUow);

        var result = await service.GetAllAsync();

        Assert.Single(result);
        Assert.Equal("Bánh kẹo", result[0].Name);
        Assert.Equal(12, result[0].ProductCount);
    }

    [Fact]
    public async Task GetByIdAsync_ExistingCategory_ReturnsCategoryWithProductCount()
    {
        var fakeRepo = new FakeCategoryRepository();
        var fakeUow = new FakeUnitOfWork();
        var catId = Guid.NewGuid();

        fakeRepo.Categories.Add(new Category
        {
            Id = catId,
            CategoryCode = "CAT000001",
            Name = "Đồ uống",
            Description = "Nước giải khát"
        });
        fakeRepo.ProductCounts[catId] = 5;

        var service = new CategoryService(fakeRepo, fakeUow);

        var result = await service.GetByIdAsync(catId);

        Assert.NotNull(result);
        Assert.Equal("CAT000001", result.Code);
        Assert.Equal("Đồ uống", result.Name);
        Assert.Equal(5, result.ProductCount);
    }

    [Fact]
    public async Task CreateAsync_ValidRequest_CreatesSuccessfullyAndTrimsInputs()
    {
        var fakeRepo = new FakeCategoryRepository();
        var fakeUow = new FakeUnitOfWork();
        var service = new CategoryService(fakeRepo, fakeUow);

        var request = new CategoryCreateRequest("  Đồ uống  ", " bev-01 ", "  Nước khoáng, trà  ");

        var result = await service.CreateAsync(request);

        Assert.NotNull(result);
        Assert.Equal("Đồ uống", result.Name);
        Assert.Equal("BEV-01", result.Code);
        Assert.Equal("Nước khoáng, trà", result.Description);
        Assert.Equal(0, result.ProductCount);
        Assert.True(fakeUow.SaveChangesCalled);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateAsync_EmptyOrWhitespaceName_ThrowsArgumentException(string invalidName)
    {
        var fakeRepo = new FakeCategoryRepository();
        var fakeUow = new FakeUnitOfWork();
        var service = new CategoryService(fakeRepo, fakeUow);

        var request = new CategoryCreateRequest(invalidName);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(request));
        Assert.Contains("Tên nhóm hàng không được để trống", ex.Message);
    }

    [Fact]
    public async Task CreateAsync_NameExceeds100Chars_ThrowsArgumentException()
    {
        var fakeRepo = new FakeCategoryRepository();
        var fakeUow = new FakeUnitOfWork();
        var service = new CategoryService(fakeRepo, fakeUow);

        var longName = new string('A', 101);
        var request = new CategoryCreateRequest(longName);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(request));
        Assert.Contains("không được vượt quá 100 ký tự", ex.Message);
    }

    [Fact]
    public async Task CreateAsync_DescriptionExceeds500Chars_ThrowsArgumentException()
    {
        var fakeRepo = new FakeCategoryRepository();
        var fakeUow = new FakeUnitOfWork();
        var service = new CategoryService(fakeRepo, fakeUow);

        var longDesc = new string('D', 501);
        var request = new CategoryCreateRequest("Bánh kẹo", "CAT01", longDesc);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(request));
        Assert.Contains("không được vượt quá 500 ký tự", ex.Message);
    }

    [Theory]
    [InlineData("CAT 001")]
    [InlineData("CAT@001")]
    [InlineData("MÃ_CÓ_DẤU")]
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVWXYZ12345")] // 31 chars
    public async Task CreateAsync_InvalidCodeFormatOrLength_ThrowsArgumentException(string invalidCode)
    {
        var fakeRepo = new FakeCategoryRepository();
        var fakeUow = new FakeUnitOfWork();
        var service = new CategoryService(fakeRepo, fakeUow);

        var request = new CategoryCreateRequest("Bánh kẹo", invalidCode);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(request));
        Assert.Contains("Mã nhóm hàng", ex.Message);
    }

    [Fact]
    public async Task CreateAsync_DuplicateName_ThrowsBusinessException()
    {
        var fakeRepo = new FakeCategoryRepository();
        var fakeUow = new FakeUnitOfWork();
        fakeRepo.Categories.Add(new Category { Id = Guid.NewGuid(), Name = "Bánh kẹo", CategoryCode = "CAT-01" });
        var service = new CategoryService(fakeRepo, fakeUow);

        var request = new CategoryCreateRequest("  bánh kẹo  ");

        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.CreateAsync(request));
        Assert.Equal("CATEGORY_NAME_EXISTS", ex.ErrorCode);
    }

    [Fact]
    public async Task CreateAsync_DuplicateCode_ThrowsBusinessException()
    {
        var fakeRepo = new FakeCategoryRepository();
        var fakeUow = new FakeUnitOfWork();
        fakeRepo.Categories.Add(new Category { Id = Guid.NewGuid(), Name = "Bánh kẹo", CategoryCode = "CAT-01" });
        var service = new CategoryService(fakeRepo, fakeUow);

        var request = new CategoryCreateRequest("Nước giải khát", "cat-01");

        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.CreateAsync(request));
        Assert.Equal("CATEGORY_CODE_EXISTS", ex.ErrorCode);
    }

    [Fact]
    public async Task CreateAsync_WhenCodeIsEmpty_GeneratesFirstCode_CAT000001()
    {
        var fakeRepo = new FakeCategoryRepository();
        var fakeUow = new FakeUnitOfWork();
        var service = new CategoryService(fakeRepo, fakeUow);

        var result = await service.CreateAsync(new CategoryCreateRequest("Đồ uống", "", null));

        Assert.Equal("CAT000001", result.Code);
        Assert.Equal("Đồ uống", result.Name);
        Assert.True(fakeUow.SaveChangesCalled);
    }

    [Fact]
    public async Task CreateAsync_WhenCodeIsEmptyAndPreviousExists_GeneratesNextSequentialCode_CAT000002()
    {
        var fakeRepo = new FakeCategoryRepository();
        fakeRepo.Categories.Add(new Category { Id = Guid.NewGuid(), Name = "Bánh kẹo", CategoryCode = "CAT000001" });
        var fakeUow = new FakeUnitOfWork();
        var service = new CategoryService(fakeRepo, fakeUow);

        var result = await service.CreateAsync(new CategoryCreateRequest("Nước ngọt", null, null));

        Assert.Equal("CAT000002", result.Code);
        Assert.Equal("Nước ngọt", result.Name);
    }

    [Fact]
    public async Task UpdateAsync_ValidRequest_UpdatesNameAndDescription()
    {
        var fakeRepo = new FakeCategoryRepository();
        var fakeUow = new FakeUnitOfWork();
        var catId = Guid.NewGuid();
        fakeRepo.Categories.Add(new Category { Id = catId, Name = "Tên cũ", CategoryCode = "CAT000001", Description = "Mô tả cũ" });
        fakeRepo.ProductCounts[catId] = 3;

        var service = new CategoryService(fakeRepo, fakeUow);

        var result = await service.UpdateAsync(catId, new CategoryUpdateRequest("  Tên mới  ", "  Mô tả mới  "));

        Assert.Equal("Tên mới", result.Name);
        Assert.Equal("Mô tả mới", result.Description);
        Assert.Equal(3, result.ProductCount);
        Assert.True(fakeUow.SaveChangesCalled);
    }

    [Fact]
    public async Task UpdateAsync_ValidationErrors_ThrowsArgumentException()
    {
        var fakeRepo = new FakeCategoryRepository();
        var fakeUow = new FakeUnitOfWork();
        var catId = Guid.NewGuid();
        var service = new CategoryService(fakeRepo, fakeUow);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.UpdateAsync(catId, new CategoryUpdateRequest("   ")));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.UpdateAsync(catId, new CategoryUpdateRequest(new string('A', 101))));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.UpdateAsync(catId, new CategoryUpdateRequest("Hợp lệ", new string('B', 501))));
    }

    [Fact]
    public async Task UpdateAsync_NonExistentCategory_ThrowsNotFoundException()
    {
        var fakeRepo = new FakeCategoryRepository();
        var fakeUow = new FakeUnitOfWork();
        var service = new CategoryService(fakeRepo, fakeUow);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.UpdateAsync(Guid.NewGuid(), new CategoryUpdateRequest("Tên mới")));
    }

    [Fact]
    public async Task UpdateAsync_DuplicateNameWithAnotherCategory_ThrowsBusinessException()
    {
        var fakeRepo = new FakeCategoryRepository();
        var fakeUow = new FakeUnitOfWork();
        var cat1Id = Guid.NewGuid();
        var cat2Id = Guid.NewGuid();
        fakeRepo.Categories.Add(new Category { Id = cat1Id, Name = "Bánh kẹo", CategoryCode = "CAT01" });
        fakeRepo.Categories.Add(new Category { Id = cat2Id, Name = "Đồ uống", CategoryCode = "CAT02" });

        var service = new CategoryService(fakeRepo, fakeUow);

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            service.UpdateAsync(cat2Id, new CategoryUpdateRequest("Bánh kẹo")));
        Assert.Equal("CATEGORY_NAME_EXISTS", ex.ErrorCode);
    }

    [Fact]
    public async Task DeleteAsync_CategoryWithProducts_ThrowsBusinessException()
    {
        var fakeRepo = new FakeCategoryRepository();
        var fakeUow = new FakeUnitOfWork();
        var catId = Guid.NewGuid();
        fakeRepo.Categories.Add(new Category { Id = catId, Name = "Bánh kẹo", CategoryCode = "CAT-01" });
        fakeRepo.ProductCounts[catId] = 5;

        var service = new CategoryService(fakeRepo, fakeUow);

        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.DeleteAsync(catId));
        Assert.Equal("CATEGORY_HAS_PRODUCTS", ex.ErrorCode);
    }

    [Fact]
    public async Task DeleteAsync_EmptyCategory_DeletesSuccessfully()
    {
        var fakeRepo = new FakeCategoryRepository();
        var fakeUow = new FakeUnitOfWork();
        var catId = Guid.NewGuid();
        var category = new Category { Id = catId, Name = "Gia vị", CategoryCode = "CAT-SPICE" };
        fakeRepo.Categories.Add(category);
        fakeRepo.ProductCounts[catId] = 0;

        var service = new CategoryService(fakeRepo, fakeUow);

        await service.DeleteAsync(catId);

        Assert.True(category.IsDeleted);
        Assert.True(fakeUow.SaveChangesCalled);
    }
}

