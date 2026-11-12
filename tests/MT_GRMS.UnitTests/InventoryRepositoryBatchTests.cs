using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Entities.Inventory;
using MT_GRMS.Infrastructure.Persistence;
using MT_GRMS.Infrastructure.Repositories;

namespace MT_GRMS.UnitTests;

public class InventoryRepositoryBatchTests
{
    [Fact]
    public async Task GetProductBatchesAsync_ReturnsEveryBranchLotWithItsOwnCurrentStock()
    {
        const string tenantId = "tenant-inventory-lots";
        var branchId = Guid.NewGuid();
        var otherBranchId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"inventory-lots-{Guid.NewGuid()}")
            .Options;
        var tenantContext = new FakeTenantContext(tenantId);

        await using var context = new AppDbContext(options, tenantContext);
        context.BranchStocks.AddRange(
            CreateLot(tenantId, branchId, productId, "LOT-EARLY", 20m, 3m, DateTime.UtcNow.AddDays(10)),
            CreateLot(tenantId, branchId, productId, "LOT-LATE", 40m, 5m, DateTime.UtcNow.AddDays(30)),
            CreateLot(tenantId, otherBranchId, productId, "LOT-OTHER-BRANCH", 99m, 0m, DateTime.UtcNow.AddDays(5)));
        await context.SaveChangesAsync();

        var repository = new InventoryRepository(context);
        var result = await repository.GetProductBatchesAsync(tenantId, branchId, productId);

        Assert.Collection(result,
            first =>
            {
                Assert.Equal("LOT-EARLY", first.BatchCode);
                Assert.Equal(20m, first.QuantityOnHand);
                Assert.Equal(3m, first.ReservedQty);
                Assert.Equal(17m, first.AvailableQty);
            },
            second =>
            {
                Assert.Equal("LOT-LATE", second.BatchCode);
                Assert.Equal(40m, second.QuantityOnHand);
                Assert.Equal(5m, second.ReservedQty);
                Assert.Equal(35m, second.AvailableQty);
            });
        Assert.DoesNotContain(result, lot => lot.BatchCode == "LOT-OTHER-BRANCH");
    }

    [Fact]
    public async Task GetPagedStockSummaryAsync_AggregatesAllBranchStockLotsAndReturnsSortedFefoBatches()
    {
        const string tenantId = "tenant-inventory-summary";
        var branchId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"inventory-summary-{Guid.NewGuid()}")
            .Options;
        var tenantContext = new FakeTenantContext(tenantId);

        await using var context = new AppDbContext(options, tenantContext);
        context.Categories.Add(new MT_GRMS.Domain.Entities.Catalog.Category
        {
            Id = categoryId,
            TenantId = tenantId,
            Name = "Sữa & Chế phẩm"
        });
        context.Branches.Add(new MT_GRMS.Domain.Entities.SaaS.Branch
        {
            Id = branchId,
            TenantId = tenantId,
            BranchCode = "BR-01",
            Name = "Chi nhánh Cầu Giấy"
        });
        context.Products.Add(new MT_GRMS.Domain.Entities.Catalog.Product
        {
            Id = productId,
            TenantId = tenantId,
            CategoryId = categoryId,
            ProductCode = "AN-SUA-01",
            Barcode = "8936099880059",
            Name = "Sữa tươi Mộc Châu",
            BaseUnit = "Chai",
            SellingPrice = 39000m,
            CostPrice = 28000m,
            MinStock = 20m
        });
        context.BranchStocks.AddRange(
            CreateLot(tenantId, branchId, productId, "LOT-LATE", 50m, 0m, DateTime.UtcNow.AddDays(25)),
            CreateLot(tenantId, branchId, productId, "LOT-EARLY", 30m, 4m, DateTime.UtcNow.AddDays(5)));
        await context.SaveChangesAsync();

        var repository = new InventoryRepository(context);
        var result = await repository.GetPagedStockSummaryAsync(
            tenantId,
            new MT_GRMS.Application.Inventory.Dtos.InventoryFilterQuery
            {
                Keyword = "AN-SUA-01",
                PageIndex = 1,
                PageSize = 20
            });

        var item = Assert.Single(result.Items);
        Assert.Equal(80m, item.PhysicalQty);
        Assert.Equal(76m, item.AvailableQty);
        Assert.Collection(item.Batches,
            first =>
            {
                Assert.Equal("LOT-EARLY", first.BatchCode);
                Assert.Equal(30m, first.QuantityOnHand);
                Assert.Equal(4m, first.ReservedQty);
                Assert.Equal(26m, first.AvailableQty);
            },
            second =>
            {
                Assert.Equal("LOT-LATE", second.BatchCode);
                Assert.Equal(50m, second.QuantityOnHand);
                Assert.Equal(0m, second.ReservedQty);
                Assert.Equal(50m, second.AvailableQty);
            });
    }

    private static BranchStock CreateLot(
        string tenantId,
        Guid branchId,
        Guid productId,
        string batchCode,
        decimal quantityOnHand,
        decimal reservedQty,
        DateTime expiryDate) => new()
        {
            TenantId = tenantId,
            BranchId = branchId,
            ProductId = productId,
            BatchCode = batchCode,
            ManufactureDate = expiryDate.AddMonths(-1),
            ExpiryDate = expiryDate,
            QuantityOnHand = quantityOnHand,
            QuantityReserved = reservedQty,
            Status = "Active"
        };

    private sealed class FakeTenantContext(string tenantId) : ITenantContext
    {
        public string CurrentTenantId { get; set; } = tenantId;
        public Guid CurrentUserId { get; set; } = Guid.NewGuid();
        public Guid? CurrentBranchId { get; set; }
        public string CurrentRole { get; set; } = "STORE_OWNER";
        public void SetContext(string newTenantId, Guid userId, Guid? branchId)
        {
            CurrentTenantId = newTenantId;
            CurrentUserId = userId;
            CurrentBranchId = branchId;
        }
        public void SetRole(string? role) => CurrentRole = role ?? string.Empty;
    }
}
