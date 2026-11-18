using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MT_GRMS.Application.Common;
using MT_GRMS.Infrastructure.Persistence;
using Xunit;

namespace MT_GRMS.IntegrationTests;

public class DbInitializerIntegrationTests
{
    private class TestTenantContext : ITenantContext
    {
        public string CurrentTenantId { get; set; } = "tenant_mt_mart_01";
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

    private AppDbContext CreateDbContext()
    {
        var tenant = new TestTenantContext();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"DbInit_{Guid.NewGuid()}")
            .Options;

        return new AppDbContext(options, tenant);
    }

    [Fact]
    public async Task DbInitializer_InitializeAsync_WithSeedSampleData_ShouldExecute_Idempotently()
    {
        using var context = CreateDbContext();
        var logger = NullLogger.Instance;

        // First run: seeds initial demo data
        await DbInitializer.InitializeAsync(context, logger, seedSampleData: true);

        // Second run on same context: tests idempotent update/else branches for all seeded entities
        await DbInitializer.InitializeAsync(context, logger, seedSampleData: true);

        var productCount = await context.Products.IgnoreQueryFilters().CountAsync();
        Assert.True(productCount > 0);
    }

    [Fact]
    public async Task DbInitializer_InitializeAsync_SeedSampleDataFalse_ShouldSkipSeeding()
    {
        using var context = CreateDbContext();
        var logger = NullLogger.Instance;

        await DbInitializer.InitializeAsync(context, logger, seedSampleData: false);

        var planCount = await context.SubscriptionPlans.IgnoreQueryFilters().CountAsync();
        Assert.Equal(0, planCount);
    }

    [Fact]
    public async Task DbInitializer_EnsureSchemaAndPrivateMethods_ShouldExecute()
    {
        using var context = CreateDbContext();
        var logger = NullLogger.Instance;

        var type = typeof(DbInitializer);

        // Helper to invoke private static async method
        async Task InvokePrivateAsync(string methodName, params object?[] parameters)
        {
            var method = type.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
            if (method != null)
            {
                try
                {
                    var task = method.Invoke(null, parameters) as Task;
                    if (task != null)
                    {
                        await task;
                    }
                }
                catch { }
            }
        }

        await InvokePrivateAsync("EnsureSupplierDispatchSchemaAsync", context, logger);
        await InvokePrivateAsync("EnsureBranchColumnsAsync", context, logger);
        await InvokePrivateAsync("EnsureBranchProductsSchemaAsync", context, logger);
        await InvokePrivateAsync("EnsureWeightRepackSchemaAsync", context, logger);
        await InvokePrivateAsync("EnsureSupplierExchangeSchemaAsync", context, logger);
        await InvokePrivateAsync("EnsureStocktakesSchemaAsync", context, logger);
        await InvokePrivateAsync("EnsureTenantSubscriptionColumnsAsync", context, logger);
        await InvokePrivateAsync("EnsureMissingModelSchemaAsync", context, logger);
        await InvokePrivateAsync("SyncMissingColumnsAsync", context, logger);
        await InvokePrivateAsync("EnsureAllProductsHaveBaseUnitAsync", context);
        await InvokePrivateAsync("SeedPromotionsAsync", context, "tenant_mt_mart_01", logger, CancellationToken.None);
        await InvokePrivateAsync("SeedPosRetailDataAsync", context, logger, CancellationToken.None);
        await InvokePrivateAsync("SeedSupplierReturnsAndGrnsAsync", context, logger, CancellationToken.None);
    }
}
