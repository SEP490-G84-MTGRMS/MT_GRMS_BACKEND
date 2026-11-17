using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Moq;
using MT_GRMS.Api.Controllers;
using MT_GRMS.Application.BusinessDays;
using MT_GRMS.Application.BusinessDays.Dtos;
using MT_GRMS.Application.Common;
using Xunit;

namespace MT_GRMS.UnitTests.Controllers;

public class BusinessDaysControllerTests
{
    private readonly Mock<IBusinessDayClosingService> _mockService;
    private readonly BusinessDaysController _controller;

    public BusinessDaysControllerTests()
    {
        _mockService = new Mock<IBusinessDayClosingService>();
        _controller = new BusinessDaysController(_mockService.Object);
    }

    [Fact]
    public async Task Current_ShouldReturnOk()
    {
        var branchId = Guid.NewGuid();
        var date = DateOnly.FromDateTime(DateTime.UtcNow);
        var response = new BusinessDateResponse(branchId, date, TimeSpan.FromHours(6));

        _mockService.Setup(s => s.GetCurrentBusinessDateAsync(branchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var result = await _controller.Current(branchId, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var apiRes = Assert.IsType<ApiResponse<BusinessDateResponse>>(okResult.Value);
        Assert.True(apiRes.Success);
        Assert.Equal(date, apiRes.Data?.BusinessDate);
    }

    [Fact]
    public async Task Preview_ShouldReturnOk()
    {
        var branchId = Guid.NewGuid();
        var date = DateOnly.FromDateTime(DateTime.UtcNow);
        var preview = new BusinessDayClosingPreview(
            branchId, date, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow,
            1_000_000m, 500_000m, 0m, 0m, 500_000m, 500_000m,
            new List<BusinessDayFundPreview>(), new List<BusinessDayBlocker>(), "PREVIEW-TOKEN");

        _mockService.Setup(s => s.PreviewAsync(branchId, date, It.IsAny<CancellationToken>()))
            .ReturnsAsync(preview);

        var result = await _controller.Preview(branchId, date, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var apiRes = Assert.IsType<ApiResponse<BusinessDayClosingPreview>>(okResult.Value);
        Assert.True(apiRes.Success);
        Assert.Equal("PREVIEW-TOKEN", apiRes.Data?.PreviewToken);
    }

    [Fact]
    public async Task Close_ShouldReturnOk()
    {
        var branchId = Guid.NewGuid();
        var date = DateOnly.FromDateTime(DateTime.UtcNow);
        var request = new CloseBusinessDayRequest(branchId, date, new List<BusinessDayFundCount>(), "TOKEN");
        var response = new BusinessDayClosingResponse(
            Guid.NewGuid(), branchId, date, "CLOSED", 2, 50,
            5_000_000m, 3_000_000m, 0m, 0m, 0m, 0m, 1_000_000m, 4_000_000m,
            5_000_000m, 5_000_000m, 0m, "REF-BDC-01", DateTime.UtcNow);

        _mockService.Setup(s => s.CloseAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var result = await _controller.Close(request, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var apiRes = Assert.IsType<ApiResponse<BusinessDayClosingResponse>>(okResult.Value);
        Assert.True(apiRes.Success);
        Assert.Equal("REF-BDC-01", apiRes.Data?.ReferenceCode);
    }

    [Fact]
    public async Task History_ShouldReturnOk()
    {
        var branchId = Guid.NewGuid();
        var date = DateOnly.FromDateTime(DateTime.UtcNow);
        var history = new List<BusinessDayClosingVersionResponse>
        {
            new(Guid.NewGuid(), branchId, date, 1, "CLOSED", true, null, "REF-01",
                5_000_000m, 3_000_000m, 0m, 0m, 0m, DateTime.UtcNow, null, null, null)
        };

        _mockService.Setup(s => s.GetHistoryAsync(branchId, date, It.IsAny<CancellationToken>()))
            .ReturnsAsync(history);

        var result = await _controller.History(branchId, date, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var apiRes = Assert.IsType<ApiResponse<IReadOnlyList<BusinessDayClosingVersionResponse>>>(okResult.Value);
        Assert.True(apiRes.Success);
        Assert.Single(apiRes.Data!);
    }

    [Fact]
    public async Task Reopen_ShouldReturnOk()
    {
        var id = Guid.NewGuid();
        var request = new ReopenBusinessDayRequest("Sua hoa don bi thieu");
        var response = new BusinessDayClosingVersionResponse(
            id, Guid.NewGuid(), DateOnly.FromDateTime(DateTime.UtcNow), 2, "REOPENED", false, null, "REF-02",
            5_000_000m, 3_000_000m, 0m, 0m, 0m, DateTime.UtcNow.AddHours(-1), DateTime.UtcNow, Guid.NewGuid(), request.Reason);

        _mockService.Setup(s => s.ReopenAsync(id, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var result = await _controller.Reopen(id, request, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var apiRes = Assert.IsType<ApiResponse<BusinessDayClosingVersionResponse>>(okResult.Value);
        Assert.True(apiRes.Success);
        Assert.Equal("REOPENED", apiRes.Data?.Status);
    }
}
