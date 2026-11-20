using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Moq;
using MT_GRMS.Api.Controllers;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Transfers;
using MT_GRMS.Domain.Enums;
using Xunit;

namespace MT_GRMS.UnitTests.Controllers;

public class TransfersControllerTests
{
    private readonly Mock<ITransferService> _mockTransferService;
    private readonly TransfersController _controller;

    public TransfersControllerTests()
    {
        _mockTransferService = new Mock<ITransferService>();
        _controller = new TransfersController(_mockTransferService.Object);
    }

    private static TransferDto CreateSampleTransfer(Guid id, string code = "TR-001")
    {
        return new TransferDto(
            Id: id,
            TransferCode: code,
            FromBranchId: Guid.NewGuid(),
            FromBranchName: "Branch 1",
            ToBranchId: Guid.NewGuid(),
            ToBranchName: "Branch 2",
            Status: "Draft",
            CreatedAt: DateTime.UtcNow,
            DispatchedAt: null,
            ReceivedAt: null,
            ExpectedDeliveryDate: null,
            CreatedBy: "User",
            TotalAmount: 1000m,
            Note: null,
            DiscrepancyNote: null,
            CancelReason: null,
            Items: new List<TransferLineDto>()
        );
    }

    [Fact]
    public async Task Get_ReturnsOkWithPagedResult()
    {
        var paged = new PagedResult<TransferDto>
        {
            Items = new List<TransferDto>
            {
                CreateSampleTransfer(Guid.NewGuid(), "TR-001")
            },
            TotalItems = 1,
            Page = 1,
            PageSize = 20
        };
        _mockTransferService.Setup(s => s.GetTransfersAsync(It.IsAny<TransferFilterParams>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(paged);

        var actionResult = await _controller.Get(null, null, null, null, null, null, 1, 20, null, null, null, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(actionResult.Result);
        var resp = Assert.IsType<ApiResponse<PagedResult<TransferDto>>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Single(resp.Data!.Items);
        Assert.Equal("TR-001", resp.Data.Items[0].TransferCode);
    }

    [Fact]
    public async Task GetById_ReturnsOkWithTransfer()
    {
        var id = Guid.NewGuid();
        var dto = CreateSampleTransfer(id, "TR-002");
        _mockTransferService.Setup(s => s.GetTransferAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);

        var actionResult = await _controller.GetById(id, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(actionResult.Result);
        var resp = Assert.IsType<ApiResponse<TransferDto>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Equal(id, resp.Data!.Id);
    }

    [Fact]
    public async Task GetStockBatches_ReturnsOkWithBatches()
    {
        var branchId = Guid.NewGuid();
        IReadOnlyList<TransferStockBatchDto> batches = new List<TransferStockBatchDto>
        {
            new(Guid.NewGuid(), "B-TR-1", null, 10m)
        };
        _mockTransferService.Setup(s => s.GetStockBatchesAsync(branchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(batches);

        var actionResult = await _controller.GetStockBatches(branchId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(actionResult.Result);
        var resp = Assert.IsType<ApiResponse<IReadOnlyList<TransferStockBatchDto>>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Single(resp.Data!);
    }

    [Fact]
    public async Task Create_ReturnsOk()
    {
        var request = new CreateTransferRequest(Guid.NewGuid(), Guid.NewGuid(), false, null, null, new List<TransferLineRequest>());
        var dto = CreateSampleTransfer(Guid.NewGuid(), "TR-NEW");
        _mockTransferService.Setup(s => s.CreateTransferAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);

        var actionResult = await _controller.Create(request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(actionResult.Result);
        var resp = Assert.IsType<ApiResponse<TransferDto>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Equal("Tạo phiếu điều chuyển thành công.", resp.Message);
    }

    [Fact]
    public async Task Receive_ReturnsOk()
    {
        var id = Guid.NewGuid();
        var request = new ReceiveTransferRequest(null, new List<ReceiveTransferLineRequest>());
        var dto = CreateSampleTransfer(id, "TR-001");
        _mockTransferService.Setup(s => s.ReceiveTransferAsync(id, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);

        var actionResult = await _controller.Receive(id, request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(actionResult.Result);
        var resp = Assert.IsType<ApiResponse<TransferDto>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Equal("Xác nhận nhận hàng thành công.", resp.Message);
    }

    [Fact]
    public async Task Dispatch_ReturnsOk()
    {
        var id = Guid.NewGuid();
        var dto = CreateSampleTransfer(id, "TR-001");
        _mockTransferService.Setup(s => s.DispatchTransferAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);

        var actionResult = await _controller.Dispatch(id, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(actionResult.Result);
        var resp = Assert.IsType<ApiResponse<TransferDto>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Equal("Xuất chuyển hàng thành công.", resp.Message);
    }

    [Fact]
    public async Task Cancel_ReturnsOk()
    {
        var id = Guid.NewGuid();
        var request = new CancelTransferRequest("Mistake");
        var dto = CreateSampleTransfer(id, "TR-001");
        _mockTransferService.Setup(s => s.CancelTransferAsync(id, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);

        var actionResult = await _controller.Cancel(id, request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(actionResult.Result);
        var resp = Assert.IsType<ApiResponse<TransferDto>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Equal("Hủy phiếu điều chuyển thành công.", resp.Message);
    }
}
