using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Moq;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Tax;
using MT_GRMS.Application.Tax.Dtos;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Domain.Entities.Sales;
using MT_GRMS.Domain.Entities.Supplier;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Infrastructure.Persistence;
using MT_GRMS.Infrastructure.Services;
using Xunit;

namespace MT_GRMS.UnitTests;

public class Circular152TaxExportServiceTests
{
    private readonly Mock<IHouseholdTaxService> _mockTaxService;
    private readonly Mock<IHouseholdTaxRepository> _mockTaxRepo;
    private readonly AppDbContext _context;
    private readonly TaxExportService _exportService;
    private readonly Guid _branchId;
    private readonly Guid _branchId2;

    public Circular152TaxExportServiceTests()
    {
        _mockTaxService = new Mock<IHouseholdTaxService>();
        _mockTaxRepo = new Mock<IHouseholdTaxRepository>();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new AppDbContext(options);

        // Seed basic tenant and branches
        var tenant = new Tenant
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Hộ Kinh Doanh Siêu Thị Mini MT",
            TaxCode = "0109988776",
            Address = "123 Đường Nguyễn Huệ, Quận 1, TP. Hồ Chí Minh"
        };
        _context.Tenants.Add(tenant);

        var branch = new Branch
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            BranchCode = "CN01",
            Name = "Chi nhánh Quận 1",
            Address = "123 Đường Nguyễn Huệ, Quận 1, TP. Hồ Chí Minh",
            IsActive = true
        };
        var branch2 = new Branch
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            BranchCode = "CN02",
            Name = "Chi nhánh Quận 2",
            Address = "456 Xa Lộ Hà Nội, Quận 2, TP. Hồ Chí Minh",
            IsActive = true
        };
        _context.Branches.AddRange(branch, branch2);

        var supplier = new Supplier
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Code = "NCC-VNM",
            Name = "Công ty Cổ Phần Sữa Việt Nam",
            Status = SupplierStatus.Active
        };
        _context.Suppliers.Add(supplier);

        var product = new Product
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            ProductCode = "SP-001",
            Barcode = "8931234567890",
            Name = "Sữa Tươi Vinamilk 180ml",
            BaseUnit = "Hộp",
            CostPrice = 8000m,
            SellingPrice = 10000m,
            Status = ProductStatus.Active
        };
        _context.Products.Add(product);

        // Seed Orders for Q1 and Q2
        var order1 = new Order
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            BranchId = branch.Id,
            OrderCode = "DH-20260115-001",
            TotalAmount = 50000m,
            DiscountAmount = 5000m,
            PaymentStatus = "PAID",
            Status = "COMPLETED",
            CreatedAt = new DateTime(2026, 1, 15, 8, 30, 0, DateTimeKind.Utc)
        };
        order1.Details.Add(new OrderDetail
        {
            Id = Guid.NewGuid(),
            OrderId = order1.Id,
            ProductId = product.Id,
            ProductName = product.Name,
            UnitName = product.BaseUnit,
            Qty = 5m,
            UnitPrice = 10000m,
            CostPriceAtSale = 8000m
        });

        var order2 = new Order
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            BranchId = branch.Id,
            OrderCode = "DH-20260420-002",
            TotalAmount = 100000m,
            DiscountAmount = 10000m,
            PaymentStatus = "PAID",
            Status = "COMPLETED",
            CreatedAt = new DateTime(2026, 4, 20, 10, 0, 0, DateTimeKind.Utc)
        };
        order2.Details.Add(new OrderDetail
        {
            Id = Guid.NewGuid(),
            OrderId = order2.Id,
            ProductId = product.Id,
            ProductName = product.Name,
            UnitName = product.BaseUnit,
            Qty = 10m,
            UnitPrice = 10000m,
            CostPriceAtSale = 8000m
        });
        _context.Orders.AddRange(order1, order2);

        // Seed GoodsReceipts for Q1 and Q2
        var receipt1 = new GoodsReceipt
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            BranchId = branch.Id,
            SupplierId = supplier.Id,
            Code = "PN-20260110-001",
            Status = "COMPLETED",
            ReceivedAtUtc = new DateTime(2026, 1, 10, 9, 0, 0, DateTimeKind.Utc),
            GrandTotal = 800000m
        };
        receipt1.Lines.Add(new GoodsReceiptLine
        {
            Id = Guid.NewGuid(),
            GoodsReceiptId = receipt1.Id,
            ProductId = product.Id,
            AcceptedQty = 100m,
            DeliveredQty = 100m,
            OrderedQty = 100m
        });

        var receipt2 = new GoodsReceipt
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            BranchId = branch.Id,
            SupplierId = supplier.Id,
            Code = "PN-20260412-002",
            Status = "COMPLETED",
            ReceivedAtUtc = new DateTime(2026, 4, 12, 9, 0, 0, DateTimeKind.Utc),
            GrandTotal = 1600000m
        };
        receipt2.Lines.Add(new GoodsReceiptLine
        {
            Id = Guid.NewGuid(),
            GoodsReceiptId = receipt2.Id,
            ProductId = product.Id,
            AcceptedQty = 200m,
            DeliveredQty = 200m,
            OrderedQty = 200m
        });
        _context.GoodsReceipts.AddRange(receipt1, receipt2);

        // Seed Cashbook entries
        var cash1 = new CashbookEntry
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            BranchId = branch.Id,
            EntryCode = "PT-001",
            AccountType = FinanceAccountType.Till,
            EntryType = CashbookEntryType.Receipt,
            Amount = 45000m,
            OccurredAtUtc = new DateTime(2026, 1, 15, 9, 0, 0, DateTimeKind.Utc),
            Description = "Thu tiền bán lẻ",
            SourceType = "POS",
            ReferenceCode = "DH-20260115-001"
        };
        var cash2 = new CashbookEntry
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            BranchId = branch.Id,
            EntryCode = "PC-001",
            AccountType = FinanceAccountType.Bank,
            EntryType = CashbookEntryType.Payment,
            Amount = 15000000m,
            OccurredAtUtc = new DateTime(2026, 1, 16, 10, 0, 0, DateTimeKind.Utc),
            Description = "Chi tiền nộp thuế GTGT vào NSNN",
            SourceType = "TAX",
            ReferenceCode = "TT-001"
        };
        var cash3 = new CashbookEntry
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            BranchId = branch.Id,
            EntryCode = "PT-002",
            AccountType = FinanceAccountType.Bank,
            EntryType = CashbookEntryType.Receipt,
            Amount = 90000m,
            OccurredAtUtc = new DateTime(2026, 4, 20, 10, 30, 0, DateTimeKind.Utc),
            Description = "Thu tiền chuyển khoản đơn hàng",
            SourceType = "POS",
            ReferenceCode = "DH-20260420-002"
        };
        _context.CashbookEntries.AddRange(cash1, cash2, cash3);

        // Seed HouseholdTaxSnapshots
        var snapshot1 = new HouseholdTaxSnapshot
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            BranchId = branch.Id,
            Year = 2026,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 1,
            AnnualAccumulatedRevenue = 1500000000m,
            HouseholdGroup = HouseholdTaxGroup.Group2_1BTo3B,
            TaxableRevenueInPeriod = 400000000m,
            VatRate = 0.01m,
            VatAmount = 4000000m,
            PitRate = 0.005m,
            PitAmount = 2000000m,
            TotalTaxAmount = 6000000m,
            CreatedAt = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        var snapshot2 = new HouseholdTaxSnapshot
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            BranchId = branch.Id,
            Year = 2026,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 2,
            AnnualAccumulatedRevenue = 3000000000m,
            HouseholdGroup = HouseholdTaxGroup.Group2_1BTo3B,
            TaxableRevenueInPeriod = 500000000m,
            VatRate = 0.01m,
            VatAmount = 5000000m,
            PitRate = 0.005m,
            PitAmount = 2500000m,
            TotalTaxAmount = 7500000m,
            CreatedAt = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        _context.HouseholdTaxSnapshots.AddRange(snapshot1, snapshot2);

        _context.SaveChanges();
        _branchId = branch.Id;
        _branchId2 = branch2.Id;

        // Setup default tax summary
        var defaultSummary = new HouseholdTaxSummaryDto
        {
            Year = 2026,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 1,
            BranchName = "Chi nhánh Quận 1",
            AnnualAccumulatedRevenue = 1_500_000_000m,
            TaxableRevenueInPeriod = 400_000_000m,
            GrossRevenueInPeriod = 400_000_000m,
            HouseholdGroup = HouseholdTaxGroup.Group2_1BTo3B,
            HouseholdGroupName = "Nhóm 2 (1 tỷ đến 3 tỷ VNĐ/năm)",
            CalculationMethod = TaxCalculationMethod.RevenuePercentage,
            VatRate = 0.01m,
            VatAmount = 4_000_000m,
            PitRate = 0.005m,
            PitAmount = 2_000_000m,
            TotalTaxAmount = 6_000_000m,
            CogsAmount = 280_000_000m,
            DeductibleExpensesInPeriod = 40_000_000m,
            VatBreakdown = new VatBreakdownDto
            {
                RevenueStandardRate10 = 300_000_000m,
                TaxAmountStandardRate10 = 3_000_000m,
                RevenueReducedRate08 = 100_000_000m,
                TaxAmountReducedRate08 = 800_000m,
                EffectiveVatRate = 0.0095m
            }
        };

        _mockTaxService
            .Setup(s => s.GetTaxSummaryAsync(It.IsAny<HouseholdTaxFilterDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(defaultSummary);

        _mockTaxRepo
            .Setup(r => r.GetDeductibleExpenseVouchersAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ExpenseVoucher>
            {
                new()
                {
                    VoucherCode = "PC-001",
                    ExpenseDate = new DateTime(2026, 1, 15),
                    Category = "Tiền thuê mặt bằng cửa hàng",
                    Amount = 15_000_000m,
                    BeneficiaryName = "Chủ nhà",
                    IsTaxDeductible = true
                },
                new()
                {
                    VoucherCode = "PC-002",
                    ExpenseDate = new DateTime(2026, 2, 5),
                    Category = "Tiền điện nước và viễn thông",
                    Amount = 5_000_000m,
                    BeneficiaryName = "Điện lực",
                    IsTaxDeductible = true
                }
            });

        _mockTaxRepo
            .Setup(r => r.GetPeriodRevenueAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((400_000_000m, 0m, 0m));

        _exportService = new TaxExportService(_mockTaxService.Object, _mockTaxRepo.Object, _context);
    }

    [Fact]
    public async Task ExportTaxSummaryToExcel_WithoutBookType_ShouldContainAll7Circular152Sheets()
    {
        // Arrange
        var filter = new HouseholdTaxFilterDto
        {
            Year = 2026,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 1
        };

        // Act
        var bytes = await _exportService.ExportTaxSummaryToExcelAsync(filter, null, CancellationToken.None);

        // Assert
        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 0);

        using var ms = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(ms);

        // Kiểm tra đủ cả 7 sheet Thông tư 152 và sheet Thông tin
        Assert.NotNull(workbook.Worksheet("Thông tin"));
        Assert.NotNull(workbook.Worksheet("S1a-HKD"));
        Assert.NotNull(workbook.Worksheet("S2a-HKD"));
        Assert.NotNull(workbook.Worksheet("S2b-HKD"));
        Assert.NotNull(workbook.Worksheet("S2c-HKD"));
        Assert.NotNull(workbook.Worksheet("S2d-HKD"));
        Assert.NotNull(workbook.Worksheet("S2e-HKD"));
        Assert.NotNull(workbook.Worksheet("S3a-HKD"));
    }

    [Fact]
    public async Task ExportTaxSummaryToExcel_WithS1aBookType_ShouldContainOnlyInfoAndS1aSheet()
    {
        // Arrange
        var filter = new HouseholdTaxFilterDto { Year = 2026, PeriodType = TaxPeriodType.Quarter, PeriodNumber = 1 };

        // Act
        var bytes = await _exportService.ExportTaxSummaryToExcelAsync(filter, AccountingBookType.S1a_RevenueGroup1, CancellationToken.None);

        // Assert
        using var ms = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(ms);

        Assert.Equal(2, workbook.Worksheets.Count);
        Assert.NotNull(workbook.Worksheet("Thông tin"));
        var ws = workbook.Worksheet("S1a-HKD");
        Assert.NotNull(ws);

        // Kiểm tra cấu trúc S1a: 3 cột [Ngày tháng A, Diễn giải B, Doanh thu 1]
        Assert.Equal("Ngày, tháng ghi sổ", ws.Cell(10, 1).GetString());
        Assert.Equal("Diễn giải", ws.Cell(10, 2).GetString());
        Assert.Contains("Doanh thu", ws.Cell(10, 3).GetString());
        Assert.Equal("A", ws.Cell(11, 1).GetString());
        Assert.Equal("B", ws.Cell(11, 2).GetString());
        Assert.Equal("1", ws.Cell(11, 3).GetString());
    }

    [Fact]
    public async Task ExportTaxSummaryToExcel_WithS2aBookType_ShouldContainVatAndPitRows()
    {
        // Arrange
        var filter = new HouseholdTaxFilterDto { Year = 2026, PeriodType = TaxPeriodType.Quarter, PeriodNumber = 1 };

        // Act
        var bytes = await _exportService.ExportTaxSummaryToExcelAsync(filter, AccountingBookType.S2a_RevenueGroup2, CancellationToken.None);

        // Assert
        using var ms = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(ms);
        var ws = workbook.Worksheet("S2a-HKD");
        Assert.NotNull(ws);

        // Kiểm tra có dòng Số thuế GTGT và dòng Số thuế TNCN
        bool hasVatRow = false;
        bool hasPitRow = false;
        bool hasTotalVatRow = false;
        bool hasTotalPitRow = false;

        for (int r = 10; r <= 50; r++)
        {
            var text = ws.Cell(r, 1).GetString();
            if (text.Contains("Số thuế GTGT phải nộp")) hasVatRow = true;
            if (text.Contains("Số thuế TNCN phải nộp")) hasPitRow = true;
            if (text.Contains("TỔNG SỐ THUẾ GTGT PHẢI NỘP TOÀN BỘ")) hasTotalVatRow = true;
            if (text.Contains("TỔNG SỐ THUẾ TNCN PHẢI NỘP TOÀN BỘ")) hasTotalPitRow = true;
        }

        Assert.True(hasVatRow, "S2a phải có dòng tính thuế GTGT");
        Assert.True(hasPitRow, "S2a phải có dòng tính thuế TNCN");
        Assert.True(hasTotalVatRow, "S2a phải có dòng tổng số thuế GTGT");
        Assert.True(hasTotalPitRow, "S2a phải có dòng tổng số thuế TNCN");
    }

    [Fact]
    public async Task ExportTaxSummaryToExcel_WithS2bBookType_ShouldContainVatAndNoPitRow()
    {
        // Arrange
        var filter = new HouseholdTaxFilterDto { Year = 2026, PeriodType = TaxPeriodType.Quarter, PeriodNumber = 1 };

        // Act
        var bytes = await _exportService.ExportTaxSummaryToExcelAsync(filter, AccountingBookType.S2b_RevenueDeclaration, CancellationToken.None);

        // Assert
        using var ms = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(ms);
        var ws = workbook.Worksheet("S2b-HKD");
        Assert.NotNull(ws);

        // Kiểm tra CÓ dòng thuế GTGT, TUYỆT ĐỐI KHÔNG CÓ dòng thuế TNCN
        bool hasVatRow = false;
        bool hasPitRow = false;

        for (int r = 10; r <= 50; r++)
        {
            var text = ws.Cell(r, 1).GetString();
            if (text.Contains("Số thuế GTGT phải nộp")) hasVatRow = true;
            if (text.Contains("Số thuế TNCN phải nộp") || text.Contains("TNCN")) hasPitRow = true;
        }

        Assert.True(hasVatRow, "S2b phải có dòng tính thuế GTGT");
        Assert.False(hasPitRow, "S2b TUYỆT ĐỐI KHÔNG CÓ dòng tính thuế TNCN (TNCN tính tại S2c)");
    }

    [Fact]
    public async Task ExportTaxSummaryToExcel_WithS2cBookType_ShouldContain4MainGroups()
    {
        // Arrange
        var filter = new HouseholdTaxFilterDto { Year = 2026, PeriodType = TaxPeriodType.Quarter, PeriodNumber = 1 };

        // Act
        var bytes = await _exportService.ExportTaxSummaryToExcelAsync(filter, AccountingBookType.S2c_RevenueAndExpense, CancellationToken.None);

        // Assert
        using var ms = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(ms);
        var ws = workbook.Worksheet("S2c-HKD");
        Assert.NotNull(ws);

        bool hasGroup1 = false;
        bool hasGroup2 = false;
        bool hasGroup2aCogs = false;
        bool hasGroup3 = false;
        bool hasGroup4 = false;

        for (int r = 10; r <= 80; r++)
        {
            var text = ws.Cell(r, 1).GetString();
            var desc = ws.Cell(r, 3).GetString();
            if (text.Contains("1. DOANH THU BÁN HÀNG HÓA")) hasGroup1 = true;
            if (text.Contains("2. CHI PHÍ SẢN XUẤT, KINH DOANH")) hasGroup2 = true;
            if (text.Contains("2.a") || desc.Contains("COGS") || desc.Contains("Giá vốn")) hasGroup2aCogs = true;
            if (text.Contains("3. Chênh lệch thu nhập")) hasGroup3 = true;
            if (text.Contains("4. Thuế Thu nhập cá nhân")) hasGroup4 = true;
        }

        Assert.True(hasGroup1, "S2c phải có Nhóm 1: Doanh thu");
        Assert.True(hasGroup2, "S2c phải có Nhóm 2: Chi phí SXKD");
        Assert.True(hasGroup2aCogs, "S2c phải có mục 2.a với dòng COGS Giá vốn");
        Assert.True(hasGroup3, "S2c phải có Nhóm 3: Chênh lệch (1) - (2)");
        Assert.True(hasGroup4, "S2c phải có Nhóm 4: Thuế TNCN phải nộp");
    }

    [Fact]
    public async Task ExportTaxSummaryToExcel_WithS2dBookType_ShouldContain12ColumnsInventoryStructure()
    {
        // Arrange
        var filter = new HouseholdTaxFilterDto { Year = 2026, PeriodType = TaxPeriodType.Quarter, PeriodNumber = 1 };

        // Act
        var bytes = await _exportService.ExportTaxSummaryToExcelAsync(filter, AccountingBookType.S2d_MaterialsAndGoods, CancellationToken.None);

        // Assert
        using var ms = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(ms);
        var ws = workbook.Worksheet("S2d-HKD");
        Assert.NotNull(ws);

        // Kiểm tra 12 cột: Col Letters dòng 11 phải là [ -, A, B, C, D, E, 1, 2, 3, 4, 5, 6 ]
        Assert.Equal("A", ws.Cell(11, 2).GetString());
        Assert.Equal("B", ws.Cell(11, 3).GetString());
        Assert.Equal("C", ws.Cell(11, 4).GetString());
        Assert.Equal("D", ws.Cell(11, 5).GetString());
        Assert.Equal("E", ws.Cell(11, 6).GetString());
        Assert.Equal("1", ws.Cell(11, 7).GetString());
        Assert.Equal("2", ws.Cell(11, 8).GetString());
        Assert.Equal("3", ws.Cell(11, 9).GetString());
        Assert.Equal("4", ws.Cell(11, 10).GetString());
        Assert.Equal("5", ws.Cell(11, 11).GetString());
        Assert.Equal("6", ws.Cell(11, 12).GetString());

        // Kiểm tra có dòng Số dư đầu kỳ, Cộng phát sinh, Số dư cuối kỳ
        bool hasInitRow = false;
        bool hasSumRow = false;
        bool hasFinalRow = false;

        for (int r = 11; r <= 40; r++)
        {
            var text = ws.Cell(r, 1).GetString();
            var desc = ws.Cell(r, 4).GetString();
            if (desc.Contains("Số dư đầu kỳ")) hasInitRow = true;
            if (text.Contains("CỘNG PHÁT SINH TRONG KỲ")) hasSumRow = true;
            if (text.Contains("SỐ DƯ CUỐI KỲ")) hasFinalRow = true;
        }

        Assert.True(hasInitRow, "S2d phải có Số dư đầu kỳ");
        Assert.True(hasSumRow, "S2d phải có Cộng phát sinh trong kỳ");
        Assert.True(hasFinalRow, "S2d phải có Số dư cuối kỳ");
    }

    [Fact]
    public async Task ExportTaxSummaryToExcel_WithS2eBookType_ShouldContainCashAndBankBlocks()
    {
        // Arrange
        var filter = new HouseholdTaxFilterDto { Year = 2026, PeriodType = TaxPeriodType.Quarter, PeriodNumber = 1 };

        // Act
        var bytes = await _exportService.ExportTaxSummaryToExcelAsync(filter, AccountingBookType.S2e_CashAndBank, CancellationToken.None);

        // Assert
        using var ms = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(ms);
        var ws = workbook.Worksheet("S2e-HKD");
        Assert.NotNull(ws);

        bool hasCashBlock = false;
        bool hasBankBlock = false;

        for (int r = 10; r <= 60; r++)
        {
            var text = ws.Cell(r, 1).GetString();
            if (text.Contains("KHỐI I: QUỸ TIỀN MẶT")) hasCashBlock = true;
            if (text.Contains("KHỐI II: TIỀN GỬI NGÂN HÀNG")) hasBankBlock = true;
        }

        Assert.True(hasCashBlock, "S2e phải có Khối I: Quỹ tiền mặt");
        Assert.True(hasBankBlock, "S2e phải có Khối II: Tiền gửi ngân hàng");
    }

    [Fact]
    public async Task ExportTaxSummaryToExcel_WithS3aBookType_ShouldContain8TaxTypesAnd12Columns()
    {
        // Arrange
        var filter = new HouseholdTaxFilterDto { Year = 2026, PeriodType = TaxPeriodType.Quarter, PeriodNumber = 1 };

        // Act
        var bytes = await _exportService.ExportTaxSummaryToExcelAsync(filter, AccountingBookType.S3a_TaxObligation, CancellationToken.None);

        // Assert
        using var ms = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(ms);
        var ws = workbook.Worksheet("S3a-HKD");
        Assert.NotNull(ws);

        // Kiểm tra các sắc thuế
        bool hasVat = false;
        bool hasPit = false;
        bool hasSpecial = false;
        bool hasExportImport = false;
        bool hasEnv = false;
        bool hasResource = false;
        bool hasLand = false;
        bool hasMonBai = false;

        for (int r = 12; r <= 22; r++)
        {
            var taxName = ws.Cell(r, 2).GetString().ToLowerInvariant();
            if (taxName.Contains("gtgt")) hasVat = true;
            if (taxName.Contains("tncn")) hasPit = true;
            if (taxName.Contains("ttđb") || taxName.Contains("tiêu thụ đặc biệt")) hasSpecial = true;
            if (taxName.Contains("xuất khẩu") || taxName.Contains("xnk")) hasExportImport = true;
            if (taxName.Contains("môi trường") || taxName.Contains("bvmt")) hasEnv = true;
            if (taxName.Contains("tài nguyên")) hasResource = true;
            if (taxName.Contains("đất")) hasLand = true;
            if (taxName.Contains("môn bài")) hasMonBai = true;
        }

        Assert.True(hasVat, "S3a phải theo dõi thuế GTGT");
        Assert.True(hasPit, "S3a phải theo dõi thuế TNCN");
        Assert.True(hasSpecial, "S3a phải theo dõi thuế TTĐB");
        Assert.True(hasExportImport, "S3a phải theo dõi thuế XNK");
        Assert.True(hasEnv, "S3a phải theo dõi thuế BVMT");
        Assert.True(hasResource, "S3a phải theo dõi thuế Tài nguyên");
        Assert.True(hasLand, "S3a phải theo dõi thuế đất");
        Assert.True(hasMonBai, "S3a phải theo dõi lệ phí môn bài");
    }

    [Fact]
    public async Task ExportTaxSummaryToExcel_DefaultOverload_AndPeriodTypes_ShouldGenerateExcelBytes()
    {
        // 1. Default overload (filter only)
        var filterQuarter = new HouseholdTaxFilterDto
        {
            Year = 2026,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 1
        };
        var bytesQuarter = await _exportService.ExportTaxSummaryToExcelAsync(filterQuarter);
        Assert.NotNull(bytesQuarter);
        Assert.NotEmpty(bytesQuarter);

        // 2. Month period
        var filterMonth = new HouseholdTaxFilterDto
        {
            Year = 2026,
            PeriodType = TaxPeriodType.Month,
            PeriodNumber = 5
        };
        var bytesMonth = await _exportService.ExportTaxSummaryToExcelAsync(filterMonth);
        Assert.NotNull(bytesMonth);
        Assert.NotEmpty(bytesMonth);

        // 3. Year period
        var filterYear = new HouseholdTaxFilterDto
        {
            Year = 2026,
            PeriodType = TaxPeriodType.Year,
            PeriodNumber = 1
        };
        var bytesYear = await _exportService.ExportTaxSummaryToExcelAsync(filterYear);
        Assert.NotNull(bytesYear);
        Assert.NotEmpty(bytesYear);
    }

    [Theory]
    [InlineData(AccountingBookType.S1a_RevenueGroup1)]
    [InlineData(AccountingBookType.S2a_RevenueGroup2)]
    [InlineData(AccountingBookType.S2b_RevenueDeclaration)]
    [InlineData(AccountingBookType.S2c_RevenueAndExpense)]
    [InlineData(AccountingBookType.S2d_MaterialsAndGoods)]
    [InlineData(AccountingBookType.S2e_CashAndBank)]
    [InlineData(AccountingBookType.S3a_TaxObligation)]
    public async Task ExportTaxSummaryToExcel_SpecificBookTypes_ShouldGenerateValidWorkbook(AccountingBookType bookType)
    {
        var filter = new HouseholdTaxFilterDto
        {
            Year = 2026,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 1,
            BranchId = _branchId
        };

        var bytes = await _exportService.ExportTaxSummaryToExcelAsync(filter, bookType, CancellationToken.None);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        using var ms = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(ms);
        Assert.NotEmpty(workbook.Worksheets);
    }

    [Fact]
    public async Task ExportTaxSummaryToExcel_AllBranches_GeneratesMultiBranchSheets()
    {
        var filter = new HouseholdTaxFilterDto
        {
            Year = 2026,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 1,
            BranchId = null // All branches
        };

        var bytes = await _exportService.ExportTaxSummaryToExcelAsync(filter);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        using var ms = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(ms);
        Assert.True(workbook.Worksheets.Count >= 7);
    }

    [Fact]
    public async Task BuildSheets_DirectCalls_PopulatedAndEmpty_ExecutesAllBranches()
    {
        var filter = new HouseholdTaxFilterDto
        {
            Year = 2026,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 1,
            BranchId = _branchId
        };
        var summary = await _mockTaxService.Object.GetTaxSummaryAsync(filter);

        var tenant = _context.Tenants.IgnoreQueryFilters().First();
        var branch = _context.Branches.IgnoreQueryFilters().First();
        var orders = _context.Orders.IgnoreQueryFilters().Include(o => o.Details).ToList();
        var receipts = _context.GoodsReceipts.IgnoreQueryFilters().Include(g => g.Lines).ToList();
        var cashEntries = _context.CashbookEntries.IgnoreQueryFilters().ToList();
        var expenses = new List<ExpenseVoucher>
        {
            new()
            {
                Id = Guid.NewGuid(),
                VoucherCode = "PC-001",
                ExpenseDate = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc),
                BeneficiaryName = "Chủ nhà",
                Category = "Tiền thuê mặt bằng cửa hàng",
                Amount = 20000m,
                IsTaxDeductible = true
            }
        };
        var snapshots = new List<HouseholdTaxSnapshot>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Year = 2026,
                PeriodNumber = 1,
                PeriodType = TaxPeriodType.Quarter,
                VatAmount = 1500m,
                PitAmount = 750m,
                TotalTaxAmount = 2250m
            }
        };

        // 1. Populated calls
        using (var wb = new XLWorkbook())
        {
            TaxExportService.BuildS1aSheet(wb, summary, filter, orders, tenant, branch);
            TaxExportService.BuildS2aSheet(wb, summary, filter, orders, tenant, branch);
            TaxExportService.BuildS2bSheet(wb, summary, filter, orders, tenant, branch);
            TaxExportService.BuildS2cSheet(wb, summary, filter, orders, expenses, tenant, branch);
            TaxExportService.BuildS2dSheet(wb, summary, filter, receipts, orders, expenses, tenant, branch);
            TaxExportService.BuildS2eSheet(wb, summary, filter, cashEntries, orders, expenses, tenant, branch);
            TaxExportService.BuildS3aSheet(wb, summary, filter, snapshots, expenses, tenant, branch);
            Assert.Equal(7, wb.Worksheets.Count);
        }

        // 2. Empty data calls
        using (var wbEmpty = new XLWorkbook())
        {
            var emptyOrders = new List<Order>();
            var emptyReceipts = new List<GoodsReceipt>();
            var emptyCash = new List<CashbookEntry>();
            var emptyExpenses = new List<ExpenseVoucher>();
            var emptySnapshots = new List<HouseholdTaxSnapshot>();

            TaxExportService.BuildS1aSheet(wbEmpty, summary, filter, emptyOrders, tenant, null);
            TaxExportService.BuildS2aSheet(wbEmpty, summary, filter, emptyOrders, null, null);
            TaxExportService.BuildS2bSheet(wbEmpty, summary, filter, emptyOrders, null, branch);
            TaxExportService.BuildS2cSheet(wbEmpty, summary, filter, emptyOrders, emptyExpenses, null, null);
            TaxExportService.BuildS2dSheet(wbEmpty, summary, filter, emptyReceipts, emptyOrders, emptyExpenses, null, null);
            TaxExportService.BuildS2eSheet(wbEmpty, summary, filter, emptyCash, emptyOrders, emptyExpenses, null, null);
            TaxExportService.BuildS3aSheet(wbEmpty, summary, filter, emptySnapshots, emptyExpenses, null, null);
            Assert.Equal(7, wbEmpty.Worksheets.Count);
        }
    }

    [Fact]
    public async Task ExportTaxSummaryToExcel_S2dAndS2e_ShouldUseRealOpeningBalancesAndNeverFabricateData()
    {
        // Tạo DbContext gắn ITenantContext đúng như runtime thực tế để query filter trả dữ liệu của tenant đang đăng nhập
        var tenantId = "tenant-s2d-s2e-real";
        var tenantContextMock = new Mock<ITenantContext>();
        tenantContextMock.SetupGet(c => c.CurrentTenantId).Returns(tenantId);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        using var tenantDb = new AppDbContext(options, tenantContextMock.Object);

        var tenant = new Tenant { Id = tenantId, Name = "HKD Thực Tế", TaxCode = "0311223344", Address = "TP.HCM" };
        var branch = new Branch { Id = Guid.NewGuid(), TenantId = tenantId, BranchCode = "CN01", Name = "CN1", Address = "TP.HCM", IsActive = true };
        var supplier = new Supplier { Id = Guid.NewGuid(), TenantId = tenantId, Code = "NCC-01", Name = "NCC Thực", Status = SupplierStatus.Active };
        var product = new Product { Id = Guid.NewGuid(), TenantId = tenantId, ProductCode = "SP-01", Barcode = "8930001", Name = "Hàng A", BaseUnit = "Hộp", CostPrice = 8000m, SellingPrice = 10000m, Status = ProductStatus.Active };
        tenantDb.Tenants.Add(tenant);
        tenantDb.Branches.Add(branch);
        tenantDb.Suppliers.Add(supplier);
        tenantDb.Products.Add(product);

        var receiptQ1 = new GoodsReceipt
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            BranchId = branch.Id,
            SupplierId = supplier.Id,
            Code = "PN-Q1-001",
            Status = "COMPLETED",
            ReceivedAtUtc = new DateTime(2026, 1, 10, 9, 0, 0, DateTimeKind.Utc),
            GrandTotal = 800000m
        };
        receiptQ1.Lines.Add(new GoodsReceiptLine { Id = Guid.NewGuid(), GoodsReceiptId = receiptQ1.Id, ProductId = product.Id, AcceptedQty = 100m, DeliveredQty = 100m, OrderedQty = 100m });
        tenantDb.GoodsReceipts.Add(receiptQ1);

        var orderQ1 = new Order
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            BranchId = branch.Id,
            OrderCode = "DH-Q1-001",
            TotalAmount = 50000m,
            DiscountAmount = 5000m,
            PaymentStatus = "PAID",
            Status = "COMPLETED",
            CreatedAt = new DateTime(2026, 1, 15, 8, 30, 0, DateTimeKind.Utc)
        };
        orderQ1.Details.Add(new OrderDetail { Id = Guid.NewGuid(), OrderId = orderQ1.Id, ProductId = product.Id, ProductName = product.Name, UnitName = product.BaseUnit, Qty = 5m, UnitPrice = 10000m, CostPriceAtSale = 8000m });
        tenantDb.Orders.Add(orderQ1);

        tenantDb.CashbookEntries.AddRange(
            new CashbookEntry
            {
                Id = Guid.NewGuid(), TenantId = tenantId, BranchId = branch.Id, EntryCode = "PT-001",
                AccountType = FinanceAccountType.Till, EntryType = CashbookEntryType.Receipt,
                Amount = 45000m, OccurredAtUtc = new DateTime(2026, 1, 15, 9, 0, 0, DateTimeKind.Utc),
                Description = "Thu tiền mặt Q1", SourceType = "POS", ReferenceCode = "DH-Q1-001"
            },
            new CashbookEntry
            {
                Id = Guid.NewGuid(), TenantId = tenantId, BranchId = branch.Id, EntryCode = "PC-001",
                AccountType = FinanceAccountType.Bank, EntryType = CashbookEntryType.Payment,
                Amount = 15000000m, OccurredAtUtc = new DateTime(2026, 1, 16, 10, 0, 0, DateTimeKind.Utc),
                Description = "Chi ngân hàng Q1", SourceType = "TAX", ReferenceCode = "TT-001"
            },
            new CashbookEntry
            {
                Id = Guid.NewGuid(), TenantId = tenantId, BranchId = branch.Id, EntryCode = "PT-002",
                AccountType = FinanceAccountType.Bank, EntryType = CashbookEntryType.Receipt,
                Amount = 90000m, OccurredAtUtc = new DateTime(2026, 4, 20, 10, 30, 0, DateTimeKind.Utc),
                Description = "Thu ngân hàng Q2", SourceType = "POS", ReferenceCode = "DH-Q2-001"
            });
        await tenantDb.SaveChangesAsync();

        var tenantExportService = new TaxExportService(_mockTaxService.Object, _mockTaxRepo.Object, tenantDb, tenantContextMock.Object);

        // 1. Q1 has no prior transactions -> opening inventory S2d = 0 / 0 (no fake 5000 / 150,000,000)
        var q1Filter = new HouseholdTaxFilterDto { Year = 2026, PeriodType = TaxPeriodType.Quarter, PeriodNumber = 1 };
        var q1S2dBytes = await tenantExportService.ExportTaxSummaryToExcelAsync(q1Filter, AccountingBookType.S2d_MaterialsAndGoods, CancellationToken.None);
        using (var ms = new MemoryStream(q1S2dBytes))
        using (var wb = new XLWorkbook(ms))
        {
            var ws = wb.Worksheet("S2d-HKD");
            Assert.Equal("Số dư đầu kỳ", ws.Cell(12, 4).GetString());
            Assert.Equal(0m, ws.Cell(12, 11).GetValue<decimal>());
            Assert.Equal(0m, ws.Cell(12, 12).GetValue<decimal>());
        }

        // 2. Q2 has prior Q1 receipt (100 qty, 800,000) minus order (5 qty, 40,000) -> opening = 95 qty / 760,000
        var q2Filter = new HouseholdTaxFilterDto { Year = 2026, PeriodType = TaxPeriodType.Quarter, PeriodNumber = 2 };
        var q2S2dBytes = await tenantExportService.ExportTaxSummaryToExcelAsync(q2Filter, AccountingBookType.S2d_MaterialsAndGoods, CancellationToken.None);
        using (var ms = new MemoryStream(q2S2dBytes))
        using (var wb = new XLWorkbook(ms))
        {
            var ws = wb.Worksheet("S2d-HKD");
            Assert.Equal(95m, ws.Cell(12, 11).GetValue<decimal>());
            Assert.Equal(760000m, ws.Cell(12, 12).GetValue<decimal>());
        }

        // 3. Q3 has NO CashbookEntries in Q3, only prior Q1/Q2 cash (+45,000 Till), bank (-15,000,000 + 90,000 = -14,910,000)
        // Must compute real opening balances (NO 20,000,000 / 50,000,000 and NO fabricated 60%/40% split)
        var q3Filter = new HouseholdTaxFilterDto { Year = 2026, PeriodType = TaxPeriodType.Quarter, PeriodNumber = 3 };
        var q3S2eBytes = await tenantExportService.ExportTaxSummaryToExcelAsync(q3Filter, AccountingBookType.S2e_CashAndBank, CancellationToken.None);
        using (var ms = new MemoryStream(q3S2eBytes))
        using (var wb = new XLWorkbook(ms))
        {
            var ws = wb.Worksheet("S2e-HKD");
            var allCellStrings = ws.CellsUsed().Select(c => c.GetString()).ToList();

            Assert.DoesNotContain(allCellStrings, s => s is "THU-TM" or "CHI-TM" or "CK-BAN" or "CK-CHI");
            Assert.Contains(allCellStrings, s => s.Contains("Không phát sinh giao dịch trong kỳ"));

            var cashInitRow = ws.RowsUsed().First(r => r.Cell(3).GetString().Contains("A. Số dư đầu kỳ Quỹ tiền mặt"));
            Assert.Equal(45000m, cashInitRow.Cell(4).GetValue<decimal>());

            var bankInitRow = ws.RowsUsed().First(r => r.Cell(3).GetString().Contains("A. Số dư đầu kỳ Tiền gửi ngân hàng"));
            Assert.Equal(-14910000m, bankInitRow.Cell(4).GetValue<decimal>());
        }
    }

    [Fact]
    public async Task ExportTaxSummaryToExcel_SummarySheet_ShouldNotMultiplyRatesBy100OrDoubleDeductCogs()
    {
        // Case 1: RevenuePercentage mode (PitRate = 0.5% already in percentage units)
        var revSummary = new HouseholdTaxSummaryDto
        {
            Year = 2026,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 1,
            HouseholdGroup = HouseholdTaxGroup.Group2_1BTo3B,
            CalculationMethod = TaxCalculationMethod.RevenuePercentage,
            TaxableRevenueInPeriod = 400_000_000m,
            VatRate = 1.0m,
            VatAmount = 4_000_000m,
            PitRate = 0.5m,
            PitAmount = 2_000_000m,
            TotalTaxAmount = 6_000_000m,
            VatBreakdown = new VatBreakdownDto
            {
                RevenueStandardRate10 = 400_000_000m,
                TaxAmountStandardRate10 = 4_000_000m
            }
        };
        _mockTaxService
            .Setup(s => s.GetTaxSummaryAsync(It.IsAny<HouseholdTaxFilterDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(revSummary);

        var revBytes = await _exportService.ExportTaxSummaryToExcelAsync(
            new HouseholdTaxFilterDto { Year = 2026, PeriodType = TaxPeriodType.Quarter, PeriodNumber = 1 },
            null,
            CancellationToken.None);

        using (var ms = new MemoryStream(revBytes))
        using (var wb = new XLWorkbook(ms))
        {
            var ws = wb.Worksheet("Tổng hợp (Tỷ lệ % doanh thu)");
            Assert.Equal("0.5%", ws.Cell(5, 4).GetString());

            // Also verify RenderCircularHeader does not use fake MST 0123456789 when tenantContext is null
            var s1a = wb.Worksheet("S1a-HKD");
            Assert.Equal("Mã số thuế: Chưa cập nhật", s1a.Cell("A4").GetString());

            // Verify S2a-HKD formulas use invariant decimal point for VAT and exact reconciled PIT matching summary.PitAmount
            var s2a = wb.Worksheet("S2a-HKD");
            var s2aFormulas = s2a.CellsUsed(c => c.HasFormula).Select(c => c.FormulaA1).ToList();
            Assert.Contains(s2aFormulas, f => f.Contains("* 0.01, 0"));
            var totalPitRow = s2a.RowsUsed().First(r => r.Cell(1).GetString().Contains("TỔNG SỐ THUẾ TNCN PHẢI NỘP TOÀN BỘ"));
            Assert.Equal(2_000_000m, totalPitRow.Cell(4).GetValue<decimal>());
        }

        // Case 2: DeclarationBased mode (DeductibleExpensesInPeriod = CogsAmount + OpexAmount)
        var declSummary = new HouseholdTaxSummaryDto
        {
            Year = 2026,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 1,
            HouseholdGroup = HouseholdTaxGroup.Group3_3BTo50B,
            CalculationMethod = TaxCalculationMethod.DeclarationBased,
            TaxableRevenueInPeriod = 500_000_000m,
            VatRate = 1.0m,
            VatAmount = 5_000_000m,
            PitRate = 15m,
            PitAmount = 22_500_000m,
            TotalTaxAmount = 27_500_000m,
            CogsAmount = 300_000_000m,
            OpexAmount = 50_000_000m,
            DeductibleExpensesInPeriod = 350_000_000m
        };
        _mockTaxService
            .Setup(s => s.GetTaxSummaryAsync(It.IsAny<HouseholdTaxFilterDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(declSummary);

        var declBytes = await _exportService.ExportTaxSummaryToExcelAsync(
            new HouseholdTaxFilterDto { Year = 2026, PeriodType = TaxPeriodType.Quarter, PeriodNumber = 1 },
            null,
            CancellationToken.None);

        using (var ms = new MemoryStream(declBytes))
        using (var wb = new XLWorkbook(ms))
        {
            var ws = wb.Worksheet("Tổng hợp (Mô hình Kê khai)");
            // Row 3 = VAT line (index 2), Row 4 = COGS (index 3), Row 5 = OPEX (index 4), Row 6 = Net profit (index 5), Row 7 = PIT (index 6)
            Assert.Equal("1%", ws.Cell(3, 4).GetString());
            Assert.Equal(300_000_000m, ws.Cell(4, 3).GetValue<decimal>());
            Assert.Equal(50_000_000m, ws.Cell(5, 3).GetValue<decimal>());
            Assert.Equal(150_000_000m, ws.Cell(6, 3).GetValue<decimal>()); // 500M - 300M - 50M (no COGS double deduction)
            Assert.Equal("15%", ws.Cell(7, 4).GetString());
        }
    }

    [Fact]
    public async Task ExportTaxSummaryToExcel_MultiTenantIsolation_ShouldNotLeakOtherTenantNameOrTaxCode()
    {
        // BUG-077: Seed Tenant B alongside existing Tenant A in the same database
        var tenantB = new Tenant
        {
            Id = "tenant-b-isolated",
            Name = "Hộ Kinh Doanh B Độc Lập",
            TaxCode = "0399887766",
            Address = "789 Lê Lợi, Quận 3, TP. Hồ Chí Minh"
        };
        _context.Tenants.Add(tenantB);
        await _context.SaveChangesAsync();

        var tenantContextMock = new Mock<ITenantContext>();
        tenantContextMock.SetupGet(c => c.CurrentTenantId).Returns(tenantB.Id);

        var isolatedService = new TaxExportService(
            _mockTaxService.Object,
            _mockTaxRepo.Object,
            _context,
            tenantContextMock.Object);

        var bytes = await isolatedService.ExportTaxSummaryToExcelAsync(
            new HouseholdTaxFilterDto { Year = 2026, PeriodType = TaxPeriodType.Quarter, PeriodNumber = 1 },
            AccountingBookType.S1a_RevenueGroup1,
            CancellationToken.None);

        using var ms = new MemoryStream(bytes);
        using var wb = new XLWorkbook(ms);
        var ws = wb.Worksheet("S1a-HKD");

        Assert.Contains("Hộ Kinh Doanh B Độc Lập", ws.Cell("A2").GetString());
        Assert.Contains("0399887766", ws.Cell("A4").GetString());
        Assert.DoesNotContain("Siêu Thị Mini MT", ws.Cell("A2").GetString());
        Assert.DoesNotContain("0109988776", ws.Cell("A4").GetString());
    }
}
