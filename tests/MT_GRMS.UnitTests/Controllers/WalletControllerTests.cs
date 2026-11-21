using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using MT_GRMS.Api.Controllers;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.SaaS.Dtos;
using MT_GRMS.Application.SaaS.Services;
using Xunit;

namespace MT_GRMS.UnitTests.Controllers;

public class WalletControllerTests
{
    private readonly Mock<IWalletService> _mockService;
    private readonly WalletController _controller;

    public WalletControllerTests()
    {
        _mockService = new Mock<IWalletService>();
        _controller = new WalletController(_mockService.Object);

        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("tenant_id", "tenant-test-01")
        }, "TestAuth"));

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };
    }

    [Fact]
    public async Task CreateTopupOrder_ShouldHandleSuccess_ArgumentException_AndKeyNotFound()
    {
        var request = new CreateTopupOrderRequest(100_000m);
        var response = new TopupOrderResponse(
            "TOPUP-01", 100_000m, "970422", "MBBank", "123456", "MT-MART",
            "TOPUP 01", "https://img.vietqr.io/123", DateTime.UtcNow.AddMinutes(15));

        _mockService.Setup(s => s.CreateTopupOrderAsync("tenant-test-01", request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var res = await _controller.CreateTopupOrder(request, CancellationToken.None);
        var okResult = Assert.IsType<OkObjectResult>(res);
        var apiRes = Assert.IsType<ApiResponse<TopupOrderResponse>>(okResult.Value);
        Assert.True(apiRes.Success);
        Assert.Equal("TOPUP-01", apiRes.Data?.OrderCode);

        // ArgumentException -> BadRequest
        _mockService.Setup(s => s.CreateTopupOrderAsync("tenant-test-01", request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("So tien khong hop le"));
        var resBad = await _controller.CreateTopupOrder(request, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(resBad);

        // KeyNotFoundException -> NotFound
        _mockService.Setup(s => s.CreateTopupOrderAsync("tenant-test-01", request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException("Tenant not found"));
        var resNotFound = await _controller.CreateTopupOrder(request, CancellationToken.None);
        Assert.IsType<NotFoundObjectResult>(resNotFound);
    }

    [Fact]
    public async Task GetTopupStatus_ShouldHandleFound_AndNotFound()
    {
        var orderCode = "TOPUP-01";
        var status = new TopupOrderStatusResponse(orderCode, "SUCCESS", 100_000m, 500_000m, DateTime.UtcNow);

        _mockService.Setup(s => s.GetTopupOrderStatusAsync("tenant-test-01", orderCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(status);

        var res = await _controller.GetTopupStatus(orderCode, CancellationToken.None);
        var okResult = Assert.IsType<OkObjectResult>(res);
        var apiRes = Assert.IsType<ApiResponse<TopupOrderStatusResponse>>(okResult.Value);
        Assert.True(apiRes.Success);
        Assert.Equal("SUCCESS", apiRes.Data?.Status);

        // Not found
        _mockService.Setup(s => s.GetTopupOrderStatusAsync("tenant-test-01", orderCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TopupOrderStatusResponse?)null);

        var resNotFound = await _controller.GetTopupStatus(orderCode, CancellationToken.None);
        Assert.IsType<NotFoundObjectResult>(resNotFound);
    }

    [Fact]
    public async Task GetBalance_ShouldReturnOk()
    {
        var balance = new WalletBalanceResponse(500_000m, 1_000_000m, 500_000m);
        _mockService.Setup(s => s.GetWalletBalanceAsync("tenant-test-01", It.IsAny<CancellationToken>()))
            .ReturnsAsync(balance);

        var res = await _controller.GetBalance(CancellationToken.None);
        var okResult = Assert.IsType<OkObjectResult>(res);
        var apiRes = Assert.IsType<ApiResponse<WalletBalanceResponse>>(okResult.Value);
        Assert.True(apiRes.Success);
        Assert.Equal(500_000m, apiRes.Data?.WalletBalance);
    }

    [Fact]
    public async Task GetTransactions_ShouldReturnOk()
    {
        var list = new List<WalletTransactionDto>
        {
            new("tx-1", "TX-01", "TOPUP", 100_000m, 0m, 100_000m, "Nap tien", "VietQR", "SUCCESS", "REF", DateTime.UtcNow)
        };

        _mockService.Setup(s => s.GetWalletTransactionsAsync("tenant-test-01", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(list);

        var res = await _controller.GetTransactions(null, CancellationToken.None);
        var okResult = Assert.IsType<OkObjectResult>(res);
        var apiRes = Assert.IsType<ApiResponse<IReadOnlyList<WalletTransactionDto>>>(okResult.Value);
        Assert.True(apiRes.Success);
        Assert.Single(apiRes.Data!);
    }
}
