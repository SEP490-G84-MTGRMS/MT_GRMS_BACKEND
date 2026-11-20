using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using MT_GRMS.Api.Controllers;
using MT_GRMS.Application.Catalog;
using MT_GRMS.Application.Catalog.Dtos;
using MT_GRMS.Application.Common;
using Xunit;

namespace MT_GRMS.UnitTests.Controllers;

public class ProductUnitsControllerTests
{
    private readonly Mock<IProductUnitService> _mockService;
    private readonly Mock<ILogger<ProductUnitsController>> _mockLogger;
    private readonly ProductUnitsController _controller;

    public ProductUnitsControllerTests()
    {
        _mockService = new Mock<IProductUnitService>();
        _mockLogger = new Mock<ILogger<ProductUnitsController>>();
        _controller = new ProductUnitsController(_mockService.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task GetUnitsByProductId_ShouldReturnOk_WhenFound()
    {
        var prodId = Guid.NewGuid();
        var list = new List<ProductUnitResponse>
        {
            new() { Id = Guid.NewGuid(), ProductId = prodId, UnitName = "Thùng", ConversionRate = 24, Price = 240_000m, IsBaseUnit = false, IsPurchaseUnit = true }
        };

        _mockService
            .Setup(s => s.GetUnitsByProductIdAsync(prodId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(list);

        var result = await _controller.GetUnitsByProductId(prodId, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var apiResponse = Assert.IsType<ApiResponse<IReadOnlyList<ProductUnitResponse>>>(okResult.Value);
        Assert.True(apiResponse.Success);
        Assert.Single(apiResponse.Data!);
    }

    [Fact]
    public async Task GetUnitsByProductId_ShouldReturnNotFound_WhenKeyNotFound()
    {
        var prodId = Guid.NewGuid();
        _mockService
            .Setup(s => s.GetUnitsByProductIdAsync(prodId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException("Product not found"));

        var result = await _controller.GetUnitsByProductId(prodId, CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task GetById_ShouldReturnNotFound_WhenNull()
    {
        var id = Guid.NewGuid();
        _mockService
            .Setup(s => s.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProductUnitResponse?)null);

        var result = await _controller.GetById(id, CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task GetById_ShouldReturnOk_WhenFound()
    {
        var id = Guid.NewGuid();
        var response = new ProductUnitResponse { Id = id, ProductId = Guid.NewGuid(), UnitName = "Hộp", ConversionRate = 10 };

        _mockService
            .Setup(s => s.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var result = await _controller.GetById(id, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var apiResponse = Assert.IsType<ApiResponse<ProductUnitResponse>>(okResult.Value);
        Assert.True(apiResponse.Success);
        Assert.Equal(id, apiResponse.Data?.Id);
    }

    [Fact]
    public async Task Create_ShouldReturnCreatedAtAction_WhenSuccessful()
    {
        var prodId = Guid.NewGuid();
        var request = new ProductUnitCreateRequest { UnitName = "Thùng", ConversionRate = 24, Price = 200_000m, IsPurchaseUnit = true };
        var response = new ProductUnitResponse { Id = Guid.NewGuid(), ProductId = prodId, UnitName = "Thùng", ConversionRate = 24, Price = 200_000m, IsPurchaseUnit = true };

        _mockService
            .Setup(s => s.CreateAsync(prodId, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var result = await _controller.Create(prodId, request, CancellationToken.None);

        var createdResult = Assert.IsType<CreatedAtActionResult>(result);
        var apiResponse = Assert.IsType<ApiResponse<ProductUnitResponse>>(createdResult.Value);
        Assert.True(apiResponse.Success);
        Assert.Equal(response.Id, apiResponse.Data?.Id);
    }

    [Fact]
    public async Task Create_ShouldHandleExceptionsProperly()
    {
        var prodId = Guid.NewGuid();
        var request = new ProductUnitCreateRequest { UnitName = "Thùng", ConversionRate = 24 };

        // KeyNotFound
        _mockService.Setup(s => s.CreateAsync(prodId, request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException());
        var res1 = await _controller.Create(prodId, request, CancellationToken.None);
        Assert.IsType<NotFoundObjectResult>(res1);

        // ArgumentException
        _mockService.Setup(s => s.CreateAsync(prodId, request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException());
        var res2 = await _controller.Create(prodId, request, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(res2);

        // InvalidOperationException
        _mockService.Setup(s => s.CreateAsync(prodId, request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException());
        var res3 = await _controller.Create(prodId, request, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(res3);
    }

    [Fact]
    public async Task Update_ShouldReturnOk_AndHandleExceptions()
    {
        var id = Guid.NewGuid();
        var request = new ProductUnitUpdateRequest { UnitName = "Thùng", ConversionRate = 24, Price = 250_000m, IsPurchaseUnit = true };
        var response = new ProductUnitResponse { Id = id, ProductId = Guid.NewGuid(), UnitName = "Thùng", ConversionRate = 24, Price = 250_000m, IsPurchaseUnit = true };

        _mockService.Setup(s => s.UpdateAsync(id, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var res = await _controller.Update(id, request, CancellationToken.None);
        var okResult = Assert.IsType<OkObjectResult>(res);
        var apiRes = Assert.IsType<ApiResponse<ProductUnitResponse>>(okResult.Value);
        Assert.True(apiRes.Success);

        // KeyNotFound
        _mockService.Setup(s => s.UpdateAsync(id, request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException());
        var res1 = await _controller.Update(id, request, CancellationToken.None);
        Assert.IsType<NotFoundObjectResult>(res1);

        // ArgumentException
        _mockService.Setup(s => s.UpdateAsync(id, request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException());
        var res2 = await _controller.Update(id, request, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(res2);

        // InvalidOperationException
        _mockService.Setup(s => s.UpdateAsync(id, request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException());
        var res3 = await _controller.Update(id, request, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(res3);
    }

    [Fact]
    public async Task Delete_ShouldReturnOk_AndHandleExceptions()
    {
        var id = Guid.NewGuid();
        _mockService.Setup(s => s.DeleteAsync(id, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var res = await _controller.Delete(id, CancellationToken.None);
        var okResult = Assert.IsType<OkObjectResult>(res);
        var apiRes = Assert.IsType<ApiResponse<object>>(okResult.Value);
        Assert.True(apiRes.Success);

        // KeyNotFound
        _mockService.Setup(s => s.DeleteAsync(id, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException());
        var res1 = await _controller.Delete(id, CancellationToken.None);
        Assert.IsType<NotFoundObjectResult>(res1);

        // InvalidOperationException
        _mockService.Setup(s => s.DeleteAsync(id, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException());
        var res2 = await _controller.Delete(id, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(res2);
    }
}
