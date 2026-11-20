using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Batches.Dtos;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Stocktakes.Dtos;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Entities.Inventory;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Infrastructure.Persistence;
using MT_GRMS.Infrastructure.Repositories;
using Xunit;

namespace MT_GRMS.IntegrationTests;

public class InventoryAndStockRepositoriesIntegrationTests
{
    private const string TenantId = "tenant-inv-repo-01";

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
            .UseInMemoryDatabase(databaseName: $"InvRepoDb_{Guid.NewGuid()}")
            .Options;

        return (new AppDbContext(options, tenant), tenant);
    }

    [Fact]
    public async Task GenericRepository_AllMethods_ShouldWork()
    {
        var (context, _) = CreateDbContext();
        using (context)
        {
            var repo = new GenericRepository<Category>(context);

            var cat = new Category
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                CategoryCode = "GEN01",
                Name = "Generic Cat",
                CreatedAt = DateTime.UtcNow
            };

            await repo.AddAsync(cat);
            await context.SaveChangesAsync();

            var found = await repo.GetByIdAsync(cat.Id);
            Assert.NotNull(found);

            var all = await repo.GetAllAsync();
            Assert.NotEmpty(all);

            var foundList = await repo.FindAsync(c => c.CategoryCode == "GEN01");
            Assert.Single(foundList);

            cat.Name = "Updated Cat";
            repo.Update(cat);
            await context.SaveChangesAsync();

            repo.Delete(cat);
            await context.SaveChangesAsync();
            Assert.True(cat.IsDeleted);
        }
    }

    [Fact]
    public async Task BatchRepository_AllMethods_ShouldWork()
    {
        var (context, _) = CreateDbContext();
        using (context)
        {
            var repo = new BatchRepository(context);

            var branch = new Branch
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                BranchCode = "BR-B",
                Name = "Chi nhánh Batch",
                CreatedAt = DateTime.UtcNow
            };
            var cat = new Category
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                CategoryCode = "CAT-B",
                Name = "Bánh",
                CreatedAt = DateTime.UtcNow
            };
            var prod = new Product
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                ProductCode = "SP-B",
                Name = "Bánh mì",
                CategoryId = cat.Id,
                Category = cat,
                BaseUnit = "Cái",
                HasBatches = true,
                CreatedAt = DateTime.UtcNow
            };
            var stock = new BranchStock
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                BranchId = branch.Id,
                ProductId = prod.Id,
                QuantityOnHand = 10,
                BatchCode = "LOT-001",
                ExpiryDate = DateTime.UtcNow.AddDays(10),
                CreatedAt = DateTime.UtcNow
            };

            await context.Branches.AddAsync(branch);
            await context.Categories.AddAsync(cat);
            await context.Products.AddAsync(prod);
            await context.BranchStocks.AddAsync(stock);
            await context.SaveChangesAsync();

            // 1. GetBranchStockForUpdateAsync
            var foundStock = await repo.GetBranchStockForUpdateAsync(TenantId, stock.Id);
            Assert.NotNull(foundStock);
            Assert.Equal("LOT-001", foundStock!.BatchCode);

            // 2. GetPagedBatchesAsync
            var paged = await repo.GetPagedBatchesAsync(TenantId, new BatchFilterQuery
            {
                BranchId = branch.Id,
                PageIndex = 1,
                PageSize = 10
            });
            Assert.NotNull(paged);

            // 3. GetBatchByIdAsync
            var batchById = await repo.GetBatchByIdAsync(TenantId, stock.Id);
            Assert.NotNull(batchById);
            Assert.Equal("LOT-001", batchById!.BatchCode);

            // 4. AddBatchAsync
            var newBatch = new Batch
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                ProductId = prod.Id,
                BatchCode = "LOT-NEW",
                ExpiryDate = DateTime.UtcNow.AddDays(90)
            };
            var addedBatch = await repo.AddBatchAsync(newBatch);
            Assert.NotNull(addedBatch);

            // 5. GetNearExpiryBatchesAsync
            var nearExpiry = await repo.GetNearExpiryBatchesAsync(TenantId, 30, branch.Id);
            Assert.NotEmpty(nearExpiry);
        }
    }

    [Fact]
    public async Task StocktakeRepository_AllMethods_ShouldWork()
    {
        var (context, _) = CreateDbContext();
        using (context)
        {
            var repo = new StocktakeRepository(context);

            var branch = new Branch
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                BranchCode = "BR-ST",
                Name = "Chi nhánh ST",
                CreatedAt = DateTime.UtcNow
            };
            var st = new Stocktake
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                Code = "KK001",
                BranchId = branch.Id,
                Branch = branch,
                Status = "Draft",
                Scope = "ALL",
                ReasonType = "PERIODIC",
                Lines = new List<StocktakeLine>(),
                CreatedAt = DateTime.UtcNow
            };
            await context.Branches.AddAsync(branch);
            await repo.AddAsync(st);
            await context.SaveChangesAsync();

            // 1. GetPagedAsync
            var paged = await repo.GetPagedAsync(TenantId, new StocktakeFilterQuery
            {
                Keyword = "KK001",
                PageIndex = 1,
                PageSize = 10
            });
            Assert.Equal(1, paged.TotalItems);

            // 2. GetByIdAsync
            var foundSt = await repo.GetByIdAsync(TenantId, st.Id);
            Assert.NotNull(foundSt);

            // 3. GenerateCodeAsync
            var generatedCode = await repo.GenerateCodeAsync(TenantId);
            Assert.NotEmpty(generatedCode);

            // 4. UpdateAsync
            st.Status = "Completed";
            await repo.UpdateAsync(st);
            var updatedSt = await repo.GetByIdAsync(TenantId, st.Id);
            Assert.Equal("Completed", updatedSt!.Status);

            // 5. Product & BranchStock for detail and scopes
            var prod = new Product
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                ProductCode = "SP-ST-01",
                Name = "Hang Hoa ST",
                BaseUnit = "Chai",
                CreatedAt = DateTime.UtcNow
            };
            var bStock = new BranchStock
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                BranchId = branch.Id,
                ProductId = prod.Id,
                QuantityOnHand = 15,
                BatchCode = "LOT-ST-01",
                CreatedAt = DateTime.UtcNow
            };
            var stLine = new StocktakeLine
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                StocktakeId = st.Id,
                ProductId = prod.Id,
                BranchStockId = bStock.Id,
                SystemQty = 15,
                ActualQty = 14,
                VarianceQty = -1,
                UnitCost = 10_000m,
                CreatedAt = DateTime.UtcNow
            };
            await context.Products.AddAsync(prod);
            await context.BranchStocks.AddAsync(bStock);
            await context.StocktakeLines.AddAsync(stLine);
            await context.SaveChangesAsync();

            // 6. GetDetailDtoByIdAsync
            var detailDto = await repo.GetDetailDtoByIdAsync(TenantId, st.Id);
            Assert.NotNull(detailDto);
            Assert.Equal("Completed", detailDto!.Status);

            // 7. GetBranchStocksForScopeAsync
            var scopeStocks = await repo.GetBranchStocksForScopeAsync(TenantId, branch.Id, "ALL", null, null);
            Assert.NotEmpty(scopeStocks);

            // 8. GetBranchStockByIdAsync
            var fetchedBStock = await repo.GetBranchStockByIdAsync(TenantId, bStock.Id);
            Assert.NotNull(fetchedBStock);

            // 9. GetGrnTraceByStockIdsAsync
            var grnTrace = await repo.GetGrnTraceByStockIdsAsync(TenantId, new List<Guid> { bStock.Id });
            Assert.NotNull(grnTrace);
        }
    }

    [Fact]
    public async Task RepackRepository_AllMethods_ShouldWork()
    {
        var (context, _) = CreateDbContext();
        using (context)
        {
            var repo = new RepackRepository(context);

            var stock = new BranchStock
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                BranchId = Guid.NewGuid(),
                ProductId = Guid.NewGuid(),
                QuantityOnHand = 50,
                CreatedAt = DateTime.UtcNow
            };
            var pkg = new Package
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                InternalCode = "PKG001",
                BranchStockId = stock.Id,
                NetWeight = 2.5m,
                Status = "AVAILABLE",
                CreatedAt = DateTime.UtcNow
            };

            await context.BranchStocks.AddAsync(stock);
            await context.Packages.AddAsync(pkg);
            await context.SaveChangesAsync();

            // 1. GetBranchStockAsync
            Assert.NotNull(await repo.GetBranchStockAsync(TenantId, stock.Id));

            // 2. GetActivePackageWeightAsync
            var weight = await repo.GetActivePackageWeightAsync(TenantId, stock.Id);
            Assert.Equal(2.5m, weight);

            // 3. GetPackagesByBranchStockAsync
            var packages = await repo.GetPackagesByBranchStockAsync(TenantId, stock.Id);
            Assert.Single(packages);

            // 4. GetPackageByIdAsync
            Assert.NotNull(await repo.GetPackageByIdAsync(TenantId, pkg.Id));

            // 5. CountPackagesCreatedTodayAsync
            Assert.Equal(1, await repo.CountPackagesCreatedTodayAsync(TenantId, DateTime.UtcNow));

            // 6. CountRepackOrdersTodayAsync
            Assert.Equal(0, await repo.CountRepackOrdersTodayAsync(TenantId, DateTime.UtcNow));

            // 7. GetProductWithUnitsAsync
            var repackProd = new Product
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                ProductCode = "SP-RP-01",
                Name = "Gao Nang Huong",
                BaseUnit = "Kg",
                CreatedAt = DateTime.UtcNow
            };
            await context.Products.AddAsync(repackProd);
            await context.SaveChangesAsync();
            var fetchedProdWithUnits = await repo.GetProductWithUnitsAsync(TenantId, repackProd.Id);
            Assert.NotNull(fetchedProdWithUnits);

            // 8. AddRepackOrderAsync
            var repackOrder = new RepackOrder
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                BranchStockId = stock.Id,
                Code = "RP-2026-001",
                InputWeight = 5m,
                CreatedAt = DateTime.UtcNow
            };
            await repo.AddRepackOrderAsync(repackOrder);
            await context.SaveChangesAsync();

            // 9. AddPackagesAsync
            var pkg2 = new Package
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                InternalCode = "PKG002",
                BranchStockId = stock.Id,
                NetWeight = 3m,
                Status = "AVAILABLE",
                CreatedAt = DateTime.UtcNow
            };
            await repo.AddPackagesAsync(new[] { pkg2 });
            await context.SaveChangesAsync();

            // 10. IsCodeConflictAsync
            Assert.True(await repo.IsCodeConflictAsync(TenantId, "PKG001", null));
            Assert.False(await repo.IsCodeConflictAsync(TenantId, "PKG999", null));
        }
    }
}
