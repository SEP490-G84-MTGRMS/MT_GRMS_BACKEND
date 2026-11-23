using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Moq;
using MT_GRMS.Api.Controllers;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Repacks;
using MT_GRMS.Application.Repacks.Dtos;
using Xunit;

namespace MT_GRMS.UnitTests.Controllers;

public class RepackOrdersControllerTests
{
    private readonly Mock<IRepackService> _mockService;
    private readonly RepackOrdersController _controller;

    public RepackOrdersControllerTests()
    {
        _mockService = new Mock<IRepackService>();
        _controller = new RepackOrdersController(_mockService.Object);
    }

    private static PackageDto CreatePackageDto(Guid id, string status = "AVAILABLE")
    {
        return new PackageDto(
            id, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "PKG-01", "Cam khay 1.5kg", null,
            1.5m, 50000m, DateTime.UtcNow, DateTime.UtcNow.AddDays(3),
            "PORTIONING", status, null, null, null, DateTime.UtcNow);
    }

    [Fact]
    public async Task GetPackages_ReturnsOkWithSummary()
    {
        var stockId = Guid.NewGuid();
        var summary = new BranchStockPackageSummaryDto(
            stockId, Guid.NewGuid(), "Cam sành", "B01", "Kg", true, true,
            10m, 0m, 0m, 0m, 0, 10m, null, 30000m, 45000m, new List<PackageDto>());
        _mockService.Setup(s => s.GetBranchStockPackagesAsync(stockId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(summary);

        var result = await _controller.GetPackages(stockId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<BranchStockPackageSummaryDto>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Equal(stockId, resp.Data!.BranchStockId);
    }

    [Fact]
    public async Task Create_ReturnsOkWithRepackOrderDto()
    {
        var req = new CreateRepackOrderRequest(
            Guid.NewGuid(),
            "PORTIONING",
            10m,
            new List<PackageLineRequest> { new(NetWeight: 9m, SellingPrice: 90000m) },
            1m,
            "PEEL_SEED");

        var dto = new RepackOrderDto(
            Guid.NewGuid(), "RP-01", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "PORTIONING", 10m, 1m, "PEEL_SEED", null, 40000m, 40000m, 50000m,
            "COMPLETED", DateTime.UtcNow, new List<PackageDto>());

        _mockService.Setup(s => s.CreateRepackOrderAsync(req, It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);

        var result = await _controller.Create(req, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<RepackOrderDto>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Equal("RP-01", resp.Data!.Code);
    }

    [Fact]
    public async Task UpdatePrice_ReturnsOkWithPackageDto()
    {
        var id = Guid.NewGuid();
        var req = new UpdatePackagePriceRequest(55000);
        var dto = CreatePackageDto(id);
        _mockService.Setup(s => s.UpdatePackagePriceAsync(id, req, It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);

        var result = await _controller.UpdatePrice(id, req, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<PackageDto>>(ok.Value);
        Assert.True(resp.Success);
    }

    [Fact]
    public async Task Quarantine_ReturnsOkWithPackageDto()
    {
        var id = Guid.NewGuid();
        var dto = CreatePackageDto(id, "QUARANTINED");
        _mockService.Setup(s => s.QuarantinePackageAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);

        var result = await _controller.Quarantine(id, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<PackageDto>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Equal("QUARANTINED", resp.Data!.Status);
    }

    [Fact]
    public async Task Release_ReturnsOkWithPackageDto()
    {
        var id = Guid.NewGuid();
        var dto = CreatePackageDto(id, "AVAILABLE");
        _mockService.Setup(s => s.ReleasePackageAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);

        var result = await _controller.Release(id, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<PackageDto>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Equal("AVAILABLE", resp.Data!.Status);
    }

    [Fact]
    public async Task Void_ReturnsOkWithPackageDto()
    {
        var id = Guid.NewGuid();
        var req = new VoidPackageRequest("WRITE_OFF", "Hư hỏng");
        var dto = CreatePackageDto(id, "VOIDED");
        _mockService.Setup(s => s.VoidPackageAsync(id, req, It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);

        var result = await _controller.Void(id, req, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<PackageDto>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Equal("VOIDED", resp.Data!.Status);
    }
}
