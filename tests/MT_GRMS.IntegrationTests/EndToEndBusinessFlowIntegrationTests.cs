using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Cashbook;
using MT_GRMS.Application.Catalog;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Common.Services;
using MT_GRMS.Application.Debt;
using MT_GRMS.Application.Debt.Dtos;
using MT_GRMS.Application.Expenses;
using MT_GRMS.Application.Expenses.Dtos;
using MT_GRMS.Application.PosOrders;
using MT_GRMS.Application.PosOrders.Dtos;
using MT_GRMS.Application.Tax;
using MT_GRMS.Application.Tax.Dtos;
using MT_GRMS.Application.Tax.Security;
using MT_GRMS.Application.Tax.Strategies;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Entities.Inventory;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Domain.Entities.Sales;
using MT_GRMS.Domain.Entities.Supplier;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Domain.Exceptions;
using MT_GRMS.Infrastructure.Persistence;
using MT_GRMS.Infrastructure.Repositories;
using Xunit;

namespace MT_GRMS.IntegrationTests;

/// <summary>
/// Bộ kiểm thử tích hợp End-to-End xuyên suốt chuỗi giá trị 5 phân hệ cốt lõi MT-GRMS:
/// FE-8 (Nhập hàng NCC) -> FE-7 (Bán hàng POS) -> FE-10 (Dòng tiền Cashbook, Công nợ, Chi phí) 
/// -> FE-17 (Hóa đơn điện tử & Sổ quỹ) -> FE-19 (Thuế & 7 Sổ kế toán TT152/2025/TT-BTC).
/// </summary>
public class EndToEndBusinessFlowIntegrationTests
{
    private static readonly Guid DefaultTillId = Guid.Parse("01a19664-6210-7b12-a9eb-6fa16ca5b9db");
    private static readonly Guid DefaultBankId = Guid.Parse("01a19664-6741-7f3d-9be7-cb4c7b876559");

    private class FakeTenantContext : ITenantContext
    {
        public string CurrentTenantId { get; set; } = "tenant-e2e-retail-01";
        public Guid CurrentUserId { get; set; } = Guid.Parse("22222222-2222-2222-2222-222222222222");
        public Guid? CurrentBranchId { get; set; } = null;
        public string CurrentRole { get; set; } = "STOREOWNER";

        public void SetContext(string tenantId, Guid userId, Guid? branchId)
        {
            CurrentTenantId = tenantId;
            CurrentUserId = userId;
            CurrentBranchId = branchId;
        }

        public void SetRole(string? role) => CurrentRole = role?.Trim() ?? string.Empty;
    }

    private class FakeThermalPrintService : IThermalPrintService
    {
        public bool PrintOrderInvoice(Order order, string printerName = "POS-58", decimal? cashReceived = null, decimal? changeAmount = null, string? cashierName = null) => true;
        public byte[] BuildEscPosReceipt(Order order, decimal? cashReceived = null, decimal? changeAmount = null, string? cashierName = null) => Array.Empty<byte>();
    }

    private AppDbContext CreateDbContext(string dbName, FakeTenantContext? tenantContext = null)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;

        return new AppDbContext(options, tenantContext ?? new FakeTenantContext());
    }

    private async Task SeedBaseDataAsync(AppDbContext context, Guid branchId, Guid prodAId, Guid supplierId)
    {
        // 1. Chi nhánh
        context.Branches.Add(new Branch
        {
            Id = branchId,
            BranchCode = "CN01",
            Name = "Chi nhánh Quận 1",
            Address = "123 Lê Lợi, P. Bến Nghé, Quận 1, TP.HCM",
            Phone = "0901234567"
        });

        // 2. Danh mục & Sản phẩm A
        var category = new Category
        {
            Id = Guid.NewGuid(),
            CategoryCode = "CAT-DAIRY",
            Name = "Sữa và chế phẩm sữa"
        };
        context.Categories.Add(category);

        var prodA = new Product
        {
            Id = prodAId,
            ProductCode = "SP-A",
            Name = "Sản phẩm A (Sữa tươi Vinamilk 1L)",
            CategoryId = category.Id,
            BaseUnit = "Hộp",
            CostPrice = 10_000m,   // Giá nhập 10.000 đ
            BasePrice = 15_000m,   // Giá bán 15.000 đ
            VatRate = 0.01m,       // 1% TT152 tạp hóa
            HasBatches = false
        };
        context.Products.Add(prodA);

        // 3. Nhà cung cấp
        context.Suppliers.Add(new Domain.Entities.Supplier.Supplier
        {
            Id = supplierId,
            Code = "NCC-01",
            Name = "Công ty Cổ phần Sữa Việt Nam (Vinamilk)",
            Phone = "02854155555",
            Email = "contact@vinamilk.com.vn",
            CurrentDebt = 0m,
            CreditLimit = 50_000_000m
        });

        // 4. Tài khoản két quầy & tài khoản ngân hàng
        context.FinanceAccounts.AddRange(
            new FinanceAccount
            {
                Id = DefaultTillId,
                BranchId = branchId,
                AccountName = "Két quầy thu ngân CN1",
                AccountType = FinanceAccountType.Till,
                CurrentBalance = 0m,
                IsActive = true
            },
            new FinanceAccount
            {
                Id = DefaultBankId,
                BranchId = branchId,
                AccountName = "Tài khoản Vietcombank CN1",
                AccountType = FinanceAccountType.Bank,
                CurrentBalance = 0m,
                IsActive = true
            }
        );

        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task EndToEnd_CompleteBusinessFlow_FE8_FE7_FE10_FE17_FE19_ShouldPass100Percent()
    {
        // =========================================================================================
        // KỊCH BẢN KIỂM THỬ TÍCH HỢP TOÀN DIỆN CHUỖI GIÁ TRỊ DOANH NGHIỆP:
        // Bước 1: Nhập kho 100 sản phẩm A (giá nhập 10.000 đ) qua GoodsReceipt -> Tăng tồn kho, phát sinh công nợ NCC
        // Bước 2: Bán lẻ 20 sản phẩm A (giá bán 15.000 đ, chiết khấu 10.000 đ) qua POS Order -> Giảm tồn kho, lưu CostPriceAtSale, thu tiền mặt Sổ quỹ
        // Bước 3: Tạo phiếu chi tiền điện 50.000 đ qua ExpenseVoucher -> Trừ quỹ tiền mặt Sổ quỹ
        // Bước 4: Kiểm tra sổ S2c-HKD -> Doanh thu = 290.000 đ, Chi phí = 250.000 đ (200.000 COGS + 50.000 điện), Lãi = 40.000 đ
        // Bước 5: Kiểm tra sổ S2d-HKD -> Nhập 100, Xuất 20, Tồn cuối 80 sản phẩm (Giá trị tồn 800.000 đ)
        // Bước 6: Kiểm tra sổ S2e-HKD -> Thu bán hàng vào quỹ (290.000 đ), chi tiền điện ra khỏi quỹ (50.000 đ), số dư khớp 100% (240.000 đ)
        // =========================================================================================

        var dbName = $"MT_GRMS_E2E_FullFlow_{Guid.NewGuid()}";
        var tenantContext = new FakeTenantContext();
        using var context = CreateDbContext(dbName, tenantContext);

        var branchId = Guid.NewGuid();
        var prodAId = Guid.NewGuid();
        var supplierId = Guid.NewGuid();

        await SeedBaseDataAsync(context, branchId, prodAId, supplierId);

        var taxRepo = new HouseholdTaxRepository(context);

        // Kỳ tính thuế Q1/2026: 01/01/2026 -> 31/03/2026
        var fromDateUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var toDateUtc = new DateTime(2026, 3, 31, 23, 59, 59, DateTimeKind.Utc);
        var testTxDate = new DateTime(2026, 2, 10, 8, 30, 0, DateTimeKind.Utc);

        // -----------------------------------------------------------------------------------------
        // BƯỚC 1: NHẬP KHO 100 SẢN PHẨM A (GIÁ NHẬP 10.000 Đ) QUA GOODSRECEIPT (FE-8)
        // -----------------------------------------------------------------------------------------
        var receipt = new GoodsReceipt
        {
            Id = Guid.NewGuid(),
            Code = "GRN-20260210-001",
            SupplierId = supplierId,
            BranchId = branchId,
            Status = "COMPLETED",
            ReceivedAtUtc = testTxDate,
            SubTotal = 1_000_000m,      // 100 * 10.000 đ
            FreightFee = 0m,
            DiscountAmount = 0m,
            VatAmount = 0m,
            GrandTotal = 1_000_000m,
            PaymentMethod = "AP",
            CreatedAt = testTxDate,
            Lines = new List<GoodsReceiptLine>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    ProductId = prodAId,
                    OrderedQty = 100,
                    DeliveredQty = 100,
                    AcceptedQty = 100,
                    RejectedQty = 0,
                    UnitPrice = 10_000m,
                    LineAmount = 1_000_000m,
                    LandedUnitCost = 10_000m,
                    QualityStatus = "ACCEPTED",
                    CreatedAt = testTxDate
                }
            }
        };

        // Tăng tồn kho chi nhánh BranchStock
        var branchStock = new BranchStock
        {
            Id = Guid.NewGuid(),
            BranchId = branchId,
            ProductId = prodAId,
            SupplierId = supplierId,
            QuantityOnHand = 100,
            QuantityReserved = 0,
            LandedCostPerUnit = 10_000m,
            RetailPrice = 15_000m,
            Status = "Active",
            CreatedAt = testTxDate
        };

        // Ghi nhận công nợ nhà cung cấp AP Ledger
        var supplierDebt = new SupplierAPLedger
        {
            Id = Guid.NewGuid(),
            SupplierId = supplierId,
            DocType = APDocType.PurchaseInvoice,
            DocNo = receipt.Code,
            Description = $"Nhập kho đơn hàng {receipt.Code}",
            Amount = 1_000_000m,
            Direction = 1,              // Tăng công nợ phải trả NCC
            BalanceAfter = 1_000_000m,
            TransactionDate = testTxDate,
            CreatedByName = "Admin Kho",
            CreatedAt = testTxDate
        };

        context.GoodsReceipts.Add(receipt);
        context.BranchStocks.Add(branchStock);
        context.SupplierAPLedgers.Add(supplierDebt);
        await context.SaveChangesAsync();

        // Kiểm tra xác nhận Bước 1
        Assert.Equal(100m, branchStock.QuantityOnHand);
        Assert.Equal(1_000_000m, receipt.GrandTotal);
        Assert.Equal(1_000_000m, supplierDebt.Amount);

        // -----------------------------------------------------------------------------------------
        // BƯỚC 2: BÁN LẺ 20 SẢN PHẨM A (GIÁ BÁN 15.000 Đ, CHIẾT KHẤU 10.000 Đ) QUA POS (FE-7)
        // Doanh thu thuần = 20 * 15.000 - 10.000 = 290.000 đ
        // Giá vốn hàng bán (COGS) = 20 * 10.000 = 200.000 đ
        // -----------------------------------------------------------------------------------------
        var orderDate = testTxDate.AddHours(2);
        var order = new Order
        {
            Id = Guid.NewGuid(),
            OrderCode = "HD202602100001",
            BranchId = branchId,
            OrderDate = orderDate,
            TotalAmount = 300_000m,         // 20 * 15.000 đ
            DiscountAmount = 10_000m,       // Chiết khấu khuyến mãi 10.000 đ
            FinalAmount = 290_000m,         // Doanh thu thuần 290.000 đ
            DepositAmount = 290_000m,
            PaymentMethod = "CASH",
            PaymentStatus = "PAID",
            Status = "Completed",
            CreatedAt = orderDate,
            Details = new List<OrderDetail>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    ProductId = prodAId,
                    LineNumber = 1,
                    Qty = 20m,
                    UnitPrice = 15_000m,
                    DiscountAmount = 10_000m,
                    ActualPrice = 14_500m,
                    CostPriceAtSale = 10_000m, // Đóng băng giá vốn 10.000 đ chống méo mó hồi tố
                    AppliedVatRate = 0.01m,
                    VatAmount = 2_900m,
                    CreatedAt = orderDate
                }
            }
        };

        // Giảm tồn kho theo FEFO: 100 - 20 = 80
        branchStock.QuantityOnHand -= 20m;
        branchStock.UpdatedAt = orderDate;

        // Ghi nhận dòng tiền thu bán hàng tiền mặt vào Sổ quỹ (FE-17)
        var cashbookSale = new CashbookEntry
        {
            Id = Guid.NewGuid(),
            EntryCode = "PT-POS-001",
            AccountId = DefaultTillId,
            AccountType = FinanceAccountType.Till,
            BranchId = branchId,
            EntryType = CashbookEntryType.Receipt,
            Amount = 290_000m,
            BalanceAfter = 290_000m,
            SourceType = "POS_SALE",
            SourceId = order.Id,
            ReferenceCode = order.OrderCode,
            AuditHash = "SHA256-HASH-SALE-001",
            Description = $"Doanh thu bán lẻ đơn {order.OrderCode}",
            OccurredAtUtc = orderDate,
            CreatedAt = orderDate
        };

        // Cập nhật số dư két quầy
        var tillAccount = await context.FinanceAccounts.FindAsync(DefaultTillId);
        Assert.NotNull(tillAccount);
        tillAccount.CurrentBalance = 290_000m;

        context.Orders.Add(order);
        context.CashbookEntries.Add(cashbookSale);
        await context.SaveChangesAsync();

        // Kiểm tra xác nhận Bước 2
        Assert.Equal(80m, branchStock.QuantityOnHand);
        Assert.Equal(290_000m, order.FinalAmount);
        Assert.Equal(10_000m, order.Details.First().CostPriceAtSale);
        Assert.Equal(290_000m, cashbookSale.Amount);

        // -----------------------------------------------------------------------------------------
        // BƯỚC 3: TẠO PHIẾU CHI TIỀN ĐIỆN 50.000 Đ QUA EXPENSEVOUCHER (FE-10)
        // Trừ quỹ tiền mặt két quầy CashbookEntries: 290.000 - 50.000 = 240.000 đ
        // -----------------------------------------------------------------------------------------
        var expenseDate = testTxDate.AddHours(4);
        var expenseVoucher = new ExpenseVoucher
        {
            Id = Guid.NewGuid(),
            VoucherCode = "PC-ELEC-202602-01",
            BranchId = branchId,
            BeneficiaryName = "Công ty Điện lực Chợ Lớn TP.HCM",
            Category = "Tiền điện sản xuất kinh doanh",
            Amount = 50_000m,
            Status = ExpenseVoucherStatus.Paid,
            Nature = FinancialNature.OperatingExpense,
            PaymentSourceType = FinanceAccountType.Till,
            PaymentSourceAccountId = DefaultTillId,
            IsTaxDeductible = true,
            InvoiceNumber = "ELEC-INV-9988",
            ExpenseDate = expenseDate,
            CreatedAt = expenseDate,
            Note = "Tiền điện tháng 02/2026 chi nhánh 1"
        };

        // Ghi nhận dòng tiền xuất quỹ Sổ quỹ
        var cashbookExpense = new CashbookEntry
        {
            Id = Guid.NewGuid(),
            EntryCode = "PC-CASH-001",
            AccountId = DefaultTillId,
            AccountType = FinanceAccountType.Till,
            BranchId = branchId,
            EntryType = CashbookEntryType.Payment,
            Amount = 50_000m,
            BalanceAfter = 240_000m,
            SourceType = "EXPENSE",
            SourceId = expenseVoucher.Id,
            ReferenceCode = expenseVoucher.VoucherCode,
            AuditHash = "SHA256-HASH-EXP-001",
            Description = $"Chi tiền điện: {expenseVoucher.Category}",
            OccurredAtUtc = expenseDate,
            CreatedAt = expenseDate
        };

        tillAccount.CurrentBalance -= 50_000m; // 290.000 - 50.000 = 240.000 đ

        context.ExpenseVouchers.Add(expenseVoucher);
        context.CashbookEntries.Add(cashbookExpense);
        await context.SaveChangesAsync();

        // Kiểm tra xác nhận Bước 3
        Assert.Equal(ExpenseVoucherStatus.Paid, expenseVoucher.Status);
        Assert.Equal(240_000m, tillAccount.CurrentBalance);

        // -----------------------------------------------------------------------------------------
        // BƯỚC 4: KIỂM TRA SỔ S2c-HKD (SỔ CHI TIẾT DOANH THU, CHI PHÍ - TT 152/2025/TT-BTC)
        // Doanh thu = 290.000 đ
        // Chi phí = 250.000 đ (200.000 COGS + 50.000 điện)
        // Lãi thuần = 40.000 đ
        // -----------------------------------------------------------------------------------------
        var s2cEntries = await taxRepo.GetBookEntriesAsync(
            fromDateUtc,
            toDateUtc,
            branchId,
            HouseholdTaxGroup.Group3_3BTo50B,
            TaxCalculationMethod.DeclarationBased,
            AccountingBookType.S2c_RevenueAndExpense);

        Assert.NotEmpty(s2cEntries);
        Assert.All(s2cEntries, e => Assert.Equal("S2c-HKD", e.BookCode));

        // 1. Doanh thu bán hàng
        var revenueEntries = s2cEntries.Where(e => e.SectionGroup == "1. Doanh thu").ToList();
        var recordedRevenue = revenueEntries.Sum(e => e.Amount);
        Assert.Equal(290_000m, recordedRevenue);

        // 2. Giá vốn hàng bán COGS
        var cogsEntries = s2cEntries.Where(e => e.Category.Contains("COGS") || e.Category.Contains("Giá vốn")).ToList();
        Assert.Single(cogsEntries);
        var recordedCogs = cogsEntries.First().Amount;
        Assert.Equal(200_000m, recordedCogs);

        // 3. Chi phí tiền điện
        var electricityEntries = s2cEntries.Where(e => e.VoucherNumber == expenseVoucher.VoucherCode).ToList();
        Assert.Single(electricityEntries);
        var recordedElectricity = electricityEntries.First().Amount;
        Assert.Equal(50_000m, recordedElectricity);

        // 4. Tổng chi phí & Lãi
        var totalRecordedExpenses = recordedCogs + recordedElectricity;
        var calculatedProfit = recordedRevenue - totalRecordedExpenses;

        Assert.Equal(250_000m, totalRecordedExpenses);
        Assert.Equal(40_000m, calculatedProfit); // Lãi = 40.000 đ chính xác 100%

        // -----------------------------------------------------------------------------------------
        // BƯỚC 5: KIỂM TRA SỔ S2d-HKD (SỔ VẬT LIỆU, DỤNG CỤ, SẢN PHẨM, HÀNG HÓA - NHẬP XUẤT TỒN)
        // Nhập 100 sản phẩm (1.000.000 đ)
        // Xuất 20 sản phẩm (200.000 đ)
        // Tồn cuối 80 sản phẩm (800.000 đ)
        // -----------------------------------------------------------------------------------------
        var s2dEntries = await taxRepo.GetBookEntriesAsync(
            fromDateUtc,
            toDateUtc,
            branchId,
            HouseholdTaxGroup.Group3_3BTo50B,
            TaxCalculationMethod.DeclarationBased,
            AccountingBookType.S2d_MaterialsAndGoods);

        Assert.NotEmpty(s2dEntries);
        Assert.All(s2dEntries, e => Assert.Equal("S2d-HKD", e.BookCode));

        // 1. Dòng nhập kho
        var inEntry = s2dEntries.FirstOrDefault(e => e.VoucherNumber == receipt.Code);
        Assert.NotNull(inEntry);
        Assert.Equal(100m, inEntry.InQuantity);
        Assert.Equal(1_000_000m, inEntry.InAmount);
        Assert.Equal(10_000m, inEntry.UnitPrice);

        // 2. Dòng xuất kho bán lẻ POS
        var outEntry = s2dEntries.FirstOrDefault(e => e.VoucherNumber == order.OrderCode);
        Assert.NotNull(outEntry);
        Assert.Equal(20m, outEntry.OutQuantity);
        Assert.Equal(200_000m, outEntry.OutAmount);
        Assert.Equal(10_000m, outEntry.UnitPrice);

        // 3. Tổng hợp Nhập - Xuất - Tồn
        var totalInQty = s2dEntries.Sum(e => e.InQuantity ?? 0m);
        var totalOutQty = s2dEntries.Sum(e => e.OutQuantity ?? 0m);
        var finalStockQty = totalInQty - totalOutQty;

        var totalInAmount = s2dEntries.Sum(e => e.InAmount ?? 0m);
        var totalOutAmount = s2dEntries.Sum(e => e.OutAmount ?? 0m);
        var finalStockValue = totalInAmount - totalOutAmount;

        Assert.Equal(100m, totalInQty);
        Assert.Equal(20m, totalOutQty);
        Assert.Equal(80m, finalStockQty);           // Tồn cuối 80 sản phẩm khớp 100%
        Assert.Equal(800_000m, finalStockValue);    // Giá trị tồn 800.000 đ khớp 100%

        // Đối chiếu với số tồn thực tế trong bảng BranchStocks
        var currentStock = await context.BranchStocks.FirstOrDefaultAsync(s => s.BranchId == branchId && s.ProductId == prodAId);
        Assert.NotNull(currentStock);
        Assert.Equal(finalStockQty, currentStock.QuantityOnHand);

        // -----------------------------------------------------------------------------------------
        // BƯỚC 6: KIỂM TRA SỔ S2e-HKD (SỔ CHI TIẾT TIỀN MẶT VÀ TIỀN GỬI NGÂN HÀNG)
        // Thu bán hàng vào quỹ = 290.000 đ
        // Chi tiền điện ra khỏi quỹ = 50.000 đ
        // Số dư khớp 100% = 240.000 đ
        // -----------------------------------------------------------------------------------------
        var s2eEntries = await taxRepo.GetBookEntriesAsync(
            fromDateUtc,
            toDateUtc,
            branchId,
            HouseholdTaxGroup.Group3_3BTo50B,
            TaxCalculationMethod.DeclarationBased,
            AccountingBookType.S2e_CashAndBank);

        Assert.NotEmpty(s2eEntries);
        Assert.All(s2eEntries, e => Assert.Equal("S2e-HKD", e.BookCode));

        var cashEntries = s2eEntries.Where(e => e.SectionGroup == "Khối I - Quỹ Tiền mặt").ToList();

        // 1. Dòng thu tiền bán lẻ
        var saleCashEntry = cashEntries.FirstOrDefault(e => e.VoucherNumber == cashbookSale.EntryCode);
        Assert.NotNull(saleCashEntry);
        Assert.Equal(290_000m, saleCashEntry.InAmount);
        Assert.Equal(0m, saleCashEntry.OutAmount);

        // 2. Dòng chi tiền điện
        var expenseCashEntry = cashEntries.FirstOrDefault(e => e.VoucherNumber == cashbookExpense.EntryCode);
        Assert.NotNull(expenseCashEntry);
        Assert.Equal(0m, expenseCashEntry.InAmount);
        Assert.Equal(50_000m, expenseCashEntry.OutAmount);

        // 3. Kiểm tra số dư cuối sổ S2e-HKD
        var lastCashRow = cashEntries.Last();
        Assert.Equal(240_000m, lastCashRow.BalanceAmount); // Số dư cuối 240.000 đ

        // Đối chiếu số dư sổ S2e-HKD với số dư tài khoản quỹ thực tế trong bảng FinanceAccounts
        var actualTill = await context.FinanceAccounts.FindAsync(DefaultTillId);
        Assert.NotNull(actualTill);
        Assert.Equal(actualTill.CurrentBalance, lastCashRow.BalanceAmount);
    }

    [Fact]
    public async Task EndToEnd_ServiceLevel_OrderService_And_CashbookService_Integration()
    {
        // =========================================================================================
        // KIỂM THỬ TẦNG SERVICE: Tự động hóa bán hàng qua OrderService
        // Xác minh OrderService tự động trừ kho BranchStock theo FEFO, đóng băng CostPriceAtSale,
        // và tự động kích hoạt CashbookService ghi nhận phiếu thu tiền mặt với chuỗi SHA-256 Hash Chain
        // =========================================================================================

        var dbName = $"MT_GRMS_E2E_OrderServiceInt_{Guid.NewGuid()}";
        var tenantContext = new FakeTenantContext();
        using var context = CreateDbContext(dbName, tenantContext);

        var branchId = Guid.NewGuid();
        var prodAId = Guid.NewGuid();
        var supplierId = Guid.NewGuid();

        await SeedBaseDataAsync(context, branchId, prodAId, supplierId);

        // Khởi tạo 100 sản phẩm A trong kho chi nhánh
        var branchStock = new BranchStock
        {
            Id = Guid.NewGuid(),
            BranchId = branchId,
            ProductId = prodAId,
            SupplierId = supplierId,
            QuantityOnHand = 100m,
            QuantityReserved = 0m,
            LandedCostPerUnit = 10_000m,
            RetailPrice = 15_000m,
            Status = "Active"
        };
        context.BranchStocks.Add(branchStock);
        await context.SaveChangesAsync();

        // Cấu hình Services & Repositories
        var unitOfWork = new UnitOfWork(context);
        var cashbookRepo = new CashbookRepository(context);
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
        var auditHashService = new AuditHashService(config);
        var accountRepo = new GenericRepository<FinanceAccount>(context);
        var transitRepo = new GenericRepository<CashInTransit>(context);
        var shiftRepo = new GenericRepository<ShiftZReport>(context);

        var cashbookService = new CashbookService(
            cashbookRepo,
            accountRepo,
            transitRepo,
            shiftRepo,
            auditHashService,
            tenantContext,
            unitOfWork);

        var orderRepo = new OrderRepository(context);
        var thermalPrint = new FakeThermalPrintService();

        var orderService = new OrderService(
            orderRepo,
            unitOfWork,
            thermalPrint,
            null, // debtService
            cashbookService);

        // Thao tác bán lẻ: Bán 20 sản phẩm A (giá 15.000 đ), chiết khấu đơn 10.000 đ
        var createRequest = new OrderCreateRequest(
            BranchId: branchId,
            CustomerId: null,
            PaymentMethod: "CASH",
            Items: new List<OrderItemCreateDto>
            {
                new(prodAId, 20m, 15_000m, 0m, "Sản phẩm A")
            },
            CashTendered: 300_000m,
            DiscountAmount: 10_000m,
            Note: "Đơn bán lẻ test tầng service"
        );

        // Thực hiện Act
        var response = await orderService.CreateOrderAsync(createRequest);

        // Kiểm tra kết quả đơn hàng
        Assert.NotNull(response);
        Assert.Equal(300_000m, response.TotalAmount);
        Assert.Equal(10_000m, response.DiscountAmount);
        Assert.Equal(290_000m, response.FinalAmount);
        Assert.Equal(10_000m, response.ChangeAmount); // 300.000 đưa - 290.000 = 10.000 tiền thừa

        // Kiểm tra chi tiết đơn hàng: CostPriceAtSale được chốt cứng 10.000 đ
        Assert.Single(response.Items);
        Assert.Equal(10_000m, response.Items.First().CostPriceAtSale);

        // Kiểm tra trừ kho chi nhánh: 100 - 20 = 80
        var updatedStock = await context.BranchStocks.FindAsync(branchStock.Id);
        Assert.NotNull(updatedStock);
        Assert.Equal(80m, updatedStock.QuantityOnHand);

        // Kiểm tra tự động đổ vào Sổ quỹ Cashbook qua CashbookService
        var cashEntries = await context.CashbookEntries.Where(c => c.SourceId == response.Id).ToListAsync();
        Assert.Single(cashEntries);
        var entry = cashEntries.First();
        Assert.Equal(290_000m, entry.Amount);
        Assert.Equal(CashbookEntryType.Receipt, entry.EntryType);
        Assert.Equal("POS_SALE", entry.SourceType);
        Assert.False(string.IsNullOrWhiteSpace(entry.AuditHash)); // Chuỗi băm bảo mật SHA-256 được thiết lập

        // Kiểm tra số dư tài khoản két quầy tăng lên 290.000 đ
        var till = await context.FinanceAccounts.FindAsync(DefaultTillId);
        Assert.NotNull(till);
        Assert.Equal(290_000m, till.CurrentBalance);
    }

    [Fact]
    public async Task EndToEnd_CreditSale_CustomerDebt_And_ElectronicInvoiceLinkage()
    {
        // =========================================================================================
        // KIỂM THỬ PHÂN HỆ KHÁCH HÀNG & BÁN NỢ FIFO (FE-08 + FE-17)
        // 1. Bán nợ tạo CustomerDebtInvoice (Hóa đơn công nợ / EInvoice) liên kết OrderId
        // 2. Không tăng quỹ tiền mặt cho đến khi khách trả nợ
        // 3. Khách hàng trả nợ (RepayDebt) -> Phân bổ FIFO và sinh phiếu thu CashbookEntry
        // =========================================================================================

        var dbName = $"MT_GRMS_E2E_DebtInt_{Guid.NewGuid()}";
        var tenantContext = new FakeTenantContext();
        using var context = CreateDbContext(dbName, tenantContext);

        var branchId = Guid.NewGuid();
        var prodAId = Guid.NewGuid();
        var supplierId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        await SeedBaseDataAsync(context, branchId, prodAId, supplierId);

        // Khởi tạo khách hàng mua nợ
        var customer = new Customer
        {
            Id = customerId,
            CustomerCode = "KH-001",
            Name = "Khách hàng Trần Văn C",
            Phone = "0987654321",
            AllowDebt = true,
            DebtLimit = 10_000_000m,
            TotalDebt = 0m,
            DebtStatus = CustomerDebtStatus.GoodStanding
        };
        context.Customers.Add(customer);

        // Khởi tạo tồn kho 50 cái
        context.BranchStocks.Add(new BranchStock
        {
            Id = Guid.NewGuid(),
            BranchId = branchId,
            ProductId = prodAId,
            SupplierId = supplierId,
            QuantityOnHand = 50m,
            QuantityReserved = 0m,
            LandedCostPerUnit = 10_000m,
            RetailPrice = 15_000m,
            Status = "Active"
        });
        await context.SaveChangesAsync();

        var unitOfWork = new UnitOfWork(context);
        var cashbookRepo = new CashbookRepository(context);
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
        var auditHashService = new AuditHashService(config);
        var accountRepo = new GenericRepository<FinanceAccount>(context);
        var transitRepo = new GenericRepository<CashInTransit>(context);
        var shiftRepo = new GenericRepository<ShiftZReport>(context);

        var cashbookService = new CashbookService(
            cashbookRepo,
            accountRepo,
            transitRepo,
            shiftRepo,
            auditHashService,
            tenantContext,
            unitOfWork);

        var debtRepo = new CustomerDebtRepository(context);
        var invoiceRepo = new GenericRepository<CustomerDebtInvoice>(context);
        var entryRepo = new GenericRepository<CustomerDebtEntry>(context);
        var allocationRepo = new GenericRepository<CustomerDebtAllocation>(context);

        var debtService = new CustomerDebtService(
            debtRepo,
            invoiceRepo,
            entryRepo,
            allocationRepo,
            cashbookService,
            tenantContext,
            unitOfWork);

        var orderRepo = new OrderRepository(context);
        var thermalPrint = new FakeThermalPrintService();

        var orderService = new OrderService(
            orderRepo,
            unitOfWork,
            thermalPrint,
            debtService,
            cashbookService);

        // Bước A: Bán nợ 10 sản phẩm A (giá 15.000 đ) = 150.000 đ
        var orderResponse = await orderService.CreateOrderAsync(new OrderCreateRequest(
            BranchId: branchId,
            CustomerId: customerId,
            PaymentMethod: "CREDIT",
            Items: new List<OrderItemCreateDto>
            {
                new(prodAId, 10m, 15_000m, 0m, "Sản phẩm A")
            },
            CreatorRole: "STOREOWNER",
            Note: "Đơn bán nợ ghi sổ"
        ));

        Assert.Equal("UNPAID", orderResponse.PaymentStatus);
        Assert.Equal(150_000m, orderResponse.FinalAmount);

        // Kiểm tra Hóa đơn nợ / EInvoice liên kết chặt chẽ với OrderId
        var debtInvoices = await context.CustomerDebtInvoices.Where(i => i.CustomerId == customerId).ToListAsync();
        Assert.Single(debtInvoices);
        var invoice = debtInvoices.First();
        Assert.Equal(orderResponse.Id, invoice.OrderId);
        Assert.Equal(150_000m, invoice.RemainingDebtAmount);

        // Quỹ tiền mặt chưa thu tiền -> Số dư vẫn bằng 0
        var till = await context.FinanceAccounts.FindAsync(DefaultTillId);
        Assert.NotNull(till);
        Assert.Equal(0m, till.CurrentBalance);

        // Bước B: Khách hàng trả nợ 150.000 đ tiền mặt
        var repayResult = await debtService.RepayDebtAsync(new RepayDebtRequest(
            CustomerId: customerId,
            Amount: 150_000m,
            PaymentSourceType: FinanceAccountType.Till,
            PaymentSourceAccountId: DefaultTillId,
            ReferenceCode: invoice.OrderCode,
            Notes: "Khách thanh toán toàn bộ nợ tiền mặt"
        ));

        Assert.NotNull(repayResult);
        Assert.Equal(0m, repayResult.RemainingDebt);

        // Hóa đơn nợ chuyển sang FullyPaid
        var updatedInvoice = await context.CustomerDebtInvoices.FindAsync(invoice.Id);
        Assert.NotNull(updatedInvoice);
        Assert.Equal(DebtInvoiceStatus.FullyPaid, updatedInvoice.Status);
        Assert.Equal(0m, updatedInvoice.RemainingDebtAmount);

        // Tiền mặt chính thức đổ vào Sổ quỹ Cashbook
        var repayCashEntry = await context.CashbookEntries.FirstOrDefaultAsync(c => c.SourceType == "DEBT_REPAYMENT");
        Assert.NotNull(repayCashEntry);
        Assert.Equal(150_000m, repayCashEntry.Amount);

        // Số dư két quầy cập nhật chính xác 150.000 đ
        Assert.Equal(150_000m, till.CurrentBalance);
    }

    [Fact]
    public async Task EndToEnd_TaxSummary_And_FinalizeTaxSnapshot_HMAC_Verification()
    {
        // =========================================================================================
        // KIỂM THỬ PHÂN HỆ QUẢN TRỊ THUẾ HỘ KINH DOANH (FE-19 TT 152/2025/TT-BTC)
        // 1. Tự động tổng hợp Doanh thu, COGS, Chi phí từ Orders và ExpenseVouchers
        // 2. Chốt kỳ tính thuế FinalizeTaxSnapshotAsync
        // 3. Ký số HMAC-SHA256 bất biến & Khóa sổ chống can thiệp hồi tố
        // =========================================================================================

        var dbName = $"MT_GRMS_E2E_TaxFinalize_{Guid.NewGuid()}";
        var tenantContext = new FakeTenantContext();
        using var context = CreateDbContext(dbName, tenantContext);

        var branchId = Guid.NewGuid();
        var prodAId = Guid.NewGuid();
        var supplierId = Guid.NewGuid();

        await SeedBaseDataAsync(context, branchId, prodAId, supplierId);

        var orderDate = new DateTime(2026, 2, 15, 10, 0, 0, DateTimeKind.Utc);

        // Đơn hàng tích lũy chuỗi để đưa Tenant vào Nhóm 3 (> 3 tỷ VNĐ - Kê khai thuế)
        var otherBranchId = Guid.NewGuid();
        context.Orders.Add(new Order
        {
            Id = Guid.NewGuid(),
            OrderCode = "HD-TENANT-ACCUM",
            BranchId = otherBranchId,
            TotalAmount = 3_200_000_000m,
            FinalAmount = 3_200_000_000m,
            PaymentStatus = "PAID",
            Status = "Completed",
            CreatedAt = orderDate.AddDays(-10)
        });

        // Doanh thu chi nhánh cần test: 290.000 đ, COGS: 200.000 đ
        context.Orders.Add(new Order
        {
            Id = Guid.NewGuid(),
            OrderCode = "HD-TAX-01",
            BranchId = branchId,
            TotalAmount = 300_000m,
            DiscountAmount = 10_000m,
            FinalAmount = 290_000m,
            PaymentStatus = "PAID",
            Status = "Completed",
            CreatedAt = orderDate,
            Details = new List<OrderDetail>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    ProductId = prodAId,
                    Qty = 20m,
                    UnitPrice = 15_000m,
                    CostPriceAtSale = 10_000m,
                    CreatedAt = orderDate
                }
            }
        });

        // Chi phí tiền điện: 50.000 đ
        context.ExpenseVouchers.Add(new ExpenseVoucher
        {
            Id = Guid.NewGuid(),
            VoucherCode = "PC-TAX-01",
            BranchId = branchId,
            BeneficiaryName = "Điện lực",
            Category = "Tiền điện sinh hoạt và kinh doanh",
            Amount = 50_000m,
            Status = ExpenseVoucherStatus.Paid,
            Nature = FinancialNature.OperatingExpense,
            ExpenseDate = orderDate,
            CreatedAt = orderDate
        });

        await context.SaveChangesAsync();

        var taxRepo = new HouseholdTaxRepository(context);
        var signatureService = new TaxSnapshotSignatureService();
        var strategyFactory = new TaxCalculationStrategyFactory(new ITaxCalculationStrategy[]
        {
            new RevenuePercentageTaxStrategy(),
            new DeclarationBasedTaxStrategy()
        });

        var taxService = new HouseholdTaxService(
            taxRepo,
            strategyFactory,
            null, // Không bắt buộc PIN trong integration test
            signatureService,
            null,
            tenantContext);

        // 1. Act: Tổng hợp nghĩa vụ thuế Q1/2026 theo phương pháp kê khai lợi nhuận
        var summary = await taxService.GetTaxSummaryAsync(new HouseholdTaxFilterDto
        {
            Year = 2026,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 1,
            BranchId = branchId,
            CalculationMethod = TaxCalculationMethod.DeclarationBased,
            Tier2Method = "PROFIT_15"
        });

        Assert.Equal(290_000m, summary.TaxableRevenueInPeriod);
        Assert.Equal(200_000m, summary.CogsAmount);
        Assert.Equal(50_000m, summary.OpexAmount);
        Assert.Equal(250_000m, summary.DeductibleExpensesInPeriod);
        Assert.False(summary.IsCogsEstimated); // Giá vốn thực tế chốt cứng từ phiếu nhập

        // 2. Act: Chốt kỳ tính thuế Finalize Snapshot
        var snapshotResult = await taxService.FinalizeTaxSnapshotAsync(
            new CreateTaxSnapshotRequest
            {
                Year = 2026,
                PeriodType = TaxPeriodType.Quarter,
                PeriodNumber = 1,
                BranchId = branchId,
                CalculationMethod = TaxCalculationMethod.DeclarationBased,
                Tier2Method = "PROFIT_15",
                ConfirmEstimatedCogsResponsible = true
            },
            finalizedBy: "store_owner_an");

        Assert.NotNull(snapshotResult);
        Assert.True(snapshotResult.IsIntegrityValid);
        Assert.False(string.IsNullOrWhiteSpace(snapshotResult.AuditSignatureHash)); // Đã được ký số HMAC-SHA256

        // 3. Xác minh tính toàn vẹn chữ ký số mật mã học
        var isIntegrityValid = await taxService.VerifySnapshotIntegrityAsync(snapshotResult.Id);
        Assert.True(isIntegrityValid);

        // 4. Kiểm tra khóa sổ: Cố tình chốt lại kỳ Q1/2026 phải ném ngoại lệ TAX_PERIOD_LOCKED
        var ex = await Assert.ThrowsAsync<BusinessException>(() => taxService.FinalizeTaxSnapshotAsync(
            new CreateTaxSnapshotRequest
            {
                Year = 2026,
                PeriodType = TaxPeriodType.Quarter,
                PeriodNumber = 1,
                BranchId = branchId,
                CalculationMethod = TaxCalculationMethod.DeclarationBased
            },
            finalizedBy: "attacker"));

        Assert.Equal("TAX_PERIOD_LOCKED", ex.ErrorCode);
    }

    [Fact]
    public async Task EndToEnd_FinancialLedgers_Immutability_Constraints_ShouldBlockModifications()
    {
        // =========================================================================================
        // KIỂM THỬ TÍNH BẤT BIẾN CỦA SỔ CÁI TÀI CHÍNH (IMMUTABLE LEDGER INVARIANTS)
        // Chặn tuyệt đối hành vi DELETE, Soft-Delete (IsDeleted=true) hoặc Modified trên:
        // CashbookEntry, CustomerDebtEntry, HouseholdTaxSnapshot
        // =========================================================================================

        var dbName = $"MT_GRMS_E2E_LedgerImmutability_{Guid.NewGuid()}";
        using var context = CreateDbContext(dbName);

        var cashEntry = new CashbookEntry
        {
            EntryCode = "CB-IMMUTABLE-01",
            AccountId = DefaultTillId,
            AccountType = FinanceAccountType.Till,
            EntryType = CashbookEntryType.Receipt,
            Amount = 100_000m,
            BalanceAfter = 100_000m,
            SourceType = "TEST",
            AuditHash = "HASH-01",
            Description = "Bút toán thử nghiệm bất biến"
        };
        context.CashbookEntries.Add(cashEntry);
        await context.SaveChangesAsync();

        // 1. Thử sửa số tiền -> Ném InvalidOperationException
        cashEntry.Amount = 999_999m;
        var modEx = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        Assert.Contains("Cấm chỉnh sửa (Modified) thực thể sổ cái tài chính bất biến", modEx.Message);

        // Tạo context mới để test soft-delete
        using var context2 = CreateDbContext(dbName);
        var entryToSoftDelete = await context2.CashbookEntries.FirstAsync();
        entryToSoftDelete.IsDeleted = true;
        var softDelEx = await Assert.ThrowsAsync<InvalidOperationException>(() => context2.SaveChangesAsync());
        Assert.Contains("Cấm soft-delete thực thể sổ cái tài chính bất biến", softDelEx.Message);

        // Tạo context mới để test hard-delete
        using var context3 = CreateDbContext(dbName);
        var entryToHardDelete = await context3.CashbookEntries.FirstAsync();
        context3.CashbookEntries.Remove(entryToHardDelete);
        var hardDelEx = await Assert.ThrowsAsync<InvalidOperationException>(() => context3.SaveChangesAsync());
        Assert.Contains("Cấm xóa thực thể sổ cái tài chính bất biến", hardDelEx.Message);
    }
}
