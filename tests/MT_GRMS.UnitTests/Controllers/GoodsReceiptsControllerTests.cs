using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Moq;
using MT_GRMS.Api.Controllers;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.GoodsReceipts;
using MT_GRMS.Application.GoodsReceipts.Dtos;
using Xunit;

namespace MT_GRMS.UnitTests.Controllers;

public class GoodsReceiptsControllerTests
{
    private readonly Mock<IGoodsReceiptService> _mockService;
    private readonly GoodsReceiptsController _controller;

    public GoodsReceiptsControllerTests()
    {
        _mockService = new Mock<IGoodsReceiptService>();
        _controller = new GoodsReceiptsController(_mockService.Object);
    }

    [Fact]
    public async Task GetGoodsReceipts_ReturnsOkWithPagedResult()
    {
        var paged = new PagedResult<GoodsReceiptDto>
        {
            Items = new List<GoodsReceiptDto>
            {
                new() { Id = Guid.NewGuid(), Code = "GRN-01", Status = "DRAFT" }
            },
            TotalItems = 1,
            Page = 1,
            PageSize = 20
        };
        _mockService.Setup(s => s.GetGoodsReceiptsAsync(It.IsAny<GoodsReceiptFilterQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(paged);

        var result = await _controller.GetGoodsReceipts(new GoodsReceiptFilterQuery(), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<PagedResult<GoodsReceiptDto>>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Single(resp.Data!.Items);
        Assert.Equal("GRN-01", resp.Data.Items[0].Code);
    }

    [Fact]
    public async Task GetById_ReturnsOkWithDto()
    {
        var id = Guid.NewGuid();
        var dto = new GoodsReceiptDto { Id = id, Code = "GRN-02", Status = "COMPLETED" };
        _mockService.Setup(s => s.GetGoodsReceiptByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);

        var result = await _controller.GetById(id, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<GoodsReceiptDto>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Equal(id, resp.Data!.Id);
    }

    [Fact]
    public async Task GetPoLookup_ReturnsOkWithLookupList()
    {
        var branchId = Guid.NewGuid();
        var list = new List<PurchaseOrderLookupDto>
        {
            new() { Id = Guid.NewGuid(), Code = "PO-01" }
        };
        _mockService.Setup(s => s.GetPoLookupAsync(branchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(list);

        var result = await _controller.GetPoLookup(branchId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<List<PurchaseOrderLookupDto>>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Single(resp.Data!);
    }

    [Fact]
    public async Task GetPoReceiptDetails_ReturnsOkWithDetails()
    {
        var details = new PoReceiptDetailsDto
        {
            Id = Guid.NewGuid(),
            Code = "PO-02"
        };
        _mockService.Setup(s => s.GetPoReceiptDetailsAsync("PO-02", It.IsAny<CancellationToken>()))
            .ReturnsAsync(details);

        var result = await _controller.GetPoReceiptDetails("PO-02", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<PoReceiptDetailsDto>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Equal("PO-02", resp.Data!.Code);
    }

    [Fact]
    public async Task CreateGoodsReceipt_ReturnsCreatedAtActionWithDto()
    {
        var id = Guid.NewGuid();
        var cmd = new CreateGoodsReceiptCommand();
        var dto = new GoodsReceiptDto { Id = id, Code = "GRN-NEW" };

        _mockService.Setup(s => s.CreateGoodsReceiptAsync(cmd, It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);

        var result = await _controller.CreateGoodsReceipt(cmd, CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(nameof(GoodsReceiptsController.GetById), created.ActionName);
        var resp = Assert.IsType<ApiResponse<GoodsReceiptDto>>(created.Value);
        Assert.True(resp.Success);
        Assert.Equal(id, resp.Data!.Id);
    }

    [Fact]
    public async Task UpdateDraft_ReturnsOkWithDto()
    {
        var id = Guid.NewGuid();
        var cmd = new CreateGoodsReceiptCommand();
        var dto = new GoodsReceiptDto { Id = id, Code = "GRN-DRAFT-UPDATED" };

        _mockService.Setup(s => s.UpdateDraftGoodsReceiptAsync(id, cmd, It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);

        var result = await _controller.UpdateDraft(id, cmd, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<GoodsReceiptDto>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Equal("GRN-DRAFT-UPDATED", resp.Data!.Code);
    }

    [Fact]
    public async Task CompleteGoodsReceipt_ReturnsOkWithDto()
    {
        var id = Guid.NewGuid();
        var cmd = new CompleteGoodsReceiptCommand();
        var dto = new GoodsReceiptDto { Id = id, Status = "COMPLETED" };

        _mockService.Setup(s => s.CompleteGoodsReceiptAsync(id, cmd, It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);

        var result = await _controller.CompleteGoodsReceipt(id, cmd, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<GoodsReceiptDto>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Equal("COMPLETED", resp.Data!.Status);
    }

    [Fact]
    public async Task CancelGoodsReceipt_ReturnsOkWithDto()
    {
        var id = Guid.NewGuid();
        var cmd = new CancelGoodsReceiptCommand { Reason = "Hủy do sai thông tin" };
        var dto = new GoodsReceiptDto { Id = id, Status = "CANCELLED" };

        _mockService.Setup(s => s.CancelGoodsReceiptAsync(id, cmd, It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);

        var result = await _controller.CancelGoodsReceipt(id, cmd, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<GoodsReceiptDto>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Equal("CANCELLED", resp.Data!.Status);
    }
}
