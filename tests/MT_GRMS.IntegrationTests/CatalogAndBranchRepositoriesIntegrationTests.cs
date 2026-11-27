using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Branches.Dtos;
using MT_GRMS.Application.Catalog.Dtos;
using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Entities.HR;
using MT_GRMS.Domain.Entities.Inventory;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Domain.Entities.Sales;
using MT_GRMS.Domain.Entities.Supplier;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Infrastructure.Persistence;
using MT_GRMS.Infrastructure.Repositories;
using Xunit;

namespace MT_GRMS.IntegrationTests;

public class CatalogAndBranchRepositoriesIntegrationTests
{
    private const string TenantId = "tenant-repo-test-01";

    private class TestTenantContext : ITenantContext
    {
        public string CurrentTenantId { get; set; } = TenantId;
        public Guid CurrentUserId { get; set; } = Guid.NewGuid();
        public Guid? CurrentBranchId { get; set; } = null;
        public void SetContext(string tenantId, Guid userId, Guid? branchId)
        {
            CurrentTenantId = tenantId;
            CurrentUserId = userId;
            CurrentBranchId = branchId;
        }
        public string CurrentRole { get; set; } = "ADMIN";
        public void SetRole(string? role) => CurrentRole = role ?? string.Empty;
    }

    private (AppDbContext Context, TestTenantContext Tenant) CreateDbContext()
    {
        var tenant = new TestTenantContext();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"RepoDb_{Guid.NewGuid()}")
            .Options;

        return (new AppDbContext(options, tenant), tenant);
    }

    [Fact]
    public async Task CategoryRepository_AllMethods_ShouldWork()
    {
        var (context, _) = CreateDbContext();
        using (context)
        {
            var repo = new CategoryRepository(context);

            var cat1 = new Category
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                CategoryCode = "CAT001",
                Name = "Gia vị",
                Description = "Gia vị nấu ăn",
                CreatedAt = DateTime.UtcNow
            };
            var cat2 = new Category
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                CategoryCode = "CAT002",
                Name = "Đồ uống",
                Description = "Các loại nước",
                CreatedAt = DateTime.UtcNow.AddMinutes(5)
            };
            await repo.AddAsync(cat1);
            await repo.AddAsync(cat2);
            await context.SaveChangesAsync();

            var prod1 = new Product
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                ProductCode = "SP001",
                Name = "Nước mắm",
                CategoryId = cat1.Id,
                BaseUnit = "Chai",
                CostPrice = 20_000m,
                SellingPrice = 30_000m,
                CreatedAt = DateTime.UtcNow
            };
            await context.Products.AddAsync(prod1);
            await context.SaveChangesAsync();

            // 1. GetAllWithProductCountAsync
            var all = await repo.GetAllWithProductCountAsync();
            Assert.Equal(2, all.Count);
            var cat1Res = all.First(c => c.Id == cat1.Id);
            Assert.Equal(1, cat1Res.ProductCount);

            // 2. GetByIdWithProductCountAsync
            var byId = await repo.GetByIdWithProductCountAsync(cat1.Id);
            Assert.NotNull(byId);
            Assert.Equal("Gia vị", byId!.Name);
            Assert.Null(await repo.GetByIdWithProductCountAsync(Guid.NewGuid()));

            // 3. ExistsByNameAsync
            Assert.True(await repo.ExistsByNameAsync("  Gia vị  "));
            Assert.False(await repo.ExistsByNameAsync("Gia vị", cat1.Id));
            Assert.False(await repo.ExistsByNameAsync("Bánh kẹo"));

            // 4. ExistsByCodeAsync
            Assert.True(await repo.ExistsByCodeAsync("cat001"));
            Assert.False(await repo.ExistsByCodeAsync("cat001", cat1.Id));
            Assert.False(await repo.ExistsByCodeAsync("CAT999"));

            // 5. GetProductCountAsync
            Assert.Equal(1, await repo.GetProductCountAsync(cat1.Id));
            Assert.Equal(0, await repo.GetProductCountAsync(cat2.Id));

            // 6. GetMaxSequentialCodeAsync
            Assert.Equal("CAT002", await repo.GetMaxSequentialCodeAsync("CAT", 6));
            Assert.Null(await repo.GetMaxSequentialCodeAsync("DOG", 6));
        }
    }

    [Fact]
    public async Task ProductRepository_AllMethods_ShouldWork()
    {
        var (context, _) = CreateDbContext();
        using (context)
        {
            var repo = new ProductRepository(context);

            var cat = new Category
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                CategoryCode = "CAT01",
                Name = "Thực phẩm",
                CreatedAt = DateTime.UtcNow
            };
            await context.Categories.AddAsync(cat);

            var prod = new Product
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                ProductCode = "SP0001",
                Name = "Gạo thơm ST25",
                Barcode = "8931234567890",
                CategoryId = cat.Id,
                BaseUnit = "Kg",
                CostPrice = 25_000m,
                SellingPrice = 35_000m,
                Status = ProductStatus.Active,
                CreatedAt = DateTime.UtcNow
            };
            var unit = new ProductUnit
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                ProductId = prod.Id,
                UnitName = "Bao 5kg",
                ConversionRate = 5m,
                Price = 170_000m,
                IsBaseUnit = false,
                CreatedAt = DateTime.UtcNow
            };
            await repo.AddAsync(prod);
            await context.ProductUnits.AddAsync(unit);
            await context.SaveChangesAsync();

            // 1. GetPagedListAsync
            var filter = new ProductQueryFilter
            {
                SearchTerm = "ST25",
                CategoryId = cat.Id,
                Status = "Active",
                PageIndex = 1,
                PageSize = 10
            };
            var (items, count) = await repo.GetPagedListAsync(filter);
            Assert.Equal(1, count);
            Assert.Single(items);

            // Filter with CategoryIds list
            var filter2 = new ProductQueryFilter { CategoryIds = new List<Guid> { cat.Id } };
            var (items2, count2) = await repo.GetPagedListAsync(filter2);
            Assert.Equal(1, count2);

            // 2. GetByIdWithCategoryAsync
            var found = await repo.GetByIdWithCategoryAsync(prod.Id);
            Assert.NotNull(found);
            Assert.Equal("Thực phẩm", found!.Category.Name);
            Assert.Single(found.Units);

            // 3. ExistsByCodeAsync & ExistsByBarcodeAsync
            Assert.True(await repo.ExistsByCodeAsync("sp0001"));
            Assert.False(await repo.ExistsByCodeAsync("sp0001", prod.Id));
            Assert.True(await repo.ExistsByBarcodeAsync("8931234567890"));
            Assert.False(await repo.ExistsByBarcodeAsync("8931234567890", prod.Id));
            Assert.False(await repo.ExistsByBarcodeAsync(""));

            // 4. GetMaxSequentialCodeAsync
            Assert.Equal("SP0001", await repo.GetMaxSequentialCodeAsync("SP", 6));

            // 5. HasOrdersAsync & GetProductIdsWithOrdersAsync
            Assert.False(await repo.HasOrdersAsync(prod.Id));
            var orderId = Guid.NewGuid();
            context.OrderDetails.Add(new OrderDetail
            {
                Id = Guid.NewGuid(),
                OrderId = orderId,
                ProductId = prod.Id,
                Qty = 2,
                UnitPrice = 35_000m,
                CreatedAt = DateTime.UtcNow
            });
            await context.SaveChangesAsync();

            Assert.True(await repo.HasOrdersAsync(prod.Id));
            var orderedSet = await repo.GetProductIdsWithOrdersAsync(new[] { prod.Id, Guid.NewGuid() });
            Assert.Contains(prod.Id, orderedSet);

            // 6. DeleteWithUnitsAsync
            await repo.DeleteWithUnitsAsync(prod);
            await context.SaveChangesAsync();
            Assert.True(prod.IsDeleted);
            var reloadedUnit = await context.ProductUnits.FindAsync(unit.Id);
            Assert.True(reloadedUnit!.IsDeleted);
        }
    }

    [Fact]
    public async Task ProductUnitRepository_AllMethods_ShouldWork()
    {
        var (context, tenant) = CreateDbContext();
        using (context)
        {
            var repo = new ProductUnitRepository(context, tenant);

            var prod = new Product
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                ProductCode = "SP02",
                Name = "Bia Saigon",
                BaseUnit = "Lon",
                CostPrice = 10_000m,
                SellingPrice = 15_000m,
                Barcode = "8930000000001",
                CreatedAt = DateTime.UtcNow
            };
            await context.Products.AddAsync(prod);

            var baseUnit = new ProductUnit
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                ProductId = prod.Id,
                UnitName = "Lon",
                ConversionRate = 1m,
                Price = 15_000m,
                IsBaseUnit = true,
                Barcode = "8930000000002",
                CreatedAt = DateTime.UtcNow
            };
            var packUnit = new ProductUnit
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                ProductId = prod.Id,
                UnitName = "Loc 6",
                ConversionRate = 6m,
                Price = 85_000m,
                IsBaseUnit = false,
                CreatedAt = DateTime.UtcNow
            };
            await repo.AddAsync(baseUnit);
            await repo.AddAsync(packUnit);
            await context.SaveChangesAsync();

            // 1. GetByIdAsync
            var u = await repo.GetByIdAsync(baseUnit.Id);
            Assert.NotNull(u);
            Assert.Equal("Lon", u!.UnitName);

            // 2. GetByProductIdAsync
            var units = await repo.GetByProductIdAsync(prod.Id);
            Assert.Equal(2, units.Count);

            // 3. ExistsByNameAsync
            Assert.True(await repo.ExistsByNameAsync(prod.Id, "lon"));
            Assert.False(await repo.ExistsByNameAsync(prod.Id, "lon", baseUnit.Id));
            Assert.False(await repo.ExistsByNameAsync(prod.Id, "Thung 24"));

            // 4. ExistsByBarcodeAsync
            Assert.True(await repo.ExistsByBarcodeAsync("8930000000002"));
            Assert.True(await repo.ExistsByBarcodeAsync("8930000000001")); // Matches product barcode
            Assert.False(await repo.ExistsByBarcodeAsync("8939999999999"));
            Assert.False(await repo.ExistsByBarcodeAsync(""));

            // 5. GetBaseUnitByProductIdAsync
            var bUnit = await repo.GetBaseUnitByProductIdAsync(prod.Id);
            Assert.NotNull(bUnit);
            Assert.True(bUnit!.IsBaseUnit);

            // 6. CountByProductIdAsync
            Assert.Equal(2, await repo.CountByProductIdAsync(prod.Id));

            // 7. Update & Delete
            packUnit.Price = 88_000m;
            repo.Update(packUnit);
            repo.Delete(packUnit);
            await context.SaveChangesAsync();
            Assert.True(packUnit.IsDeleted);
        }
    }

    [Fact]
    public async Task BranchRepository_AllMethods_ShouldWork()
    {
        var (context, _) = CreateDbContext();
        using (context)
        {
            var repo = new BranchRepository(context);

            var branch1 = new Branch
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                BranchCode = "CN01",
                Name = "Chi nhánh Cầu Giấy",
                Address = "88 Cầu Giấy",
                Phone = "0981111111",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            var branch2 = new Branch
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                BranchCode = "CN02",
                Name = "Chi nhánh Đống Đa",
                Address = "12 Chùa Bộc",
                Phone = "0982222222",
                IsActive = false,
                CreatedAt = DateTime.UtcNow.AddMinutes(1)
            };
            await repo.AddAsync(branch1);
            await repo.AddAsync(branch2);
            await context.SaveChangesAsync();

            // 1. ExistsByCodeAsync
            Assert.True(await repo.ExistsByCodeAsync("CN01"));
            Assert.False(await repo.ExistsByCodeAsync("CN99"));

            // 2. CountActiveAsync & CountTotalAsync
            Assert.Equal(1, await repo.CountActiveAsync());
            Assert.Equal(2, await repo.CountTotalAsync());

            // 3. GetPagedListAsync
            var (items, total) = await repo.GetPagedListAsync("Cầu Giấy", null, "Active", null, null, null, null, 1, 10);
            Assert.Equal(1, total);
            Assert.Single(items);

            var (byCode, _) = await repo.GetPagedListAsync(null, null, null, new[] { "CN01" }, null, null, null, 1, 10);
            Assert.Single(byCode);

            var (byName, _) = await repo.GetPagedListAsync(null, null, null, null, new[] { "Đống Đa" }, null, null, 1, 10);
            Assert.Single(byName);

            var (byAddr, _) = await repo.GetPagedListAsync(null, null, null, null, null, new[] { "Chùa Bộc" }, null, 1, 10);
            Assert.Single(byAddr);

            var (byPhone, _) = await repo.GetPagedListAsync(null, null, null, null, null, null, new[] { "0981111111" }, 1, 10);
            Assert.Single(byPhone);

            var (byInactive, _) = await repo.GetPagedListAsync(null, null, "Inactive", null, null, null, null, 1, 10);
            Assert.Single(byInactive);

            var (byAll, _) = await repo.GetPagedListAsync(null, null, "all", null, null, null, null, 1, 10);
            Assert.Equal(2, byAll.Count);

            // 4. GetBranchDetailAsync
            var emp1 = new Employee
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                BranchId = branch1.Id,
                FullName = "Nhân viên 1",
                EmployeeCode = "NV01",
                Phone = "0911111111",
                Status = EmployeeStatus.Active,
                StartDate = DateTime.UtcNow.AddMonths(-1),
                CreatedAt = DateTime.UtcNow
            };
            var emp2 = new Employee
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                BranchId = branch1.Id,
                FullName = "Nhân viên 2",
                EmployeeCode = "NV02",
                Phone = "0922222222",
                Status = EmployeeStatus.Inactive,
                StartDate = DateTime.UtcNow.AddMonths(-2),
                CreatedAt = DateTime.UtcNow.AddMinutes(1)
            };
            await context.Employees.AddRangeAsync(emp1, emp2);
            await context.SaveChangesAsync();

            var b = await repo.GetBranchDetailAsync(branch1.Id);
            Assert.NotNull(b);
            Assert.Equal("CN01", b!.Code);
            Assert.Equal(2, b.TotalEmployees);
            Assert.Equal(1, b.ActiveEmployees);
            Assert.Equal(2, b.RecentEmployees.Count);

            var bNull = await repo.GetBranchDetailAsync(Guid.NewGuid());
            Assert.Null(bNull);
        }
    }

    [Fact]
    public async Task BranchProductRepository_AllMethods_ShouldWork()
    {
        var (context, tenant) = CreateDbContext();
        using (context)
        {
            var repo = new BranchProductRepository(context, tenant);

            var cat = new Category
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                CategoryCode = "CAT-BP",
                Name = "Sữa",
                CreatedAt = DateTime.UtcNow
            };
            var branch = new Branch
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                BranchCode = "CN01",
                Name = "Chi nhánh 1",
                CreatedAt = DateTime.UtcNow
            };
            var prod = new Product
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                ProductCode = "SP01",
                Name = "Sữa đặc",
                CategoryId = cat.Id,
                Category = cat,
                BaseUnit = "Hộp",
                CostPrice = 20_000m,
                SellingPrice = 25_000m,
                CreatedAt = DateTime.UtcNow
            };
            var bp = new BranchProduct
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                BranchId = branch.Id,
                ProductId = prod.Id,
                Branch = branch,
                Product = prod,
                Status = "Active",
                CreatedAt = DateTime.UtcNow
            };
            await context.Categories.AddAsync(cat);
            await context.Branches.AddAsync(branch);
            await context.Products.AddAsync(prod);
            await repo.AddBranchProductAsync(bp);
            await context.SaveChangesAsync();

            // 1. GetBranchProductAsync
            var found = await repo.GetBranchProductAsync(branch.Id, prod.Id);
            Assert.NotNull(found);
            Assert.Equal("Active", found!.Status);

            // 2. GetPagedBranchProductsAsync
            var (items, count) = await repo.GetPagedBranchProductsAsync(branch.Id, "Sữa", null, "Active", null, 1, 10);
            Assert.Equal(1, count);
            Assert.Single(items);

            // 3. GetBranchSummariesAsync
            var summaries = await repo.GetBranchSummariesAsync();
            Assert.NotEmpty(summaries);

            // 4. GetProductByIdAsync & GetBranchByIdAsync
            Assert.NotNull(await repo.GetProductByIdAsync(prod.Id));
            Assert.NotNull(await repo.GetBranchByIdAsync(branch.Id));

            // 5. RemoveBranchProduct
            repo.RemoveBranchProduct(bp);
            await context.SaveChangesAsync();
            Assert.True(bp.IsDeleted);

            // 6. BranchStocks methods
            var stock1 = new BranchStock
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                BranchId = branch.Id,
                ProductId = prod.Id,
                QuantityOnHand = 10,
                CreatedAt = DateTime.UtcNow
            };
            await repo.AddBranchStockAsync(stock1);
            await context.SaveChangesAsync();

            var stocks = await repo.GetBranchStocksByProductAsync(branch.Id, prod.Id);
            Assert.NotEmpty(stocks);

            repo.RemoveBranchStocks(new[] { stock1 });
            await context.SaveChangesAsync();
            Assert.True(stock1.IsDeleted);

            // 7. Supplier & Assignable products
            var sup = new Supplier
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                Code = "SUP-BP",
                Name = "Nha Cung Cap BP",
                Status = SupplierStatus.Active,
                CreatedAt = DateTime.UtcNow
            };
            var supProd = new SupplierProduct
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                SupplierId = sup.Id,
                ProductId = prod.Id,
                Supplier = sup,
                Product = prod,
                CreatedAt = DateTime.UtcNow
            };
            var assignment = new SupplierBranchAssignment
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                SupplierId = sup.Id,
                BranchId = branch.Id,
                Status = SupplierStatus.Active,
                Supplier = sup,
                CreatedAt = DateTime.UtcNow
            };
            await context.Suppliers.AddAsync(sup);
            await context.SupplierProducts.AddAsync(supProd);
            await context.SupplierBranchAssignments.AddAsync(assignment);
            await context.SaveChangesAsync();

            var fetchedSupProd = await repo.GetSupplierProductAsync(prod.Id, sup.Id);
            Assert.NotNull(fetchedSupProd);

            var activeSupIds = await repo.GetActiveSupplierIdsForBranchAsync(branch.Id);
            Assert.Contains(sup.Id, activeSupIds);

            var assignables = await repo.GetAssignableProductsForBranchAsync(branch.Id);
            Assert.NotNull(assignables);
        }
    }

    [Fact]
    public async Task BranchProductRepository_QueryFilters_ShouldFilterCorrectly()
    {
        var (context, tenant) = CreateDbContext();
        using (context)
        {
            var repo = new BranchProductRepository(context, tenant);

            var cat = new Category
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                CategoryCode = "CAT-DAIRY",
                Name = "Sữa Tươi",
                CreatedAt = DateTime.UtcNow
            };
            var branch = new Branch
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                BranchCode = "BR-HN01",
                Name = "Hà Nội Store",
                CreatedAt = DateTime.UtcNow
            };
            var prod = new Product
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                ProductCode = "SKU-MILK-01",
                Name = "Sữa Ba Vì",
                CategoryId = cat.Id,
                Category = cat,
                BaseUnit = "Hộp",
                CostPrice = 15_000m,
                SellingPrice = 20_000m,
                MinStock = 5,
                CreatedAt = DateTime.UtcNow
            };
            var bp = new BranchProduct
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                BranchId = branch.Id,
                ProductId = prod.Id,
                Branch = branch,
                Product = prod,
                Status = "Active",
                CreatedAt = DateTime.UtcNow
            };
            var stock = new BranchStock
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                BranchId = branch.Id,
                ProductId = prod.Id,
                QuantityOnHand = 20,
                CreatedAt = DateTime.UtcNow
            };
            var sup = new Supplier
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                Code = "SUP-BV",
                Name = "Công ty Ba Vì",
                Status = SupplierStatus.Active,
                CreatedAt = DateTime.UtcNow
            };
            var supProd = new SupplierProduct
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                SupplierId = sup.Id,
                ProductId = prod.Id,
                Supplier = sup,
                Product = prod,
                CreatedAt = DateTime.UtcNow
            };

            await context.Categories.AddAsync(cat);
            await context.Branches.AddAsync(branch);
            await context.Products.AddAsync(prod);
            await context.BranchProducts.AddAsync(bp);
            await context.BranchStocks.AddAsync(stock);
            await context.Suppliers.AddAsync(sup);
            await context.SupplierProducts.AddAsync(supProd);
            await context.SaveChangesAsync();

            // 1. Filter by Skus
            var (items1, cnt1) = await repo.GetPagedBranchProductsAsync(branch.Id, new BranchProductQueryFilter { Skus = new() { "SKU-MILK" } }, 1, 10);
            Assert.Equal(1, cnt1);

            // 2. Filter by Names
            var (items2, cnt2) = await repo.GetPagedBranchProductsAsync(branch.Id, new BranchProductQueryFilter { Names = new() { "Ba Vì" } }, 1, 10);
            Assert.Equal(1, cnt2);

            // 3. Filter by BranchCodes
            var (items3, cnt3) = await repo.GetPagedBranchProductsAsync(branch.Id, new BranchProductQueryFilter { BranchCodes = new() { "HN01" } }, 1, 10);
            Assert.Equal(1, cnt3);

            // 4. Filter by BranchCode string
            var (items4, cnt4) = await repo.GetPagedBranchProductsAsync(branch.Id, new BranchProductQueryFilter { BranchCode = "HN01" }, 1, 10);
            Assert.Equal(1, cnt4);

            // 5. Filter by BranchNames
            var (items5, cnt5) = await repo.GetPagedBranchProductsAsync(branch.Id, new BranchProductQueryFilter { BranchNames = new() { "Hà Nội" } }, 1, 10);
            Assert.Equal(1, cnt5);

            // 6. Filter by SupplierCodes
            var (items6, cnt6) = await repo.GetPagedBranchProductsAsync(branch.Id, new BranchProductQueryFilter { SupplierCodes = new() { "SUP-BV" } }, 1, 10);
            Assert.Equal(1, cnt6);

            // 7. Filter by SupplierNames
            var (items7, cnt7) = await repo.GetPagedBranchProductsAsync(branch.Id, new BranchProductQueryFilter { SupplierNames = new() { "Ba Vì" } }, 1, 10);
            Assert.Equal(1, cnt7);

            // 8. Filter by Category
            var (items8, cnt8) = await repo.GetPagedBranchProductsAsync(branch.Id, new BranchProductQueryFilter { Category = "Sữa Tươi" }, 1, 10);
            Assert.Equal(1, cnt8);

            // 9. Filter by Status
            var (items9, cnt9) = await repo.GetPagedBranchProductsAsync(branch.Id, new BranchProductQueryFilter { Status = "Active" }, 1, 10);
            Assert.Equal(1, cnt9);

            // 10. Filter by StockAlertFilter NORMAL (qty 20 > minStock 5)
            var (items10, cnt10) = await repo.GetPagedBranchProductsAsync(branch.Id, new BranchProductQueryFilter { StockAlertFilter = "NORMAL" }, 1, 10);
            Assert.Equal(1, cnt10);

            // 11. Filter by StockAlertFilter OUT_OF_STOCK (qty 20 != 0)
            var (items11, cnt11) = await repo.GetPagedBranchProductsAsync(branch.Id, new BranchProductQueryFilter { StockAlertFilter = "OUT_OF_STOCK" }, 1, 10);
            Assert.Equal(0, cnt11);

            // 12. Filter by StockAlertFilter LOW (qty 20 > minStock 5 -> 0)
            var (items12, cnt12) = await repo.GetPagedBranchProductsAsync(branch.Id, new BranchProductQueryFilter { StockAlertFilter = "LOW" }, 1, 10);
            Assert.Equal(0, cnt12);
        }
    }

    [Fact]
    public async Task ProductRepository_MultiTokenFiltersAndStatus_AndImageUrlsPersistence_ShouldWork()
    {
        var (context, _) = CreateDbContext();
        using (context)
        {
            var repo = new ProductRepository(context);
            var cat = new Category { Id = Guid.NewGuid(), TenantId = TenantId, Name = "Đồ uống" };
            var p1 = new Product
            {
                Id = Guid.NewGuid(), TenantId = TenantId, CategoryId = cat.Id, ProductCode = "SP-001",
                Name = "Coca Cola 330ml", Barcode = "8930001", BaseUnit = "Lon", SellingPrice = 10000m,
                Status = ProductStatus.Active,
                ImageUrls = new List<string> { "https://cdn.test/mat-sau.jpg", "https://cdn.test/mat-truoc.jpg" }
            };
            var p2 = new Product
            {
                Id = Guid.NewGuid(), TenantId = TenantId, CategoryId = cat.Id, ProductCode = "SP-002",
                Name = "Pepsi 330ml", Barcode = "8930002", BaseUnit = "Lon", SellingPrice = 10000m,
                Status = ProductStatus.Inactive
            };
            var p3 = new Product
            {
                Id = Guid.NewGuid(), TenantId = TenantId, CategoryId = cat.Id, ProductCode = "SP-003",
                Name = "Nước suối 500ml", Barcode = "8930003", BaseUnit = "Chai", SellingPrice = 5000m,
                Status = ProductStatus.Active
            };

            await context.Categories.AddAsync(cat);
            await context.Products.AddRangeAsync(p1, p2, p3);
            await context.SaveChangesAsync();

            // Verify multi-image persistence after reload
            var reloaded = await repo.GetByIdWithCategoryAsync(p1.Id);
            Assert.NotNull(reloaded);
            Assert.Equal("https://cdn.test/mat-sau.jpg", reloaded!.ImageUrl);
            Assert.Equal(2, reloaded.ImageUrls.Count);
            Assert.Equal("https://cdn.test/mat-truoc.jpg", reloaded.ImageUrls[1]);

            // Multi-token Skus filter
            var (skuItems, skuTotal) = await repo.GetPagedListAsync(new ProductQueryFilter
            {
                Skus = new List<string> { "SP-001", "SP-003" },
                PageIndex = 1,
                PageSize = 10
            });
            Assert.Equal(2, skuTotal);
            Assert.Equal(2, skuItems.Count);

            // Multi-token Names filter
            var (nameItems, nameTotal) = await repo.GetPagedListAsync(new ProductQueryFilter
            {
                Names = new List<string> { "Coca", "Pepsi" },
                PageIndex = 1,
                PageSize = 10
            });
            Assert.Equal(2, nameTotal);
            Assert.Equal(2, nameItems.Count);

            // Status filter (Discontinued mapped to Inactive)
            var (statusItems, statusTotal) = await repo.GetPagedListAsync(new ProductQueryFilter
            {
                Status = "Discontinued",
                PageIndex = 1,
                PageSize = 10
            });
            Assert.Equal(1, statusTotal);
            Assert.Single(statusItems);
            Assert.Equal("SP-002", statusItems[0].ProductCode);
        }
    }
}
