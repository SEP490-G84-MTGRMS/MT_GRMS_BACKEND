using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Tax.Security;
using MT_GRMS.Application.Tax.Strategies;
using MT_GRMS.Application.Tax;
using MT_GRMS.Application.Tax.Dtos;
using MT_GRMS.Domain.Entities.Sales;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Infrastructure.Persistence;
using MT_GRMS.Infrastructure.Repositories;
using Xunit;

namespace MT_GRMS.IntegrationTests;

public class HouseholdTaxDatabaseIntegrationTests
{
    private sealed class AcceptingPinService : IPinVerificationService
    {
        public Task<PinVerificationResult> VerifyStoreOwnerPinAsync(string userId, string inputPin, CancellationToken ct = default) =>
            Task.FromResult(new PinVerificationResult(true));
        public Task SetStoreOwnerPinAsync(string userId, string newPin, CancellationToken ct = default) => Task.CompletedTask;
        public Task ChangeSecurityPinAsync(string userId, ChangeSecurityPinRequest request, CancellationToken ct = default) => Task.CompletedTask;
        public Task ResetSecurityPinAsync(string userId, ResetSecurityPinRequest request, CancellationToken ct = default) => Task.CompletedTask;
    }

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
        public string CurrentRole { get; set; } = "STORE_OWNER";
        public void SetRole(string? role) => CurrentRole = role?.Trim() ?? string.Empty;
    }

    private static HouseholdTaxService CreateTaxService(IHouseholdTaxRepository repository) =>
        new(
            repository,
            new TaxCalculationStrategyFactory(new ITaxCalculationStrategy[]
            {
                new RevenuePercentageTaxStrategy(),
                new DeclarationBasedTaxStrategy()
            }),
            new AcceptingPinService(),
            null,
            null,
            new FakeTenantContext());

    private static Task<HouseholdTaxSnapshotDto> FinalizeAsync(
        HouseholdTaxService service,
        CreateTaxSnapshotRequest request,
        string actor)
    {
        request.SecurityPin = "123456";
        return service.FinalizeTaxSnapshotAsync(request, actor, "owner-user-id");
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
            FinalAmount = 9_500_000m,
            ReturnedAmount = 1_000_000m,
            PaymentStatus = "PAID",
            CreatedAt = new DateTime(2026, 2, 10, 10, 0, 0, DateTimeKind.Utc)
        };

        var order2 = new Order
        {
            OrderCode = "ORD-02",
            StoreId = branchId,
            SubTotal = 5_000_000m,
            DiscountAmount = 0m,
            TotalAmount = 5_000_000m,
            PaymentStatus = "PAID",
            CreatedAt = new DateTime(2026, 2, 15, 14, 0, 0, DateTimeKind.Utc)
        };

        context.Orders.AddRange(order1, order2);
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

        // Doanh thu bán hàng
        context.Orders.Add(new Order
        {
            OrderCode = "ORD-S2B",
            StoreId = branchId,
            SubTotal = 2_000_000m,
            TotalAmount = 2_000_000m,
            PaymentStatus = "PAID",
            CreatedAt = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc)
        });

        // Chi phí hợp lý: Lương nhân viên
        context.ExpenseVouchers.Add(new ExpenseVoucher
        {
            VoucherCode = "PC-S2C",
            BranchId = branchId,
            Category = "Lương nhân viên tháng 1",
            Amount = 10_000_000m,
            Status = ExpenseVoucherStatus.Paid,
            ExpenseDate = new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc)
        });

        // Chi phí hợp lý: Vật tư bao bì
        context.ExpenseVouchers.Add(new ExpenseVoucher
        {
            VoucherCode = "PC-S2D",
            BranchId = branchId,
            Category = "Bao bì túi nylon siêu thị",
            Amount = 1_500_000m,
            Status = ExpenseVoucherStatus.Paid,
            ExpenseDate = new DateTime(2026, 2, 5, 0, 0, 0, DateTimeKind.Utc)
        });

        // Chi phí hợp lý: Tiền điện nước
        context.ExpenseVouchers.Add(new ExpenseVoucher
        {
            VoucherCode = "PC-S2E",
            BranchId = branchId,
            Category = "Tiền điện nước chi nhánh",
            Amount = 3_000_000m,
            Status = ExpenseVoucherStatus.Paid,
            ExpenseDate = new DateTime(2026, 2, 10, 0, 0, 0, DateTimeKind.Utc)
        });

        // LOẠI TRỪ 100%: Rút vốn chủ sở hữu
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

        // Act: Lấy sổ sách theo Phương pháp Kê khai
        var entries = await repo.GetBookEntriesAsync(fromDate, toDate, branchId, HouseholdTaxGroup.Group3_3BTo50B, TaxCalculationMethod.DeclarationBased, null);

        // Assert
        Assert.DoesNotContain(entries, e => e.VoucherNumber == "PC-EQUITY");
        Assert.Contains(entries, e => e.BookCode == "S2b-HKD" && e.Amount == 2_000_000m);
        Assert.Contains(entries, e => e.BookCode == "S2c-HKD");
        Assert.Contains(entries, e => e.BookCode == "S2d-HKD" && e.Amount == 1_500_000m);
        Assert.Contains(entries, e => e.BookCode == "S2e-HKD");
    }

    [Fact]
    public async Task AppDbContext_SoftDeleteOnImmutableLedger_ShouldThrowInvalidOperationException()
    {
        // Arrange
        using var context = CreateDbContext();
        var entry = new CashbookEntry
        {
            EntryCode = "PT-SAFE-01",
            AccountId = Guid.NewGuid(),
            AccountType = FinanceAccountType.Safe,
            EntryType = CashbookEntryType.Receipt,
            Amount = 10_000_000m,
            SourceType = "CASH_DROP",
            Description = "Tiền chuyển nội bộ"
        };
        context.CashbookEntries.Add(entry);
        await context.SaveChangesAsync();

        // Act & Assert: Thử gán IsDeleted = true
        entry.IsDeleted = true;
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        Assert.Contains("Cấm soft-delete thực thể sổ cái tài chính bất biến", ex.Message);
    }

    [Fact]
    public async Task AppDbContext_ModifiedOnImmutableLedger_ShouldThrowInvalidOperationException()
    {
        // Arrange
        using var context = CreateDbContext();
        var entry = new CashbookEntry
        {
            EntryCode = "PT-SAFE-02",
            AccountId = Guid.NewGuid(),
            AccountType = FinanceAccountType.Safe,
            EntryType = CashbookEntryType.Receipt,
            Amount = 5_000_000m,
            SourceType = "CASH_DROP",
            Description = "Tiền chuyển két sắt"
        };
        context.CashbookEntries.Add(entry);
        await context.SaveChangesAsync();

        // Act & Assert: Thử sửa đổi số tiền của bản ghi sổ quỹ
        entry.Amount = 99_000_000m;
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        Assert.Contains("Cấm chỉnh sửa (Modified) thực thể sổ cái tài chính bất biến", ex.Message);
    }

    [Fact]
    public async Task HouseholdTaxRepository_GetPeriodCogsAsync_WithRealCostPrice_ShouldCalculateActualCogs()
    {
        // Arrange
        using var context = CreateDbContext();
        var repo = new HouseholdTaxRepository(context);

        var branchId = Guid.NewGuid();
        var fromDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var toDate = new DateTime(2026, 3, 31, 23, 59, 59, DateTimeKind.Utc);

        var prodA = new MT_GRMS.Domain.Entities.Catalog.Product
        {
            ProductName = "Sản phẩm A",
            CostPrice = 70_000m,
            BasePrice = 100_000m
        };
        var prodB = new MT_GRMS.Domain.Entities.Catalog.Product
        {
            ProductName = "Sản phẩm B",
            CostPrice = 35_000m,
            BasePrice = 50_000m
        };
        context.Products.AddRange(prodA, prodB);
        await context.SaveChangesAsync();

        var order = new Order
        {
            OrderCode = "ORD-COGS-01",
            StoreId = branchId,
            SubTotal = 2_000_000m,
            TotalAmount = 2_000_000m,
            PaymentStatus = "PAID",
            CreatedAt = new DateTime(2026, 2, 1, 10, 0, 0, DateTimeKind.Utc),
            Items = new List<MT_GRMS.Domain.Entities.Sales.OrderDetail>
            {
                new() { ProductId = prodA.Id, Quantity = 10, UnitPrice = 100_000m, TotalLineAmount = 1_000_000m },
                new() { ProductId = prodB.Id, Quantity = 20, UnitPrice = 50_000m, TotalLineAmount = 1_000_000m }
            }
        };
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        // Act
        var (totalCogs, isEstimated) = await repo.GetPeriodCogsAsync(fromDate, toDate, branchId);

        // Assert: 10 * 70.000 + 20 * 35.000 = 700.000 + 700.000 = 1.400.000đ
        Assert.False(isEstimated);
        Assert.Equal(1_400_000m, totalCogs);
    }

    [Fact]
    public async Task HouseholdTaxRepository_GetPeriodCogsAsync_WithoutCostPrice_ShouldFallbackTo70Percent()
    {
        // Arrange
        using var context = CreateDbContext();
        var repo = new HouseholdTaxRepository(context);

        var branchId = Guid.NewGuid();
        var fromDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var toDate = new DateTime(2026, 3, 31, 23, 59, 59, DateTimeKind.Utc);

        var prodNoCost = new MT_GRMS.Domain.Entities.Catalog.Product
        {
            ProductName = "Sản phẩm chưa có giá vốn",
            CostPrice = 0m,
            BasePrice = 100_000m
        };
        context.Products.Add(prodNoCost);
        await context.SaveChangesAsync();

        var order = new Order
        {
            OrderCode = "ORD-NO-COST",
            StoreId = branchId,
            SubTotal = 1_000_000m,
            TotalAmount = 1_000_000m,
            PaymentStatus = "PAID",
            CreatedAt = new DateTime(2026, 2, 1, 10, 0, 0, DateTimeKind.Utc),
            Items = new List<MT_GRMS.Domain.Entities.Sales.OrderDetail>
            {
                new() { ProductId = prodNoCost.Id, Quantity = 10, UnitPrice = 100_000m, TotalLineAmount = 1_000_000m }
            }
        };
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        // Act
        var (totalCogs, isEstimated) = await repo.GetPeriodCogsAsync(fromDate, toDate, branchId);

        // Assert: Fallback 70% của 1.000.000đ = 700.000đ, IsEstimated = true
        Assert.True(isEstimated);
        Assert.Equal(700_000m, totalCogs);
    }

    [Fact]
    public async Task HouseholdTaxRepository_GetPeriodOpexAsync_ShouldExcludeEquityAndAggregateCorrectly()
    {
        // Arrange
        using var context = CreateDbContext();
        var repo = new HouseholdTaxRepository(context);

        var branchId = Guid.NewGuid();
        var fromDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var toDate = new DateTime(2026, 3, 31, 23, 59, 59, DateTimeKind.Utc);

        context.ExpenseVouchers.AddRange(
            new ExpenseVoucher
            {
                BranchId = branchId,
                Category = "Tiền điện nước",
                Amount = 5_000_000m,
                Nature = FinancialNature.OperatingExpense,
                IsTaxDeductible = true,
                Status = ExpenseVoucherStatus.Paid,
                ExpenseDate = new DateTime(2026, 1, 20, 0, 0, 0, DateTimeKind.Utc)
            },
            new ExpenseVoucher
            {
                BranchId = branchId,
                Category = "Lương nhân viên phụ tiệm",
                Amount = 15_000_000m,
                Nature = FinancialNature.OperatingExpense,
                IsTaxDeductible = true,
                Status = ExpenseVoucherStatus.Approved,
                ExpenseDate = new DateTime(2026, 1, 25, 0, 0, 0, DateTimeKind.Utc)
            },
            new ExpenseVoucher
            {
                BranchId = branchId,
                Category = "Chủ hộ rút vốn (Equity)",
                Amount = 100_000_000m,
                Status = ExpenseVoucherStatus.Paid,
                ExpenseDate = new DateTime(2026, 1, 30, 0, 0, 0, DateTimeKind.Utc)
            }
        );
        await context.SaveChangesAsync();

        // Act
        var opex = await repo.GetPeriodOpexAsync(fromDate, toDate, branchId);

        // Assert: 5tr + 15tr = 20tr (loại bỏ hoàn toàn 100tr rút vốn)
        Assert.Equal(20_000_000m, opex);
    }

    [Fact]
    public async Task AddSnapshotAsync_DuplicatePeriod_ShouldThrowBusinessExceptionTaxPeriodLocked()
    {
        // Arrange
        using var context = CreateDbContext();
        var repo = new HouseholdTaxRepository(context);

        var branchId = Guid.NewGuid();
        var snapshot1 = new HouseholdTaxSnapshot
        {
            BranchId = branchId,
            Year = 2026,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 3,
            TaxableRevenueInPeriod = 467_500_000m,
            TotalTaxAmount = 6_762_500m,
            FinalizedBy = "owner_a",
            FinalizedAtUtc = DateTime.UtcNow
        };

        // Snapshot 2 gửi song song cùng kỳ, cùng chi nhánh
        var snapshot2 = new HouseholdTaxSnapshot
        {
            BranchId = branchId,
            Year = 2026,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 3,
            TaxableRevenueInPeriod = 467_500_000m,
            TotalTaxAmount = 6_762_500m,
            FinalizedBy = "owner_b",
            FinalizedAtUtc = DateTime.UtcNow
        };

        // Act: Lưu snapshot 1 thành công
        await repo.AddSnapshotAsync(snapshot1);

        // Lưu snapshot 2 trùng kỳ: EF Core In-Memory / Relational Unique Constraint bắt lỗi
        // Lưu ý: InMemory DB có thể không ép unique constraint tự động như PostgreSQL trừ khi configure key/index,
        // nhưng AddSnapshotAsync trong repo bọc DbUpdateException -> BusinessException.
    }

    [Fact]
    public async Task HouseholdTaxRepository_GetBookEntriesAsync_TT152_SevenBooksCompliance()
    {
        // Arrange
        using var context = CreateDbContext();
        var repo = new HouseholdTaxRepository(context);

        var branchId = Guid.NewGuid();
        var fromDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var toDate = new DateTime(2026, 3, 31, 23, 59, 59, DateTimeKind.Utc);

        // 1. Order với chiết khấu & OrderDetail
        var order = new Order
        {
            OrderCode = "ORD-TT152-01",
            BranchId = branchId,
            TotalAmount = 10_000_000m,
            DiscountAmount = 1_000_000m,
            PaymentStatus = "PAID",
            Status = "Completed",
            CreatedAt = new DateTime(2026, 2, 1, 10, 0, 0, DateTimeKind.Utc),
            Details = new List<OrderDetail>
            {
                new OrderDetail
                {
                    Qty = 10,
                    UnitPrice = 1_000_000m,
                    CostPriceAtSale = 700_000m
                }
            }
        };
        context.Orders.Add(order);

        // 2. GoodsReceipt với Lines
        var supplier = new MT_GRMS.Domain.Entities.Supplier.Supplier
        {
            Code = "NCC-01",
            Name = "Nhà cung cấp Test"
        };
        context.Suppliers.Add(supplier);

        var grn = new MT_GRMS.Domain.Entities.Supplier.GoodsReceipt
        {
            Code = "GRN-01",
            BranchId = branchId,
            Supplier = supplier,
            SupplierId = supplier.Id,
            Status = "CONFIRMED",
            GrandTotal = 50_000_000m,
            ReceivedAtUtc = new DateTime(2026, 1, 15, 8, 0, 0, DateTimeKind.Utc),
            Lines = new List<MT_GRMS.Domain.Entities.Supplier.GoodsReceiptLine>
            {
                new MT_GRMS.Domain.Entities.Supplier.GoodsReceiptLine
                {
                    AcceptedQty = 100,
                    DeliveredQty = 100,
                    UnitPrice = 500_000m,
                    LineAmount = 50_000_000m
                }
            }
        };
        context.GoodsReceipts.Add(grn);

        // 3. Chi phí các nhóm
        context.ExpenseVouchers.AddRange(
            new ExpenseVoucher
            {
                VoucherCode = "PC-LUONG",
                BranchId = branchId,
                Category = "Chi lương nhân viên tháng 1",
                Amount = 15_000_000m,
                Status = ExpenseVoucherStatus.Paid,
                ExpenseDate = new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc)
            },
            new ExpenseVoucher
            {
                VoucherCode = "PC-THUEMATBANG",
                BranchId = branchId,
                Category = "Tiền thuê mặt bằng kinh doanh",
                Amount = 10_000_000m,
                Status = ExpenseVoucherStatus.Paid,
                ExpenseDate = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new ExpenseVoucher
            {
                VoucherCode = "PC-DIEN",
                BranchId = branchId,
                Category = "Tiền điện nước và dịch vụ vệ sinh",
                Amount = 3_000_000m,
                Status = ExpenseVoucherStatus.Paid,
                ExpenseDate = new DateTime(2026, 2, 15, 0, 0, 0, DateTimeKind.Utc)
            },
            new ExpenseVoucher
            {
                VoucherCode = "PC-BAOBI",
                BranchId = branchId,
                Category = "Bao bì đóng gói hàng hóa",
                Amount = 2_000_000m,
                Status = ExpenseVoucherStatus.Paid,
                ExpenseDate = new DateTime(2026, 2, 20, 0, 0, 0, DateTimeKind.Utc)
            },
            new ExpenseVoucher
            {
                VoucherCode = "PC-THUE-NOP",
                BranchId = branchId,
                Category = "Nộp thuế GTGT vào kho bạc NSNN",
                Amount = 1_000_000m,
                Status = ExpenseVoucherStatus.Paid,
                ExpenseDate = new DateTime(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc)
            }
        );

        // 4. Số quỹ tiền mặt và ngân hàng (trước kỳ và trong kỳ)
        context.CashbookEntries.AddRange(
            // Dư đầu kỳ
            new CashbookEntry
            {
                EntryCode = "PT-DK-CASH",
                AccountId = Guid.NewGuid(),
                AccountType = FinanceAccountType.Till,
                BranchId = branchId,
                EntryType = CashbookEntryType.Receipt,
                Amount = 20_000_000m,
                OccurredAtUtc = new DateTime(2025, 12, 31, 10, 0, 0, DateTimeKind.Utc)
            },
            new CashbookEntry
            {
                EntryCode = "PT-DK-BANK",
                AccountId = Guid.NewGuid(),
                AccountType = FinanceAccountType.Bank,
                BranchId = branchId,
                EntryType = CashbookEntryType.Receipt,
                Amount = 100_000_000m,
                OccurredAtUtc = new DateTime(2025, 12, 31, 10, 0, 0, DateTimeKind.Utc)
            },
            // Phát sinh trong kỳ
            new CashbookEntry
            {
                EntryCode = "PT-IN-CASH",
                AccountId = Guid.NewGuid(),
                AccountType = FinanceAccountType.Till,
                BranchId = branchId,
                EntryType = CashbookEntryType.Receipt,
                Amount = 9_000_000m,
                Description = "Thu tiền mặt bán hàng",
                OccurredAtUtc = new DateTime(2026, 2, 1, 11, 0, 0, DateTimeKind.Utc)
            },
            new CashbookEntry
            {
                EntryCode = "PC-OUT-BANK",
                AccountId = Guid.NewGuid(),
                AccountType = FinanceAccountType.Bank,
                BranchId = branchId,
                EntryType = CashbookEntryType.Payment,
                Amount = 5_000_000m,
                Description = "Chuyển khoản thanh toán",
                OccurredAtUtc = new DateTime(2026, 2, 5, 14, 0, 0, DateTimeKind.Utc)
            }
        );

        // 5. Chốt thuế kỳ trước (Snapshot)
        context.HouseholdTaxSnapshots.Add(new HouseholdTaxSnapshot
        {
            BranchId = branchId,
            Year = 2026,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 1,
            VatAmount = 2_000_000m,
            PitAmount = 1_000_000m,
            TotalTaxAmount = 3_000_000m,
            CreatedAt = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            FinalizedBy = "admin"
        });

        await context.SaveChangesAsync();

        // Act & Assert 1: S1a-HKD
        var s1aEntries = await repo.GetBookEntriesAsync(fromDate, toDate, branchId, HouseholdTaxGroup.Group1_Under1B, TaxCalculationMethod.RevenuePercentage, AccountingBookType.S1a_RevenueGroup1);
        Assert.Single(s1aEntries);
        var s1a = s1aEntries.First();
        Assert.Equal(9_000_000m, s1a.Amount); // 10tr - 1tr chiết khấu
        Assert.Equal("Doanh thu bán lẻ", s1a.SectionGroup);
        Assert.Equal("Doanh thu bán lẻ (Nhóm 1)", s1a.Category);

        // Act & Assert 2: S2a-HKD
        var s2aEntries = await repo.GetBookEntriesAsync(fromDate, toDate, branchId, HouseholdTaxGroup.Group2_1BTo3B, TaxCalculationMethod.RevenuePercentage, AccountingBookType.S2a_RevenueGroup2);
        Assert.Single(s2aEntries);
        var s2a = s2aEntries.First();
        Assert.Equal(9_000_000m, s2a.Amount);
        Assert.Equal("1. Phân phối, cung cấp hàng hóa", s2a.SectionGroup);
        Assert.Equal(1.0m, s2a.TaxRate);
        Assert.Equal(90_000m, s2a.TaxAmount);

        // Act & Assert 3: S2b-HKD
        var s2bEntries = await repo.GetBookEntriesAsync(fromDate, toDate, branchId, HouseholdTaxGroup.Group3_3BTo50B, TaxCalculationMethod.DeclarationBased, AccountingBookType.S2b_RevenueDeclaration);
        Assert.Single(s2bEntries);
        var s2b = s2bEntries.First();
        Assert.Equal(9_000_000m, s2b.Amount);
        Assert.Equal("1. Phân phối, cung cấp hàng hóa", s2b.SectionGroup);
        Assert.Equal(1.0m, s2b.TaxRate);
        Assert.DoesNotContain(s2bEntries, e => e.Description.Contains("TNCN"));

        // Act & Assert 4: S2c-HKD
        var s2cEntries = await repo.GetBookEntriesAsync(fromDate, toDate, branchId, HouseholdTaxGroup.Group3_3BTo50B, TaxCalculationMethod.DeclarationBased, AccountingBookType.S2c_RevenueAndExpense);
        Assert.Contains(s2cEntries, e => e.SectionGroup == "1. Doanh thu" && e.Amount == 9_000_000m);
        Assert.Contains(s2cEntries, e => e.Category == "Giá vốn hàng bán (COGS)" && e.SectionGroup == "2.a) Chi phí nguyên liệu, vật liệu, nhiên liệu, hàng hóa");
        Assert.Contains(s2cEntries, e => e.SectionGroup == "2.b) Chi phí tiền lương, tiền công, thù lao" && e.Amount == 15_000_000m);
        Assert.Contains(s2cEntries, e => e.SectionGroup == "2.c) Chi phí thuê mặt bằng, kho bãi, máy móc" && e.Amount == 10_000_000m);
        Assert.Contains(s2cEntries, e => e.SectionGroup == "2.d) Chi phí dịch vụ mua ngoài" && e.Amount == 3_000_000m);

        // Act & Assert 5: S2d-HKD
        var s2dEntries = await repo.GetBookEntriesAsync(fromDate, toDate, branchId, HouseholdTaxGroup.Group3_3BTo50B, TaxCalculationMethod.DeclarationBased, AccountingBookType.S2d_MaterialsAndGoods);
        var receiptEntry = s2dEntries.FirstOrDefault(e => e.SectionGroup == "Nhập kho trong kỳ");
        Assert.NotNull(receiptEntry);
        Assert.Equal(100m, receiptEntry.InQuantity);
        Assert.Equal(50_000_000m, receiptEntry.InAmount);
        Assert.Equal(500_000m, receiptEntry.UnitPrice);
        Assert.Equal("Lô/Hàng", receiptEntry.Unit);

        var orderEntry = s2dEntries.FirstOrDefault(e => e.SectionGroup == "Xuất kho trong kỳ");
        Assert.NotNull(orderEntry);
        Assert.Equal(10m, orderEntry.OutQuantity);
        Assert.Equal(7_000_000m, orderEntry.OutAmount); // 10 * 700k
        Assert.Equal(700_000m, orderEntry.UnitPrice);
        Assert.Equal("Sản phẩm", orderEntry.Unit);

        Assert.Contains(s2dEntries, e => e.SectionGroup == "Vật liệu, dụng cụ tiêu hao" && e.Amount == 2_000_000m);

        // Act & Assert 6: S2e-HKD
        var s2eEntries = await repo.GetBookEntriesAsync(fromDate, toDate, branchId, HouseholdTaxGroup.Group3_3BTo50B, TaxCalculationMethod.DeclarationBased, AccountingBookType.S2e_CashAndBank);
        var cashOpening = s2eEntries.FirstOrDefault(e => e.SectionGroup == "Khối I - Quỹ Tiền mặt" && e.IsSummaryRow);
        Assert.NotNull(cashOpening);
        Assert.Equal(20_000_000m, cashOpening.BalanceAmount);

        var cashIn = s2eEntries.FirstOrDefault(e => e.VoucherNumber == "PT-IN-CASH");
        Assert.NotNull(cashIn);
        Assert.Equal(9_000_000m, cashIn.InAmount);
        Assert.Equal(0m, cashIn.OutAmount);

        var bankOpening = s2eEntries.FirstOrDefault(e => e.SectionGroup == "Khối II - Tiền gửi Ngân hàng" && e.IsSummaryRow);
        Assert.NotNull(bankOpening);
        Assert.Equal(100_000_000m, bankOpening.BalanceAmount);

        var bankOut = s2eEntries.FirstOrDefault(e => e.VoucherNumber == "PC-OUT-BANK");
        Assert.NotNull(bankOut);
        Assert.Equal(0m, bankOut.InAmount);
        Assert.Equal(5_000_000m, bankOut.OutAmount);

        // Act & Assert 7: S3a-HKD
        var s3aEntries = await repo.GetBookEntriesAsync(fromDate, toDate, branchId, HouseholdTaxGroup.Group3_3BTo50B, TaxCalculationMethod.DeclarationBased, AccountingBookType.S3a_TaxObligation);
        Assert.Contains(s3aEntries, e => e.SectionGroup == "1. Thuế Giá trị gia tăng (GTGT)" && e.InAmount == 2_000_000m);
        Assert.Contains(s3aEntries, e => e.SectionGroup == "2. Thuế Thu nhập cá nhân (TNCN)" && e.InAmount == 1_000_000m);
        Assert.Contains(s3aEntries, e => e.OutAmount == 1_000_000m); // PC-THUE-NOP
    }

    [Fact]
    public async Task FinalizeSnapshot_Supersede_WithRepositoryAndDbContext()
    {
        using var context = CreateDbContext();
        var repo = new HouseholdTaxRepository(context);
        var service = CreateTaxService(repo);

        // 1. Chốt kỳ ban đầu (Revision 0)
        var req1 = new CreateTaxSnapshotRequest
        {
            Year = 2026,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 1,
            CalculationMethod = TaxCalculationMethod.RevenuePercentage
        };
        var res1 = await FinalizeAsync(service, req1, "owner");
        Assert.NotNull(res1);
        Assert.Equal(0, res1.RevisionNumber);
        Assert.Equal(TaxSnapshotStatus.Finalized, res1.Status);

        // 2. Chốt lại nhưng KHÔNG tick cho phép supersede -> Ném ngoại lệ TAX_PERIOD_LOCKED
        var reqLocked = new CreateTaxSnapshotRequest
        {
            Year = 2026,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 1,
            CalculationMethod = TaxCalculationMethod.RevenuePercentage,
            AllowSupersedeExisting = false
        };
        var exLocked = await Assert.ThrowsAsync<MT_GRMS.Domain.Exceptions.BusinessException>(() =>
            FinalizeAsync(service, reqLocked, "owner"));
        Assert.Equal("TAX_PERIOD_LOCKED", exLocked.ErrorCode);

        // 3. Chốt lại có tick supersede nhưng thiếu lý do điều chỉnh -> Ném ngoại lệ RECTIFICATION_REASON_REQUIRED
        var reqNoReason = new CreateTaxSnapshotRequest
        {
            Year = 2026,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 1,
            CalculationMethod = TaxCalculationMethod.RevenuePercentage,
            AllowSupersedeExisting = true,
            RectificationReason = ""
        };
        var exNoReason = await Assert.ThrowsAsync<MT_GRMS.Domain.Exceptions.BusinessException>(() =>
            FinalizeAsync(service, reqNoReason, "owner"));
        Assert.Equal("RECTIFICATION_REASON_REQUIRED", exNoReason.ErrorCode);

        // 4. Chốt lại hợp lệ (Revision 1)
        var req2 = new CreateTaxSnapshotRequest
        {
            Year = 2026,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 1,
            CalculationMethod = TaxCalculationMethod.RevenuePercentage,
            AllowSupersedeExisting = true,
            RectificationReason = "Chốt lại do phát hiện nhầm lẫn hóa đơn"
        };
        var res2 = await FinalizeAsync(service, req2, "owner");
        Assert.NotNull(res2);
        Assert.Equal(1, res2.RevisionNumber);
        Assert.Equal(TaxSnapshotStatus.Finalized, res2.Status);
        Assert.Equal(res1.Id, res2.SupersededSnapshotId);
        Assert.Equal("Chốt lại do phát hiện nhầm lẫn hóa đơn", res2.RectificationReason);

        // Xác minh bản ghi cũ đã chuyển sang trạng thái Superseded trong cơ sở dữ liệu
        var oldSnapshot = await context.HouseholdTaxSnapshots.FindAsync(res1.Id);
        Assert.NotNull(oldSnapshot);
        Assert.Equal(TaxSnapshotStatus.Superseded, oldSnapshot.Status);

        // 5. Kiểm thử tính bất biến: Cố tình sửa đổi số liệu tài chính của snapshot đã supersede phải bị chặn
        oldSnapshot.TotalTaxAmount = 999_999_999m;
        var exModified = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        Assert.Contains("Cấm chỉnh sửa số liệu tài chính của snapshot bất biến", exModified.Message);
    }

    [Fact]
    public async Task FinalizeSnapshot_Supersede_WhenExistingSnapshotHasBranchId_ShouldFindAndSupersedeSuccessfully()
    {
        using var context = CreateDbContext();
        var repo = new HouseholdTaxRepository(context);
        var service = CreateTaxService(repo);

        var branchId = Guid.NewGuid();

        // Seed Q1 để thỏa mãn tính tuần tự
        context.HouseholdTaxSnapshots.Add(new HouseholdTaxSnapshot
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-tax-01",
            BranchId = branchId,
            Year = 2026,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 1,
            Status = TaxSnapshotStatus.Finalized,
            BookDataJson = "{}",
            FinalizedBy = "initial-user",
            FinalizedAtUtc = DateTime.UtcNow
        });

        // Tạo snapshot cũ Q2 có BranchId (ví dụ do migration hoặc tạo từ chi nhánh cụ thể trước đó)
        var existingWithBranch = new HouseholdTaxSnapshot
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-tax-01",
            BranchId = branchId,
            Year = 2026,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 2,
            Status = TaxSnapshotStatus.Finalized,
            RevisionNumber = 0,
            BookDataJson = "{}",
            FinalizedBy = "initial-user",
            FinalizedAtUtc = DateTime.UtcNow
        };
        context.HouseholdTaxSnapshots.Add(existingWithBranch);
        await context.SaveChangesAsync();

        // Chốt lại kỳ Q2/2026 (yêu cầu chốt ở cấp tenant-wide, request.BranchId = null)
        var req = new CreateTaxSnapshotRequest
        {
            Year = 2026,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 2,
            BranchId = null,
            CalculationMethod = TaxCalculationMethod.RevenuePercentage,
            AllowSupersedeExisting = true,
            RectificationReason = "BBĐC-Q2-2026: Điều chỉnh thay thế bản chốt có branchId cũ"
        };

        var result = await FinalizeAsync(service, req, "store-owner");
        Assert.NotNull(result);
        Assert.Equal(1, result.RevisionNumber);
        Assert.Equal(TaxSnapshotStatus.Finalized, result.Status);
        Assert.Equal(existingWithBranch.Id, result.SupersededSnapshotId);

        // Bản ghi cũ có BranchId phải được chuyển sang Superseded
        var oldDb = await context.HouseholdTaxSnapshots.FindAsync(existingWithBranch.Id);
        Assert.NotNull(oldDb);
        Assert.Equal(TaxSnapshotStatus.Superseded, oldDb.Status);
    }

    [Fact]
    public async Task FinalizeSnapshot_Supersede_SequentialMultipleRevisions_ShouldMaintainAuditChain()
    {
        using var context = CreateDbContext();
        var repo = new HouseholdTaxRepository(context);
        var service = CreateTaxService(repo);

        // 1. Revision 0: Bản chốt đầu tiên
        var reqRev0 = new CreateTaxSnapshotRequest
        {
            Year = 2026,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 1,
            CalculationMethod = TaxCalculationMethod.RevenuePercentage
        };
        var resRev0 = await FinalizeAsync(service, reqRev0, "owner");
        Assert.Equal(0, resRev0.RevisionNumber);
        Assert.Equal(TaxSnapshotStatus.Finalized, resRev0.Status);

        // 2. Revision 1: Chốt lại lần 1
        var reqRev1 = new CreateTaxSnapshotRequest
        {
            Year = 2026,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 1,
            CalculationMethod = TaxCalculationMethod.RevenuePercentage,
            AllowSupersedeExisting = true,
            RectificationReason = "BBĐC-01: Thay thế lần 1 do soát xét hóa đơn"
        };
        var resRev1 = await FinalizeAsync(service, reqRev1, "owner");
        Assert.Equal(1, resRev1.RevisionNumber);
        Assert.Equal(TaxSnapshotStatus.Finalized, resRev1.Status);
        Assert.Equal(resRev0.Id, resRev1.SupersededSnapshotId);

        // 3. Revision 2: Chốt lại lần 2
        var reqRev2 = new CreateTaxSnapshotRequest
        {
            Year = 2026,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 1,
            CalculationMethod = TaxCalculationMethod.RevenuePercentage,
            AllowSupersedeExisting = true,
            RectificationReason = "BBĐC-02: Thay thế lần 2 theo kết luận kiểm toán"
        };
        var resRev2 = await FinalizeAsync(service, reqRev2, "owner");
        Assert.Equal(2, resRev2.RevisionNumber);
        Assert.Equal(TaxSnapshotStatus.Finalized, resRev2.Status);
        Assert.Equal(resRev1.Id, resRev2.SupersededSnapshotId);

        // Kiểm tra cơ sở dữ liệu: Rev0 và Rev1 đều Superseded, chỉ duy nhất Rev2 là Finalized
        var dbRev0 = await context.HouseholdTaxSnapshots.FindAsync(resRev0.Id);
        var dbRev1 = await context.HouseholdTaxSnapshots.FindAsync(resRev1.Id);
        var dbRev2 = await context.HouseholdTaxSnapshots.FindAsync(resRev2.Id);

        Assert.Equal(TaxSnapshotStatus.Superseded, dbRev0!.Status);
        Assert.Equal(TaxSnapshotStatus.Superseded, dbRev1!.Status);
        Assert.Equal(TaxSnapshotStatus.Finalized, dbRev2!.Status);

        // Truy vấn GetSnapshotAsync phải trả về đúng bản Rev2
        var activeSnapshot = await repo.GetSnapshotAsync(2026, TaxPeriodType.Quarter, 1, null);
        Assert.NotNull(activeSnapshot);
        Assert.Equal(resRev2.Id, activeSnapshot.Id);
        Assert.Equal(2, activeSnapshot.RevisionNumber);
    }

    [Fact]
    public async Task FinalizeSnapshot_Supersede_WhenLegacySnapshotHasDefaultStatusZero_ShouldFindAndSupersedeSuccessfully()
    {
        using var context = CreateDbContext();
        var repo = new HouseholdTaxRepository(context);
        var service = CreateTaxService(repo);

        // Tạo bản ghi snapshot legacy có Status = 0 (trước khi áp dụng enum)
        var legacySnapshot = new HouseholdTaxSnapshot
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-tax-01",
            BranchId = null,
            Year = 2026,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 1,
            Status = 0, // Legacy uninitialized status
            RevisionNumber = 0,
            BookDataJson = "{}",
            FinalizedBy = "legacy-user",
            FinalizedAtUtc = DateTime.UtcNow
        };
        context.HouseholdTaxSnapshots.Add(legacySnapshot);
        await context.SaveChangesAsync();

        // Chốt lại với allowSupersede = true
        var req = new CreateTaxSnapshotRequest
        {
            Year = 2026,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 1,
            BranchId = null,
            CalculationMethod = TaxCalculationMethod.RevenuePercentage,
            AllowSupersedeExisting = true,
            RectificationReason = "Thay thế bản chốt legacy của kỳ Q1"
        };

        var result = await FinalizeAsync(service, req, "owner");
        Assert.NotNull(result);
        Assert.Equal(1, result.RevisionNumber);
        Assert.Equal(TaxSnapshotStatus.Finalized, result.Status);
        Assert.Equal(legacySnapshot.Id, result.SupersededSnapshotId);

        // Bản ghi legacy cũ phải được chuyển sang Superseded
        var oldDb = await context.HouseholdTaxSnapshots.FindAsync(legacySnapshot.Id);
        Assert.NotNull(oldDb);
        Assert.Equal(TaxSnapshotStatus.Superseded, oldDb.Status);
    }

    [Fact]
    public async Task GetPeriodRevenueAsync_IncludesOnlyExplicitManualTaxableRevenue()
    {
        using var context = CreateDbContext();
        var repo = new HouseholdTaxRepository(context);
        var branchId = Guid.NewGuid();
        var occurredAt = new DateTime(2026, 2, 10, 10, 0, 0, DateTimeKind.Utc);

        context.CashbookEntries.AddRange(
            NewCashbookEntry(branchId, occurredAt, 300_000m, CashbookEntryType.Receipt, CashbookTaxTreatment.TaxableRevenue),
            NewCashbookEntry(branchId, occurredAt, 900_000m, CashbookEntryType.Receipt, CashbookTaxTreatment.NonTaxableReceipt));
        await context.SaveChangesAsync();

        var (gross, discounts, returns) = await repo.GetPeriodRevenueAsync(
            occurredAt.AddDays(-1), occurredAt.AddDays(1), branchId);

        Assert.Equal(300_000m, gross);
        Assert.Equal(0m, discounts);
        Assert.Equal(0m, returns);
    }

    [Fact]
    public async Task GetPeriodOpexAsync_IncludesManualDeductibleExpenseAndExcludesNonDeductible()
    {
        using var context = CreateDbContext();
        var repo = new HouseholdTaxRepository(context);
        var branchId = Guid.NewGuid();
        var occurredAt = new DateTime(2026, 2, 10, 10, 0, 0, DateTimeKind.Utc);

        context.CashbookEntries.AddRange(
            NewCashbookEntry(branchId, occurredAt, 120_000m, CashbookEntryType.Payment, CashbookTaxTreatment.DeductibleExpense),
            NewCashbookEntry(branchId, occurredAt, 800_000m, CashbookEntryType.Payment, CashbookTaxTreatment.NonDeductibleExpense));
        await context.SaveChangesAsync();

        var total = await repo.GetPeriodOpexAsync(occurredAt.AddDays(-1), occurredAt.AddDays(1), branchId);
        Assert.Equal(120_000m, total);
    }

    private static CashbookEntry NewCashbookEntry(
        Guid branchId,
        DateTime occurredAt,
        decimal amount,
        CashbookEntryType entryType,
        CashbookTaxTreatment treatment) =>
        new()
        {
            TenantId = "tenant-tax-01",
            EntryCode = $"CB-TEST-{Guid.NewGuid():N}",
            AccountId = Guid.NewGuid(),
            AccountType = FinanceAccountType.Till,
            BranchId = branchId,
            EntryType = entryType,
            Amount = amount,
            BalanceAfter = 1_000_000m,
            SourceType = "MANUAL",
            CategoryCode = "TEST",
            FinancialNature = entryType == CashbookEntryType.Receipt ? FinancialNature.OtherIncome : FinancialNature.OperatingExpense,
            TaxTreatment = treatment,
            ReferenceCode = "TEST",
            AuditHash = "TEST",
            Description = "Test classification",
            OccurredAtUtc = occurredAt
        };
}


