using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Moq;
using MT_GRMS.Api.Controllers;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Debt;
using MT_GRMS.Application.Debt.Dtos;
using MT_GRMS.Domain.Enums;
using Xunit;

namespace MT_GRMS.UnitTests.Controllers;

public class CustomerDebtControllerTests
{
    private readonly Mock<ICustomerDebtService> _mockDebtService;
    private readonly CustomerDebtController _controller;

    public CustomerDebtControllerTests()
    {
        _mockDebtService = new Mock<ICustomerDebtService>();
        _controller = new CustomerDebtController(_mockDebtService.Object);
    }

    [Fact]
    public async Task RecordSaleDebt_ReturnsOk()
    {
        var request = new RecordSaleDebtRequest(Guid.NewGuid(), Guid.NewGuid(), "ORD-01", Guid.NewGuid(), 500000m, null, null);
        var response = new CustomerDebtInvoiceResponse(Guid.NewGuid(), request.CustomerId, request.OrderId, "ORD-01", 500000m, 500000m, DateTime.UtcNow.AddDays(30), "Unpaid", false);
        _mockDebtService.Setup(s => s.RecordSaleDebtAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var actionResult = await _controller.RecordSaleDebt(request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(actionResult.Result);
        var resp = Assert.IsType<ApiResponse<CustomerDebtInvoiceResponse>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Equal(500000m, resp.Data!.TotalDebtAmount);
    }

    [Fact]
    public async Task RepayDebt_ReturnsOk()
    {
        var request = new RepayDebtRequest(Guid.NewGuid(), 200000m, FinanceAccountType.Till, Guid.NewGuid(), null, null);
        var response = new RepaymentResultResponse(Guid.NewGuid(), "DE-01", 200000m, 300000m, 0m, new List<AllocationDetailResponse>());
        _mockDebtService.Setup(s => s.RepayDebtAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var actionResult = await _controller.RepayDebt(request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(actionResult.Result);
        var resp = Assert.IsType<ApiResponse<RepaymentResultResponse>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Equal(300000m, resp.Data!.RemainingDebt);
    }

    [Fact]
    public async Task OffsetGoodsReturnDebt_ReturnsOk()
    {
        var request = new OffsetGoodsReturnDebtRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 100000m, false, null, null);
        var response = new CustomerDebtEntryResponse(Guid.NewGuid(), "DE-01", request.CustomerId, "RETURN_OFFSET", -100000m, 0m, 0m, "REF", "Desc", DateTime.UtcNow);
        _mockDebtService.Setup(s => s.OffsetGoodsReturnDebtAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var actionResult = await _controller.OffsetGoodsReturnDebt(request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(actionResult.Result);
        var resp = Assert.IsType<ApiResponse<CustomerDebtEntryResponse>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Equal(-100000m, resp.Data!.Amount);
    }

    [Fact]
    public async Task AdjustDebt_ReturnsOk()
    {
        var request = new AdjustDebtRequest(Guid.NewGuid(), 1000m, true, "Correction", "1234");
        var response = new CustomerDebtEntryResponse(Guid.NewGuid(), "DE-02", request.CustomerId, "ADJUSTMENT", 1000m, 1000m, 0m, "REF", "Desc", DateTime.UtcNow);
        _mockDebtService.Setup(s => s.AdjustDebtAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var actionResult = await _controller.AdjustDebt(request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(actionResult.Result);
        var resp = Assert.IsType<ApiResponse<CustomerDebtEntryResponse>>(ok.Value);
        Assert.True(resp.Success);
    }

    [Fact]
    public async Task WriteOffBadDebt_ReturnsOk()
    {
        var request = new WriteOffBadDebtRequest(Guid.NewGuid(), 5000m, "Bad debt", "http://example.com");
        var response = new CustomerDebtEntryResponse(Guid.NewGuid(), "DE-03", request.CustomerId, "WRITE_OFF", -5000m, 0m, 0m, "REF", "Desc", DateTime.UtcNow);
        _mockDebtService.Setup(s => s.WriteOffBadDebtAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var actionResult = await _controller.WriteOffBadDebt(request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(actionResult.Result);
        var resp = Assert.IsType<ApiResponse<CustomerDebtEntryResponse>>(ok.Value);
        Assert.True(resp.Success);
    }

    [Fact]
    public async Task GetProfile_ReturnsOk()
    {
        var customerId = Guid.NewGuid();
        var response = new CustomerDebtProfileResponse(Guid.NewGuid(), customerId, 1000000m, 30, 150000m, 0m, "Normal", null, null, new List<CustomerDebtInvoiceResponse>());
        _mockDebtService.Setup(s => s.GetProfileAsync(customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var actionResult = await _controller.GetProfile(customerId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(actionResult.Result);
        var resp = Assert.IsType<ApiResponse<CustomerDebtProfileResponse>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Equal(150000m, resp.Data!.CurrentDebtBalance);
    }

    [Fact]
    public async Task GetLedger_ReturnsOk()
    {
        var customerId = Guid.NewGuid();
        var list = new List<CustomerDebtEntryResponse>
        {
            new(Guid.NewGuid(), "DE-01", customerId, "SALE_DEBT", 1000m, 1000m, 0m, "REF", "Desc", DateTime.UtcNow)
        };
        _mockDebtService.Setup(s => s.GetLedgerAsync(customerId, 1, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(list);

        var actionResult = await _controller.GetLedger(customerId, 1, 50, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(actionResult.Result);
        var resp = Assert.IsType<ApiResponse<List<CustomerDebtEntryResponse>>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Single(resp.Data!);
    }
}
