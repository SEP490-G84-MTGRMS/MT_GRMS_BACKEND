using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using MT_GRMS.Api.Controllers;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.PurchaseOrders;
using MT_GRMS.Application.PurchaseOrders.Dispatch;
using MT_GRMS.Application.PurchaseOrders.Dtos;
using Xunit;

namespace MT_GRMS.UnitTests.Controllers;

public class PurchaseOrdersControllerTests
{
    private readonly Mock<IPurchaseOrderService> _mockPoService;
    private readonly Mock<ISupplierDispatchPackageService> _mockDispatchPackageService;
    private readonly Mock<IMatrixExcelImportService> _mockMatrixExcelImportService;
    private readonly PurchaseOrdersController _controller;

    public PurchaseOrdersControllerTests()
    {
        _mockPoService = new Mock<IPurchaseOrderService>();
        _mockDispatchPackageService = new Mock<ISupplierDispatchPackageService>();
        _mockMatrixExcelImportService = new Mock<IMatrixExcelImportService>();

        _controller = new PurchaseOrdersController(
            _mockPoService.Object,
            _mockDispatchPackageService.Object,
            _mockMatrixExcelImportService.Object);
    }

    [Fact]
    public async Task GetPurchaseOrders_ReturnsOkWithPagedResult()
    {
        var paged = new PagedResult<PurchaseOrderDto>
        {
            Items = new List<PurchaseOrderDto>
            {
                new() { Id = Guid.NewGuid(), Code = "PO-01", Status = "APPROVED" }
            },
            TotalItems = 1,
            Page = 1,
            PageSize = 20
        };
        _mockPoService.Setup(s => s.GetPagedPurchaseOrdersAsync(It.IsAny<PurchaseOrderFilterQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(paged);

        var result = await _controller.GetPurchaseOrders(new PurchaseOrderFilterQuery(), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<PagedResult<PurchaseOrderDto>>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Single(resp.Data!.Items);
    }

    [Fact]
    public async Task GetById_ReturnsOkWithDto()
    {
        var id = Guid.NewGuid();
        var dto = new PurchaseOrderDto { Id = id, Code = "PO-02", Status = "CONFIRMED" };
        _mockPoService.Setup(s => s.GetPurchaseOrderByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);

        var result = await _controller.GetById(id, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<PurchaseOrderDto>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Equal(id, resp.Data!.Id);
    }

    [Fact]
    public async Task GetOrderMatrix_ReturnsOkWithMatrix()
    {
        var matrix = new OrderMatrixResponse();
        _mockPoService.Setup(s => s.GetOrderMatrixAsync(It.IsAny<OrderMatrixQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(matrix);

        var result = await _controller.GetOrderMatrix(new OrderMatrixQuery(), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<OrderMatrixResponse>>(ok.Value);
        Assert.True(resp.Success);
    }

    [Fact]
    public async Task GetDispatchHub_ReturnsOkWithHub()
    {
        var hub = new DispatchHubResponse();
        _mockPoService.Setup(s => s.GetDispatchHubAsync(It.IsAny<DispatchHubQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(hub);

        var result = await _controller.GetDispatchHub(new DispatchHubQuery(), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<DispatchHubResponse>>(ok.Value);
        Assert.True(resp.Success);
    }

    [Fact]
    public async Task GetDispatchSupplierHub_ReturnsOkWithSupplierHub()
    {
        var hub = new DispatchSupplierHubResponse();
        _mockPoService.Setup(s => s.GetDispatchSupplierHubAsync(It.IsAny<DispatchSupplierHubQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(hub);

        var result = await _controller.GetDispatchSupplierHub(new DispatchSupplierHubQuery(), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<DispatchSupplierHubResponse>>(ok.Value);
        Assert.True(resp.Success);
    }

    [Fact]
    public async Task GetDispatchSupplierDetail_ReturnsOkWithDetail()
    {
        var detail = new DispatchSupplierDetailResponse();
        _mockPoService.Setup(s => s.GetDispatchSupplierDetailAsync(It.IsAny<DispatchSupplierDetailQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);

        var result = await _controller.GetDispatchSupplierDetail(Guid.NewGuid(), 14, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<DispatchSupplierDetailResponse>>(ok.Value);
        Assert.True(resp.Success);
    }

    [Fact]
    public async Task SaveMatrixDraft_ReturnsOkWithDto()
    {
        var dto = new PurchaseOrderDto { Id = Guid.NewGuid(), Code = "PO-DRAFT-01" };
        var cmd = new SaveMatrixDraftCommand();
        _mockPoService.Setup(s => s.SaveMatrixDraftAsync(cmd, It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);

        var result = await _controller.SaveMatrixDraft(cmd, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<PurchaseOrderDto>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Equal("PO-DRAFT-01", resp.Data!.Code);
    }

    [Fact]
    public async Task PreviewMatrixExcel_FileNullOrEmpty_ReturnsBadRequest()
    {
        var resultNull = await _controller.PreviewMatrixExcel(null!, Guid.NewGuid(), DateTime.UtcNow, 7);
        Assert.IsType<BadRequestObjectResult>(resultNull);

        var emptyFile = new FormFile(new MemoryStream(Array.Empty<byte>()), 0, 0, "file", "empty.xlsx");
        var resultEmpty = await _controller.PreviewMatrixExcel(emptyFile, Guid.NewGuid(), DateTime.UtcNow, 7);
        Assert.IsType<BadRequestObjectResult>(resultEmpty);
    }

    [Fact]
    public async Task PreviewMatrixExcel_InvalidJson_ReturnsBadRequest()
    {
        var content = Encoding.UTF8.GetBytes("fake-excel-content");
        var file = new FormFile(new MemoryStream(content), 0, content.Length, "file", "test.xlsx");

        var result = await _controller.PreviewMatrixExcel(
            file, Guid.NewGuid(), DateTime.UtcNow, 7, allowedProducts: "invalid-not-json");

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<object>>(badRequest.Value);
        Assert.False(resp.Success);
        Assert.Equal("FORMAT_INVALID", resp.ErrorCode);
    }

    [Fact]
    public async Task PreviewMatrixExcel_ValidFile_ReturnsOk()
    {
        var content = Encoding.UTF8.GetBytes("fake-excel-content");
        var file = new FormFile(new MemoryStream(content), 0, content.Length, "file", "test.xlsx");

        var valResult = new MatrixExcelValidationResultDto { Valid = true };
        _mockMatrixExcelImportService.Setup(s => s.PreviewAsync(It.IsAny<Stream>(), It.IsAny<MatrixExcelPreviewQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(valResult);

        var result = await _controller.PreviewMatrixExcel(
            file, Guid.NewGuid(), DateTime.UtcNow, 7, allowedProducts: "[]");

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<MatrixExcelValidationResultDto>>(ok.Value);
        Assert.True(resp.Success);
    }

    [Fact]
    public async Task ImportMatrixExcel_ReturnsOkWithImportResult()
    {
        var cmd = new ImportMatrixExcelCommand();
        var importResult = new MatrixExcelImportResultDto { Success = true };
        _mockMatrixExcelImportService.Setup(s => s.ImportAsync(cmd, It.IsAny<CancellationToken>()))
            .ReturnsAsync(importResult);

        var result = await _controller.ImportMatrixExcel(cmd, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<MatrixExcelImportResultDto>>(ok.Value);
        Assert.True(resp.Success);
    }

    [Fact]
    public async Task PreviewSplitMatrixPo_ReturnsOkWithBatchResult()
    {
        var cmd = new SplitMatrixPoCommand();
        var batchResult = new SplitMatrixBatchResultDto { SuccessCount = 3 };
        _mockPoService.Setup(s => s.PreviewSplitMatrixPoAsync(cmd, It.IsAny<CancellationToken>()))
            .ReturnsAsync(batchResult);

        var result = await _controller.PreviewSplitMatrixPo(cmd, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<SplitMatrixBatchResultDto>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Equal(3, resp.Data!.SuccessCount);
    }

    [Fact]
    public async Task SplitMatrixPo_ReturnsOkWithBatchResult()
    {
        var cmd = new SplitMatrixPoCommand();
        var batchResult = new SplitMatrixBatchResultDto { SuccessCount = 2, AmendedCount = 1 };
        _mockPoService.Setup(s => s.SplitMatrixPoBatchAsync(cmd, It.IsAny<CancellationToken>()))
            .ReturnsAsync(batchResult);

        var result = await _controller.SplitMatrixPo(cmd, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<SplitMatrixBatchResultDto>>(ok.Value);
        Assert.True(resp.Success);
    }

    [Fact]
    public async Task UpdateStatus_FromQueryOrBody_ReturnsOkWithDto()
    {
        var id = Guid.NewGuid();
        var dto = new PurchaseOrderDto { Id = id, Status = "DISPATCHED" };
        _mockPoService.Setup(s => s.UpdatePurchaseOrderStatusAsync(id, "DISPATCHED", "Đã chốt", It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);

        // Via query parameters
        var resultQuery = await _controller.UpdateStatus(id, "DISPATCHED", "Đã chốt", null, CancellationToken.None);
        var okQuery = Assert.IsType<OkObjectResult>(resultQuery);
        var respQuery = Assert.IsType<ApiResponse<PurchaseOrderDto>>(okQuery.Value);
        Assert.True(respQuery.Success);
        Assert.Equal("DISPATCHED", respQuery.Data!.Status);

        // Via body
        var body = new UpdatePurchaseOrderStatusRequest { Status = "DISPATCHED", Reason = "Đã chốt" };
        var resultBody = await _controller.UpdateStatus(id, null, null, body, CancellationToken.None);
        var okBody = Assert.IsType<OkObjectResult>(resultBody);
        var respBody = Assert.IsType<ApiResponse<PurchaseOrderDto>>(okBody.Value);
        Assert.True(respBody.Success);
    }

    [Fact]
    public async Task ListDispatchPackages_ReturnsOkWithList()
    {
        var list = new List<SupplierDispatchPackageDto>
        {
            new() { Id = Guid.NewGuid(), SupplierCode = "SUP-01" }
        };
        _mockDispatchPackageService.Setup(s => s.ListAsync(It.IsAny<Guid?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(list);

        var result = await _controller.ListDispatchPackages(null, 50, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<IReadOnlyList<SupplierDispatchPackageDto>>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Single(resp.Data!);
    }

    [Fact]
    public async Task GetDispatchPackage_NotFoundAndFound()
    {
        var pkgId = Guid.NewGuid();

        // 1. Not found
        _mockDispatchPackageService.Setup(s => s.GetByIdAsync(pkgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SupplierDispatchPackageDto?)null);

        var resultNull = await _controller.GetDispatchPackage(pkgId, CancellationToken.None);
        Assert.IsType<NotFoundObjectResult>(resultNull);

        // 2. Found
        var pkg = new SupplierDispatchPackageDto { Id = pkgId, SupplierCode = "SUP-02" };
        _mockDispatchPackageService.Setup(s => s.GetByIdAsync(pkgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pkg);

        var resultFound = await _controller.GetDispatchPackage(pkgId, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(resultFound);
        var resp = Assert.IsType<ApiResponse<SupplierDispatchPackageDto>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Equal(pkgId, resp.Data!.Id);
    }

    [Fact]
    public async Task PackageOperations_RetryAndCancel_ReturnOk()
    {
        var pkgId = Guid.NewGuid();
        var pkg = new SupplierDispatchPackageDto { Id = pkgId };
        _mockDispatchPackageService.Setup(s => s.GetByIdAsync(pkgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pkg);

        // Retry generation
        var genResult = await _controller.RetryPackageGeneration(pkgId, CancellationToken.None);
        Assert.IsType<OkObjectResult>(genResult);

        // Retry email
        var emailResult = await _controller.RetryPackageEmail(pkgId, CancellationToken.None);
        Assert.IsType<OkObjectResult>(emailResult);

        // Cancel schedule
        var cancelResult = await _controller.CancelPackageSchedule(pkgId, CancellationToken.None);
        Assert.IsType<OkObjectResult>(cancelResult);
    }
}
