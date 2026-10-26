using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Infrastructure.Persistence;
using MT_GRMS.Infrastructure.Repositories;
using Xunit;

namespace MT_GRMS.IntegrationTests;

public class CustomerDebtDatabaseIntegrationTests
{
    private class FakeTenantContext : ITenantContext
    {
        public string CurrentTenantId { get; set; } = "tenant-test-01";
        public Guid CurrentUserId { get; set; } = Guid.Parse("11111111-1111-1111-1111-111111111111");
        public Guid? CurrentBranchId { get; set; } = null;
        public void SetContext(string tenantId, Guid userId, Guid? branchId)
        {
            CurrentTenantId = tenantId;
            CurrentUserId = userId;
            CurrentBranchId = branchId;
        }
    }

    private AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"MT_GRMS_DebtIntTestDb_{Guid.NewGuid()}")
            .Options;

        return new AppDbContext(options, new FakeTenantContext());
    }

    [Fact]
    public async Task CustomerDebtRepository_GetOpenInvoices_ShouldReturnOrderedByCreatedAt()
    {
        using var context = CreateDbContext();
        var repo = new CustomerDebtRepository(context);

        var customerId = Guid.NewGuid();

        var invNew = new CustomerDebtInvoice
        {
            CustomerId = customerId,
            OrderCode = "ORD-NEW",
            TotalDebtAmount = 300_000m,
            RemainingDebtAmount = 300_000m,
            Status = DebtInvoiceStatus.Unpaid,
            DueDate = DateTime.UtcNow.AddDays(15),
            CreatedAt = DateTime.UtcNow.AddDays(-1)
        };

        var invOld = new CustomerDebtInvoice
        {
            CustomerId = customerId,
            OrderCode = "ORD-OLD",
            TotalDebtAmount = 500_000m,
            RemainingDebtAmount = 500_000m,
            Status = DebtInvoiceStatus.Unpaid,
            DueDate = DateTime.UtcNow.AddDays(5),
            CreatedAt = DateTime.UtcNow.AddDays(-10)
        };

        var invPaid = new CustomerDebtInvoice
        {
            CustomerId = customerId,
            OrderCode = "ORD-PAID",
            TotalDebtAmount = 200_000m,
            RemainingDebtAmount = 0,
            Status = DebtInvoiceStatus.FullyPaid,
            DueDate = DateTime.UtcNow.AddDays(5),
            CreatedAt = DateTime.UtcNow.AddDays(-15)
        };

        context.CustomerDebtInvoices.AddRange(invNew, invOld, invPaid);
        await context.SaveChangesAsync();

        var openInvoices = await repo.GetOpenInvoicesAsync(customerId);

        Assert.Equal(2, openInvoices.Count);
        Assert.Equal("ORD-OLD", openInvoices.First().OrderCode); // Cũ nhất lên đầu theo FIFO
        Assert.Equal("ORD-NEW", openInvoices.Last().OrderCode);
    }

    [Fact]
    public async Task CustomerDebtRepository_HasOverdueInvoices_ShouldDetectPastDueInvoices()
    {
        using var context = CreateDbContext();
        var repo = new CustomerDebtRepository(context);

        var customerId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var invOverdue = new CustomerDebtInvoice
        {
            CustomerId = customerId,
            OrderCode = "ORD-OVERDUE",
            TotalDebtAmount = 400_000m,
            RemainingDebtAmount = 400_000m,
            Status = DebtInvoiceStatus.Unpaid,
            DueDate = now.AddDays(-2) // Quá hạn 2 ngày
        };

        context.CustomerDebtInvoices.Add(invOverdue);
        await context.SaveChangesAsync();

        var hasOverdue = await repo.HasOverdueInvoicesAsync(customerId, now);
        Assert.True(hasOverdue);
    }

    [Fact]
    public async Task AppDbContext_MultiTenantFilter_ShouldIsolateCustomerDebtData()
    {
        var dbName = $"MT_GRMS_TenantIsolateDb_{Guid.NewGuid()}";
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(dbName).Options;

        var tenant1 = new FakeTenantContext { CurrentTenantId = "tenant-A" };
        var tenant2 = new FakeTenantContext { CurrentTenantId = "tenant-B" };

        var customerId = Guid.NewGuid();

        using (var ctx1 = new AppDbContext(options, tenant1))
        {
            ctx1.CustomerDebtProfiles.Add(new CustomerDebtProfile
            {
                CustomerId = customerId,
                CreditLimit = 5_000_000m,
                CurrentDebtBalance = 1_000_000m
            });
            await ctx1.SaveChangesAsync();
        }

        // Tenant 2 truy vấn không được thấy profile của Tenant 1
        using (var ctx2 = new AppDbContext(options, tenant2))
        {
            var profileForTenant2 = await ctx2.CustomerDebtProfiles.FirstOrDefaultAsync(p => p.CustomerId == customerId);
            Assert.Null(profileForTenant2);
        }
    }
}
