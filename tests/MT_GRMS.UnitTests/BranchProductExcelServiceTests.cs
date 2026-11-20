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
    public async Task ExportAsync_ValidBranch_ReturnsExcelFileDto_WithSupplierStyleHeaders()
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
                    Unit = "Chai",
                    RetailPrice = 15000m,
                    MinOrderQuantity = 6,
                    PrimarySupplierCode = "NCC-01",
                    PrimarySupplierName = "Nhà cung cấp 1",
                    CostPrice = 12000m,
                    AllowRepack = true,
                    ProcessedSellingPricePerKg = 25000m,
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
                    Unit = "Cái",
                    RetailPrice = 20000m,
                    MinOrderQuantity = 1,
                    UnitPrices = new List<BranchProductUnitPriceDto>()
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
        Assert.StartsWith("SP_Theo_CH_", result.FileName);

        using var ms = new MemoryStream(result.Content);
        using var wb = new XLWorkbook(ms);
        var ws = wb.Worksheet(BranchProductExcelService.SheetName);
        Assert.Equal("Mã hàng", ws.Cell(1, 1).GetString());
        Assert.Equal("Tên hàng", ws.Cell(1, 2).GetString());
        Assert.Equal("Mã CH", ws.Cell(1, 3).GetString());
        Assert.Equal("Tên CH", ws.Cell(1, 4).GetString());
        Assert.Equal("Đơn vị", ws.Cell(1, 5).GetString());
        Assert.Equal("Giá bán", ws.Cell(1, 6).GetString());
        Assert.Equal("MOQ", ws.Cell(1, 7).GetString());
        Assert.Equal("ProductId", ws.Cell(1, 8).GetString());

        Assert.Equal("SKU-01", ws.Cell(2, 1).GetString());
        Assert.Equal("Chai", ws.Cell(2, 5).GetString());
        Assert.Equal(15000m, ws.Cell(2, 6).GetValue<decimal>());
        Assert.Equal(6, ws.Cell(2, 7).GetValue<int>());
        Assert.Equal("NCC-01", ws.Cell(2, 9).GetString());
        Assert.Equal("Nhà cung cấp 1", ws.Cell(2, 10).GetString());
        Assert.Equal(15, ws.LastColumnUsed()!.ColumnNumber());
        Assert.Equal(12000m, ws.Cell(2, 11).GetValue<decimal>());
        Assert.Equal("Chai: 15.000 đ · Lốc: 85.000 đ · Khay: 25.000 đ/kg", ws.Cell(2, 12).GetString());
        Assert.Equal("Chai", ws.Cell(2, 13).GetString());
        Assert.Equal("Hoạt động", ws.Cell(2, 14).GetString());

        var prices = wb.Worksheet("Gia_Theo_Don_Vi");
        Assert.True(prices.Protection.IsProtected);
        Assert.True(prices.Cell(3, 6).Style.Protection.Locked);
        Assert.False(prices.Cell(3, 8).Style.Protection.Locked);
        Assert.Equal("Lốc", prices.Cell(3, 6).GetString());
        prices.Cell(3, 8).Value = 90000m;

        using var edited = new MemoryStream();
        wb.SaveAs(edited);
        edited.Position = 0;
        var preview = await _service.ImportAsync(branchId, edited, "edited.xlsx",
            new BranchProductQueryFilter { PreviewOnly = true });
        var change = Assert.Single(preview.Rows);
        Assert.True(preview.IsPreview);
        Assert.Equal("OK", change.Status);
        Assert.StartsWith("Lốc:", change.UnitPricesSummary);

        prices.Cell(3, 6).Value = "Đơn vị sai";
        using var invalidFile = new MemoryStream();
        wb.SaveAs(invalidFile);
        invalidFile.Position = 0;
        var invalid = await _service.ImportAsync(branchId, invalidFile, "invalid.xlsx",
            new BranchProductQueryFilter { PreviewOnly = true });
        Assert.Equal("ERROR", Assert.Single(invalid.Rows).Status);
        _mockBranchProductService.Verify(service => service.UpdateProductAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<UpdateBranchProductCommand>(), It.IsAny<CancellationToken>()), Times.Never);
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
    public async Task ImportAsync_MissingSkuOrPriceHeader_ThrowsBusinessException()
    {
        var branchId = Guid.NewGuid();
        var branch = new Branch { Id = branchId, BranchCode = "CH-01", Name = "Store 1", IsDeleted = false };
        _mockRepo.Setup(r => r.GetBranchByIdAsync(branchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(branch);

        using var stream = CreateWorkbookStream(ws =>
        {
            ws.Cell(1, 1).Value = "ProductId";
            ws.Cell(1, 2).Value = "Mã hàng";
            // Missing Giá bán
        });

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.ImportAsync(branchId, stream, "test.xlsx"));
        Assert.Equal("MISSING_HEADERS", ex.ErrorCode);
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
            ws.Cell(1, 1).Value = "Mã hàng";
            ws.Cell(1, 2).Value = "Tên hàng";
            ws.Cell(1, 3).Value = "Mã CH";
            ws.Cell(1, 4).Value = "Tên CH";
            ws.Cell(1, 5).Value = "Đơn vị";
            ws.Cell(1, 6).Value = "Giá bán";
            ws.Cell(1, 7).Value = "MOQ";
            ws.Cell(1, 8).Value = "ProductId";
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
            ws.Cell(1, 1).Value = "Mã hàng";
            ws.Cell(1, 2).Value = "Tên hàng";
            ws.Cell(1, 3).Value = "Mã CH";
            ws.Cell(1, 4).Value = "Tên CH";
            ws.Cell(1, 5).Value = "Đơn vị";
            ws.Cell(1, 6).Value = "Giá bán";
            ws.Cell(1, 7).Value = "MOQ";
            ws.Cell(1, 8).Value = "ProductId";

            // Row 2: Valid
            ws.Cell(2, 1).Value = "SKU-01";
            ws.Cell(2, 2).Value = "Product 1";
            ws.Cell(2, 5).Value = "Chai";
            ws.Cell(2, 6).Value = "18,000 đ";
            ws.Cell(2, 7).Value = 1;
            ws.Cell(2, 8).Value = productId1.ToString();

            // Row 3: Unassigned
            ws.Cell(3, 1).Value = "SKU-UNKNOWN";
            ws.Cell(3, 6).Value = "20000";
            ws.Cell(3, 8).Value = Guid.NewGuid().ToString();

            // Row 4: Invalid price
            ws.Cell(4, 1).Value = "SKU-01";
            ws.Cell(4, 6).Value = "-1000";
            ws.Cell(4, 8).Value = productId1.ToString();

            // Row 5: Zero price
            ws.Cell(5, 1).Value = "SKU-01";
            ws.Cell(5, 6).Value = "0";
            ws.Cell(5, 8).Value = productId1.ToString();
        });

        var result = await _service.ImportAsync(branchId, stream, "branch_import.xlsx");

        Assert.NotNull(result);
        Assert.Equal(4, result.Rows.Count);
        Assert.Equal(1, result.Summary.SuccessCount);
        Assert.Equal(3, result.Summary.FailedCount);
        Assert.Equal("OK", result.Rows[0].Status);
        Assert.Equal("ERROR", result.Rows[1].Status);
        Assert.Equal("ERROR", result.Rows[2].Status);
        Assert.Equal("ERROR", result.Rows[3].Status);

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
                new() { UnitId = Guid.NewGuid(), UnitName = "Hộp", Price = 10000m, IsBaseUnit = true, ConversionRate = 1m }
            }
        };

        var paged = new PagedResult<BranchProductDto>
        {
            Items = new List<BranchProductDto> { bp },
            TotalItems = 1
        };
        _mockBranchProductService.Setup(s => s.GetBranchProductsAsync(branchId, It.IsAny<BranchProductQueryFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(paged);

        using var stream1 = CreateWorkbookStream(ws =>
        {
            ws.Cell(1, 1).Value = "Mã SKU";
            ws.Cell(1, 2).Value = "Tên sản phẩm";
            ws.Cell(1, 3).Value = "Mã CH";
            ws.Cell(1, 4).Value = "Tên CH";
            ws.Cell(1, 5).Value = "ĐVT";
            ws.Cell(1, 6).Value = "Giá bán lẻ";
            ws.Cell(1, 7).Value = "MOQ";
            ws.Cell(1, 8).Value = "Product ID";

            ws.Cell(2, 1).Value = "SKU-BP";
            ws.Cell(2, 2).Value = "Product BP";
            ws.Cell(2, 3).Value = "CN01";
            ws.Cell(2, 4).Value = "Chi nhánh 1";
            ws.Cell(2, 5).Value = "Hộp";
            ws.Cell(2, 6).Value = "12000";
            ws.Cell(2, 7).Value = 1;
            ws.Cell(2, 8).Value = productId.ToString();
        });

        var res1 = await _service.ImportAsync(branchId, stream1, "alias1.xlsx");
        Assert.Equal(1, res1.Summary.SuccessCount);

        using var stream2 = CreateWorkbookStream(ws =>
        {
            ws.Cell(1, 1).Value = "ProductCode";
            ws.Cell(1, 2).Value = "ProductName";
            ws.Cell(1, 3).Value = "BranchCode";
            ws.Cell(1, 4).Value = "BranchName";
            ws.Cell(1, 5).Value = "Unit";
            ws.Cell(1, 6).Value = "RetailPrice";
            ws.Cell(1, 7).Value = "MinOrder";
            ws.Cell(1, 8).Value = "Id";

            ws.Cell(2, 1).Value = "SKU-BP";
            ws.Cell(2, 2).Value = "Product BP";
            ws.Cell(2, 6).Value = "15000";
            ws.Cell(2, 8).Value = productId.ToString();
        });

        var res2 = await _service.ImportAsync(branchId, stream2, "alias2.xlsx");
        Assert.Equal(1, res2.Summary.SuccessCount);
    }

    [Fact]
    public async Task ImportAsync_ProductOutOfScreenFilter_ReturnsError()
    {
        var branchId = Guid.NewGuid();
        var branch = new Branch { Id = branchId, BranchCode = "CH-01", Name = "Store 1", IsDeleted = false };
        _mockRepo.Setup(r => r.GetBranchByIdAsync(branchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(branch);

        var inFilterId = Guid.NewGuid();
        var outFilterId = Guid.NewGuid();
        var unitId = Guid.NewGuid();

        var paged = new PagedResult<BranchProductDto>
        {
            Items = new List<BranchProductDto>
            {
                new()
                {
                    ProductId = inFilterId,
                    Sku = "SKU-IN",
                    Name = "In Filter",
                    Status = "Active",
                    UnitPrices =
                    [
                        new() { UnitId = unitId, UnitName = "Chai", Price = 10000m, IsBaseUnit = true, ConversionRate = 1m }
                    ]
                },
                new()
                {
                    ProductId = outFilterId,
                    Sku = "SKU-OUT",
                    Name = "Out Filter",
                    Status = "Active",
                    UnitPrices =
                    [
                        new() { UnitId = unitId, UnitName = "Chai", Price = 10000m, IsBaseUnit = true, ConversionRate = 1m }
                    ]
                }
            },
            TotalItems = 2
        };
        _mockBranchProductService.Setup(s => s.GetBranchProductsAsync(branchId, It.IsAny<BranchProductQueryFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(paged);

        using var stream = CreateWorkbookStream(ws =>
        {
            ws.Cell(1, 1).Value = "Mã hàng";
            ws.Cell(1, 2).Value = "Tên hàng";
            ws.Cell(1, 3).Value = "Mã CH";
            ws.Cell(1, 4).Value = "Tên CH";
            ws.Cell(1, 5).Value = "Đơn vị";
            ws.Cell(1, 6).Value = "Giá bán";
            ws.Cell(1, 7).Value = "MOQ";
            ws.Cell(1, 8).Value = "ProductId";

            ws.Cell(2, 1).Value = "SKU-OUT";
            ws.Cell(2, 2).Value = "Out Filter";
            ws.Cell(2, 6).Value = "12000";
            ws.Cell(2, 8).Value = outFilterId.ToString();
        });

        var filter = new BranchProductQueryFilter { Skus = ["SKU-IN"] };
        var result = await _service.ImportAsync(branchId, stream, "filter.xlsx", filter);

        Assert.Equal(1, result.Rows.Count);
        Assert.Equal("ERROR", result.Rows[0].Status);
        Assert.Contains("bộ lọc", result.Rows[0].Message, StringComparison.OrdinalIgnoreCase);
        _mockBranchProductService.Verify(
            s => s.UpdateProductAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<UpdateBranchProductCommand>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExportAsync_WithSkuFilter_OnlyExportsMatchingRows()
    {
        var branchId = Guid.NewGuid();
        var branch = new Branch { Id = branchId, BranchCode = "CH-01", Name = "Store 1", IsDeleted = false };
        _mockRepo.Setup(r => r.GetBranchByIdAsync(branchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(branch);

        var paged = new PagedResult<BranchProductDto>
        {
            Items =
            [
                new()
                {
                    ProductId = Guid.NewGuid(),
                    BranchCode = "CH-01",
                    BranchName = "Store 1",
                    Sku = "SKU-KEEP",
                    Name = "Keep",
                    RetailPrice = 1m,
                    UnitPrices = [new() { UnitId = Guid.NewGuid(), UnitName = "Chai", Price = 1m, IsBaseUnit = true, ConversionRate = 1m }]
                },
                new()
                {
                    ProductId = Guid.NewGuid(),
                    BranchCode = "CH-01",
                    BranchName = "Store 1",
                    Sku = "SKU-DROP",
                    Name = "Drop",
                    RetailPrice = 2m,
                    UnitPrices = [new() { UnitId = Guid.NewGuid(), UnitName = "Chai", Price = 2m, IsBaseUnit = true, ConversionRate = 1m }]
                }
            ],
            TotalItems = 2
        };
        _mockBranchProductService.Setup(s => s.GetBranchProductsAsync(branchId, It.IsAny<BranchProductQueryFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(paged);

        var file = await _service.ExportAsync(branchId, new BranchProductQueryFilter { Skus = ["SKU-KEEP"] });
        using var ms = new MemoryStream(file.Content);
        using var wb = new XLWorkbook(ms);
        var ws = wb.Worksheet(BranchProductExcelService.SheetName);

        Assert.Equal("SKU-KEEP", ws.Cell(2, 1).GetString());
        Assert.True(string.IsNullOrWhiteSpace(ws.Cell(3, 1).GetString()));
    }
}
