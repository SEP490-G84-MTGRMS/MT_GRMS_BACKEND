using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Entities;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Infrastructure.Persistence;
using MT_GRMS.Infrastructure.Repositories;
using Xunit;

namespace MT_GRMS.IntegrationTests;

public class PnLReportDatabaseIntegrationTests
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
            .UseInMemoryDatabase(databaseName: $"MT_GRMS_PnLIntTestDb_{Guid.NewGuid()}")
            .Options;

        return new AppDbContext(options, new FakeTenantContext());
    }

    [Fact]
    public async Task PnLReportRepository_GetRevenueAsync_ShouldAggregateOrdersAndReturnsCorrectly()
    {
        using var context = CreateDbContext();
        var repo = new PnLReportRepository(context);

        var branchId = Guid.NewGuid();
        var now = new DateTime(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);

        var order1 = new Order
        {
            OrderCode = "ORD-01",
            StoreId = branchId,
            SubTotal = 1_000_000m,
            DiscountAmount = 50_000m,
            TotalAmount = 950_000m,
            PaymentStatus = PaymentStatus.Paid,
            CreatedAt = now
        };

        var order2 = new Order
        {
            OrderCode = "ORD-02",
            StoreId = branchId,
            SubTotal = 500_000m,
            DiscountAmount = 0m,
            TotalAmount = 500_000m,
            PaymentStatus = PaymentStatus.Paid,
            CreatedAt = now.AddHours(2)
        };

        // Bút toán trả hàng
        var returnEntry = new CustomerDebtEntry
        {
            EntryCode = "RET-01",
            BranchId = branchId,
            EntryType = CustomerDebtType.GoodsReturnOffset,
            Amount = 100_000m,
            OccurredAtUtc = now.AddHours(3)
        };

        context.Orders.AddRange(order1, order2);
        context.CustomerDebtEntries.Add(returnEntry);
        await context.SaveChangesAsync();

        var from = now.Date;
        var to = from.AddDays(1);
        var result = await repo.GetRevenueAsync(branchId, from, to);

        Assert.Equal(1_500_000m, result.GrossRevenue);
        Assert.Equal(50_000m, result.Discounts);
        Assert.Equal(100_000m, result.SalesReturns);
    }

    [Fact]
    public async Task PnLReportRepository_GetOperatingExpensesAsync_ShouldAggregateByCategory()
    {
        using var context = CreateDbContext();
        var repo = new PnLReportRepository(context);

        var branchId = Guid.NewGuid();
        var now = new DateTime(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);

        var exp1 = new ExpenseVoucher
        {
            BranchId = branchId,
            Category = "Tiện ích",
            Amount = 300_000m,
            Status = ExpenseVoucherStatus.Approved,
            CreatedAt = now
        };

        var exp2 = new ExpenseVoucher
        {
            BranchId = branchId,
            Category = "Tiện ích",
            Amount = 200_000m,
            Status = ExpenseVoucherStatus.Paid,
            CreatedAt = now.AddHours(1)
        };

        var exp3 = new ExpenseVoucher
        {
            BranchId = branchId,
            Category = "Văn phòng phẩm",
            Amount = 150_000m,
            Status = ExpenseVoucherStatus.Paid,
            CreatedAt = now.AddHours(2)
        };

        // Bị hủy -> không được tính
        var expCancelled = new ExpenseVoucher
        {
            BranchId = branchId,
            Category = "Tiện ích",
            Amount = 1_000_000m,
            Status = ExpenseVoucherStatus.Cancelled,
            CreatedAt = now.AddHours(3)
        };

        context.ExpenseVouchers.AddRange(exp1, exp2, exp3, expCancelled);
        await context.SaveChangesAsync();

        var expenses = await repo.GetOperatingExpensesAsync(branchId, now.Date, now.Date.AddDays(1));

        Assert.Equal(2, expenses.Count);
        var utilities = expenses.First(e => e.Category == "Tiện ích");
        Assert.Equal(500_000m, utilities.Amount);
    }

    [Fact]
    public async Task PnLReportRepository_GetBadDebtExpenseAsync_ShouldAggregateOnlyBadDebtWriteOffs()
    {
        using var context = CreateDbContext();
        var repo = new PnLReportRepository(context);

        var branchId = Guid.NewGuid();
        var now = new DateTime(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);

        var badDebtEntry = new CustomerDebtEntry
        {
            EntryCode = "WO-01",
            BranchId = branchId,
            EntryType = CustomerDebtType.BadDebtWriteOff,
            Amount = 250_000m,
            OccurredAtUtc = now
        };

        var normalRepayment = new CustomerDebtEntry
        {
            EntryCode = "REP-01",
            BranchId = branchId,
            EntryType = CustomerDebtType.DebtRepayment,
            Amount = 500_000m,
            OccurredAtUtc = now.AddHours(1)
        };

        context.CustomerDebtEntries.AddRange(badDebtEntry, normalRepayment);
        await context.SaveChangesAsync();

        var badDebt = await repo.GetBadDebtExpenseAsync(branchId, now.Date, now.Date.AddDays(1));
        Assert.Equal(250_000m, badDebt); // Chỉ tính 250k xóa nợ xấu
    }
}
