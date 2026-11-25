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
        Assert.Equal(300_000m, ledger[0].TotalIncreased);
        Assert.Equal(100_000m, ledger[0].TotalDecreased);
        Assert.Equal(200_000m, ledger[0].CurrentBalance);
    }

    [Fact]
    public async Task CustomerDebtRepository_GetLedger_ExcludesPendingOrdersAndReversedReceipts_AndComputesFullHistoryTotals()
    {
        using var context = CreateDbContext();
        var repo = new CustomerDebtRepository(context);

        var customerId = Guid.NewGuid();
        var t0 = new DateTime(2026, 11, 20, 10, 0, 0, DateTimeKind.Utc);

        // Completed debt order (should be included)
        context.Orders.Add(new Order
        {
            CustomerId = customerId, OrderCode = "ORD-COMP", PaymentMethod = "DEBT",
            PaymentStatus = "DEBT", Status = "Completed", FinalAmount = 500_000m,
            OrderDate = t0
        });
        // PendingPayment order (must be excluded)
        context.Orders.Add(new Order
        {
            CustomerId = customerId, OrderCode = "ORD-PEND", PaymentMethod = "DEBT",
            PaymentStatus = "PENDING", Status = "PendingPayment", FinalAmount = 900_000m,
            OrderDate = t0.AddMinutes(1)
        });
        // Same-second repayment (legacy SourceType = "DEBT") -> must sort AFTER DebtIncurred
        var legacyReceiptId = Guid.NewGuid();
        context.CashbookEntries.Add(new CashbookEntry
        {
            Id = legacyReceiptId, SourceType = "DEBT", SourceId = customerId,
            EntryCode = "PT-LEGACY", Amount = 150_000m, AccountId = Guid.NewGuid(),
            EntryType = CashbookEntryType.Receipt, OccurredAtUtc = t0
        });
        // Reversed receipt via COMPENSATING SourceId (must be excluded)
        var voidedByCompId = Guid.NewGuid();
        context.CashbookEntries.Add(new CashbookEntry
        {
            Id = voidedByCompId, SourceType = "DEBT_REPAYMENT", SourceId = customerId,
            EntryCode = "PT-VOID-1", Amount = 200_000m, AccountId = Guid.NewGuid(),
            EntryType = CashbookEntryType.Receipt, OccurredAtUtc = t0.AddMinutes(5)
        });
        context.CashbookEntries.Add(new CashbookEntry
        {
            Id = Guid.NewGuid(), SourceType = "COMPENSATING", SourceId = voidedByCompId,
            EntryCode = "PC-COMP-1", Amount = 200_000m, AccountId = Guid.NewGuid(),
            EntryType = CashbookEntryType.Payment, OccurredAtUtc = t0.AddMinutes(6)
        });
        // Reversed receipt via REVERSAL ReversedEntryId (must be excluded)
        var voidedByRevId = Guid.NewGuid();
        context.CashbookEntries.Add(new CashbookEntry
        {
            Id = voidedByRevId, SourceType = "DEBT_REPAYMENT", SourceId = customerId,
            EntryCode = "PT-VOID-2", Amount = 100_000m, AccountId = Guid.NewGuid(),
            EntryType = CashbookEntryType.Receipt, OccurredAtUtc = t0.AddMinutes(7)
        });
        context.CashbookEntries.Add(new CashbookEntry
        {
            Id = Guid.NewGuid(), SourceType = "REVERSAL", ReversedEntryId = voidedByRevId,
            EntryCode = "PC-REV-2", Amount = 100_000m, AccountId = Guid.NewGuid(),
            EntryType = CashbookEntryType.Payment, OccurredAtUtc = t0.AddMinutes(8)
        });
        // Active repayment
        context.CashbookEntries.Add(new CashbookEntry
        {
            Id = Guid.NewGuid(), SourceType = "DEBT_REPAYMENT", SourceId = customerId,
            EntryCode = "PT-ACTIVE", Amount = 50_000m, AccountId = Guid.NewGuid(),
            EntryType = CashbookEntryType.Receipt, OccurredAtUtc = t0.AddMinutes(10)
        });
        await context.SaveChangesAsync();

        // Request pageSize = 1 to verify full-history totals are NOT truncated by pagination
        var page1 = await repo.GetLedgerAsync(customerId, 1, 1);
        var latest = Assert.Single(page1);
        Assert.Equal("PT-ACTIVE", latest.EntryCode);
        Assert.Equal(350_000m, latest.BalanceBefore);
        Assert.Equal(300_000m, latest.BalanceAfter);
        Assert.Equal(500_000m, latest.TotalIncreased);
        Assert.Equal(200_000m, latest.TotalDecreased);
        Assert.Equal(300_000m, latest.CurrentBalance);
        Assert.Equal(3, latest.TotalCount);

        // Verify full chronological order & same-second ordering (DebtIncurred before DebtRepayment)
        var all = await repo.GetLedgerAsync(customerId, 1, 50);
        Assert.Equal(3, all.Count);
        Assert.Equal("ORD-COMP", all[2].EntryCode);
        Assert.Equal(0m, all[2].BalanceBefore);
        Assert.Equal(500_000m, all[2].BalanceAfter);
        Assert.Equal("PT-LEGACY", all[1].EntryCode);
        Assert.Equal(500_000m, all[1].BalanceBefore);
        Assert.Equal(350_000m, all[1].BalanceAfter);
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
