using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ClosedXML.Excel;
using Moq;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.PurchaseOrders;
using MT_GRMS.Application.PurchaseOrders.Dtos;
using MT_GRMS.Infrastructure.Services;
using Xunit;

namespace MT_GRMS.UnitTests;

public class MatrixExcelImportServiceTests
{
    private static readonly DateTime TargetDeliveryDate =
        OrderMatrixRules.GetVietnamNow().Date.AddDays(1);

    private static MemoryStream BuildSampleWorkbook(
        Guid branchId,
        Guid supplierId,
        string sku,
        string supplierCode,
        decimal qty,
        string? sheetName = "PO_Matrix",
        string? dateHeader = null,
        bool includeSkuCol = true,
        Action<IXLWorksheet>? customize = null)
    {
        dateHeader ??= $"T4 12/11\n[{TargetDeliveryDate:yyyy-MM-dd}]";

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(sheetName ?? "PO_Matrix");
        ws.Cell(1, 1).Value = "HỆ THỐNG QUẢN LÝ BÁN LẺ MT-GRMS — ĐẶT HÀNG NHẬP KHO";
        ws.Cell(2, 1).Value = $"Chi nhánh: Test | Mã hệ thống: {branchId}";
        ws.Cell(3, 1).Value = $"Nhà cung cấp: NCC | Mã NCC: {supplierId}";
        ws.Cell(4, 1).Value = "meta";
        ws.Cell(5, 1).Value = "";

        ws.Cell(6, 1).Value = "STT";
        ws.Cell(6, 2).Value = "Ngành hàng";
        ws.Cell(6, 3).Value = "Mã vạch";
        if (includeSkuCol)
        {
            ws.Cell(6, 4).Value = "Mã sản phẩm";
        }
        else
        {
            ws.Cell(6, 4).Value = "Khác";
        }
        ws.Cell(6, 5).Value = "Mã NCC";
        ws.Cell(6, 6).Value = "Tên sản phẩm";
        ws.Cell(6, 7).Value = "ĐVT";
        ws.Cell(6, 8).Value = "MOQ";
        ws.Cell(6, 9).Value = "Bội số";
        ws.Cell(6, 10).Value = "Đơn giá (VNĐ)";
        ws.Cell(6, 11).Value = dateHeader;
        ws.Cell(6, 12).Value = "Tổng SL";
        ws.Cell(6, 13).Value = "Thành tiền (VNĐ)";

        ws.Cell(7, 1).Value = 1;
        ws.Cell(7, 4).Value = sku;
        ws.Cell(7, 5).Value = supplierCode;
        ws.Cell(7, 6).Value = "SP Test";
        ws.Cell(7, 11).Value = qty;

        customize?.Invoke(ws);

        var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;
        return ms;
    }

    private static (OrderMatrixResponse matrix, Guid branchId, Guid supplierId, Guid productId) SetupMatrix(decimal moq = 1, decimal multiple = 1)
    {
        var branchId = Guid.NewGuid();
        var supplierId = Guid.NewGuid();
        var productId = Guid.NewGuid();

        var matrix = new OrderMatrixResponse
        {
            BranchId = branchId,
            BranchName = "CN A",
            ViewFrom = TargetDeliveryDate.AddDays(-1),
            DurationDays = 7,
            Products = new List<OrderMatrixProductDto>
            {
                new()
                {
                    Id = productId,
                    SupplierId = supplierId,
                    Sku = "SP001",
                    Name = "SP Test",
                    Moq = (int)moq,
                    OrderMultiple = (int)multiple,
                    IsProductActive = true,
                    IsSupplierActive = true,
                    IsSupplierProductActive = true,
                    IsSupplierBranchActive = true,
                    IsBranchProductActive = true
                }
            },
            SupplierConfigs = new List<OrderMatrixSupplierDeliveryConfigDto>
            {
                new()
                {
                    SupplierId = supplierId,
                    SupplierCode = "VNM",
                    SupplierName = "Vinamilk",
                    IsActive = true,
                    IsAssignedToBranch = true,
                    ActiveDeliveryDays = new List<int> { 0, 1, 2, 3, 4, 5, 6 }
                }
            },
            Cells = new List<OrderMatrixCellDto>(),
            DayStatuses = new List<OrderMatrixDayStatusDto>
            {
                new()
                {
                    SupplierId = supplierId,
                    DeliveryDate = TargetDeliveryDate,
                    DeliveryDateStatus = "OPEN"
                }
            }
        };

        return (matrix, branchId, supplierId, productId);
    }

    [Fact]
    public async Task Preview_BranchMismatch_ReturnsError()
    {
        var (matrix, branchId, supplierId, productId) = SetupMatrix();
        var otherBranch = Guid.NewGuid();

        var po = new Mock<IPurchaseOrderService>();
        po.Setup(x => x.GetOrderMatrixAsync(It.IsAny<OrderMatrixQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(matrix);

        var tenant = new Mock<ITenantContext>();
        tenant.SetupGet(t => t.CurrentRole).Returns("STORE_OWNER");

        var svc = new MatrixExcelImportService(po.Object, tenant.Object);
        await using var stream = BuildSampleWorkbook(otherBranch, supplierId, "SP001", "VNM", 10);

        var result = await svc.PreviewAsync(stream, new MatrixExcelPreviewQuery
        {
            BranchId = branchId,
            SupplierId = supplierId,
            ViewFrom = TargetDeliveryDate.AddDays(-1),
            DurationDays = 7,
            AllowedProducts = new List<MatrixExcelAllowedProductDto>
            {
                new() { ProductId = productId, SupplierId = supplierId }
            },
            FileName = "t.xlsx",
            FileSize = 10
        });

        Assert.False(result.Valid);
        Assert.Contains(result.Errors, e => e.Code == "BRANCH_MISMATCH");
    }

    [Fact]
    public async Task Preview_ProductOutOfFilter_ReturnsError()
    {
        var (matrix, branchId, supplierId, productId) = SetupMatrix();
        var otherProduct = Guid.NewGuid();

        var po = new Mock<IPurchaseOrderService>();
        po.Setup(x => x.GetOrderMatrixAsync(It.IsAny<OrderMatrixQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(matrix);

        var tenant = new Mock<ITenantContext>();
        tenant.SetupGet(t => t.CurrentRole).Returns("STORE_OWNER");
        var svc = new MatrixExcelImportService(po.Object, tenant.Object);
        await using var stream = BuildSampleWorkbook(branchId, supplierId, "SP001", "VNM", 10);

        var result = await svc.PreviewAsync(stream, new MatrixExcelPreviewQuery
        {
            BranchId = branchId,
            SupplierId = supplierId,
            ViewFrom = TargetDeliveryDate.AddDays(-1),
            DurationDays = 7,
            AllowedProducts = new List<MatrixExcelAllowedProductDto>
            {
                new() { ProductId = otherProduct, SupplierId = supplierId }
            },
            FileName = "t.xlsx"
        });

        Assert.Contains(result.Errors, e => e.Code == "PRODUCT_OUT_OF_FILTER" || e.Code == "PRODUCT_NOT_IN_MATRIX");
    }

    [Fact]
    public async Task Preview_CorruptedStream_ReturnsFormatInvalid()
    {
        var (matrix, branchId, supplierId, productId) = SetupMatrix();
        var po = new Mock<IPurchaseOrderService>();
        po.Setup(x => x.GetOrderMatrixAsync(It.IsAny<OrderMatrixQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(matrix);

        var tenant = new Mock<ITenantContext>();
        var svc = new MatrixExcelImportService(po.Object, tenant.Object);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("not excel"));

        var result = await svc.PreviewAsync(stream, new MatrixExcelPreviewQuery
        {
            BranchId = branchId,
            SupplierId = supplierId,
            ViewFrom = TargetDeliveryDate.AddDays(-1),
            DurationDays = 7,
            FileName = "corrupted.xlsx"
        });

        Assert.False(result.Valid);
        Assert.Contains(result.Errors, e => e.Code == "FORMAT_INVALID");
    }

    [Fact]
    public async Task Preview_MissingSheet_ReturnsSheetNotFound()
    {
        var (matrix, branchId, supplierId, productId) = SetupMatrix();
        var po = new Mock<IPurchaseOrderService>();
        po.Setup(x => x.GetOrderMatrixAsync(It.IsAny<OrderMatrixQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(matrix);

        var tenant = new Mock<ITenantContext>();
        var svc = new MatrixExcelImportService(po.Object, tenant.Object);

        using var wb = new XLWorkbook();
        wb.Worksheets.Add("PO_Matrix"); // completely empty, used range is null
        using var stream = new MemoryStream();
        wb.SaveAs(stream);
        stream.Position = 0;

        var result = await svc.PreviewAsync(stream, new MatrixExcelPreviewQuery
        {
            BranchId = branchId,
            SupplierId = supplierId,
            ViewFrom = TargetDeliveryDate.AddDays(-1),
            DurationDays = 7,
            FileName = "no_sheet.xlsx"
        });

        Assert.False(result.Valid);
        Assert.Contains(result.Errors, e => e.Code == "SHEET_NOT_FOUND");
    }

    [Fact]
    public async Task Preview_MissingSkuColumn_ReturnsSheetNotFound()
    {
        var (matrix, branchId, supplierId, productId) = SetupMatrix();
        var po = new Mock<IPurchaseOrderService>();
        po.Setup(x => x.GetOrderMatrixAsync(It.IsAny<OrderMatrixQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(matrix);

        var tenant = new Mock<ITenantContext>();
        var svc = new MatrixExcelImportService(po.Object, tenant.Object);
        await using var stream = BuildSampleWorkbook(branchId, supplierId, "SP001", "VNM", 10, includeSkuCol: false);

        var result = await svc.PreviewAsync(stream, new MatrixExcelPreviewQuery
        {
            BranchId = branchId,
            SupplierId = supplierId,
            ViewFrom = TargetDeliveryDate.AddDays(-1),
            DurationDays = 7,
            FileName = "missing_sku.xlsx"
        });

        Assert.False(result.Valid);
        Assert.Contains(result.Errors, e => e.Code == "SHEET_NOT_FOUND");
    }

    [Fact]
    public async Task Preview_NoDeliveryDateColumns_ReturnsNoDeliveryDate()
    {
        var (matrix, branchId, supplierId, productId) = SetupMatrix();
        var po = new Mock<IPurchaseOrderService>();
        po.Setup(x => x.GetOrderMatrixAsync(It.IsAny<OrderMatrixQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(matrix);

        var tenant = new Mock<ITenantContext>();
        var svc = new MatrixExcelImportService(po.Object, tenant.Object);
        await using var stream = BuildSampleWorkbook(branchId, supplierId, "SP001", "VNM", 10, dateHeader: "Ghi chú");

        var result = await svc.PreviewAsync(stream, new MatrixExcelPreviewQuery
        {
            BranchId = branchId,
            SupplierId = supplierId,
            ViewFrom = TargetDeliveryDate.AddDays(-1),
            DurationDays = 7,
            FileName = "no_date.xlsx"
        });

        Assert.False(result.Valid);
        Assert.Contains(result.Errors, e => e.Code == "NO_DELIVERY_DATE");
    }

    [Fact]
    public async Task Preview_InvalidPeriod_ReturnsInvalidPeriod()
    {
        var (matrix, branchId, supplierId, productId) = SetupMatrix();
        var po = new Mock<IPurchaseOrderService>();
        po.Setup(x => x.GetOrderMatrixAsync(It.IsAny<OrderMatrixQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(matrix);

        var tenant = new Mock<ITenantContext>();
        var svc = new MatrixExcelImportService(po.Object, tenant.Object);
        // Date outside view period
        var outsideDate = TargetDeliveryDate.AddDays(30);
        await using var stream = BuildSampleWorkbook(branchId, supplierId, "SP001", "VNM", 10, dateHeader: $"T4\n[{outsideDate:yyyy-MM-dd}]");

        var result = await svc.PreviewAsync(stream, new MatrixExcelPreviewQuery
        {
            BranchId = branchId,
            SupplierId = supplierId,
            ViewFrom = TargetDeliveryDate.AddDays(-1),
            DurationDays = 7,
            FileName = "invalid_period.xlsx"
        });

        Assert.False(result.Valid);
        Assert.Contains(result.Errors, e => e.Code == "INVALID_PERIOD");
    }

    [Fact]
    public async Task Preview_SupplierMismatch_ReturnsSupplierMismatch()
    {
        var (matrix, branchId, supplierId, productId) = SetupMatrix();
        var otherSupplier = Guid.NewGuid();

        var po = new Mock<IPurchaseOrderService>();
        po.Setup(x => x.GetOrderMatrixAsync(It.IsAny<OrderMatrixQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(matrix);

        var tenant = new Mock<ITenantContext>();
        var svc = new MatrixExcelImportService(po.Object, tenant.Object);
        // Set file supplier banner to otherSupplier
        await using var stream = BuildSampleWorkbook(branchId, otherSupplier, "SP001", "VNM", 10);

        var result = await svc.PreviewAsync(stream, new MatrixExcelPreviewQuery
        {
            BranchId = branchId,
            SupplierId = supplierId,
            ViewFrom = TargetDeliveryDate.AddDays(-1),
            DurationDays = 7,
            FileName = "sup_mismatch.xlsx"
        });

        Assert.False(result.Valid);
        Assert.Contains(result.Errors, e => e.Code == "SUPPLIER_MISMATCH");
    }

    [Fact]
    public async Task Preview_DuplicateProductRow_ReturnsDuplicateProduct()
    {
        var (matrix, branchId, supplierId, productId) = SetupMatrix();
        var po = new Mock<IPurchaseOrderService>();
        po.Setup(x => x.GetOrderMatrixAsync(It.IsAny<OrderMatrixQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(matrix);

        var tenant = new Mock<ITenantContext>();
        var svc = new MatrixExcelImportService(po.Object, tenant.Object);

        await using var stream = BuildSampleWorkbook(branchId, supplierId, "SP001", "VNM", 10, customize: ws =>
        {
            // Row 8 duplicate SP001
            ws.Cell(8, 1).Value = 2;
            ws.Cell(8, 4).Value = "SP001";
            ws.Cell(8, 5).Value = "VNM";
            ws.Cell(8, 6).Value = "SP Test";
            ws.Cell(8, 11).Value = 5;
        });

        var result = await svc.PreviewAsync(stream, new MatrixExcelPreviewQuery
        {
            BranchId = branchId,
            SupplierId = supplierId,
            ViewFrom = TargetDeliveryDate.AddDays(-1),
            DurationDays = 7,
            AllowedProducts = new List<MatrixExcelAllowedProductDto>
            {
                new() { ProductId = productId, SupplierId = supplierId }
            },
            FileName = "dup.xlsx"
        });

        Assert.False(result.Valid);
        Assert.Contains(result.Errors, e => e.Code == "DUPLICATE_PRODUCT");
    }

    [Fact]
    public async Task Preview_NegativeQuantity_ReturnsNegativeQuantity()
    {
        var (matrix, branchId, supplierId, productId) = SetupMatrix();
        var po = new Mock<IPurchaseOrderService>();
        po.Setup(x => x.GetOrderMatrixAsync(It.IsAny<OrderMatrixQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(matrix);

        var tenant = new Mock<ITenantContext>();
        var svc = new MatrixExcelImportService(po.Object, tenant.Object);
        await using var stream = BuildSampleWorkbook(branchId, supplierId, "SP001", "VNM", -5);

        var result = await svc.PreviewAsync(stream, new MatrixExcelPreviewQuery
        {
            BranchId = branchId,
            SupplierId = supplierId,
            ViewFrom = TargetDeliveryDate.AddDays(-1),
            DurationDays = 7,
            AllowedProducts = new List<MatrixExcelAllowedProductDto>
            {
                new() { ProductId = productId, SupplierId = supplierId }
            },
            FileName = "neg.xlsx"
        });

        Assert.False(result.Valid);
        Assert.Contains(result.Errors, e => e.Code == "NEGATIVE_QUANTITY");
    }

    [Fact]
    public async Task Preview_MoqViolation_ReturnsMoqViolation()
    {
        // MOQ = 10, entered qty = 5
        var (matrix, branchId, supplierId, productId) = SetupMatrix(moq: 10, multiple: 1);
        var po = new Mock<IPurchaseOrderService>();
        po.Setup(x => x.GetOrderMatrixAsync(It.IsAny<OrderMatrixQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(matrix);

        var tenant = new Mock<ITenantContext>();
        var svc = new MatrixExcelImportService(po.Object, tenant.Object);
        await using var stream = BuildSampleWorkbook(branchId, supplierId, "SP001", "VNM", 5);

        var result = await svc.PreviewAsync(stream, new MatrixExcelPreviewQuery
        {
            BranchId = branchId,
            SupplierId = supplierId,
            ViewFrom = TargetDeliveryDate.AddDays(-1),
            DurationDays = 7,
            AllowedProducts = new List<MatrixExcelAllowedProductDto>
            {
                new() { ProductId = productId, SupplierId = supplierId }
            },
            FileName = "moq.xlsx"
        });

        Assert.False(result.Valid);
        Assert.Contains(result.Errors, e => e.Code == "MOQ_NOT_MET");
    }

    [Fact]
    public async Task Preview_OrderMultipleViolation_ReturnsMultipleViolationWarning()
    {
        // OrderMultiple = 5, entered qty = 7 (MOQ = 1)
        var (matrix, branchId, supplierId, productId) = SetupMatrix(moq: 1, multiple: 5);
        var po = new Mock<IPurchaseOrderService>();
        po.Setup(x => x.GetOrderMatrixAsync(It.IsAny<OrderMatrixQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(matrix);

        var tenant = new Mock<ITenantContext>();
        var svc = new MatrixExcelImportService(po.Object, tenant.Object);
        await using var stream = BuildSampleWorkbook(branchId, supplierId, "SP001", "VNM", 7);

        var result = await svc.PreviewAsync(stream, new MatrixExcelPreviewQuery
        {
            BranchId = branchId,
            SupplierId = supplierId,
            ViewFrom = TargetDeliveryDate.AddDays(-1),
            DurationDays = 7,
            AllowedProducts = new List<MatrixExcelAllowedProductDto>
            {
                new() { ProductId = productId, SupplierId = supplierId }
            },
            FileName = "multiple.xlsx"
        });

        Assert.Contains(result.Warnings, e => e.Code == "ORDER_MULTIPLE_INVALID");
    }

    [Fact]
    public async Task Preview_HappyPath_ReturnsValidWithCellsToApply()
    {
        var (matrix, branchId, supplierId, productId) = SetupMatrix(moq: 1, multiple: 1);
        var po = new Mock<IPurchaseOrderService>();
        po.Setup(x => x.GetOrderMatrixAsync(It.IsAny<OrderMatrixQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(matrix);

        var tenant = new Mock<ITenantContext>();
        tenant.SetupGet(t => t.CurrentRole).Returns("STORE_OWNER");
        var svc = new MatrixExcelImportService(po.Object, tenant.Object);
        await using var stream = BuildSampleWorkbook(branchId, supplierId, "SP001", "VNM", 15);

        var result = await svc.PreviewAsync(stream, new MatrixExcelPreviewQuery
        {
            BranchId = branchId,
            SupplierId = supplierId,
            ViewFrom = TargetDeliveryDate.AddDays(-1),
            DurationDays = 7,
            AllowedProducts = new List<MatrixExcelAllowedProductDto>
            {
                new() { ProductId = productId, SupplierId = supplierId }
            },
            FileName = "valid.xlsx",
            FileSize = 100
        });

        Assert.True(result.Valid);
        Assert.Equal(0, result.Summary.ErrorCount);
        Assert.Single(result.CellsToApply);
        Assert.Equal(15m, result.CellsToApply[0].Quantity);
    }

    [Fact]
    public async Task ImportAsync_HappyPath_SavesDraftCells()
    {
        var (matrix, branchId, supplierId, productId) = SetupMatrix(moq: 1, multiple: 1);
        var po = new Mock<IPurchaseOrderService>();
        po.Setup(x => x.GetOrderMatrixAsync(It.IsAny<OrderMatrixQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(matrix);
        po.Setup(x => x.SaveMatrixDraftAsync(It.IsAny<SaveMatrixDraftCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PurchaseOrderDto { Id = Guid.NewGuid(), Code = "PO-01" });

        var tenant = new Mock<ITenantContext>();
        tenant.SetupGet(t => t.CurrentRole).Returns("STORE_OWNER");
        var svc = new MatrixExcelImportService(po.Object, tenant.Object);

        var command = new ImportMatrixExcelCommand
        {
            BranchId = branchId,
            SupplierId = supplierId,
            ViewFrom = TargetDeliveryDate.AddDays(-1),
            ViewTo = TargetDeliveryDate.AddDays(5),
            AllowedProducts = new List<MatrixExcelAllowedProductDto>
            {
                new() { ProductId = productId, SupplierId = supplierId }
            },
            Cells = new List<ImportMatrixExcelCellDto>
            {
                new()
                {
                    ProductId = productId,
                    SupplierId = supplierId,
                    DeliveryDate = TargetDeliveryDate,
                    Quantity = 20,
                    ProductCode = "SP001",
                    ProductName = "SP Test"
                }
            }
        };

        var result = await svc.ImportAsync(command);

        Assert.True(result.Success);
        Assert.Equal(0, result.Summary.ErrorCount);
        Assert.Equal(1, result.Summary.AppliedCells);
    }

    [Fact]
    public async Task ImportAsync_ProductOutOfFilter_ReturnsError()
    {
        var (matrix, branchId, supplierId, productId) = SetupMatrix(moq: 1, multiple: 1);
        var otherProduct = Guid.NewGuid();

        var po = new Mock<IPurchaseOrderService>();
        po.Setup(x => x.GetOrderMatrixAsync(It.IsAny<OrderMatrixQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(matrix);

        var tenant = new Mock<ITenantContext>();
        tenant.SetupGet(t => t.CurrentRole).Returns("STORE_OWNER");
        var svc = new MatrixExcelImportService(po.Object, tenant.Object);

        var command = new ImportMatrixExcelCommand
        {
            BranchId = branchId,
            SupplierId = supplierId,
            ViewFrom = TargetDeliveryDate.AddDays(-1),
            ViewTo = TargetDeliveryDate.AddDays(5),
            AllowedProducts = new List<MatrixExcelAllowedProductDto>
            {
                new() { ProductId = otherProduct, SupplierId = supplierId }
            },
            Cells = new List<ImportMatrixExcelCellDto>
            {
                new()
                {
                    ProductId = productId,
                    SupplierId = supplierId,
                    DeliveryDate = TargetDeliveryDate,
                    Quantity = 20,
                    ProductCode = "SP001"
                }
            }
        };

        var result = await svc.ImportAsync(command);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Code == "PRODUCT_OUT_OF_FILTER");
    }

    [Fact]
    public async Task Preview_VariousBranches_ShouldBeExercised()
    {
        var (matrix, branchId, supplierId, productId) = SetupMatrix(moq: 1, multiple: 1);
        var product2Id = Guid.NewGuid();
        var supplier2Id = Guid.NewGuid();

        // Add disabled product and ambiguous SKU
        ((List<OrderMatrixProductDto>)matrix.Products).Add(new OrderMatrixProductDto
        {
            Id = product2Id,
            SupplierId = supplierId,
            Sku = "SP_DISABLED",
            Name = "SP Disabled",
            IsProductActive = false,
            RowDisabledReason = "Tạm ngừng kinh doanh"
        });
        ((List<OrderMatrixProductDto>)matrix.Products).Add(new OrderMatrixProductDto
        {
            Id = Guid.NewGuid(),
            SupplierId = supplier2Id,
            Sku = "SP_MULTI",
            Name = "SP Multi 1"
        });
        ((List<OrderMatrixProductDto>)matrix.Products).Add(new OrderMatrixProductDto
        {
            Id = Guid.NewGuid(),
            SupplierId = supplierId,
            Sku = "SP_MULTI",
            Name = "SP Multi 2"
        });
        ((List<OrderMatrixSupplierDeliveryConfigDto>)matrix.SupplierConfigs).Add(new OrderMatrixSupplierDeliveryConfigDto
        {
            SupplierId = supplier2Id,
            SupplierCode = "SUP2",
            SupplierName = "Supplier 2",
            IsActive = true
        });

        // Add a finalized day status and an existing owner-adjusted cell
        var pastDate = TargetDeliveryDate.AddDays(2);
        ((List<OrderMatrixDayStatusDto>)matrix.DayStatuses).Add(new OrderMatrixDayStatusDto
        {
            SupplierId = supplierId,
            DeliveryDate = pastDate,
            DeliveryDateStatus = "FINALIZED"
        });
        ((List<OrderMatrixCellDto>)matrix.Cells).Add(new OrderMatrixCellDto
        {
            ProductId = productId,
            SupplierId = supplierId,
            DeliveryDate = TargetDeliveryDate,
            EmployeeQty = 10,
            StoreQty = 12,
            IsOwnerAdjusted = true
        });

        var po = new Mock<IPurchaseOrderService>();
        po.Setup(x => x.GetOrderMatrixAsync(It.IsAny<OrderMatrixQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(matrix);

        // Staff role
        var tenant = new Mock<ITenantContext>();
        tenant.SetupGet(t => t.CurrentRole).Returns("STAFF");

        var svc = new MatrixExcelImportService(po.Object, tenant.Object);

        await using var stream = BuildSampleWorkbook(branchId, supplierId, "SP001", "VNM", 15, customize: ws =>
        {
            // Row 7 has SP001, quantity 15 on TargetDeliveryDate. Since it's owner-locked, staff should get OWNER_LOCKED_SKIPPED warning
            
            // Add column for pastDate (FINALIZED)
            ws.Cell(6, 14).Value = $"T6 14/11\n[{pastDate:yyyy-MM-dd}]";
            ws.Cell(7, 14).Value = 99; // Finalized day modified -> DATE_ALREADY_FINALIZED

            // Row 8: Unknown SKU
            ws.Cell(8, 1).Value = 2;
            ws.Cell(8, 4).Value = "SP_UNKNOWN";
            ws.Cell(8, 6).Value = "Unknown";
            ws.Cell(8, 11).Value = 5;

            // Row 9: Disabled product
            ws.Cell(9, 1).Value = 3;
            ws.Cell(9, 4).Value = "SP_DISABLED";
            ws.Cell(9, 5).Value = "VNM";
            ws.Cell(9, 6).Value = "SP Disabled";
            ws.Cell(9, 11).Value = 5;

            // Row 10: Blank SKU (should continue)
            ws.Cell(10, 1).Value = 4;
            ws.Cell(10, 4).Value = "";

            // Row 11: SP001 with invalid decimal quantity "12.5"
            ws.Cell(11, 1).Value = 5;
            ws.Cell(11, 4).Value = "SP001_OTHER";
            ws.Cell(11, 11).Value = "12.5";

            // Row 12: Blank quantity "-"
            ws.Cell(12, 1).Value = 6;
            ws.Cell(12, 4).Value = "SP001";
            ws.Cell(12, 11).Value = "-";

            // Row 13: Summary row "TỔNG CỘNG"
            ws.Cell(13, 4).Value = "TỔNG CỘNG";
        });

        var result = await svc.PreviewAsync(stream, new MatrixExcelPreviewQuery
        {
            BranchId = branchId,
            SupplierId = supplierId,
            ViewFrom = TargetDeliveryDate.AddDays(-1),
            DurationDays = 7,
            FileName = "multi_branches.xlsx"
        });

        Assert.False(result.Valid);
        Assert.Contains(result.Warnings, w => w.Code == "OWNER_LOCKED_SKIPPED");
        Assert.Contains(result.Errors, e => e.Code == "DATE_ALREADY_FINALIZED");
        Assert.Contains(result.Errors, e => e.Code == "PRODUCT_NOT_IN_MATRIX");
        Assert.Contains(result.Errors, e => e.Code == "PRODUCT_INACTIVE");
    }

    [Fact]
    public async Task ImportAsync_StaffRole_AndBlockedCells_HandledCorrectly()
    {
        var (matrix, branchId, supplierId, productId) = SetupMatrix(moq: 10, multiple: 1);
        var lockedDate = TargetDeliveryDate.AddDays(3);
        ((List<OrderMatrixDayStatusDto>)matrix.DayStatuses).Add(new OrderMatrixDayStatusDto
        {
            SupplierId = supplierId,
            DeliveryDate = lockedDate,
            DeliveryDateStatus = "LOCKED"
        });
        ((List<OrderMatrixCellDto>)matrix.Cells).Add(new OrderMatrixCellDto
        {
            ProductId = productId,
            SupplierId = supplierId,
            DeliveryDate = TargetDeliveryDate,
            EmployeeQty = 5,
            StoreQty = 10,
            IsOwnerAdjusted = true
        });

        var po = new Mock<IPurchaseOrderService>();
        po.Setup(x => x.GetOrderMatrixAsync(It.IsAny<OrderMatrixQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(matrix);
        po.Setup(x => x.SaveMatrixDraftAsync(It.IsAny<SaveMatrixDraftCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PurchaseOrderDto { Id = Guid.NewGuid(), Code = "PO-02" });

        var tenant = new Mock<ITenantContext>();
        tenant.SetupGet(t => t.CurrentRole).Returns("STAFF");
        var svc = new MatrixExcelImportService(po.Object, tenant.Object);

        var validDate = TargetDeliveryDate.AddDays(1);
        var command = new ImportMatrixExcelCommand
        {
            BranchId = branchId,
            SupplierId = supplierId,
            ViewFrom = TargetDeliveryDate.AddDays(-1),
            ViewTo = TargetDeliveryDate.AddDays(5),
            AllowedProducts = new List<MatrixExcelAllowedProductDto>
            {
                new() { ProductId = productId, SupplierId = supplierId }
            },
            Cells = new List<ImportMatrixExcelCellDto>
            {
                // Cell 1: Product not in matrix
                new()
                {
                    ProductId = Guid.NewGuid(),
                    SupplierId = supplierId,
                    DeliveryDate = validDate,
                    Quantity = 20
                },
                // Cell 2: Blocked cell (LOCKED)
                new()
                {
                    ProductId = productId,
                    SupplierId = supplierId,
                    DeliveryDate = lockedDate,
                    Quantity = 20
                },
                // Cell 3: Owner locked (staff skipped)
                new()
                {
                    ProductId = productId,
                    SupplierId = supplierId,
                    DeliveryDate = TargetDeliveryDate,
                    Quantity = 20
                },
                // Cell 4: MOQ violation (MOQ=10, Qty=5)
                new()
                {
                    ProductId = productId,
                    SupplierId = supplierId,
                    DeliveryDate = validDate,
                    Quantity = 5
                },
                // Cell 5: Valid cell for staff
                new()
                {
                    ProductId = productId,
                    SupplierId = supplierId,
                    DeliveryDate = TargetDeliveryDate.AddDays(2),
                    Quantity = 30
                }
            }
        };

        var result = await svc.ImportAsync(command);

        Assert.True(result.Success);
        Assert.Contains(result.Errors, e => e.Code == "PRODUCT_OUT_OF_FILTER");
        Assert.Contains(result.Errors, e => e.Code == "BLOCKED_CELL_MODIFIED");
        Assert.Contains(result.Warnings, w => w.Code == "OWNER_LOCKED_SKIPPED");
        Assert.Contains(result.Errors, e => e.Code == "MOQ_NOT_MET");
        Assert.Equal(1, result.Summary.AppliedCells);
    }

    [Fact]
    public async Task ValidateAsync_DisabledRowProduct_ReturnsProductInactiveError()
    {
        var (matrix, branchId, supplierId, productId) = SetupMatrix();
        matrix.Products[0].IsProductActive = false; // Makes IsRowDisabled = true
        var mockPoSvc = new Mock<IPurchaseOrderService>();
        mockPoSvc.Setup(s => s.GetOrderMatrixAsync(It.IsAny<OrderMatrixQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(matrix);

        var tenantCtx = new Mock<ITenantContext>();
        tenantCtx.Setup(t => t.CurrentRole).Returns("STAFF");

        var svc = new MatrixExcelImportService(mockPoSvc.Object, tenantCtx.Object);

        using var ms = BuildSampleWorkbook(branchId, supplierId, "SP001", "VNM", 10);
        var result = await svc.PreviewAsync(ms, new MatrixExcelPreviewQuery
        {
            BranchId = branchId,
            SupplierId = supplierId,
            ViewFrom = matrix.ViewFrom,
            DurationDays = 7,
            FileName = "matrix.xlsx"
        });

        Assert.Contains(result.Errors, e => e.Code == "PRODUCT_INACTIVE");
    }

    [Fact]
    public async Task ValidateAsync_FinalizedAndSupplierOffDates_ReturnsDateFinalizedAndCutoffPassedErrors()
    {
        var (matrix, branchId, supplierId, productId) = SetupMatrix();
        var finalizedDate = TargetDeliveryDate;
        var offDate = TargetDeliveryDate.AddDays(1);

        matrix.DayStatuses = new List<OrderMatrixDayStatusDto>
        {
            new()
            {
                SupplierId = supplierId,
                DeliveryDate = finalizedDate,
                DeliveryDateStatus = OrderMatrixRules.StatusFinalized
            },
            new()
            {
                SupplierId = supplierId,
                DeliveryDate = offDate,
                DeliveryDateStatus = OrderMatrixRules.StatusSupplierOff
            }
        };

        var mockPoSvc = new Mock<IPurchaseOrderService>();
        mockPoSvc.Setup(s => s.GetOrderMatrixAsync(It.IsAny<OrderMatrixQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(matrix);

        var tenantCtx = new Mock<ITenantContext>();
        tenantCtx.Setup(t => t.CurrentRole).Returns("STORE_OWNER");

        var svc = new MatrixExcelImportService(mockPoSvc.Object, tenantCtx.Object);

        var command = new ImportMatrixExcelCommand
        {
            BranchId = branchId,
            ViewFrom = matrix.ViewFrom,
            ViewTo = matrix.ViewFrom.AddDays(7),
            AllowedProducts = new List<MatrixExcelAllowedProductDto>
            {
                new() { ProductId = productId, SupplierId = supplierId }
            },
            Cells = new List<ImportMatrixExcelCellDto>
            {
                new()
                {
                    ProductId = productId,
                    SupplierId = supplierId,
                    DeliveryDate = finalizedDate,
                    Quantity = 50
                },
                new()
                {
                    ProductId = productId,
                    SupplierId = supplierId,
                    DeliveryDate = offDate,
                    Quantity = 50
                }
            }
        };

        var result = await svc.ImportAsync(command);

        Assert.Contains(result.Errors, e => e.Code == "BLOCKED_CELL_MODIFIED");
        Assert.Equal(2, result.Errors.Count);
    }

    [Fact]
    public async Task ValidateAsync_NegativeAndDecimalQuantity_ReturnsNegativeAndDecimalErrors()
    {
        var (matrix, branchId, supplierId, productId) = SetupMatrix();
        var mockPoSvc = new Mock<IPurchaseOrderService>();
        mockPoSvc.Setup(s => s.GetOrderMatrixAsync(It.IsAny<OrderMatrixQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(matrix);

        var tenantCtx = new Mock<ITenantContext>();
        tenantCtx.Setup(t => t.CurrentRole).Returns("STAFF");

        var svc = new MatrixExcelImportService(mockPoSvc.Object, tenantCtx.Object);

        using var ms = BuildSampleWorkbook(branchId, supplierId, "SP001", "VNM", 10, customize: ws =>
        {
            ws.Cell(7, 11).Value = -5; // Negative quantity
        });

        var result = await svc.PreviewAsync(ms, new MatrixExcelPreviewQuery
        {
            BranchId = branchId,
            SupplierId = supplierId,
            ViewFrom = matrix.ViewFrom,
            DurationDays = 7,
            FileName = "matrix.xlsx"
        });

        Assert.Contains(result.Errors, e => e.Code == "NEGATIVE_QUANTITY");
    }
}
