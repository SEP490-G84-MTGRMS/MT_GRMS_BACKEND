using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Infrastructure.Persistence;
using Xunit;

namespace MT_GRMS.IntegrationTests;

public class UnitOfWorkIntegrationTests
{
    private class TestTenantContext : ITenantContext
    {
        public string CurrentTenantId { get; set; } = "tenant-uow-01";
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
            .UseInMemoryDatabase(databaseName: $"UowDb_{Guid.NewGuid()}")
            .Options;

        return new AppDbContext(options, tenant);
    }

    [Fact]
    public async Task UnitOfWork_AllMethods_ShouldWork()
    {
        using var context = CreateDbContext();
        var uow = new UnitOfWork(context);

        // 1. HasActiveTransaction
        Assert.False(uow.HasActiveTransaction);

        // 2. Transaction methods against non-relational (no-op safety)
        await uow.BeginTransactionAsync();
        await uow.CommitTransactionAsync();
        await uow.RollbackTransactionAsync();

        // 3. SaveChangesAsync
        var cat = new Category
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-uow-01",
            CategoryCode = "UOW-CAT",
            Name = "Category UoW",
            CreatedAt = DateTime.UtcNow
        };
        await context.Categories.AddAsync(cat);
        var affected = await uow.SaveChangesAsync();
        Assert.Equal(1, affected);

        // 4. ExecuteInTransactionAsync
        bool executed = false;
        await uow.ExecuteInTransactionAsync(async ct =>
        {
            executed = true;
            await Task.Yield();
        });
        Assert.True(executed);
    }
}
