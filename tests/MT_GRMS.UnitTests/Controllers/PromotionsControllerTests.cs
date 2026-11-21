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

public class PromotionsControllerTests
{
    private readonly Mock<IPromotionService> _mockService;
    private readonly PromotionsController _controller;

    public PromotionsControllerTests()
    {
        _mockService = new Mock<IPromotionService>();
        _controller = new PromotionsController(_mockService.Object);
    }

    private static PromotionResponse CreateTestPromotion(Guid id, string code = "KM-01")
    {
        return new PromotionResponse(
            Id: id,
            PromotionCode: code,
            PromotionName: "Giam gia khai truong",
            Type: "ORDER_DISCOUNT",
            DiscountValue: 10m,
            MinOrderValue: 100_000m,
            MaxDiscountAmount: 50_000m,
            UsageLimit: 100,
            UsedCount: 10,
            StartDate: DateTime.UtcNow,
            EndDate: DateTime.UtcNow.AddDays(7),
            IsActive: true,
            Description: "Mo ta",
            TargetBranches: "ALL",
            BudgetLimit: null,
            TotalDiscountGiven: 100_000m,
            CreatedAt: DateTime.UtcNow,
            Conditions: new List<PromotionConditionDto>());
    }

    [Fact]
    public async Task GetPromotions_ShouldReturnOk_WithPagedResult()
    {
        var paged = new PagedResult<PromotionResponse>
        {
            Items = new List<PromotionResponse> { CreateTestPromotion(Guid.NewGuid()) },
            Page = 1,
            PageSize = 20,
            TotalItems = 1
        };

        _mockService
            .Setup(s => s.GetPagedPromotionsAsync(It.IsAny<PromotionFilterQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(paged);

        var result = await _controller.GetPromotions(new PromotionFilterQuery(), CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var apiRes = Assert.IsType<ApiResponse<PagedResult<PromotionResponse>>>(okResult.Value);
        Assert.True(apiRes.Success);
        Assert.Single(apiRes.Data!.Items);
    }

    [Fact]
    public async Task GetById_ShouldReturnOk()
    {
        var id = Guid.NewGuid();
        var promo = CreateTestPromotion(id);
        _mockService
            .Setup(s => s.GetPromotionByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(promo);

        var result = await _controller.GetById(id, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var apiRes = Assert.IsType<ApiResponse<PromotionResponse>>(okResult.Value);
        Assert.True(apiRes.Success);
        Assert.Equal(id, apiRes.Data?.Id);
    }

    [Fact]
    public async Task Create_ShouldReturnCreatedAtAction()
    {
        var id = Guid.NewGuid();
        var promo = CreateTestPromotion(id, "KM-NEW");
        var request = new PromotionCreateRequest(
            "KM-NEW", "Giam gia", "ORDER_DISCOUNT", 10m, 100_000m, 50_000m,
            100, DateTime.UtcNow, DateTime.UtcNow.AddDays(10), true, "Mo ta");

        _mockService
            .Setup(s => s.CreatePromotionAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(promo);

        var result = await _controller.Create(request, CancellationToken.None);

        var createdResult = Assert.IsType<CreatedAtActionResult>(result);
        var apiRes = Assert.IsType<ApiResponse<PromotionResponse>>(createdResult.Value);
        Assert.True(apiRes.Success);
        Assert.Equal(id, apiRes.Data?.Id);
    }

    [Fact]
    public async Task Update_ShouldReturnOk()
    {
        var id = Guid.NewGuid();
        var promo = CreateTestPromotion(id);
        var request = new PromotionUpdateRequest(
            "Giam gia updated", "ORDER_DISCOUNT", 10m, 100_000m, 50_000m,
            100, DateTime.UtcNow, DateTime.UtcNow.AddDays(10), true, "Mo ta");

        _mockService
            .Setup(s => s.UpdatePromotionAsync(id, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(promo);

        var result = await _controller.Update(id, request, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var apiRes = Assert.IsType<ApiResponse<PromotionResponse>>(okResult.Value);
        Assert.True(apiRes.Success);
        Assert.Equal(id, apiRes.Data?.Id);
    }

    [Fact]
    public async Task ToggleStatus_ShouldReturnOk()
    {
        var id = Guid.NewGuid();
        _mockService
            .Setup(s => s.TogglePromotionStatusAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _controller.ToggleStatus(id, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var apiRes = Assert.IsType<ApiResponse<bool>>(okResult.Value);
        Assert.True(apiRes.Success);
        Assert.True(apiRes.Data);
    }

    [Fact]
    public async Task Delete_ShouldReturnOk()
    {
        var id = Guid.NewGuid();
        _mockService
            .Setup(s => s.DeletePromotionAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _controller.Delete(id, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var apiRes = Assert.IsType<ApiResponse<bool>>(okResult.Value);
        Assert.True(apiRes.Success);
        Assert.True(apiRes.Data);
    }

    [Fact]
    public async Task EvaluateCart_ShouldReturnOk()
    {
        var request = new EvaluateCartRequest(new List<CartItemDto>(), 100_000m);
        var evalResponse = new EvaluateCartResponse(
            SubTotal: 100_000m,
            TotalDiscountAmount: 10_000m,
            FinalAmount: 90_000m,
            AppliedPromotions: new List<string> { "KM-01" },
            GiftItems: new List<CartGiftItemDto>());

        _mockService
            .Setup(s => s.EvaluateCartAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(evalResponse);

        var result = await _controller.EvaluateCart(request, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var apiRes = Assert.IsType<ApiResponse<EvaluateCartResponse>>(okResult.Value);
        Assert.True(apiRes.Success);
        Assert.Equal(90_000m, apiRes.Data?.FinalAmount);
    }

    [Fact]
    public async Task SeedSampleData_ShouldReturnOk()
    {
        _mockService
            .Setup(s => s.SeedSampleDataAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(5);

        var result = await _controller.SeedSampleData(CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var apiRes = Assert.IsType<ApiResponse<int>>(okResult.Value);
        Assert.True(apiRes.Success);
        Assert.Equal(5, apiRes.Data);
    }
}
