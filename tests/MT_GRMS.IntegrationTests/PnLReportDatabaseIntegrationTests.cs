using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Entities.Sales;
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
        public string CurrentRole { get; set; } = "ADMIN";
        public void SetRole(string? role) => CurrentRole = role?.Trim() ?? string.Empty;
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
            FinalAmount = 950_000m,
            PaymentStatus = "PAID",
            CreatedAt = now
        };

        var order2 = new Order
        {
            OrderCode = "ORD-02",
            StoreId = branchId,
            SubTotal = 500_000m,
            DiscountAmount = 0m,
            TotalAmount = 500_000m,
            PaymentStatus = "PAID",
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

    [Fact]
    public async Task PnLReport_And_S2cHkd_ShouldBe100PercentIdentical_InSamePeriod()
    {
        using var context = CreateDbContext();
        var pnlRepo = new PnLReportRepository(context);
        var taxRepo = new HouseholdTaxRepository(context);

        var branchId = Guid.NewGuid();
        var prodId = Guid.NewGuid();
        var fromUtc = new DateTime(2026, 9, 30, 17, 0, 0, DateTimeKind.Utc); // 01/10/2026 00:00 VN
        var toUtc = new DateTime(2026, 10, 31, 16, 59, 59, DateTimeKind.Utc); // 31/10/2026 23:59:59 VN
        var orderTime = new DateTime(2026, 10, 15, 10, 0, 0, DateTimeKind.Utc);

        context.Products.Add(new MT_GRMS.Domain.Entities.Catalog.Product
        {
            Id = prodId,
            ProductCode = "SP-AUDIT-01",
            Name = "Sữa tươi Vinamilk 1L",
            CostPrice = 24_000m,
            SellingPrice = 35_000m
        });

        var order = new Order
        {
            OrderCode = "ORD-AUDIT-01",
            BranchId = branchId,
            TotalAmount = 3_500_000m,
            DiscountAmount = 100_000m,
            FinalAmount = 3_400_000m,
            PaymentStatus = "PAID",
            Status = "Completed",
            CreatedAt = orderTime
        };
        order.Details.Add(new OrderDetail
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            ProductId = prodId,
            Qty = 100m,
            UnitPrice = 35_000m,
            ActualPrice = 34_000m,
            CostPriceAtSale = 25_000m // FEFO Cost = 2,500,000
        });
        context.Orders.Add(order);

        // Return entry: 350,000 VND (10% of GrossRevenue)
        context.CustomerDebtEntries.Add(new CustomerDebtEntry
        {
            EntryCode = "RET-AUDIT-01",
            BranchId = branchId,
            EntryType = CustomerDebtType.GoodsReturnOffset,
            Amount = 350_000m,
            OccurredAtUtc = orderTime.AddHours(2)
        });

        // Operating expense voucher: 500,000 VND
        context.ExpenseVouchers.Add(new ExpenseVoucher
        {
            VoucherCode = "PC-AUDIT-01",
            BranchId = branchId,
            Category = "Tiền điện nước cửa hàng",
            Amount = 500_000m,
            Status = ExpenseVoucherStatus.Paid,
            Nature = FinancialNature.OperatingExpense,
            ExpenseDate = orderTime.AddHours(3),
            CreatedAt = orderTime.AddHours(3)
        });

        // Owner draw voucher (excluded from both PnL and S2c): 10,000,000 VND
        context.ExpenseVouchers.Add(new ExpenseVoucher
        {
            VoucherCode = "PC-EQUITY-01",
            BranchId = branchId,
            Category = "Rút vốn chủ hộ (Capital/Equity)",
            Amount = 10_000_000m,
            Status = ExpenseVoucherStatus.Paid,
            Nature = FinancialNature.OwnerDraw,
            ExpenseDate = orderTime.AddHours(4),
            CreatedAt = orderTime.AddHours(4)
        });

        await context.SaveChangesAsync();

        // 1. Query FE-10 PnL Report
        var pnlRev = await pnlRepo.GetRevenueAsync(branchId, fromUtc, toUtc);
        var pnlCogs = await pnlRepo.GetCogsAsync(branchId, fromUtc, toUtc);
        var pnlExpenses = await pnlRepo.GetOperatingExpensesAsync(branchId, fromUtc, toUtc);
        var pnlNetRev = pnlRev.GrossRevenue - pnlRev.Discounts - pnlRev.SalesReturns;
        var pnlTotalOpex = pnlExpenses.Sum(x => x.Amount);

        // 2. Query FE-19 S2c-HKD
        var (taxGross, taxDisc, taxRet) = await taxRepo.GetPeriodRevenueAsync(fromUtc, toUtc.AddSeconds(1), branchId);
        var (taxCogs, _) = await taxRepo.GetPeriodCogsAsync(fromUtc, toUtc.AddSeconds(1), branchId);
        var taxOpex = await taxRepo.GetPeriodOpexAsync(fromUtc, toUtc, branchId);
        var taxNetRev = taxGross - taxDisc - taxRet;

        // Assert 100% reconciliation between FE-10 and FE-19 S2c-HKD
        Assert.Equal(taxNetRev, pnlNetRev);       // 3,500,000 - 100,000 - 350,000 = 3,050,000
        Assert.Equal(taxCogs, pnlCogs.TotalCogs); // 2,500,000 - 250,000 (return COGS) = 2,250,000
        Assert.Equal(taxOpex, pnlTotalOpex);      // 500,000 (Owner draw excluded)
    }
}
