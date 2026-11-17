using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Moq;
using MT_GRMS.Api.Controllers;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.HR.ShiftConfigs;
using MT_GRMS.Application.HR.ShiftConfigs.Dtos;
using MT_GRMS.Domain.Exceptions;
using Xunit;

namespace MT_GRMS.UnitTests.Controllers;

public class ShiftConfigsControllerTests
{
    private readonly Mock<IShiftConfigService> _mockService;
    private readonly ShiftConfigsController _controller;

    public ShiftConfigsControllerTests()
    {
        _mockService = new Mock<IShiftConfigService>();
        _controller = new ShiftConfigsController(_mockService.Object);
    }

    private static ShiftConfigResponse CreateTestConfig(Guid id, string code = "CA-SANG")
    {
        return new ShiftConfigResponse(
            Id: id,
            BranchId: Guid.NewGuid(),
            BranchIds: new List<Guid>(),
            ShiftCode: code,
            ShiftName: "Ca sang",
            StartTime: TimeSpan.FromHours(6),
            EndTime: TimeSpan.FromHours(14),
            DurationHours: 8,
            IsCheckInNextDay: false,
            Description: "Ca sang",
            IsActive: true,
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: null);
    }

    [Fact]
    public async Task GetShiftConfigs_ShouldReturnOk()
    {
        var paged = new PagedResult<ShiftConfigResponse>
        {
            Items = new List<ShiftConfigResponse> { CreateTestConfig(Guid.NewGuid()) },
            Page = 1,
            PageSize = 20,
            TotalItems = 1
        };

        _mockService
            .Setup(s => s.GetShiftConfigsPagedAsync(It.IsAny<ShiftConfigQueryFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(paged);

        var result = await _controller.GetShiftConfigs(new ShiftConfigQueryFilter(), CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var apiRes = Assert.IsType<ApiResponse<PagedResult<ShiftConfigResponse>>>(okResult.Value);
        Assert.True(apiRes.Success);
        Assert.Single(apiRes.Data!.Items);
    }

    [Fact]
    public async Task GetById_ShouldReturnOk_WhenFound_AndNotFound_WhenThrows()
    {
        var id = Guid.NewGuid();
        var config = CreateTestConfig(id);
        _mockService.Setup(s => s.GetShiftConfigByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(config);

        var resFound = await _controller.GetById(id, CancellationToken.None);
        var okResult = Assert.IsType<OkObjectResult>(resFound);
        var apiRes = Assert.IsType<ApiResponse<ShiftConfigResponse>>(okResult.Value);
        Assert.True(apiRes.Success);
        Assert.Equal(id, apiRes.Data?.Id);

        _mockService.Setup(s => s.GetShiftConfigByIdAsync(id, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NotFoundException("ShiftConfig", id));

        var resNotFound = await _controller.GetById(id, CancellationToken.None);
        Assert.IsType<NotFoundObjectResult>(resNotFound);
    }

    [Fact]
    public async Task Create_ShouldHandleSuccess_DuplicateCode_AndBusinessException()
    {
        var id = Guid.NewGuid();
        var config = CreateTestConfig(id);
        var request = new CreateShiftConfigRequest("Ca sang", TimeSpan.FromHours(6), TimeSpan.FromHours(14));

        _mockService.Setup(s => s.CreateShiftConfigAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(config);

        var res = await _controller.Create(request, CancellationToken.None);
        var createdResult = Assert.IsType<CreatedAtActionResult>(res);
        var apiRes = Assert.IsType<ApiResponse<ShiftConfigResponse>>(createdResult.Value);
        Assert.True(apiRes.Success);

        // Conflict DUPLICATE_SHIFT_CODE
        _mockService.Setup(s => s.CreateShiftConfigAsync(request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BusinessException("DUPLICATE_SHIFT_CODE", "Ma ca trung"));
        var resConflict = await _controller.Create(request, CancellationToken.None);
        Assert.IsType<ConflictObjectResult>(resConflict);

        // General BusinessException
        _mockService.Setup(s => s.CreateShiftConfigAsync(request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BusinessException("INVALID_TIME", "Gio bat dau sai"));
        var resBad = await _controller.Create(request, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(resBad);
    }

    [Fact]
    public async Task Update_ShouldHandleSuccess_NotFound_AndBusinessException()
    {
        var id = Guid.NewGuid();
        var config = CreateTestConfig(id);
        var request = new UpdateShiftConfigRequest("Ca chieu", TimeSpan.FromHours(14), TimeSpan.FromHours(22));

        _mockService.Setup(s => s.UpdateShiftConfigAsync(id, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(config);

        var res = await _controller.Update(id, request, CancellationToken.None);
        var okResult = Assert.IsType<OkObjectResult>(res);
        var apiRes = Assert.IsType<ApiResponse<ShiftConfigResponse>>(okResult.Value);
        Assert.True(apiRes.Success);

        // NotFound
        _mockService.Setup(s => s.UpdateShiftConfigAsync(id, request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NotFoundException("ShiftConfig", id));
        var resNotFound = await _controller.Update(id, request, CancellationToken.None);
        Assert.IsType<NotFoundObjectResult>(resNotFound);

        // BusinessException
        _mockService.Setup(s => s.UpdateShiftConfigAsync(id, request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BusinessException("INVALID_TIME", "Loi"));
        var resBad = await _controller.Update(id, request, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(resBad);
    }

    [Fact]
    public async Task UpdateStatus_ShouldHandleSuccess_AndNotFound()
    {
        var id = Guid.NewGuid();
        var config = CreateTestConfig(id);
        var request = new UpdateShiftConfigStatusRequest(false);

        _mockService.Setup(s => s.UpdateShiftConfigStatusAsync(id, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(config);

        var res = await _controller.UpdateStatus(id, request, CancellationToken.None);
        var okResult = Assert.IsType<OkObjectResult>(res);
        var apiRes = Assert.IsType<ApiResponse<ShiftConfigResponse>>(okResult.Value);
        Assert.True(apiRes.Success);

        // NotFound
        _mockService.Setup(s => s.UpdateShiftConfigStatusAsync(id, request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NotFoundException("ShiftConfig", id));
        var resNotFound = await _controller.UpdateStatus(id, request, CancellationToken.None);
        Assert.IsType<NotFoundObjectResult>(resNotFound);
    }

    [Fact]
    public async Task Delete_ShouldHandleSuccess_AndNotFound()
    {
        var id = Guid.NewGuid();
        _mockService.Setup(s => s.DeleteShiftConfigAsync(id, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var res = await _controller.Delete(id, CancellationToken.None);
        Assert.IsType<NoContentResult>(res);

        // NotFound
        _mockService.Setup(s => s.DeleteShiftConfigAsync(id, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NotFoundException("ShiftConfig", id));
        var resNotFound = await _controller.Delete(id, CancellationToken.None);
        Assert.IsType<NotFoundObjectResult>(resNotFound);
    }
}
