using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Infrastructure.Persistence;
using MT_GRMS.Infrastructure.Repositories;
using Xunit;

namespace MT_GRMS.IntegrationTests;

public class CashbookDatabaseIntegrationTests
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
        public string CurrentRole { get; set; } = "ADMIN";
        public void SetRole(string? role) => CurrentRole = role?.Trim() ?? string.Empty;
    }

    private AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"MT_GRMS_FinanceIntTestDb_{Guid.NewGuid()}")
            .Options;

        return new AppDbContext(options, new FakeTenantContext());
    }

    [Fact]
    public async Task CashbookRepository_AddAndPersist_ShouldSaveWithAuditHashAndBalanceAfter()
    {
        using var context = CreateDbContext();
        var repo = new CashbookRepository(context);

        var accountId = Guid.NewGuid();
        var branchId = Guid.NewGuid();

        var entry1 = new CashbookEntry
        {
            EntryCode = "CB-001",
            AccountId = accountId,
            AccountType = FinanceAccountType.Till,
            BranchId = branchId,
            EntryType = CashbookEntryType.Receipt,
            Amount = 500_000m,
            BalanceAfter = 500_000m,
            SourceType = "OPENING_FLOAT",
            AuditHash = "HASH-001",
            Description = "Tiền lẻ đầu ca",
            OccurredAtUtc = DateTime.UtcNow
        };

        var entry2 = new CashbookEntry
        {
            EntryCode = "CB-002",
            AccountId = accountId,
            AccountType = FinanceAccountType.Till,
            BranchId = branchId,
            EntryType = CashbookEntryType.Receipt,
            Amount = 200_000m,
            BalanceAfter = 700_000m,
            SourceType = "POS",
            AuditHash = "HASH-002",
            Description = "Bán hàng tiền mặt",
            OccurredAtUtc = DateTime.UtcNow.AddMinutes(5)
        };

        await repo.AddAsync(entry1);
        await repo.AddAsync(entry2);
        await context.SaveChangesAsync();

        var lastHash = await repo.GetLastAuditHashAsync(accountId);
        Assert.Equal("HASH-002", lastHash);

        var entries = await repo.FindAsync(e => e.AccountId == accountId);
        Assert.Equal(2, entries.Count);
        Assert.Equal(700_000m, entries.Last().BalanceAfter);
    }

    [Fact]
    public async Task ExpenseRepository_Queries_ShouldAggregateCorrectly()
    {
        using var context = CreateDbContext();
        var repo = new ExpenseRepository(context);

        var branchId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var v1 = new ExpenseVoucher
        {
            BranchId = branchId,
            BeneficiaryName = "Nhà xe Tuấn",
            Category = "Cước vận chuyển",
            Amount = 150_000m,
            Status = ExpenseVoucherStatus.Approved,
            CreatedAt = now.Date.AddHours(2)
        };

        var v2 = new ExpenseVoucher
        {
            BranchId = branchId,
            BeneficiaryName = "Nhà xe Tuấn",
            Category = "Cước vận chuyển",
            Amount = 250_000m,
            Status = ExpenseVoucherStatus.Paid,
            CreatedAt = now.Date.AddHours(4)
        };

        var vCancelled = new ExpenseVoucher
        {
            BranchId = branchId,
            BeneficiaryName = "Nhà xe Tuấn",
            Category = "Cước vận chuyển",
            Amount = 500_000m,
            Status = ExpenseVoucherStatus.Cancelled,
            CreatedAt = now.Date.AddHours(5)
        };

        await repo.AddAsync(v1);
        await repo.AddAsync(v2);
        await repo.AddAsync(vCancelled);
        await context.SaveChangesAsync();

        var dailySum = await repo.GetDailyExpenseSumAsync(branchId, now);
        Assert.Equal(400_000m, dailySum);

        var vendorSum = await repo.GetSameVendorDailySumAsync(branchId, "Nhà xe Tuấn", "Cước vận chuyển", now);
        Assert.Equal(400_000m, vendorSum);
    }

    [Fact]
    public async Task ShiftZReport_Queries_ShouldFilterActiveAndClosedShifts()
    {
        using var context = CreateDbContext();
        var repo = new CashbookRepository(context);

        var tillId = Guid.NewGuid();

        var shift1 = new ShiftZReport
        {
            TillId = tillId,
            OpeningFloat = 500_000m,
            Status = ShiftState.Closed,
            RetainedFloatAmount = 300_000m,
            ClosedAtUtc = DateTime.UtcNow.AddHours(-4)
        };

        var shift2 = new ShiftZReport
        {
            TillId = tillId,
            OpeningFloat = 300_000m,
            Status = ShiftState.Open,
            CreatedAt = DateTime.UtcNow.AddMinutes(-30)
        };

        context.ShiftZReports.Add(shift1);
        context.ShiftZReports.Add(shift2);
        await context.SaveChangesAsync();

        var active = await repo.GetActiveShiftByTillIdAsync(tillId);
        Assert.NotNull(active);
        Assert.Equal(shift2.Id, active.Id);

        var lastClosed = await repo.GetLastClosedShiftByTillIdAsync(tillId);
        Assert.NotNull(lastClosed);
        Assert.Equal(shift1.Id, lastClosed.Id);
        Assert.Equal(300_000m, lastClosed.RetainedFloatAmount);
    }
}
