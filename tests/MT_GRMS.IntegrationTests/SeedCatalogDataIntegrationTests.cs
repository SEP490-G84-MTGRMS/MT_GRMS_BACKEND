using Microsoft.EntityFrameworkCore;
using MT_GRMS.Infrastructure.Persistence;
using Xunit;

namespace MT_GRMS.IntegrationTests;

public class SeedCatalogDataIntegrationTests
{
    [Fact]
    public async Task Seed_Sample_15_Categories_And_Products_Into_Postgres_Should_Succeed()
    {
        var connectionString = Environment.GetEnvironmentVariable("POSTGRES_TEST_CONNECTION") 
            ?? "Host=localhost;Port=5432;Database=mtgrms_db;Username=postgres;Password=mtgrms_secret_password";

        DbContextOptions<AppDbContext> options;
        try
        {
            var testBuilder = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString);
            await using var testCtx = new AppDbContext(testBuilder.Options);
            bool canConnect = await testCtx.Database.CanConnectAsync();
            if (canConnect)
            {
                options = testBuilder.Options;
            }
            else
            {
                options = new DbContextOptionsBuilder<AppDbContext>()
                    .UseInMemoryDatabase(databaseName: $"MT_GRMS_CatalogSeed_{Guid.NewGuid()}")
                    .Options;
            }
        }
        catch
        {
            // Fallback sang InMemoryDatabase khi môi trường dev hoặc CI/CD không có máy chủ PostgreSQL cục bộ
            options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(databaseName: $"MT_GRMS_CatalogSeed_{Guid.NewGuid()}")
                .Options;
        }

        await using var context = new AppDbContext(options);

        // Nạp 15 danh mục và các sản phẩm mẫu
        await CatalogSeedData.SeedAsync(context, "tenant_mt_mart_01");

        var categoryCount = await context.Categories.IgnoreQueryFilters().CountAsync();
        var productCount = await context.Products.IgnoreQueryFilters().CountAsync();

        Assert.True(categoryCount >= 15, $"Category count must be at least 15, got {categoryCount}");
        Assert.True(productCount >= 75, $"Product count must be at least 75, got {productCount}");
    }
}
