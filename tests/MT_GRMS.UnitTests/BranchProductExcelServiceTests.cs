using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ClosedXML.Excel;
using Moq;
using MT_GRMS.Application.Branches;
using MT_GRMS.Application.Branches.Dtos;
using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Domain.Exceptions;
using MT_GRMS.Infrastructure.Services;
using Xunit;

namespace MT_GRMS.UnitTests;

public class BranchProductExcelServiceTests
{
    private readonly Mock<IBranchProductService> _mockBranchProductService;
    private readonly Mock<IBranchProductRepository> _mockRepo;
    private readonly BranchProductExcelService _service;

    public BranchProductExcelServiceTests()
    {
        _mockBranchProductService = new Mock<IBranchProductService>();
        _mockRepo = new Mock<IBranchProductRepository>();
        _service = new BranchProductExcelService(_mockBranchProductService.Object, _mockRepo.Object);
    }

    private static MemoryStream CreateWorkbookStream(Action<IXLWorksheet> populate)
    {
        var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(BranchProductExcelService.SheetName);
        populate(ws);
        var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;
        return ms;
    }

    [Fact]
    public async Task ExportAsync_BranchNotFoundOrDeleted_ThrowsNotFoundException()
    {
        var branchId = Guid.NewGuid();
        _mockRepo.Setup(r => r.GetBranchByIdAsync(branchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Branch?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _service.ExportAsync(branchId));

        var deletedBranch = new Branch { Id = branchId, IsDeleted = true };
        _mockRepo.Setup(r => r.GetBranchByIdAsync(branchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(deletedBranch);

        await Assert.ThrowsAsync<NotFoundException>(() => _service.ExportAsync(branchId));
    }

    [Fact]
    public async Task ExportAsync_ValidBranch_ReturnsExcelFileDto()
    {
        var branchId = Guid.NewGuid();
        var branch = new Branch { Id = branchId, BranchCode = "CH-01", Name = "Store 1", IsDeleted = false };
        _mockRepo.Setup(r => r.GetBranchByIdAsync(branchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(branch);

        var paged = new PagedResult<BranchProductDto>
        {
            Items = new List<BranchProductDto>
            {
                new()
                {
                    ProductId = Guid.NewGuid(),
                    BranchCode = "CH-01",
                    BranchName = "Store 1",
                    Sku = "SKU-01",
                    Name = "Product 1",
                    UnitPrices = new List<BranchProductUnitPriceDto>
                    {
                        new() { UnitId = Guid.NewGuid(), UnitName = "Chai", Price = 15000m, IsBaseUnit = true, ConversionRate = 1m },
                        new() { UnitId = Guid.NewGuid(), UnitName = "Lốc", Price = 85000m, IsBaseUnit = false, ConversionRate = 6m }
                    }
                },
                new()
                {
                    ProductId = Guid.NewGuid(),
                    BranchCode = "CH-01",
                    BranchName = "Store 1",
                    Sku = "SKU-02",
                    Name = "Product 2",
                    UnitPrices = new List<BranchProductUnitPriceDto>() // Empty units
                }
            },
            TotalItems = 2,
            Page = 1,
            PageSize = 10000
        };

        _mockBranchProductService.Setup(s => s.GetBranchProductsAsync(branchId, It.IsAny<BranchProductQueryFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(paged);

        var result = await _service.ExportAsync(branchId);

        Assert.NotNull(result);
        Assert.NotNull(result.Content);
        Assert.True(result.Content.Length > 0);
        Assert.Contains("CH-01", result.FileName);

        using var ms = new MemoryStream(result.Content);
        using var wb = new XLWorkbook(ms);
        var ws = wb.Worksheet(BranchProductExcelService.SheetName);
        Assert.Equal("ProductId", ws.Cell(1, 1).GetString());
        Assert.Equal("SKU", ws.Cell(1, 4).GetString());
        Assert.Equal("Chai", ws.Cell(1, 6).GetString());
        Assert.Equal("Lốc", ws.Cell(1, 7).GetString());
    }

    [Fact]
    public async Task ImportAsync_BranchNotFoundOrDeleted_ThrowsNotFoundException()
    {
        var branchId = Guid.NewGuid();
        _mockRepo.Setup(r => r.GetBranchByIdAsync(branchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Branch?)null);

        using var ms = new MemoryStream(new byte[] { 1, 2, 3 });
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.ImportAsync(branchId, ms, "test.xlsx"));

        var deletedBranch = new Branch { Id = branchId, IsDeleted = true };
        _mockRepo.Setup(r => r.GetBranchByIdAsync(branchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(deletedBranch);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.ImportAsync(branchId, ms, "test.xlsx"));
    }

    [Fact]
    public async Task ImportAsync_NullOrUnreadableStream_ThrowsBusinessException()
    {
        var branchId = Guid.NewGuid();
        var branch = new Branch { Id = branchId, BranchCode = "CH-01", Name = "Store 1", IsDeleted = false };
        _mockRepo.Setup(r => r.GetBranchByIdAsync(branchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(branch);

        await Assert.ThrowsAsync<BusinessException>(() =>
            _service.ImportAsync(branchId, null!, "test.xlsx"));

        var closedStream = new MemoryStream();
        closedStream.Close();
        await Assert.ThrowsAsync<BusinessException>(() =>
            _service.ImportAsync(branchId, closedStream, "test.xlsx"));
    }

    [Fact]
    public async Task ImportAsync_InvalidFormat_ThrowsBusinessException()
    {
        var branchId = Guid.NewGuid();
        var branch = new Branch { Id = branchId, BranchCode = "CH-01", Name = "Store 1", IsDeleted = false };
        _mockRepo.Setup(r => r.GetBranchByIdAsync(branchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(branch);

        using var ms = new MemoryStream(new byte[] { 1, 2, 3 });
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.ImportAsync(branchId, ms, "test.txt"));
        Assert.Equal("INVALID_FORMAT", ex.ErrorCode);
    }

    [Fact]
    public async Task ImportAsync_CorruptExcel_ThrowsBusinessException()
    {
        var branchId = Guid.NewGuid();
        var branch = new Branch { Id = branchId, BranchCode = "CH-01", Name = "Store 1", IsDeleted = false };
        _mockRepo.Setup(r => r.GetBranchByIdAsync(branchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(branch);

        using var ms = new MemoryStream(Encoding.UTF8.GetBytes("not excel"));
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.ImportAsync(branchId, ms, "test.xlsx"));
        Assert.Equal("INVALID_FILE", ex.ErrorCode);
    }

    [Fact]
    public async Task ImportAsync_MissingSkuHeader_ThrowsBusinessException()
    {
        var branchId = Guid.NewGuid();
        var branch = new Branch { Id = branchId, BranchCode = "CH-01", Name = "Store 1", IsDeleted = false };
        _mockRepo.Setup(r => r.GetBranchByIdAsync(branchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(branch);

        using var stream = CreateWorkbookStream(ws =>
        {
            ws.Cell(1, 1).Value = "ProductId";
            // Missing SKU
        });

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.ImportAsync(branchId, stream, "test.xlsx"));
        Assert.Equal("MISSING_HEADERS", ex.ErrorCode);
    }

    [Fact]
    public async Task ImportAsync_MissingUnitColumns_ThrowsBusinessException()
    {
        var branchId = Guid.NewGuid();
        var branch = new Branch { Id = branchId, BranchCode = "CH-01", Name = "Store 1", IsDeleted = false };
        _mockRepo.Setup(r => r.GetBranchByIdAsync(branchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(branch);

        using var stream = CreateWorkbookStream(ws =>
        {
            ws.Cell(1, 1).Value = "ProductId";
            ws.Cell(1, 2).Value = "SKU";
            // Only fixed headers, no unit columns
        });

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.ImportAsync(branchId, stream, "test.xlsx"));
        Assert.Equal("MISSING_UNIT_COLUMNS", ex.ErrorCode);
    }

    [Fact]
    public async Task ImportAsync_EmptyDataRows_ThrowsBusinessException()
    {
        var branchId = Guid.NewGuid();
        var branch = new Branch { Id = branchId, BranchCode = "CH-01", Name = "Store 1", IsDeleted = false };
        _mockRepo.Setup(r => r.GetBranchByIdAsync(branchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(branch);

        _mockBranchProductService.Setup(s => s.GetBranchProductsAsync(branchId, It.IsAny<BranchProductQueryFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<BranchProductDto> { Items = new List<BranchProductDto>() });

        using var stream = CreateWorkbookStream(ws =>
        {
            ws.Cell(1, 1).Value = "ProductId";
            ws.Cell(1, 2).Value = "SKU";
            ws.Cell(1, 3).Value = "Chai";
        });

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.ImportAsync(branchId, stream, "test.xlsx"));
        Assert.Equal("EMPTY_DATA", ex.ErrorCode);
    }

    [Fact]
    public async Task ImportAsync_SuccessAndErrorRows_ProcessesCorrectly()
    {
        var branchId = Guid.NewGuid();
        var branch = new Branch { Id = branchId, BranchCode = "CH-01", Name = "Store 1", IsDeleted = false };
        _mockRepo.Setup(r => r.GetBranchByIdAsync(branchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(branch);

        var productId1 = Guid.NewGuid();
        var unitIdChai = Guid.NewGuid();
        var bp1 = new BranchProductDto
        {
            ProductId = productId1,
            Sku = "SKU-01",
            Name = "Product 1",
            Status = "Active",
            UnitPrices = new List<BranchProductUnitPriceDto>
            {
                new() { UnitId = unitIdChai, UnitName = "Chai", Price = 15000m, IsBaseUnit = true, ConversionRate = 1m }
            }
        };

        var paged = new PagedResult<BranchProductDto>
        {
            Items = new List<BranchProductDto> { bp1 },
            TotalItems = 1
        };
        _mockBranchProductService.Setup(s => s.GetBranchProductsAsync(branchId, It.IsAny<BranchProductQueryFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(paged);

        using var stream = CreateWorkbookStream(ws =>
        {
            ws.Cell(1, 1).Value = "ProductId";
            ws.Cell(1, 2).Value = "MaCH";
            ws.Cell(1, 3).Value = "TenCH";
            ws.Cell(1, 4).Value = "SKU";
            ws.Cell(1, 5).Value = "TenSanPham";
            ws.Cell(1, 6).Value = "Chai";

            // Row 2: Note
            ws.Cell(2, 1).Value = "KHÔNG SỬA CỘT";

            // Row 3: Valid row
            ws.Cell(3, 1).Value = productId1.ToString();
            ws.Cell(3, 4).Value = "SKU-01";
            ws.Cell(3, 5).Value = "Product 1";
            ws.Cell(3, 6).Value = "18,000 đ";

            // Row 4: Unassigned product
            ws.Cell(4, 1).Value = Guid.NewGuid().ToString();
            ws.Cell(4, 4).Value = "SKU-UNKNOWN";
            ws.Cell(4, 5).Value = "Unknown";
            ws.Cell(4, 6).Value = "20000";

            // Row 5: Negative price
            ws.Cell(5, 1).Value = productId1.ToString();
            ws.Cell(5, 4).Value = "SKU-01";
            ws.Cell(5, 6).Value = "-1000";

            // Row 6: Zero price
            ws.Cell(6, 1).Value = productId1.ToString();
            ws.Cell(6, 4).Value = "SKU-01";
            ws.Cell(6, 6).Value = "0";

            // Row 7: No price change filled
            ws.Cell(7, 1).Value = productId1.ToString();
            ws.Cell(7, 4).Value = "SKU-01";
            ws.Cell(7, 6).Value = "";
        });

        var result = await _service.ImportAsync(branchId, stream, "branch_import.xlsx");

        Assert.NotNull(result);
        Assert.Equal(5, result.Rows.Count);
        Assert.Equal(1, result.Summary.SuccessCount);
        Assert.Equal(4, result.Summary.FailedCount);

        // Row 3: OK
        Assert.Equal("OK", result.Rows[0].Status);

        // Row 4: ERROR
        Assert.Equal("ERROR", result.Rows[1].Status);

        // Row 5: ERROR
        Assert.Equal("ERROR", result.Rows[2].Status);

        // Row 6: ERROR
        Assert.Equal("ERROR", result.Rows[3].Status);

        // Row 7: ERROR
        Assert.Equal("ERROR", result.Rows[4].Status);

        _mockBranchProductService.Verify(s => s.UpdateProductAsync(
            branchId,
            productId1,
            It.Is<UpdateBranchProductCommand>(c => c.RetailPrice == 18000m),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ImportAsync_WithHeaderAliases_ShouldNormalizeAndProcessCorrectly()
    {
        var branchId = Guid.NewGuid();
        var branch = new Branch { Id = branchId, BranchCode = "CN01", Name = "Chi nhánh 1" };
        var productId = Guid.NewGuid();

        _mockRepo.Setup(r => r.GetBranchByIdAsync(branchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(branch);

        var bp = new BranchProductDto
        {
            ProductId = productId,
            Sku = "SKU-BP",
            Name = "Product BP",
            Status = "Active",
            UnitPrices = new List<BranchProductUnitPriceDto>
            {
                new() { UnitId = Guid.NewGuid(), UnitName = "Hộp", Price = 10000m, IsBaseUnit = true, ConversionRate = 1m },
                new() { UnitId = Guid.NewGuid(), UnitName = "Thùng", Price = 100000m, IsBaseUnit = false, ConversionRate = 10m }
            }
        };

        var paged = new PagedResult<BranchProductDto>
        {
            Items = new List<BranchProductDto> { bp },
            TotalItems = 1
        };
        _mockBranchProductService.Setup(s => s.GetBranchProductsAsync(branchId, It.IsAny<BranchProductQueryFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(paged);

        // Stream 1: Vietnamese aliases
        using var stream1 = CreateWorkbookStream(ws =>
        {
            ws.Cell(1, 1).Value = "Product ID";
            ws.Cell(1, 2).Value = "Mã CH";
            ws.Cell(1, 3).Value = "Tên CH";
            ws.Cell(1, 4).Value = "Mã SKU";
            ws.Cell(1, 5).Value = "Tên sản phẩm";
            ws.Cell(1, 6).Value = "Hộp";
            ws.Cell(1, 7).Value = "Thùng";

            ws.Cell(2, 1).Value = productId.ToString();
            ws.Cell(2, 2).Value = "CN01";
            ws.Cell(2, 3).Value = "Chi nhánh 1";
            ws.Cell(2, 4).Value = "SKU-BP";
            ws.Cell(2, 5).Value = "Product BP";
            ws.Cell(2, 6).Value = "12000";
            ws.Cell(2, 7).Value = "115000";
        });

        var res1 = await _service.ImportAsync(branchId, stream1, "alias1.xlsx");
        Assert.NotNull(res1);
        Assert.Equal(1, res1.Summary.SuccessCount);

        // Stream 2: English aliases
        using var stream2 = CreateWorkbookStream(ws =>
        {
            ws.Cell(1, 1).Value = "Id";
            ws.Cell(1, 2).Value = "BranchCode";
            ws.Cell(1, 3).Value = "BranchName";
            ws.Cell(1, 4).Value = "ProductCode";
            ws.Cell(1, 5).Value = "ProductName";
            ws.Cell(1, 6).Value = "Hộp";
            ws.Cell(1, 7).Value = "Thùng";

            ws.Cell(2, 1).Value = productId.ToString();
            ws.Cell(2, 2).Value = "CN01";
            ws.Cell(2, 3).Value = "Chi nhánh 1";
            ws.Cell(2, 4).Value = "SKU-BP";
            ws.Cell(2, 5).Value = "Product BP";
            ws.Cell(2, 6).Value = "15000";
            ws.Cell(2, 7).Value = "140000";
        });

        var res2 = await _service.ImportAsync(branchId, stream2, "alias2.xlsx");
        Assert.NotNull(res2);
        Assert.Equal(1, res2.Summary.SuccessCount);
    }
}
