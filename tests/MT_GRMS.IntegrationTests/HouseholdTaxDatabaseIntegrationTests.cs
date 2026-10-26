using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Entities;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Infrastructure.Persistence;
using MT_GRMS.Infrastructure.Repositories;
using Xunit;

namespace MT_GRMS.IntegrationTests;

public class HouseholdTaxDatabaseIntegrationTests
{
    private class FakeTenantContext : ITenantContext
    {
        public string CurrentTenantId { get; set; } = "tenant-tax-01";
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
            .UseInMemoryDatabase(databaseName: $"MT_GRMS_TaxIntTestDb_{Guid.NewGuid()}")
            .Options;

        return new AppDbContext(options, new FakeTenantContext());
    }

    [Fact]
    public async Task HouseholdTaxRepository_GetPeriodRevenueAsync_ShouldAggregateGrossDiscountsAndReturns()
    {
        // Arrange
        using var context = CreateDbContext();
        var repo = new HouseholdTaxRepository(context);

        var branchId = Guid.NewGuid();
        var fromDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var toDate = new DateTime(2026, 3, 31, 23, 59, 59, DateTimeKind.Utc);

        var order1 = new Order
        {
            OrderCode = "ORD-01",
            StoreId = branchId,
            SubTotal = 10_000_000m,
            DiscountAmount = 500_000m,
            TotalAmount = 9_500_000m,
            PaymentStatus = PaymentStatus.Paid,
            CreatedAt = new DateTime(2026, 2, 10, 10, 0, 0, DateTimeKind.Utc)
        };

        var order2 = new Order
        {
            OrderCode = "ORD-02",
            StoreId = branchId,
            SubTotal = 5_000_000m,
            DiscountAmount = 0m,
            TotalAmount = 5_000_000m,
            PaymentStatus = PaymentStatus.Paid,
            CreatedAt = new DateTime(2026, 2, 15, 14, 0, 0, DateTimeKind.Utc)
        };

        var returnEntry = new CustomerDebtEntry
        {
            BranchId = branchId,
            EntryType = CustomerDebtType.GoodsReturnOffset,
            Amount = 1_000_000m,
            OccurredAtUtc = new DateTime(2026, 2, 20, 9, 0, 0, DateTimeKind.Utc)
        };

        context.Orders.AddRange(order1, order2);
        context.CustomerDebtEntries.Add(returnEntry);
        await context.SaveChangesAsync();

        // Act
        var (gross, discount, returns) = await repo.GetPeriodRevenueAsync(fromDate, toDate, branchId);

        // Assert
        Assert.Equal(15_000_000m, gross);
        Assert.Equal(500_000m, discount);
        Assert.Equal(1_000_000m, returns);
    }

    [Fact]
    public async Task HouseholdTaxRepository_GetBookEntriesAsync_ShouldExcludeEquityAndClassifyBooksCorrectly()
    {
        // Arrange
        using var context = CreateDbContext();
        var repo = new HouseholdTaxRepository(context);

        var branchId = Guid.NewGuid();
        var fromDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var toDate = new DateTime(2026, 3, 31, 23, 59, 59, DateTimeKind.Utc);

        // S2b: Doanh thu bán hàng
        context.Orders.Add(new Order
        {
            OrderCode = "ORD-S2B",
            StoreId = branchId,
            SubTotal = 2_000_000m,
            TotalAmount = 2_000_000m,
            PaymentStatus = PaymentStatus.Paid,
            CreatedAt = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc)
        });

        // S2c: Tiền lương
        context.ExpenseVouchers.Add(new ExpenseVoucher
        {
            VoucherCode = "PC-S2C",
            BranchId = branchId,
            Category = "Lương nhân viên tháng 1",
            Amount = 10_000_000m,
            Status = ExpenseVoucherStatus.Paid,
            ExpenseDate = new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc)
        });

        // S2d: Vật tư / bao bì
        context.ExpenseVouchers.Add(new ExpenseVoucher
        {
            VoucherCode = "PC-S2D",
            BranchId = branchId,
            Category = "Bao bì túi nylon siêu thị",
            Amount = 1_500_000m,
            Status = ExpenseVoucherStatus.Paid,
            ExpenseDate = new DateTime(2026, 2, 5, 0, 0, 0, DateTimeKind.Utc)
        });

        // S2e: Chi phí dịch vụ mua ngoài
        context.ExpenseVouchers.Add(new ExpenseVoucher
        {
            VoucherCode = "PC-S2E",
            BranchId = branchId,
            Category = "Tiền điện nước chi nhánh",
            Amount = 3_000_000m,
            Status = ExpenseVoucherStatus.Paid,
            ExpenseDate = new DateTime(2026, 2, 10, 0, 0, 0, DateTimeKind.Utc)
        });

        // LOẠI TRỪ 100%: Vốn chủ sở hữu rút vốn
        context.ExpenseVouchers.Add(new ExpenseVoucher
        {
            VoucherCode = "PC-EQUITY",
            BranchId = branchId,
            Category = "Chủ hộ rút vốn kinh doanh (Equity)",
            Amount = 50_000_000m,
            Status = ExpenseVoucherStatus.Paid,
            ExpenseDate = new DateTime(2026, 2, 12, 0, 0, 0, DateTimeKind.Utc)
        });

        await context.SaveChangesAsync();

        // Act
        var entries = await repo.GetBookEntriesAsync(fromDate, toDate, branchId, null);

        // Assert
        Assert.Equal(4, entries.Count); // 4 hợp lệ, 1 rút vốn bị loại trừ hoàn toàn
        Assert.DoesNotContain(entries, e => e.VoucherNumber == "PC-EQUITY");

        Assert.Contains(entries, e => e.BookCode == "S2b-HKD" && e.Amount == 2_000_000m);
        Assert.Contains(entries, e => e.BookCode == "S2c-HKD" && e.Amount == 10_000_000m);
        Assert.Contains(entries, e => e.BookCode == "S2d-HKD" && e.Amount == 1_500_000m);
        Assert.Contains(entries, e => e.BookCode == "S2e-HKD" && e.Amount == 3_000_000m);
    }

    [Fact]
    public async Task HouseholdTaxRepository_AddAndGetSnapshot_ShouldPersistAndRetrieveSnapshot()
    {
        // Arrange
        using var context = CreateDbContext();
        var repo = new HouseholdTaxRepository(context);

        var snapshot = new HouseholdTaxSnapshot
        {
            PeriodType = TaxPeriodType.Quarter,
            Year = 2026,
            PeriodNumber = 1,
            AnnualAccumulatedRevenue = 1_500_000_000m,
            HouseholdGroup = HouseholdTaxGroup.Group2_1BTo3B,
            IsTaxExempt = false,
            TaxableRevenueInPeriod = 400_000_000m,
            VatRate = 1.0m,
            VatAmount = 4_000_000m,
            PitRate = 0.5m,
            PitAmount = 2_000_000m,
            TotalTaxAmount = 6_000_000m,
            BookDataJson = "[{\"VoucherNumber\":\"ORD-01\",\"Amount\":400000000}]",
            FinalizedBy = "accountant@mtgrms.vn",
            Notes = "Kỳ thuế Q1/2026 đã chốt"
        };

        // Act
        await repo.AddSnapshotAsync(snapshot);
        var retrieved = await repo.GetSnapshotAsync(2026, TaxPeriodType.Quarter, 1, null);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(6_000_000m, retrieved.TotalTaxAmount);
        Assert.Equal(HouseholdTaxGroup.Group2_1BTo3B, retrieved.HouseholdGroup);
        Assert.Equal("accountant@mtgrms.vn", retrieved.FinalizedBy);
    }
}
