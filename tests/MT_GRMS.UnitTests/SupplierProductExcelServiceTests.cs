using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ClosedXML.Excel;
using Moq;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Suppliers;
using MT_GRMS.Domain.Entities;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Entities.Supplier;
using MT_GRMS.Domain.Exceptions;
using MT_GRMS.Infrastructure.Services;
using Xunit;

namespace MT_GRMS.UnitTests;

public class SupplierProductExcelServiceTests
{
    private readonly Mock<ISupplierRepository> _mockRepo;
    private readonly Mock<IUnitOfWork> _mockUow;
    private readonly SupplierProductExcelService _service;

    public SupplierProductExcelServiceTests()
    {
        _mockRepo = new Mock<ISupplierRepository>();
        _mockUow = new Mock<IUnitOfWork>();
        _service = new SupplierProductExcelService(_mockRepo.Object, _mockUow.Object);
    }

    private static MemoryStream CreateWorkbookStream(Action<IXLWorksheet> populate)
    {
        var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(SupplierProductExcelService.SheetName);
        populate(ws);
        var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;
        return ms;
    }

    [Fact]
    public async Task ExportAsync_SupplierNotFound_ThrowsNotFoundException()
    {
        var supplierId = Guid.NewGuid();
        _mockRepo.Setup(r => r.GetByIdAsync(supplierId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Supplier?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _service.ExportAsync(supplierId));
    }

    [Fact]
    public async Task ExportAsync_ValidSupplier_ReturnsExcelFileDto()
    {
        var supplierId = Guid.NewGuid();
        var productId1 = Guid.NewGuid();
        var productId2 = Guid.NewGuid();

        var supplier = new Supplier
        {
            Id = supplierId,
            Code = "NCC/01",
            Name = "Supplier One"
        };
        _mockRepo.Setup(r => r.GetByIdAsync(supplierId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(supplier);

        var list = new List<SupplierProduct>
        {
            new()
            {
                Id = Guid.NewGuid(),
                SupplierId = supplierId,
                ProductId = productId1,
                Product = new Product { ProductCode = "SKU01", Name = "Product 1", BaseUnit = "Thùng" },
                DeliveryUoM = "Thùng",
                AgreedWholesalePrice = 50000m,
                MinimumOrderQuantity = 5
            },
            new()
            {
                Id = Guid.NewGuid(),
                SupplierId = supplierId,
                ProductId = productId2,
                Product = new Product
                {
                    ProductCode = "SKU02",
                    Name = "Product 2",
                    BaseUnit = "Cái",
                    CostPrice = 32000m
                },
                DeliveryUoM = string.Empty,
                AgreedWholesalePrice = null,
                MinimumOrderQuantity = 0
            }
        };
        _mockRepo.Setup(r => r.GetSupplierProductsForSupplierAsync(supplierId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(list);

        var result = await _service.ExportAsync(supplierId);

        Assert.NotNull(result);
        Assert.NotNull(result.Content);
        Assert.True(result.Content.Length > 0);
        Assert.Contains("SP_Theo_NCC", result.FileName);
        Assert.Contains("NCC_01", result.FileName);

        using var ms = new MemoryStream(result.Content);
        using var wb = new XLWorkbook(ms);
        var ws = wb.Worksheet(SupplierProductExcelService.SheetName);
        Assert.Equal("Mã hàng", ws.Cell(1, 1).GetString());
        Assert.Equal("Giá nhập sỉ", ws.Cell(1, 6).GetString());
        Assert.Equal("SKU01", ws.Cell(2, 1).GetString());
        Assert.Equal(productId1.ToString(), ws.Cell(2, 8).GetString());
        Assert.Equal(50000.0, ws.Cell(2, 6).GetDouble());
        Assert.Equal(5, ws.Cell(2, 7).GetDouble());
        Assert.Equal(32000.0, ws.Cell(3, 6).GetDouble());
        Assert.Equal(1, ws.Cell(3, 7).GetDouble());
    }

    [Fact]
    public async Task ImportAsync_SupplierNotFound_ThrowsNotFoundException()
    {
        var supplierId = Guid.NewGuid();
        _mockRepo.Setup(r => r.GetByIdAsync(supplierId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Supplier?)null);

        using var ms = new MemoryStream(new byte[] { 1, 2, 3 });
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.ImportAsync(supplierId, ms, "test.xlsx"));
    }

    [Fact]
    public async Task ImportAsync_NullOrUnreadableStream_ThrowsBusinessException()
    {
        var supplierId = Guid.NewGuid();
        var supplier = new Supplier { Id = supplierId, Code = "NCC-01", Name = "Test" };
        _mockRepo.Setup(r => r.GetByIdAsync(supplierId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(supplier);

        await Assert.ThrowsAsync<BusinessException>(() =>
            _service.ImportAsync(supplierId, null!, "test.xlsx"));

        var closedStream = new MemoryStream();
        closedStream.Close();
        await Assert.ThrowsAsync<BusinessException>(() =>
            _service.ImportAsync(supplierId, closedStream, "test.xlsx"));
    }

    [Fact]
    public async Task ImportAsync_InvalidFileExtension_ThrowsBusinessException()
    {
        var supplierId = Guid.NewGuid();
        var supplier = new Supplier { Id = supplierId, Code = "NCC-01", Name = "Test" };
        _mockRepo.Setup(r => r.GetByIdAsync(supplierId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(supplier);

        using var ms = new MemoryStream(new byte[] { 1, 2, 3 });
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.ImportAsync(supplierId, ms, "test.pdf"));
        Assert.Equal("INVALID_FORMAT", ex.ErrorCode);
    }

    [Fact]
    public async Task ImportAsync_CorruptedStream_ThrowsBusinessException()
    {
        var supplierId = Guid.NewGuid();
        var supplier = new Supplier { Id = supplierId, Code = "NCC-01", Name = "Test" };
        _mockRepo.Setup(r => r.GetByIdAsync(supplierId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(supplier);

        using var ms = new MemoryStream(Encoding.UTF8.GetBytes("not a zip / excel"));
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.ImportAsync(supplierId, ms, "test.xlsx"));
        Assert.Equal("INVALID_FILE", ex.ErrorCode);
    }

    [Fact]
    public async Task ImportAsync_MissingRequiredHeaders_ThrowsBusinessException()
    {
        var supplierId = Guid.NewGuid();
        var supplier = new Supplier { Id = supplierId, Code = "NCC-01", Name = "Test" };
        _mockRepo.Setup(r => r.GetByIdAsync(supplierId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(supplier);

        using var stream = CreateWorkbookStream(ws =>
        {
            ws.Cell(1, 1).Value = "Mã SKU";
            // Missing GiaNhapSi and MOQ
        });

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.ImportAsync(supplierId, stream, "test.xlsx"));
        Assert.Equal("MISSING_HEADERS", ex.ErrorCode);
    }

    [Fact]
    public async Task ImportAsync_EmptyDataRows_ThrowsBusinessException()
    {
        var supplierId = Guid.NewGuid();
        var supplier = new Supplier { Id = supplierId, Code = "NCC-01", Name = "Test" };
        _mockRepo.Setup(r => r.GetByIdAsync(supplierId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(supplier);
        _mockRepo.Setup(r => r.GetSupplierProductsForSupplierAsync(supplierId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SupplierProduct>());

        using var stream = CreateWorkbookStream(ws =>
        {
            ws.Cell(1, 1).Value = "ProductId";
            ws.Cell(1, 2).Value = "SKU";
            ws.Cell(1, 3).Value = "Giá nhập sỉ";
            ws.Cell(1, 4).Value = "MOQ";
        });

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.ImportAsync(supplierId, stream, "test.xlsx"));
        Assert.Equal("EMPTY_DATA", ex.ErrorCode);
    }

    [Fact]
    public async Task ImportAsync_SuccessAndErrorRows_ProcessesCorrectly()
    {
        var supplierId = Guid.NewGuid();
        var productId1 = Guid.NewGuid();
        var productId2 = Guid.NewGuid();

        var supplier = new Supplier { Id = supplierId, Code = "NCC-01", Name = "Test Supplier" };
        _mockRepo.Setup(r => r.GetByIdAsync(supplierId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(supplier);

        var sp1 = new SupplierProduct
        {
            Id = Guid.NewGuid(),
            SupplierId = supplierId,
            ProductId = productId1,
            Product = new Product { ProductCode = "SKU-OK", Name = "Product OK" }
        };
        var sp2 = new SupplierProduct
        {
            Id = Guid.NewGuid(),
            SupplierId = supplierId,
            ProductId = productId2,
            Product = new Product { ProductCode = "SKU-EMPTY-PRICE", Name = "Product 2" }
        };

        _mockRepo.Setup(r => r.GetSupplierProductsForSupplierAsync(supplierId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SupplierProduct> { sp1, sp2 });

        using var stream = CreateWorkbookStream(ws =>
        {
            // Row 1: Headers
            ws.Cell(1, 1).Value = "ProductId";
            ws.Cell(1, 2).Value = "SKU";
            ws.Cell(1, 3).Value = "TenSanPham";
            ws.Cell(1, 4).Value = "GiaNhapSi";
            ws.Cell(1, 5).Value = "MOQ";

            // Row 2: Note row
            ws.Cell(2, 1).Value = "KHÔNG SỬA CÁC CỘT NÀY";

            // Row 3: Valid row by ProductId
            ws.Cell(3, 1).Value = productId1.ToString();
            ws.Cell(3, 2).Value = "SKU-OK";
            ws.Cell(3, 3).Value = "Product OK";
            ws.Cell(3, 4).Value = "45,000 đ";
            ws.Cell(3, 5).Value = 10;

            // Row 4: Valid row with blank price (resets agreed price)
            ws.Cell(4, 1).Value = productId2.ToString();
            ws.Cell(4, 2).Value = "SKU-EMPTY-PRICE";
            ws.Cell(4, 3).Value = "Product 2";
            ws.Cell(4, 4).Value = "";
            ws.Cell(4, 5).Value = 2;

            // Row 5: Unassigned SKU
            ws.Cell(5, 1).Value = Guid.NewGuid().ToString();
            ws.Cell(5, 2).Value = "SKU-UNKNOWN";
            ws.Cell(5, 3).Value = "Unknown";
            ws.Cell(5, 4).Value = "10000";
            ws.Cell(5, 5).Value = 1;

            // Row 6: Invalid price (negative)
            ws.Cell(6, 1).Value = productId1.ToString();
            ws.Cell(6, 2).Value = "SKU-OK";
            ws.Cell(6, 4).Value = "-500";
            ws.Cell(6, 5).Value = 1;

            // Row 7: Invalid MOQ (< 1)
            ws.Cell(7, 1).Value = productId1.ToString();
            ws.Cell(7, 2).Value = "SKU-OK";
            ws.Cell(7, 4).Value = "20000";
            ws.Cell(7, 5).Value = 0;
        });

        var result = await _service.ImportAsync(supplierId, stream, "import.xlsx");

        Assert.NotNull(result);
        Assert.Equal(5, result.Rows.Count);
        Assert.Equal(2, result.Summary.SuccessCount);
        Assert.Equal(3, result.Summary.FailedCount);

        // Row 3 (OK)
        Assert.Equal("OK", result.Rows[0].Status);
        Assert.Equal(45000m, sp1.AgreedWholesalePrice);
        Assert.Equal(10, sp1.MinimumOrderQuantity);

        // Row 4 (OK - reset price)
        Assert.Equal("OK", result.Rows[1].Status);
        Assert.Null(sp2.AgreedWholesalePrice);
        Assert.Equal(2, sp2.MinimumOrderQuantity);

        // Row 5 (ERROR - unassigned)
        Assert.Equal("ERROR", result.Rows[2].Status);

        // Row 6 (ERROR - negative price)
        Assert.Equal("ERROR", result.Rows[3].Status);

        // Row 7 (ERROR - MOQ < 1)
        Assert.Equal("ERROR", result.Rows[4].Status);

        _mockUow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ImportAsync_WithHeaderAliases_ShouldNormalizeAndProcessCorrectly()
    {
        var supplierId = Guid.NewGuid();
        var supplier = new Supplier { Id = supplierId, Code = "NCC-01", Name = "Supplier 1" };
        var productId = Guid.NewGuid();
        var product = new Product { Id = productId, ProductCode = "SKU-ALIAS", Name = "Product Alias", BaseUnit = "Gói" };
        var sp = new SupplierProduct { SupplierId = supplierId, ProductId = productId, Product = product, MinimumOrderQuantity = 1 };

        _mockRepo.Setup(r => r.GetByIdAsync(supplierId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(supplier);
        _mockRepo.Setup(r => r.GetSupplierProductsForSupplierAsync(supplierId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SupplierProduct> { sp });

        // Stream 1 with Vietnamese alias headers
        using var stream1 = CreateWorkbookStream(ws =>
        {
            ws.Cell(1, 1).Value = "Product ID";
            ws.Cell(1, 2).Value = "Mã SKU";
            ws.Cell(1, 3).Value = "Ten san pham";
            ws.Cell(1, 4).Value = "ĐVT";
            ws.Cell(1, 5).Value = "Gia nhap si";
            ws.Cell(1, 6).Value = "MinOrder";

            ws.Cell(2, 1).Value = productId.ToString();
            ws.Cell(2, 2).Value = "SKU-ALIAS";
            ws.Cell(2, 3).Value = "Product Alias";
            ws.Cell(2, 4).Value = "Gói";
            ws.Cell(2, 5).Value = "30000";
            ws.Cell(2, 6).Value = 5;
        });

        var res1 = await _service.ImportAsync(supplierId, stream1, "alias1.xlsx");
        Assert.NotNull(res1);
        Assert.Equal(1, res1.Summary.SuccessCount);
        Assert.Equal(30000m, sp.AgreedWholesalePrice);
        Assert.Equal(5, sp.MinimumOrderQuantity);

        // Stream 2 with English alias headers
        using var stream2 = CreateWorkbookStream(ws =>
        {
            ws.Cell(1, 1).Value = "Id";
            ws.Cell(1, 2).Value = "ProductCode";
            ws.Cell(1, 3).Value = "ProductName";
            ws.Cell(1, 4).Value = "Unit";
            ws.Cell(1, 5).Value = "Price";
            ws.Cell(1, 6).Value = "MinimumOrderQuantity";

            ws.Cell(2, 1).Value = productId.ToString();
            ws.Cell(2, 2).Value = "SKU-ALIAS";
            ws.Cell(2, 3).Value = "Product Alias";
            ws.Cell(2, 4).Value = "Gói";
            ws.Cell(2, 5).Value = "35000";
            ws.Cell(2, 6).Value = 10;
        });

        var res2 = await _service.ImportAsync(supplierId, stream2, "alias2.xlsx");
        Assert.NotNull(res2);
        Assert.Equal(1, res2.Summary.SuccessCount);
        Assert.Equal(35000m, sp.AgreedWholesalePrice);
        Assert.Equal(10, sp.MinimumOrderQuantity);
    }

    [Fact]
    public async Task ExportAsync_WithSkuFilter_OnlyExportsMatchingRows()
    {
        var supplierId = Guid.NewGuid();
        var productId1 = Guid.NewGuid();
        var productId2 = Guid.NewGuid();
        var supplier = new Supplier { Id = supplierId, Code = "NCC01", Name = "NCC" };
        _mockRepo.Setup(r => r.GetByIdAsync(supplierId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(supplier);

        var list = new List<SupplierProduct>
        {
            new()
            {
                SupplierId = supplierId,
                ProductId = productId1,
                Product = new Product { ProductCode = "ABC-01", Name = "Keep" },
                Status = MT_GRMS.Domain.Enums.SupplierProductStatus.Active,
                MinimumOrderQuantity = 1
            },
            new()
            {
                SupplierId = supplierId,
                ProductId = productId2,
                Product = new Product { ProductCode = "XYZ-99", Name = "Drop" },
                Status = MT_GRMS.Domain.Enums.SupplierProductStatus.Active,
                MinimumOrderQuantity = 1
            }
        };
        _mockRepo.Setup(r => r.GetSupplierProductsForSupplierAsync(supplierId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(list);

        var filter = new MT_GRMS.Application.Suppliers.Dtos.SupplierProductQueryFilter
        {
            Skus = new List<string> { "ABC" }
        };

        var result = await _service.ExportAsync(supplierId, filter);
        using var ms = new MemoryStream(result.Content);
        using var wb = new XLWorkbook(ms);
        var ws = wb.Worksheet(SupplierProductExcelService.SheetName);
        Assert.Equal("ABC-01", ws.Cell(2, 1).GetString());
        Assert.Equal(productId1.ToString(), ws.Cell(2, 8).GetString());
        Assert.True(string.IsNullOrWhiteSpace(ws.Cell(3, 1).GetString()));
    }

    [Fact]
    public async Task ImportAsync_ProductOutOfFilter_ReturnsErrorRow()
    {
        var supplierId = Guid.NewGuid();
        var inFilterId = Guid.NewGuid();
        var outFilterId = Guid.NewGuid();
        var supplier = new Supplier { Id = supplierId, Code = "NCC01", Name = "NCC" };
        _mockRepo.Setup(r => r.GetByIdAsync(supplierId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(supplier);

        var spIn = new SupplierProduct
        {
            SupplierId = supplierId,
            ProductId = inFilterId,
            Product = new Product { ProductCode = "IN-01", Name = "In Filter" },
            Status = MT_GRMS.Domain.Enums.SupplierProductStatus.Active,
            MinimumOrderQuantity = 1
        };
        var spOut = new SupplierProduct
        {
            SupplierId = supplierId,
            ProductId = outFilterId,
            Product = new Product { ProductCode = "OUT-99", Name = "Out Filter" },
            Status = MT_GRMS.Domain.Enums.SupplierProductStatus.Active,
            MinimumOrderQuantity = 1
        };
        _mockRepo.Setup(r => r.GetSupplierProductsForSupplierAsync(supplierId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SupplierProduct> { spIn, spOut });

        using var stream = CreateWorkbookStream(ws =>
        {
            ws.Cell(1, 1).Value = "ProductId";
            ws.Cell(1, 2).Value = "SKU";
            ws.Cell(1, 3).Value = "GiaNhapSi";
            ws.Cell(1, 4).Value = "MOQ";

            ws.Cell(2, 1).Value = inFilterId.ToString();
            ws.Cell(2, 2).Value = "IN-01";
            ws.Cell(2, 3).Value = "12000";
            ws.Cell(2, 4).Value = 3;

            ws.Cell(3, 1).Value = outFilterId.ToString();
            ws.Cell(3, 2).Value = "OUT-99";
            ws.Cell(3, 3).Value = "15000";
            ws.Cell(3, 4).Value = 2;
        });

        var filter = new MT_GRMS.Application.Suppliers.Dtos.SupplierProductQueryFilter
        {
            Skus = new List<string> { "IN-" }
        };

        var result = await _service.ImportAsync(supplierId, stream, "filter.xlsx", filter);

        Assert.Equal(2, result.Rows.Count);
        Assert.Equal(1, result.Summary.SuccessCount);
        Assert.Equal(1, result.Summary.FailedCount);
        Assert.Equal("OK", result.Rows[0].Status);
        Assert.Equal("ERROR", result.Rows[1].Status);
        Assert.Contains("bộ lọc", result.Rows[1].Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(12000m, spIn.AgreedWholesalePrice);
        Assert.Null(spOut.AgreedWholesalePrice);
    }
}
