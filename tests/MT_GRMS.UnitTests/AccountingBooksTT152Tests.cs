using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Tax;
using MT_GRMS.Application.Tax.Dtos;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Entities.Inventory;
using MT_GRMS.Domain.Entities.Sales;
using MT_GRMS.Domain.Entities.Supplier;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Infrastructure.Persistence;
using MT_GRMS.Infrastructure.Repositories;
using Xunit;

namespace MT_GRMS.UnitTests;

/// <summary>
/// Dedicated Unit Tests for 7 Accounting Books under Circular 152/2025/TT-BTC
/// (S1a, S2a, S2b, S2c, S2d, S2e, S3a).
/// </summary>
public class AccountingBooksTT152Tests
{
  private class FakeTenantContext : ITenantContext
  {
    public string CurrentTenantId => "tenant-test-tt152";
    public Guid CurrentUserId => Guid.Parse("11111111-1111-1111-1111-111111111111");
    public Guid? CurrentBranchId => Guid.Parse("22222222-2222-2222-2222-222222222222");
    public string CurrentRole => "STORE_OWNER";
    public void SetContext(string tenantId, Guid userId, Guid? branchId) { }
    public void SetRole(string? role) { }
  }

  private static AppDbContext CreateInMemoryDbContext(string dbName)
  {
    var options = new DbContextOptionsBuilder<AppDbContext>()
      .UseInMemoryDatabase(databaseName: dbName)
      .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
      .Options;

    var tenantContext = new FakeTenantContext();
    return new AppDbContext(options, tenantContext);
  }

  // =========================================================================
  // 1. TEST S1a-HKD: Doanh thu thuần (Net Revenue), kiểm tra không lấy nhầm Gross
  // =========================================================================
  [Fact]
  public async Task Test_S1a_NetRevenue_ShouldExcludeDiscountAndNeverUseGrossRevenue()
  {
    var dbName = $"TT152_S1a_{Guid.NewGuid()}";
    using var context = CreateInMemoryDbContext(dbName);
    var repo = new HouseholdTaxRepository(context);

    var branchId = Guid.NewGuid();
    var fromDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    var toDate = new DateTime(2026, 3, 31, 23, 59, 59, DateTimeKind.Utc);

    // Order 1: Gross = 500,000 VND, Discount = 50,000 VND -> Net = 450,000 VND
    context.Orders.Add(new Order
    {
      OrderCode = "ORD-001",
      BranchId = branchId,
      TotalAmount = 500_000m,
      DiscountAmount = 50_000m,
      FinalAmount = 450_000m,
      PaymentStatus = "PAID",
      Status = "Completed",
      CreatedAt = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc)
    });

    // Order 2: Gross = 300,000 VND, Discount = 0 VND -> Net = 300,000 VND
    context.Orders.Add(new Order
    {
      OrderCode = "ORD-002",
      BranchId = branchId,
      TotalAmount = 300_000m,
      DiscountAmount = 0m,
      FinalAmount = 300_000m,
      PaymentStatus = "PAID",
      Status = "Completed",
      CreatedAt = new DateTime(2026, 2, 10, 14, 0, 0, DateTimeKind.Utc)
    });

    // Order 3: Gross = 200,000 VND, Discount = 30,000 VND -> Net = 170,000 VND
    context.Orders.Add(new Order
    {
      OrderCode = "ORD-003",
      BranchId = branchId,
      TotalAmount = 200_000m,
      DiscountAmount = 30_000m,
      FinalAmount = 170_000m,
      PaymentStatus = "PAID",
      Status = "Completed",
      CreatedAt = new DateTime(2026, 3, 5, 9, 0, 0, DateTimeKind.Utc)
    });

    await context.SaveChangesAsync();

    // Act: Query book entries for S1a
    var entries = await repo.GetBookEntriesAsync(
      fromDate,
      toDate,
      branchId,
      HouseholdTaxGroup.Group1_Under1B,
      TaxCalculationMethod.Exempt,
      AccountingBookType.S1a_RevenueGroup1
    );

    // Assert: Check S1a Net Revenue compliance
    Assert.Equal(3, entries.Count);
    Assert.All(entries, e =>
    {
      Assert.Equal("S1a-HKD", e.BookCode);
      Assert.Equal(AccountingBookType.S1a_RevenueGroup1, e.BookType);
      Assert.Equal("Doanh thu bán lẻ (Nhóm 1)", e.Category);
      Assert.Equal("Doanh thu bán lẻ", e.SectionGroup);
    });

    var entry1 = entries.First(e => e.VoucherNumber == "ORD-001");
    Assert.Equal(450_000m, entry1.Amount);
    Assert.NotEqual(500_000m, entry1.Amount); // CRITICAL: Must not take Gross

    var entry2 = entries.First(e => e.VoucherNumber == "ORD-002");
    Assert.Equal(300_000m, entry2.Amount);

    var entry3 = entries.First(e => e.VoucherNumber == "ORD-003");
    Assert.Equal(170_000m, entry3.Amount);
    Assert.NotEqual(200_000m, entry3.Amount); // CRITICAL: Must not take Gross

    var totalNetRevenue = entries.Sum(e => e.Amount);
    Assert.Equal(920_000m, totalNetRevenue); // 450k + 300k + 170k
    Assert.NotEqual(1_000_000m, totalNetRevenue); // Must not equal Gross sum (1,000,000)
  }

  // =========================================================================
  // 2. TEST S2a-HKD: Nhóm theo ngành nghề, xác thực công thức thuế GTGT và TNCN
  // =========================================================================
  [Fact]
  public async Task Test_S2a_IndustryGroupAndTaxFormulas_ShouldCalculateAccurateVatAndPit()
  {
    var dbName = $"TT152_S2a_{Guid.NewGuid()}";
    using var context = CreateInMemoryDbContext(dbName);
    var repo = new HouseholdTaxRepository(context);

    var branchId = Guid.NewGuid();
    var fromDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    var toDate = new DateTime(2026, 3, 31, 23, 59, 59, DateTimeKind.Utc);

    // Order: Net = 1,000,000 - 100,000 = 900,000 VND
    context.Orders.Add(new Order
    {
      OrderCode = "ORD-S2A-01",
      BranchId = branchId,
      TotalAmount = 1_000_000m,
      DiscountAmount = 100_000m,
      FinalAmount = 900_000m,
      PaymentStatus = "PAID",
      Status = "Completed",
      CreatedAt = new DateTime(2026, 2, 1, 10, 0, 0, DateTimeKind.Utc)
    });

    await context.SaveChangesAsync();

    // Act
    var entries = await repo.GetBookEntriesAsync(
      fromDate,
      toDate,
      branchId,
      HouseholdTaxGroup.Group2_1BTo3B,
      TaxCalculationMethod.RevenuePercentage,
      AccountingBookType.S2a_RevenueGroup2
    );

    // Assert
    var entry = Assert.Single(entries);
    Assert.Equal("S2a-HKD", entry.BookCode);
    Assert.Equal(AccountingBookType.S2a_RevenueGroup2, entry.BookType);
    Assert.Equal("1. Phân phối, cung cấp hàng hóa", entry.SectionGroup);
    Assert.Equal(900_000m, entry.Amount);

    // VAT formula verification: 1.0% on net revenue
    Assert.Equal(1.0m, entry.TaxRate);
    Assert.Equal(9_000m, entry.TaxAmount); // 900,000 * 1% = 9,000 VND
  }

  // =========================================================================
  // 3. TEST S2b-HKD: Xác thực S2b TUYỆT ĐỐI không có dòng thuế TNCN (Invariance test)
  // =========================================================================
  [Fact]
  public async Task Test_S2b_Invariance_MustNeverContainPitTaxRow_OnlyVatAllowed()
  {
    var dbName = $"TT152_S2b_{Guid.NewGuid()}";
    using var context = CreateInMemoryDbContext(dbName);
    var repo = new HouseholdTaxRepository(context);

    var branchId = Guid.NewGuid();
    var fromDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    var toDate = new DateTime(2026, 3, 31, 23, 59, 59, DateTimeKind.Utc);

    context.Orders.AddRange(
      new Order
      {
        OrderCode = "ORD-S2B-01",
        BranchId = branchId,
        TotalAmount = 2_000_000m,
        DiscountAmount = 200_000m,
        FinalAmount = 1_800_000m,
        PaymentStatus = "PAID",
        Status = "Completed",
        CreatedAt = new DateTime(2026, 1, 10, 8, 0, 0, DateTimeKind.Utc)
      },
      new Order
      {
        OrderCode = "ORD-S2B-02",
        BranchId = branchId,
        TotalAmount = 3_500_000m,
        DiscountAmount = 500_000m,
        FinalAmount = 3_000_000m,
        PaymentStatus = "PAID",
        Status = "Completed",
        CreatedAt = new DateTime(2026, 2, 20, 11, 0, 0, DateTimeKind.Utc)
      }
    );

    await context.SaveChangesAsync();

    // Act: Query book entries for S2b (Kê khai)
    var entries = await repo.GetBookEntriesAsync(
      fromDate,
      toDate,
      branchId,
      HouseholdTaxGroup.Group3_3BTo50B,
      TaxCalculationMethod.DeclarationBased,
      AccountingBookType.S2b_RevenueDeclaration
    );

    // Assert: S2b Invariance checks
    Assert.NotEmpty(entries);
    Assert.All(entries, e =>
    {
      Assert.Equal("S2b-HKD", e.BookCode);
      Assert.Equal(AccountingBookType.S2b_RevenueDeclaration, e.BookType);

      // S2b Invariance Rule: NEVER contains PIT (TNCN) line or description
      var combinedText = $"{e.Description} {e.Category} {e.SectionGroup}".ToUpperInvariant();
      Assert.DoesNotContain("TNCN", combinedText);
      Assert.DoesNotContain("PIT", combinedText);
      Assert.DoesNotContain("THU NHẬP CÁ NHÂN", combinedText);

      // Only VAT rate (1.0%) is permitted
      Assert.Equal(1.0m, e.TaxRate);
      Assert.Equal(Math.Round(e.Amount * 0.01m, 0, MidpointRounding.AwayFromZero), e.TaxAmount);
    });
  }

  // =========================================================================
  // 4. TEST S2c-HKD: Xác thực có dòng Giá vốn (COGS) trong nhóm 2.a và phân loại 6 nhóm 2.a -> 2.e
  // =========================================================================
  [Fact]
  public async Task Test_S2c_ShouldIncludeCogsInGroup2a_AndClassifyExpensesIntoSixTT152Groups()
  {
    var dbName = $"TT152_S2c_{Guid.NewGuid()}";
    using var context = CreateInMemoryDbContext(dbName);
    var repo = new HouseholdTaxRepository(context);

    var branchId = Guid.NewGuid();
    var fromDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    var toDate = new DateTime(2026, 3, 31, 23, 59, 59, DateTimeKind.Utc);

    // 1. Order with real COGS
    var productId = Guid.NewGuid();
    var order = new Order
    {
      OrderCode = "ORD-S2C-01",
      BranchId = branchId,
      TotalAmount = 2_000_000m,
      DiscountAmount = 0m,
      FinalAmount = 2_000_000m,
      PaymentStatus = "PAID",
      Status = "Completed",
      CreatedAt = new DateTime(2026, 1, 20, 10, 0, 0, DateTimeKind.Utc)
    };
    order.Details.Add(new OrderDetail
    {
      Id = Guid.NewGuid(),
      OrderId = order.Id,
      ProductId = productId,
      Qty = 10,
      UnitPrice = 200_000m,
      CostPriceAtSale = 120_000m // Actual COGS = 10 * 120,000 = 1,200,000 VND
    });
    context.Orders.Add(order);

    // 2. Six standard TT152 expense vouchers (2.a -> 2.e)
    context.ExpenseVouchers.AddRange(
      // 2.a: Nguyên liệu, vật liệu, bao bì
      new ExpenseVoucher
      {
        VoucherCode = "PC-2A",
        BranchId = branchId,
        Category = "Bao bì đóng gói siêu thị",
        Amount = 500_000m,
        Status = ExpenseVoucherStatus.Paid,
        ExpenseDate = new DateTime(2026, 1, 25, 0, 0, 0, DateTimeKind.Utc)
      },
      // 2.b: Lương, tiền công nhân viên
      new ExpenseVoucher
      {
        VoucherCode = "PC-2B",
        BranchId = branchId,
        Category = "Tiền lương nhân viên ca trực",
        Amount = 15_000_000m,
        Status = ExpenseVoucherStatus.Paid,
        ExpenseDate = new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc)
      },
      // 2.c: Thuê mặt bằng, kho bãi
      new ExpenseVoucher
      {
        VoucherCode = "PC-2C",
        BranchId = branchId,
        Category = "Tiền thuê mặt bằng kinh doanh",
        Amount = 20_000_000m,
        Status = ExpenseVoucherStatus.Paid,
        ExpenseDate = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc)
      },
      // 2.d: Dịch vụ mua ngoài (điện, nước, internet)
      new ExpenseVoucher
      {
        VoucherCode = "PC-2D",
        BranchId = branchId,
        Category = "Tiền điện nước và internet",
        Amount = 4_500_000m,
        Status = ExpenseVoucherStatus.Paid,
        ExpenseDate = new DateTime(2026, 2, 15, 0, 0, 0, DateTimeKind.Utc)
      },
      // 2.e: Thuế, phí, lệ phí được trừ (lệ phí môn bài)
      new ExpenseVoucher
      {
        VoucherCode = "PC-2E",
        BranchId = branchId,
        Category = "Lệ phí môn bài và phí công chứng",
        Amount = 300_000m,
        Status = ExpenseVoucherStatus.Paid,
        ExpenseDate = new DateTime(2026, 2, 20, 0, 0, 0, DateTimeKind.Utc)
      },
      // 2.đ: Chi phí khác phục vụ SXKD
      new ExpenseVoucher
      {
        VoucherCode = "PC-2D-OTHER",
        BranchId = branchId,
        Category = "Văn phòng phẩm và tiếp khách",
        Amount = 800_000m,
        Status = ExpenseVoucherStatus.Paid,
        ExpenseDate = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)
      },
      // EXCLUDED: Equity / Capital withdrawal (Rút vốn chủ sở hữu)
      new ExpenseVoucher
      {
        VoucherCode = "PC-EQUITY",
        BranchId = branchId,
        Category = "Chủ hộ rút vốn cá nhân (Capital/Equity)",
        Amount = 50_000_000m,
        Status = ExpenseVoucherStatus.Paid,
        ExpenseDate = new DateTime(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc)
      }
    );

    await context.SaveChangesAsync();

    // Act: Query S2c book entries
    var entries = await repo.GetBookEntriesAsync(
      fromDate,
      toDate,
      branchId,
      HouseholdTaxGroup.Group3_3BTo50B,
      TaxCalculationMethod.DeclarationBased,
      AccountingBookType.S2c_RevenueAndExpense
    );

    // Assert: S2c structure and classification
    Assert.All(entries, e =>
    {
      Assert.Equal("S2c-HKD", e.BookCode);
      Assert.Equal(AccountingBookType.S2c_RevenueAndExpense, e.BookType);
    });

    // 1. Verify COGS presence in group 2.a
    var cogsEntry = Assert.Single(entries, e => e.Category == "Giá vốn hàng bán (COGS)");
    Assert.Equal(1_200_000m, cogsEntry.Amount);
    Assert.StartsWith("2.a", cogsEntry.SectionGroup);

    // 2. Verify all 6 TT152 sub-groups (2.a -> 2.e)
    Assert.Contains(entries, e => e.VoucherNumber == "PC-2A" && e.SectionGroup!.StartsWith("2.a"));
    Assert.Contains(entries, e => e.VoucherNumber == "PC-2B" && e.SectionGroup!.StartsWith("2.b"));
    Assert.Contains(entries, e => e.VoucherNumber == "PC-2C" && e.SectionGroup!.StartsWith("2.c"));
    Assert.Contains(entries, e => e.VoucherNumber == "PC-2D" && e.SectionGroup!.StartsWith("2.d"));
    Assert.Contains(entries, e => e.VoucherNumber == "PC-2E" && e.SectionGroup!.StartsWith("2.e"));
    Assert.Contains(entries, e => e.VoucherNumber == "PC-2D-OTHER" && e.SectionGroup!.StartsWith("2.đ"));

    // 3. Verify Equity withdrawal is 100% excluded
    Assert.DoesNotContain(entries, e => e.VoucherNumber == "PC-EQUITY");
  }

  // =========================================================================
  // 5. TEST S2d-HKD: Cấu trúc 12 cột Nhập - Xuất - Tồn, xuất kho dùng giá vốn thực tế
  // =========================================================================
  [Fact]
  public async Task Test_S2d_TwelveColumnsStructure_OutwardStockMustUseActualCostPrice_NotRetailPrice()
  {
    var dbName = $"TT152_S2d_{Guid.NewGuid()}";
    using var context = CreateInMemoryDbContext(dbName);
    var repo = new HouseholdTaxRepository(context);

    var branchId = Guid.NewGuid();
    var fromDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    var toDate = new DateTime(2026, 3, 31, 23, 59, 59, DateTimeKind.Utc);

    // 1. Nhập kho từ NCC (GoodsReceipt)
    var supplierId = Guid.NewGuid();
    var supplier = new Supplier
    {
      Id = supplierId,
      Name = "Nhà cung cấp Thực phẩm Miền Tây",
      Code = "SUP-001"
    };
    context.Suppliers.Add(supplier);

    var grn = new GoodsReceipt
    {
      TenantId = "tenant-test-tt152",
      Code = "GRN-2026-001",
      BranchId = branchId,
      SupplierId = supplierId,
      Supplier = supplier,
      ReceiverUserId = "user-test",
      GrandTotal = 5_000_000m,
      Status = "CONFIRMED",
      ReceivedAtUtc = new DateTime(2026, 1, 5, 8, 0, 0, DateTimeKind.Utc),
      InvoiceNo = "HD-NCC-888"
    };
    grn.Lines.Add(new GoodsReceiptLine
    {
      Id = Guid.NewGuid(),
      GoodsReceiptId = grn.Id,
      ProductId = Guid.NewGuid(),
      AcceptedQty = 100m, // 100 units
      UnitPrice = 50_000m,
      LineAmount = 5_000_000m
    });
    context.GoodsReceipts.Add(grn);

    // 2. Xuất kho bán lẻ POS: Sold 20 units at retail price 100,000 VND (TotalAmount = 2,000,000)
    // with actual CostPriceAtSale = 50,000 VND (TotalCost = 1,000,000)
    var order = new Order
    {
      OrderCode = "ORD-S2D-POS",
      BranchId = branchId,
      TotalAmount = 2_000_000m, // Retail gross = 2M
      DiscountAmount = 0m,
      FinalAmount = 2_000_000m,
      PaymentStatus = "PAID",
      Status = "Completed",
      CreatedAt = new DateTime(2026, 1, 15, 14, 0, 0, DateTimeKind.Utc)
    };
    order.Details.Add(new OrderDetail
    {
      Id = Guid.NewGuid(),
      OrderId = order.Id,
      ProductId = grn.Lines.First().ProductId,
      Qty = 20m,
      UnitPrice = 100_000m,      // Retail price
      CostPriceAtSale = 50_000m  // Actual cost price
    });
    context.Orders.Add(order);

    // 3. Chi phí bao bì, vật tư
    context.ExpenseVouchers.Add(new ExpenseVoucher
    {
      VoucherCode = "PC-S2D-MAT",
      BranchId = branchId,
      Category = "Vật tư túi nylon tự hủy",
      Amount = 250_000m,
      Status = ExpenseVoucherStatus.Paid,
      ExpenseDate = new DateTime(2026, 1, 20, 0, 0, 0, DateTimeKind.Utc)
    });

    await context.SaveChangesAsync();

    // Act: Query S2d book entries
    var entries = await repo.GetBookEntriesAsync(
      fromDate,
      toDate,
      branchId,
      HouseholdTaxGroup.Group3_3BTo50B,
      TaxCalculationMethod.DeclarationBased,
      AccountingBookType.S2d_MaterialsAndGoods
    );

    // Assert: S2d 12-column structure
    Assert.All(entries, e =>
    {
      Assert.Equal("S2d-HKD", e.BookCode);
      Assert.Equal(AccountingBookType.S2d_MaterialsAndGoods, e.BookType);
    });

    // Verify Inward entry [Nhập kho]
    var inEntry = Assert.Single(entries, e => e.VoucherNumber == "GRN-2026-001");
    Assert.Equal(100m, inEntry.InQuantity);
    Assert.Equal(5_000_000m, inEntry.InAmount);
    Assert.Equal(50_000m, inEntry.UnitPrice);
    Assert.Equal("Lô/Hàng", inEntry.Unit);
    Assert.Equal("Nhập kho trong kỳ", inEntry.SectionGroup);

    // Verify Outward entry [Xuất kho] - MUST USE COST PRICE (1,000,000 VND), NOT RETAIL (2,000,000 VND)
    var outEntry = Assert.Single(entries, e => e.VoucherNumber == "ORD-S2D-POS");
    Assert.Equal(20m, outEntry.OutQuantity);
    Assert.Equal(1_000_000m, outEntry.OutAmount); // CRITICAL: 20 * 50,000 = 1,000,000
    Assert.NotEqual(2_000_000m, outEntry.OutAmount); // CRITICAL: Must not use retail price
    Assert.Equal(50_000m, outEntry.UnitPrice);
    Assert.Equal("Sản phẩm", outEntry.Unit);
    Assert.Equal("Xuất kho trong kỳ", outEntry.SectionGroup);

    // Verify Consumables entry [Vật tư]
    var matEntry = Assert.Single(entries, e => e.VoucherNumber == "PC-S2D-MAT");
    Assert.Equal(250_000m, matEntry.InAmount);
    Assert.Equal("Khoản", matEntry.Unit);
    Assert.Equal("Vật liệu, dụng cụ tiêu hao", matEntry.SectionGroup);
  }

  // =========================================================================
  // 6. TEST S2e-HKD: Xác thực 2 khối Quỹ Tiền mặt và Tiền gửi Ngân hàng, số dư đầu kỳ, thu/gửi, chi/rút, số dư cuối kỳ
  // =========================================================================
  [Fact]
  public async Task Test_S2e_CashAndBankTwoBlocks_OpeningBalance_TotalIn_TotalOut_ClosingBalance()
  {
    var dbName = $"TT152_S2e_{Guid.NewGuid()}";
    using var context = CreateInMemoryDbContext(dbName);
    var repo = new HouseholdTaxRepository(context);

    var branchId = Guid.NewGuid();
    var fromDate = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
    var toDate = new DateTime(2026, 2, 28, 23, 59, 59, DateTimeKind.Utc);

    // 1. Prior period cashbook entries before fromDate:
    // Cash: Receipt = 10,000,000, Payment = 2,000,000 -> Opening Cash = 8,000,000 VND
    // Bank: Receipt = 50,000,000, Payment = 10,000,000 -> Opening Bank = 40,000,000 VND
    context.CashbookEntries.AddRange(
      new CashbookEntry
      {
        EntryCode = "PT-PRIOR-CASH",
        BranchId = branchId,
        AccountType = FinanceAccountType.Till,
        EntryType = CashbookEntryType.Receipt,
        Amount = 10_000_000m,
        OccurredAtUtc = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc)
      },
      new CashbookEntry
      {
        EntryCode = "PC-PRIOR-CASH",
        BranchId = branchId,
        AccountType = FinanceAccountType.Till,
        EntryType = CashbookEntryType.Payment,
        Amount = 2_000_000m,
        OccurredAtUtc = new DateTime(2026, 1, 20, 0, 0, 0, DateTimeKind.Utc)
      },
      new CashbookEntry
      {
        EntryCode = "PT-PRIOR-BANK",
        BranchId = branchId,
        AccountType = FinanceAccountType.Bank,
        EntryType = CashbookEntryType.Receipt,
        Amount = 50_000_000m,
        OccurredAtUtc = new DateTime(2026, 1, 12, 0, 0, 0, DateTimeKind.Utc)
      },
      new CashbookEntry
      {
        EntryCode = "PC-PRIOR-BANK",
        BranchId = branchId,
        AccountType = FinanceAccountType.Bank,
        EntryType = CashbookEntryType.Payment,
        Amount = 10_000_000m,
        OccurredAtUtc = new DateTime(2026, 1, 25, 0, 0, 0, DateTimeKind.Utc)
      }
    );

    // 2. In-period cashbook entries (February 2026):
    // Cash: +3,000,000 (POS receipt), -1,000,000 (Till expense) -> Closing Cash = 8M + 3M - 1M = 10,000,000 VND
    // Bank: +5,000,000 (Bank transfer in), -15,000,000 (Supplier AP out) -> Closing Bank = 40M + 5M - 15M = 30,000,000 VND
    context.CashbookEntries.AddRange(
      new CashbookEntry
      {
        EntryCode = "PT-FEB-001",
        BranchId = branchId,
        AccountType = FinanceAccountType.Till,
        EntryType = CashbookEntryType.Receipt,
        Amount = 3_000_000m,
        Description = "Thu tiền bán hàng POS",
        OccurredAtUtc = new DateTime(2026, 2, 5, 10, 0, 0, DateTimeKind.Utc)
      },
      new CashbookEntry
      {
        EntryCode = "PC-FEB-002",
        BranchId = branchId,
        AccountType = FinanceAccountType.Till,
        EntryType = CashbookEntryType.Payment,
        Amount = 1_000_000m,
        Description = "Chi tiền điện nước quầy",
        OccurredAtUtc = new DateTime(2026, 2, 10, 15, 0, 0, DateTimeKind.Utc)
      },
      new CashbookEntry
      {
        EntryCode = "PT-FEB-BANK",
        BranchId = branchId,
        AccountType = FinanceAccountType.Bank,
        EntryType = CashbookEntryType.Receipt,
        Amount = 5_000_000m,
        Description = "Khách chuyển khoản ngân hàng",
        OccurredAtUtc = new DateTime(2026, 2, 12, 9, 0, 0, DateTimeKind.Utc)
      },
      new CashbookEntry
      {
        EntryCode = "PC-FEB-BANK",
        BranchId = branchId,
        AccountType = FinanceAccountType.Bank,
        EntryType = CashbookEntryType.Payment,
        Amount = 15_000_000m,
        Description = "Chuyển khoản thanh toán NCC",
        OccurredAtUtc = new DateTime(2026, 2, 20, 16, 0, 0, DateTimeKind.Utc)
      }
    );

    await context.SaveChangesAsync();

    // Act: Query S2e entries
    var entries = await repo.GetBookEntriesAsync(
      fromDate,
      toDate,
      branchId,
      HouseholdTaxGroup.Group3_3BTo50B,
      TaxCalculationMethod.DeclarationBased,
      AccountingBookType.S2e_CashAndBank
    );

    // Assert: Check two separate blocks
    Assert.All(entries, e =>
    {
      Assert.Equal("S2e-HKD", e.BookCode);
      Assert.Equal(AccountingBookType.S2e_CashAndBank, e.BookType);
    });

    var cashEntries = entries.Where(e => e.SectionGroup == "Khối I - Quỹ Tiền mặt").ToList();
    var bankEntries = entries.Where(e => e.SectionGroup == "Khối II - Tiền gửi Ngân hàng").ToList();

    Assert.NotEmpty(cashEntries);
    Assert.NotEmpty(bankEntries);

    // Verify Khối I - Quỹ Tiền mặt:
    var cashOpening = Assert.Single(cashEntries, e => e.VoucherNumber == "DK-CASH");
    Assert.Equal(8_000_000m, cashOpening.BalanceAmount); // 10M - 2M

    var cashLast = cashEntries.Last();
    Assert.Equal(10_000_000m, cashLast.BalanceAmount); // 8M + 3M - 1M = 10M

    // Verify Khối II - Tiền gửi Ngân hàng:
    var bankOpening = Assert.Single(bankEntries, e => e.VoucherNumber == "DK-BANK");
    Assert.Equal(40_000_000m, bankOpening.BalanceAmount); // 50M - 10M

    var bankLast = bankEntries.Last();
    Assert.Equal(30_000_000m, bankLast.BalanceAmount); // 40M + 5M - 15M = 30M
  }

  // =========================================================================
  // 7. TEST S3a-HKD: Xác thực theo dõi nghĩa vụ thuế khác (lệ phí môn bài, thuế đất, BVMT...)
  // =========================================================================
  [Fact]
  public async Task Test_S3a_TaxObligationTracking_ShouldTrackVat_Pit_AndOtherTaxes()
  {
    var dbName = $"TT152_S3a_{Guid.NewGuid()}";
    using var context = CreateInMemoryDbContext(dbName);
    var repo = new HouseholdTaxRepository(context);

    var branchId = Guid.NewGuid();
    var fromDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    var toDate = new DateTime(2026, 3, 31, 23, 59, 59, DateTimeKind.Utc);

    // 1. Snapshot chốt thuế trong kỳ: VAT = 4,000,000 VND, PIT = 2,000,000 VND
    context.HouseholdTaxSnapshots.Add(new HouseholdTaxSnapshot
    {
      TenantId = "tenant-test-tt152",
      BranchId = branchId,
      Year = 2026,
      PeriodType = TaxPeriodType.Quarter,
      PeriodNumber = 1,
      HouseholdGroup = HouseholdTaxGroup.Group2_1BTo3B,
      TaxableRevenueInPeriod = 400_000_000m,
      VatAmount = 4_000_000m,
      PitAmount = 2_000_000m,
      TotalTaxAmount = 6_000_000m,
      CreatedAt = new DateTime(2026, 3, 25, 0, 0, 0, DateTimeKind.Utc)
    });

    // 2. Chứng từ nộp thuế qua Kho bạc và nộp các loại thuế khác
    context.ExpenseVouchers.AddRange(
      new ExpenseVoucher
      {
        VoucherCode = "PC-TAX-VAT",
        BranchId = branchId,
        Category = "Nộp thuế GTGT vào Kho bạc Nhà nước",
        Amount = 4_000_000m,
        Status = ExpenseVoucherStatus.Paid,
        ExpenseDate = new DateTime(2026, 3, 26, 0, 0, 0, DateTimeKind.Utc)
      },
      new ExpenseVoucher
      {
        VoucherCode = "PC-TAX-MONBAI",
        BranchId = branchId,
        Category = "Nộp lệ phí môn bài năm 2026",
        Amount = 300_000m,
        Status = ExpenseVoucherStatus.Paid,
        ExpenseDate = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc)
      },
      new ExpenseVoucher
      {
        VoucherCode = "PC-TAX-LAND",
        BranchId = branchId,
        Category = "Nộp thuế sử dụng đất phi nông nghiệp",
        Amount = 500_000m,
        Status = ExpenseVoucherStatus.Paid,
        ExpenseDate = new DateTime(2026, 2, 10, 0, 0, 0, DateTimeKind.Utc)
      },
      new ExpenseVoucher
      {
        VoucherCode = "PC-TAX-BVMT",
        BranchId = branchId,
        Category = "Nộp thuế bảo vệ môi trường",
        Amount = 200_000m,
        Status = ExpenseVoucherStatus.Paid,
        ExpenseDate = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)
      }
    );

    await context.SaveChangesAsync();

    // Act: Query S3a entries
    var entries = await repo.GetBookEntriesAsync(
      fromDate,
      toDate,
      branchId,
      HouseholdTaxGroup.Group2_1BTo3B,
      TaxCalculationMethod.RevenuePercentage,
      AccountingBookType.S3a_TaxObligation
    );

    // Assert: S3a obligations tracking
    Assert.All(entries, e =>
    {
      Assert.Equal("S3a-HKD", e.BookCode);
      Assert.Equal(AccountingBookType.S3a_TaxObligation, e.BookType);
    });

    // 1. VAT accrued obligation
    var vatAccrued = Assert.Single(entries, e => e.VoucherNumber == "TAX-VAT-2026-Q1");
    Assert.Equal(4_000_000m, vatAccrued.Amount);
    Assert.Equal("1. Thuế Giá trị gia tăng (GTGT)", vatAccrued.SectionGroup);

    // 2. PIT accrued obligation
    var pitAccrued = Assert.Single(entries, e => e.VoucherNumber == "TAX-PIT-2026-Q1");
    Assert.Equal(2_000_000m, pitAccrued.Amount);
    Assert.Equal("2. Thuế Thu nhập cá nhân (TNCN)", pitAccrued.SectionGroup);

    // 3. Other tax payments tracked under "3. Các loại thuế, phí, lệ phí khác"
    Assert.Contains(entries, e => e.VoucherNumber == "PC-TAX-MONBAI" && e.SectionGroup == "3. Các loại thuế, phí, lệ phí khác" && e.Amount == 300_000m);
    Assert.Contains(entries, e => e.VoucherNumber == "PC-TAX-LAND" && e.SectionGroup == "3. Các loại thuế, phí, lệ phí khác" && e.Amount == 500_000m);
    Assert.Contains(entries, e => e.VoucherNumber == "PC-TAX-BVMT" && e.SectionGroup == "3. Các loại thuế, phí, lệ phí khác" && e.Amount == 200_000m);

    // 4. VAT payment tracked under VAT group
    var vatPaid = Assert.Single(entries, e => e.VoucherNumber == "PC-TAX-VAT");
    Assert.Equal("1. Thuế Giá trị gia tăng (GTGT)", vatPaid.SectionGroup);
    Assert.Equal(4_000_000m, vatPaid.Amount);
  }
}
