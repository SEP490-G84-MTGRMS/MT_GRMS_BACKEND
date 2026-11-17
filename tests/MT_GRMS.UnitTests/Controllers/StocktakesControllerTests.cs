using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Moq;
using MT_GRMS.Api.Controllers;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Stocktakes;
using MT_GRMS.Application.Stocktakes.Dtos;
using MT_GRMS.Application.SupplierReturns.Dtos;
using Xunit;

namespace MT_GRMS.UnitTests.Controllers;

public class StocktakesControllerTests
{
    private readonly Mock<IStocktakeService> _mockService;
    private readonly StocktakesController _controller;

    public StocktakesControllerTests()
    {
        _mockService = new Mock<IStocktakeService>();
        _controller = new StocktakesController(_mockService.Object);
    }

    private static StocktakeDetailDto CreateDetailDto(Guid id, string status = "DRAFT")
    {
        return new StocktakeDetailDto(
            id, "KK-20261101-001", Guid.NewGuid(), "BR01", "Chi nhánh 1",
            "ALL", "PERIODIC", status, "NV Kho", DateTime.UtcNow,
            null, null, null, null, "Ghi chú", new List<StocktakeLineDto>());
    }

    [Fact]
    public async Task GetPaged_ReturnsOkWithPagedResult()
    {
        var paged = new PagedResult<StocktakeListItemDto>
        {
            Items = new List<StocktakeListItemDto>
            {
                new(Guid.NewGuid(), "KK-01", Guid.NewGuid(), "BR01", "CN1", "ALL", "PERIODIC", "DRAFT",
                    "NV", DateTime.UtcNow, null, null, null, 1, 0, 0, null)
            },
            TotalItems = 1,
            Page = 1,
            PageSize = 20
        };
        _mockService.Setup(s => s.GetPagedAsync(It.IsAny<StocktakeFilterQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(paged);

        var result = await _controller.GetPaged(new StocktakeFilterQuery(), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<PagedResult<StocktakeListItemDto>>>(ok.Value);
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
        var resp = Assert.IsType<ApiResponse<StocktakeDetailDto>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Equal(id, resp.Data!.Id);
    }

    [Fact]
    public async Task Create_ReturnsCreatedAtActionWithDetailDto()
    {
        var id = Guid.NewGuid();
        var cmd = new CreateStocktakeCommand(Guid.NewGuid(), "ALL", "PERIODIC", "Note", null, null, null);
        var dto = CreateDetailDto(id);
        _mockService.Setup(s => s.CreateAsync(cmd, It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);

        var result = await _controller.Create(cmd, CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(nameof(StocktakesController.GetById), created.ActionName);
        var resp = Assert.IsType<ApiResponse<StocktakeDetailDto>>(created.Value);
        Assert.True(resp.Success);
        Assert.Equal(id, resp.Data!.Id);
    }

    [Fact]
    public async Task Update_ReturnsOkWithDetailDto()
    {
        var id = Guid.NewGuid();
        var cmd = new UpdateStocktakeCommand("ANOMALY", "Cập nhật", new List<StocktakeLineInputDto>());
        var dto = CreateDetailDto(id);
        _mockService.Setup(s => s.UpdateAsync(id, cmd, It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);

        var result = await _controller.Update(id, cmd, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<StocktakeDetailDto>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Equal(id, resp.Data!.Id);
    }

    [Fact]
    public async Task Submit_ReturnsOkWithDetailDto()
    {
        var id = Guid.NewGuid();
        var dto = CreateDetailDto(id, "PENDING_APPROVAL");
        _mockService.Setup(s => s.SubmitAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);

        var result = await _controller.Submit(id, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<StocktakeDetailDto>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Equal("PENDING_APPROVAL", resp.Data!.Status);
    }

    [Fact]
    public async Task Review_ReturnsOkWithDetailDto()
    {
        var id = Guid.NewGuid();
        var cmd = new ReviewStocktakeCommand("APPROVE", "Duyệt");
        var dto = CreateDetailDto(id, "APPROVED");
        _mockService.Setup(s => s.ReviewAsync(id, cmd, It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);

        var result = await _controller.Review(id, cmd, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<StocktakeDetailDto>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Equal("APPROVED", resp.Data!.Status);
    }

    [Fact]
    public async Task CreateReturnFromStocktake_ReturnsOkWithResult()
    {
        var id = Guid.NewGuid();
        var cmd = new CreateReturnFromStocktakeCommand(Guid.NewGuid(), "EXCHANGE", "Hàng lỗi", null, new());
        var returns = new List<SupplierReturnDetailDto>();
        _mockService.Setup(s => s.CreateReturnFromStocktakeAsync(id, cmd, It.IsAny<CancellationToken>()))
            .ReturnsAsync(returns);

        var result = await _controller.CreateReturnFromStocktake(id, cmd, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<object>>(ok.Value);
        Assert.True(resp.Success);
    }
}
