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
    private static MemoryStream BuildSampleWorkbook(Guid branchId, Guid supplierId, string sku, string supplierCode, decimal qty)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("PO_Matrix");
        ws.Cell(1, 1).Value = "HỆ THỐNG QUẢN LÝ BÁN LẺ MT-GRMS — ĐẶT HÀNG NHẬP KHO";
        ws.Cell(2, 1).Value = $"Chi nhánh: Test | Mã hệ thống: {branchId}";
        ws.Cell(3, 1).Value = $"Nhà cung cấp: NCC | Mã NCC: {supplierId}";
        ws.Cell(4, 1).Value = "meta";
        ws.Cell(5, 1).Value = "";
        ws.Cell(6, 1).Value = "STT";
        ws.Cell(6, 2).Value = "Ngành hàng";
        ws.Cell(6, 3).Value = "Mã vạch";
        ws.Cell(6, 4).Value = "Mã sản phẩm";
        ws.Cell(6, 5).Value = "Mã NCC";
        ws.Cell(6, 6).Value = "Tên sản phẩm";
        ws.Cell(6, 7).Value = "ĐVT";
        ws.Cell(6, 8).Value = "MOQ";
        ws.Cell(6, 9).Value = "Bội số";
        ws.Cell(6, 10).Value = "Đơn giá (VNĐ)";
        ws.Cell(6, 11).Value = "T4 12/11\n[2026-11-12]";
        ws.Cell(6, 12).Value = "Tổng SL";
        ws.Cell(6, 13).Value = "Thành tiền (VNĐ)";

        ws.Cell(7, 1).Value = 1;
        ws.Cell(7, 4).Value = sku;
        ws.Cell(7, 5).Value = supplierCode;
        ws.Cell(7, 6).Value = "SP Test";
        ws.Cell(7, 11).Value = qty;

        var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;
        return ms;
    }

    [Fact]
    public async Task Preview_BranchMismatch_ReturnsError()
    {
        var branchId = Guid.NewGuid();
        var otherBranch = Guid.NewGuid();
        var supplierId = Guid.NewGuid();
        var productId = Guid.NewGuid();

        var po = new Mock<IPurchaseOrderService>();
        po.Setup(x => x.GetOrderMatrixAsync(It.IsAny<OrderMatrixQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrderMatrixResponse
            {
                BranchId = branchId,
                BranchName = "CN A",
                ViewFrom = new DateTime(2026, 11, 12),
                DurationDays = 7,
                Products = new List<OrderMatrixProductDto>
                {
                    new()
                    {
                        Id = productId,
                        SupplierId = supplierId,
                        Sku = "SP001",
                        Name = "SP Test",
                        Moq = 1,
                        OrderMultiple = 1,
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
                        DeliveryDate = new DateTime(2026, 11, 12),
                        DeliveryDateStatus = "OPEN"
                    }
                }
            });

        var tenant = new Mock<ITenantContext>();
        tenant.SetupGet(t => t.CurrentRole).Returns("STORE_OWNER");

        var svc = new MatrixExcelImportService(po.Object, tenant.Object);
        await using var stream = BuildSampleWorkbook(otherBranch, supplierId, "SP001", "VNM", 10);

        var result = await svc.PreviewAsync(stream, new MatrixExcelPreviewQuery
        {
            BranchId = branchId,
            SupplierId = supplierId,
            ViewFrom = new DateTime(2026, 11, 12),
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
        var branchId = Guid.NewGuid();
        var supplierId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var otherProduct = Guid.NewGuid();

        var po = new Mock<IPurchaseOrderService>();
        po.Setup(x => x.GetOrderMatrixAsync(It.IsAny<OrderMatrixQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrderMatrixResponse
            {
                BranchId = branchId,
                BranchName = "CN A",
                ViewFrom = new DateTime(2026, 11, 12),
                DurationDays = 7,
                Products = new List<OrderMatrixProductDto>
                {
                    new()
                    {
                        Id = productId,
                        SupplierId = supplierId,
                        Sku = "SP001",
                        Name = "SP Test",
                        Moq = 1,
                        OrderMultiple = 1,
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
                        DeliveryDate = new DateTime(2026, 11, 12),
                        DeliveryDateStatus = "OPEN"
                    }
                }
            });

        var tenant = new Mock<ITenantContext>();
        tenant.SetupGet(t => t.CurrentRole).Returns("STORE_OWNER");
        var svc = new MatrixExcelImportService(po.Object, tenant.Object);
        await using var stream = BuildSampleWorkbook(branchId, supplierId, "SP001", "VNM", 10);

        // Allow-list không chứa SP001
        var result = await svc.PreviewAsync(stream, new MatrixExcelPreviewQuery
        {
            BranchId = branchId,
            SupplierId = supplierId,
            ViewFrom = new DateTime(2026, 11, 12),
            DurationDays = 7,
            AllowedProducts = new List<MatrixExcelAllowedProductDto>
            {
                new() { ProductId = otherProduct, SupplierId = supplierId }
            },
            FileName = "t.xlsx"
        });

        Assert.Contains(result.Errors, e => e.Code == "PRODUCT_OUT_OF_FILTER" || e.Code == "PRODUCT_NOT_IN_MATRIX");
    }
}
