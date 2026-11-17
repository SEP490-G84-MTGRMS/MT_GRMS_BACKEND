using MT_GRMS.Domain.Entities.Sales;
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
        public string CurrentRole { get; set; } = "STORE_OWNER";
        public void SetRole(string? role) => CurrentRole = role?.Trim() ?? string.Empty;
    }

    private AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"MT_GRMS_DebtIntTestDb_{Guid.NewGuid()}")
            .Options;

        return new AppDbContext(options, new FakeTenantContext());
    }

    [Fact]
    public async Task CustomerDebtRepository_GetLedger_ShouldReadOrdersAndReceipts()
    {
        using var context = CreateDbContext();
        var repo = new CustomerDebtRepository(context);

        var customerId = Guid.NewGuid();

        context.Orders.Add(new Order
        {
            CustomerId = customerId, OrderCode = "ORD-01", PaymentMethod = "DEBT",
            PaymentStatus = "DEBT", Status = "Completed", FinalAmount = 300_000m,
            OrderDate = DateTime.UtcNow.AddDays(-1)
        });
        context.CashbookEntries.Add(new CashbookEntry
        {
            Id = Guid.NewGuid(), SourceType = "DEBT_REPAYMENT", SourceId = customerId,
            EntryCode = "PT-01", Amount = 100_000m, AccountId = Guid.NewGuid(),
            EntryType = CashbookEntryType.Receipt, OccurredAtUtc = DateTime.UtcNow
        });
        await context.SaveChangesAsync();
        var ledger = await repo.GetLedgerAsync(customerId, 1, 20);
        Assert.Equal(2, ledger.Count);
        Assert.Equal("DebtRepayment", ledger[0].EntryType);
        Assert.Equal("DebtIncurred", ledger[1].EntryType);
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
            ctx1.Customers.Add(new Customer
            {
                Id = customerId,
                CustomerCode = "KH-TEST-TENANT",
                Name = "Khách Hàng Test Đa Tenant",
                Phone = "0909999999",
                TotalDebt = 1_000_000m
            });
            await ctx1.SaveChangesAsync();
        }

        // Tenant 2 truy vấn không được thấy profile của Tenant 1
        using (var ctx2 = new AppDbContext(options, tenant2))
        {
            var customerForTenant2 = await ctx2.Customers.FirstOrDefaultAsync(p => p.Id == customerId);
            Assert.Null(customerForTenant2);
        }
    }
}
