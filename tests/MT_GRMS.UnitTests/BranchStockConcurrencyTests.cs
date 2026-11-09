using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Entities.Inventory;
using MT_GRMS.Infrastructure.Persistence;
using Xunit;

namespace MT_GRMS.UnitTests;

public class BranchStockConcurrencyTests
{
    private class FakeTenantContext : ITenantContext
    {
        public string CurrentTenantId => "tenant-test";
        public Guid CurrentUserId => Guid.NewGuid();
        public Guid? CurrentBranchId => Guid.NewGuid();
        public string CurrentRole => "ADMIN";
        public void SetContext(string tenantId, Guid userId, Guid? branchId) { }
        public void SetRole(string? role) { }
    }

    [Fact]
    public async Task BranchStock_WhenUpdated_ShouldAutomaticallyIncrementRowVersion()
    {
        // Arrange
        var dbName = $"MT_GRMS_StockTestDb_{Guid.NewGuid()}";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        var tenantContext = new FakeTenantContext();

        using (var context = new AppDbContext(options, tenantContext))
        {
            var stock = new BranchStock
            {
                Id = Guid.NewGuid(),
                BranchId = Guid.NewGuid(),
                ProductId = Guid.NewGuid(),
                QuantityOnHand = 100m,
                RowVersion = 1
            };
            context.BranchStocks.Add(stock);
            await context.SaveChangesAsync();

            Assert.Equal(1, stock.RowVersion);
        }

        // Act & Assert
        using (var context = new AppDbContext(options, tenantContext))
        {
            var existingStock = await context.BranchStocks.FirstAsync();
            existingStock.QuantityOnHand = 95m; // Modified
            await context.SaveChangesAsync();

            Assert.Equal(2, existingStock.RowVersion);
        }
    }
}
