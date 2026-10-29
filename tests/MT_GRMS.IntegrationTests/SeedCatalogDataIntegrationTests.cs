using Microsoft.EntityFrameworkCore;
using MT_GRMS.Infrastructure.Persistence;
using Xunit;

namespace MT_GRMS.IntegrationTests;

public class SeedCatalogDataIntegrationTests
{
    [Fact]
    public async Task Seed_Sample_15_Categories_And_Products_Into_Postgres_Should_Succeed()
    {
        var connectionString = "Host=localhost;Port=5432;Database=mtgrms_db;Username=postgres;Password=mtgrms_secret_password";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        await using var context = new AppDbContext(options);

        // Nạp 15 danh mục và các sản phẩm mẫu
        await CatalogSeedData.SeedAsync(context, "tenant_mt_mart_01");

        var categoryCount = await context.Categories.IgnoreQueryFilters().CountAsync();
        var productCount = await context.Products.IgnoreQueryFilters().CountAsync();

        Assert.True(categoryCount >= 15, $"Category count must be at least 15, got {categoryCount}");
        Assert.True(productCount >= 75, $"Product count must be at least 75, got {productCount}");
    }
}
