using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Moq;
using MT_GRMS.Application.Tax;
using MT_GRMS.Application.Tax.Dtos;
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

    public Circular152TaxExportServiceTests()
    {
        _mockTaxService = new Mock<IHouseholdTaxService>();
        _mockTaxRepo = new Mock<IHouseholdTaxRepository>();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new AppDbContext(options);

        // Seed basic tenant and branch
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
            Address = "123 Đường Nguyễn Huệ, Quận 1, TP. Hồ Chí Minh"
        };
        _context.Branches.Add(branch);
        _context.SaveChanges();

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
}
