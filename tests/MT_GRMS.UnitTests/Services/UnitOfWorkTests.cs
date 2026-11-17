using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Common;
using MT_GRMS.Infrastructure.Persistence;
using Xunit;

namespace MT_GRMS.UnitTests.Services;

public class UnitOfWorkTests
{
    private class FakeTenantContext : ITenantContext
    {
        public string CurrentTenantId { get; set; } = "tenant-01";
        public Guid CurrentUserId { get; set; } = Guid.NewGuid();
        public Guid? CurrentBranchId { get; set; } = null;
        public string CurrentRole { get; set; } = "ADMIN";
        public void SetContext(string tenantId, Guid userId, Guid? branchId) { }
        public void SetRole(string? role) { }
    }

    [Fact]
    public async Task UnitOfWork_NonRelationalDatabase_ShouldExecuteSafely()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"UowDb_{Guid.NewGuid()}")
            .Options;
        using var db = new AppDbContext(options, new FakeTenantContext());
        var uow = new UnitOfWork(db);

        // SaveChangesAsync
        var saved = await uow.SaveChangesAsync();
        Assert.Equal(0, saved);

        // In non-relational, HasActiveTransaction is always false
        Assert.False(uow.HasActiveTransaction);

        // Transaction methods are safe no-ops in non-relational
        await uow.BeginTransactionAsync();
        await uow.CommitTransactionAsync();
        await uow.RollbackTransactionAsync();

        // ExecuteInTransactionAsync simply runs operation
        var executed = false;
        await uow.ExecuteInTransactionAsync(ct =>
        {
            executed = true;
            return Task.CompletedTask;
        });
        Assert.True(executed);
    }
}
