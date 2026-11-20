using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Moq;
using MT_GRMS.Api.Controllers;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.SupplierReturns;
using MT_GRMS.Application.SupplierReturns.Dtos;
using Xunit;

namespace MT_GRMS.UnitTests.Controllers;

public class SupplierReturnsControllerTests
{
    private readonly Mock<ISupplierReturnService> _mockService;
    private readonly SupplierReturnsController _controller;

    public SupplierReturnsControllerTests()
    {
        _mockService = new Mock<ISupplierReturnService>();
        _controller = new SupplierReturnsController(_mockService.Object);
    }

    private static SupplierReturnDetailDto CreateDetailDto(Guid id, string status = "DRAFT")
    {
        return new SupplierReturnDetailDto(
            id, "RTV-001", Guid.NewGuid(), "GRN-01", Guid.NewGuid(), "SUP01", "NCC 1",
            Guid.NewGuid(), "BR01", "CN 1", status, "Hàng hỏng", null, null, null, null,
            10, 0, 0, 300000, DateTime.UtcNow, null, null, null, null,
            new List<SupplierReturnLineDto>());
    }

    [Fact]
    public async Task GetPaged_ReturnsOkWithPagedResult()
    {
        var paged = new PagedResult<SupplierReturnListItemDto>
        {
            Items = new List<SupplierReturnListItemDto>
            {
                new(Guid.NewGuid(), "RTV-01", Guid.NewGuid(), "GRN-01", Guid.NewGuid(), "SUP01", "NCC 1",
                    Guid.NewGuid(), "BR01", "CN 1", "DRAFT", "Lỗi", 1, 10, 0, 0, 100000, DateTime.UtcNow, null)
            },
            TotalItems = 1,
            Page = 1,
            PageSize = 20
        };
        _mockService.Setup(s => s.GetPagedAsync(It.IsAny<SupplierReturnFilterQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(paged);

        var result = await _controller.GetPaged(new SupplierReturnFilterQuery(), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<PagedResult<SupplierReturnListItemDto>>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Single(resp.Data!.Items);
    }

    [Fact]
    public async Task GetById_ReturnsOkWithDetailDto()
    {
        var id = Guid.NewGuid();
        var dto = CreateDetailDto(id);
        _mockService.Setup(s => s.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);

        var result = await _controller.GetById(id, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<SupplierReturnDetailDto>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Equal(id, resp.Data!.Id);
    }

    [Fact]
    public async Task GetEligibleGrns_ReturnsOkWithList()
    {
        var list = new List<EligibleGrnLookupDto>
        {
            new(Guid.NewGuid(), "GRN-01", Guid.NewGuid(), "NCC 1", Guid.NewGuid(), "CN 1", DateTime.UtcNow, 2)
        };
        _mockService.Setup(s => s.GetEligibleGrnsAsync(It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(list);

        var result = await _controller.GetEligibleGrns(null, null, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<List<EligibleGrnLookupDto>>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Single(resp.Data!);
    }

    [Fact]
    public async Task GetGrnLines_ReturnsOkWithList()
    {
        var grnId = Guid.NewGuid();
        var list = new List<GrnReturnableLineDto>
        {
            new(Guid.NewGuid(), Guid.NewGuid(), "P01", "Sản phẩm 1", "Cái", "B01", null, 10, 0, 0, 10, 50000)
        };
        _mockService.Setup(s => s.GetGrnReturnableLinesAsync(grnId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(list);

        var result = await _controller.GetGrnLines(grnId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<List<GrnReturnableLineDto>>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Single(resp.Data!);
    }

    [Fact]
    public async Task Create_ReturnsCreatedAtActionWithDetailDto()
    {
        var id = Guid.NewGuid();
        var cmd = new CreateSupplierReturnCommand(Guid.NewGuid(), "Hỏng", null, null, new());
        var dto = CreateDetailDto(id);
        _mockService.Setup(s => s.CreateAsync(cmd, It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);

        var result = await _controller.Create(cmd, CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(nameof(SupplierReturnsController.GetById), created.ActionName);
        var resp = Assert.IsType<ApiResponse<SupplierReturnDetailDto>>(created.Value);
        Assert.True(resp.Success);
        Assert.Equal(id, resp.Data!.Id);
    }

    [Fact]
    public async Task Submit_ReturnsOkWithDetailDto()
    {
        var id = Guid.NewGuid();
        var dto = CreateDetailDto(id, "APPROVED");
        _mockService.Setup(s => s.SubmitAsync(id, "Gửi", It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);

        var result = await _controller.Submit(id, "Gửi", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<SupplierReturnDetailDto>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Equal("APPROVED", resp.Data!.Status);
    }

    [Fact]
    public async Task RecordSupplierResponse_ReturnsOkWithDetailDto()
    {
        var id = Guid.NewGuid();
        var cmd = new RecordSupplierResponseCommand("APPROVED_FULL", "Đồng ý", null);
        var dto = CreateDetailDto(id, "APPROVED");
        _mockService.Setup(s => s.RecordSupplierResponseAsync(id, cmd, It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);

        var result = await _controller.RecordSupplierResponse(id, cmd, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<SupplierReturnDetailDto>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Equal("APPROVED", resp.Data!.Status);
    }

    [Fact]
    public async Task CompleteHandover_ReturnsOkWithDetailDto()
    {
        var id = Guid.NewGuid();
        var cmd = new CompleteHandoverCommand("NV Kho", "Giao tài xế", null);
        var dto = CreateDetailDto(id, "COMPLETED");
        _mockService.Setup(s => s.CompleteHandoverAsync(id, cmd, It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);

        var result = await _controller.CompleteHandover(id, cmd, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<SupplierReturnDetailDto>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Equal("COMPLETED", resp.Data!.Status);
    }

    [Fact]
    public async Task Cancel_ReturnsOkWithDetailDto()
    {
        var id = Guid.NewGuid();
        var cmd = new CancelSupplierReturnCommand("Hủy");
        var dto = CreateDetailDto(id, "CANCELLED");
        _mockService.Setup(s => s.CancelAsync(id, cmd, It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);

        var result = await _controller.Cancel(id, cmd, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<SupplierReturnDetailDto>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Equal("CANCELLED", resp.Data!.Status);
    }

    [Fact]
    public async Task CreateExchangePurchaseOrders_ReturnsOkWithList()
    {
        var id = Guid.NewGuid();
        var list = new List<ExchangePurchaseOrderSummaryDto>
        {
            new(Guid.NewGuid(), "PO-EX-01", "CONFIRMED", 10, 300000, DateTime.UtcNow, DateTime.UtcNow)
        };
        _mockService.Setup(s => s.CreateExchangePurchaseOrdersAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(list);

        var result = await _controller.CreateExchangePurchaseOrders(id, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<IReadOnlyList<ExchangePurchaseOrderSummaryDto>>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Single(resp.Data!);
    }

    [Fact]
    public async Task GetRelatedPurchaseOrders_ReturnsOkWithList()
    {
        var id = Guid.NewGuid();
        var list = new List<ExchangePurchaseOrderSummaryDto>
        {
            new(Guid.NewGuid(), "PO-EX-01", "CONFIRMED", 10, 300000, DateTime.UtcNow, DateTime.UtcNow)
        };
        _mockService.Setup(s => s.GetRelatedPurchaseOrdersAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(list);

        var result = await _controller.GetRelatedPurchaseOrders(id, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<IReadOnlyList<ExchangePurchaseOrderSummaryDto>>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Single(resp.Data!);
    }

    [Fact]
    public async Task RecordCostSharePayment_ReturnsOkWithDetailDto()
    {
        var id = Guid.NewGuid();
        var cmd = new RecordCostSharePaymentCommand(100000, "CK");
        var dto = CreateDetailDto(id);
        _mockService.Setup(s => s.RecordCostSharePaymentAsync(id, cmd, It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);

        var result = await _controller.RecordCostSharePayment(id, cmd, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<SupplierReturnDetailDto>>(ok.Value);
        Assert.True(resp.Success);
    }
}

