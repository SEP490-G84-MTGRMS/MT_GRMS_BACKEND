using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Moq;
using MT_GRMS.Api.Controllers;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Promotions;
using MT_GRMS.Application.Promotions.Dtos;
using Xunit;

namespace MT_GRMS.UnitTests.Controllers;

public class VouchersControllerTests
{
    private readonly Mock<IPromotionService> _mockService;
    private readonly VouchersController _controller;

    public VouchersControllerTests()
    {
        _mockService = new Mock<IPromotionService>();
        _controller = new VouchersController(_mockService.Object);
    }

    private static VoucherBatchResponse CreateTestBatch(Guid id, string code = "VCH-01")
    {
        return new VoucherBatchResponse(
            Id: id,
            BatchCode: code,
            Name: "Dot phat hanh Tet",
            DiscountType: "PERCENT",
            DiscountValue: 15m,
            MinOrderValue: 200_000m,
            MaxDiscountAmount: 100_000m,
            TotalQuantity: 100,
            UsedQuantity: 5,
            StartDate: DateTime.UtcNow,
            EndDate: DateTime.UtcNow.AddDays(15),
            Status: "ACTIVE",
            Prefix: "TET",
            Description: "Mo ta",
            CreatedAt: DateTime.UtcNow);
    }

    [Fact]
    public async Task GetBatches_ShouldReturnOk()
    {
        var paged = new PagedResult<VoucherBatchResponse>
        {
            Items = new List<VoucherBatchResponse> { CreateTestBatch(Guid.NewGuid()) },
            Page = 1,
            PageSize = 20,
            TotalItems = 1
        };

        _mockService
            .Setup(s => s.GetPagedVoucherBatchesAsync(1, 20, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(paged);

        var result = await _controller.GetBatches(1, 20, null, null, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var apiRes = Assert.IsType<ApiResponse<PagedResult<VoucherBatchResponse>>>(okResult.Value);
        Assert.True(apiRes.Success);
        Assert.Single(apiRes.Data!.Items);
    }

    [Fact]
    public async Task GetBatchById_ShouldReturnOk()
    {
        var id = Guid.NewGuid();
        var batch = CreateTestBatch(id);
        _mockService
            .Setup(s => s.GetVoucherBatchByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(batch);

        var result = await _controller.GetBatchById(id, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var apiRes = Assert.IsType<ApiResponse<VoucherBatchResponse>>(okResult.Value);
        Assert.True(apiRes.Success);
        Assert.Equal(id, apiRes.Data?.Id);
    }

    [Fact]
    public async Task DeleteBatch_ShouldReturnOk()
    {
        var id = Guid.NewGuid();
        _mockService
            .Setup(s => s.DeletePromotionAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _controller.DeleteBatch(id, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var apiRes = Assert.IsType<ApiResponse<bool>>(okResult.Value);
        Assert.True(apiRes.Success);
        Assert.True(apiRes.Data);
    }

    [Fact]
    public async Task ToggleBatchStatus_ShouldReturnOk()
    {
        var id = Guid.NewGuid();
        _mockService
            .Setup(s => s.TogglePromotionStatusAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _controller.ToggleBatchStatus(id, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var apiRes = Assert.IsType<ApiResponse<bool>>(okResult.Value);
        Assert.True(apiRes.Success);
        Assert.True(apiRes.Data);
    }

    [Fact]
    public async Task CreateBatch_ShouldReturnCreatedAtAction()
    {
        var id = Guid.NewGuid();
        var batch = CreateTestBatch(id, "VCH-NEW");
        var request = new VoucherBatchCreateRequest(
            "VCH-NEW", "Dot phat hanh", "PERCENT", 15m, 200_000m, 100_000m,
            100, DateTime.UtcNow, DateTime.UtcNow.AddDays(30), "TET", "Mo ta");

        _mockService
            .Setup(s => s.CreateVoucherBatchAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(batch);

        var result = await _controller.CreateBatch(request, CancellationToken.None);

        var createdResult = Assert.IsType<CreatedAtActionResult>(result);
        var apiRes = Assert.IsType<ApiResponse<VoucherBatchResponse>>(createdResult.Value);
        Assert.True(apiRes.Success);
        Assert.Equal(id, apiRes.Data?.Id);
    }

    [Fact]
    public async Task GenerateCodes_ShouldReturnOk()
    {
        var id = Guid.NewGuid();
        var request = new GenerateVoucherCodesRequest(50, "TET");
        _mockService
            .Setup(s => s.GenerateVoucherCodesAsync(id, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(50);

        var result = await _controller.GenerateCodes(id, request, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var apiRes = Assert.IsType<ApiResponse<int>>(okResult.Value);
        Assert.True(apiRes.Success);
        Assert.Equal(50, apiRes.Data);
    }

    [Fact]
    public async Task GetCodes_ShouldReturnOk()
    {
        var id = Guid.NewGuid();
        var codes = new List<VoucherCodeDto>
        {
            new(Guid.NewGuid(), id, "TET-ABC123", "UNUSED", null, null, null, null, null, null, DateTime.UtcNow)
        };
        var paged = new PagedResult<VoucherCodeDto>
        {
            Items = codes,
            Page = 1,
            PageSize = 50,
            TotalItems = 1
        };

        _mockService
            .Setup(s => s.GetVoucherCodesAsync(id, 1, 50, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(paged);

        var result = await _controller.GetCodes(id, 1, 50, null, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var apiRes = Assert.IsType<ApiResponse<PagedResult<VoucherCodeDto>>>(okResult.Value);
        Assert.True(apiRes.Success);
        Assert.Single(apiRes.Data!.Items);
    }

    [Fact]
    public async Task ValidateVoucher_ShouldReturnOk()
    {
        var request = new ValidateVoucherRequest { Code = "TET-ABC123", CartTotal = 250_000m };
        var response = new ValidateVoucherResponse(true, "Voucher hop le", 50_000m, "TET-ABC123");

        _mockService
            .Setup(s => s.ValidateVoucherAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var result = await _controller.ValidateVoucher(request, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var apiRes = Assert.IsType<ApiResponse<ValidateVoucherResponse>>(okResult.Value);
        Assert.True(apiRes.Success);
        Assert.True(apiRes.Data?.IsValid);
    }

    [Fact]
    public async Task RedeemVoucher_ShouldReturnOk_WhenSuccess_AndBadRequest_WhenFailed()
    {
        var request = new RedeemVoucherRequest("TET-ABC123", Guid.NewGuid(), "ORD-01");
        var successResponse = new RedeemVoucherResponse(true, "Ap dung thanh cong", "TET-ABC123");

        _mockService
            .Setup(s => s.RedeemVoucherAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(successResponse);

        var resSuccess = await _controller.RedeemVoucher(request, CancellationToken.None);
        var okResult = Assert.IsType<OkObjectResult>(resSuccess);
        var apiRes = Assert.IsType<ApiResponse<RedeemVoucherResponse>>(okResult.Value);
        Assert.True(apiRes.Success);

        // Fail path
        var failResponse = new RedeemVoucherResponse(false, "Voucher da het han", "TET-ABC123");
        _mockService
            .Setup(s => s.RedeemVoucherAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(failResponse);

        var resFail = await _controller.RedeemVoucher(request, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(resFail);
    }
}
