using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using MT_GRMS.Api.Controllers;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Promotions;
using MT_GRMS.Application.Promotions.Dtos;
using MT_GRMS.Domain.Entities.Sales;
using MT_GRMS.Domain.Exceptions;
using MT_GRMS.Infrastructure.Persistence;
using Xunit;

namespace MT_GRMS.UnitTests.Controllers;

public class LoyaltyConfigControllerTests
{
    private readonly Mock<ILoyaltyService> _mockLoyalty;
    private readonly Mock<ITenantContext> _mockTenant;
    private const string TenantId = "tenant-loyalty-01";

    public LoyaltyConfigControllerTests()
    {
        _mockLoyalty = new Mock<ILoyaltyService>();
        _mockTenant = new Mock<ITenantContext>();
        _mockTenant.SetupGet(t => t.CurrentTenantId).Returns(TenantId);
    }

    private AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"LoyaltyDb_{Guid.NewGuid()}")
            .Options;

        return new AppDbContext(options, _mockTenant.Object);
    }

    private static ControllerContext CreateContext()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, "Store Owner")
        }, "TestAuth"));

        return new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };
    }

    [Fact]
    public async Task GetConfig_ShouldReturnOk()
    {
        using var context = CreateDbContext();
        var config = new LoyaltyConfigDto(true, 10_000m, 1, 1, 1_000m, 100, false);
        _mockLoyalty.Setup(s => s.GetConfigAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(config);

        var controller = new LoyaltyConfigController(_mockLoyalty.Object, context, _mockTenant.Object)
        {
            ControllerContext = CreateContext()
        };

        var result = await controller.GetConfig(CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result);
        var apiRes = Assert.IsType<ApiResponse<LoyaltyConfigDto>>(ok.Value);
        Assert.True(apiRes.Success);
        Assert.True(apiRes.Data?.IsEnabled);
    }

    [Fact]
    public async Task UpdateConfig_ShouldReturnOk()
    {
        using var context = CreateDbContext();
        var request = new UpdateLoyaltyConfigRequest(true, 20_000m, 2, 2, 2_000m, 200, false);
        var config = new LoyaltyConfigDto(true, 20_000m, 2, 2, 2_000m, 200, false);
        _mockLoyalty.Setup(s => s.UpdateConfigAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(config);

        var controller = new LoyaltyConfigController(_mockLoyalty.Object, context, _mockTenant.Object)
        {
            ControllerContext = CreateContext()
        };

        var result = await controller.UpdateConfig(request, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result);
        var apiRes = Assert.IsType<ApiResponse<LoyaltyConfigDto>>(ok.Value);
        Assert.True(apiRes.Success);
    }

    [Fact]
    public async Task AdjustPoints_ShouldHandleValidation_NotFound_AndSuccess()
    {
        using var context = CreateDbContext();
        var customer = new Customer
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            CustomerCode = "KH-01",
            Name = "Nguyen Van A",
            Phone = "0901234567",
            RewardPoints = 50
        };
        context.Customers.Add(customer);
        await context.SaveChangesAsync();

        var controller = new LoyaltyConfigController(_mockLoyalty.Object, context, _mockTenant.Object)
        {
            ControllerContext = CreateContext()
        };

        // Invalid: negative points
        var reqNeg = new AdjustLoyaltyPointsRequest("0901234567", "ADD", -10, "Thuong");
        await Assert.ThrowsAsync<BusinessException>(() => controller.AdjustPoints(reqNeg, CancellationToken.None));

        // Invalid: empty phone
        var reqEmptyPhone = new AdjustLoyaltyPointsRequest("", "ADD", 10, "Thuong");
        await Assert.ThrowsAsync<BusinessException>(() => controller.AdjustPoints(reqEmptyPhone, CancellationToken.None));

        // Invalid: invalid type
        var reqBadType = new AdjustLoyaltyPointsRequest("0901234567", "MULTIPLY", 10, "Thuong");
        await Assert.ThrowsAsync<BusinessException>(() => controller.AdjustPoints(reqBadType, CancellationToken.None));

        // Invalid: reason too long (> 500 chars)
        var reqLongReason = new AdjustLoyaltyPointsRequest("0901234567", "ADD", 10, new string('A', 505));
        await Assert.ThrowsAsync<BusinessException>(() => controller.AdjustPoints(reqLongReason, CancellationToken.None));

        // Invalid: customer not found
        var reqNotFound = new AdjustLoyaltyPointsRequest("0999999999", "ADD", 10, "Thuong");
        await Assert.ThrowsAsync<NotFoundException>(() => controller.AdjustPoints(reqNotFound, CancellationToken.None));

        // Invalid: insufficient points to subtract
        var reqOverSub = new AdjustLoyaltyPointsRequest("0901234567", "SUB", 100, "Tru diem");
        await Assert.ThrowsAsync<BusinessException>(() => controller.AdjustPoints(reqOverSub, CancellationToken.None));

        // Success: ADD
        var reqAdd = new AdjustLoyaltyPointsRequest("0901234567", "ADD", 20, "Thuong dip Tet");
        var resAdd = await controller.AdjustPoints(reqAdd, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(resAdd);
        var apiRes = Assert.IsType<ApiResponse<LoyaltyLedgerDto>>(ok.Value);
        Assert.True(apiRes.Success);
        Assert.Equal(70, apiRes.Data?.BalanceAfter);

        // Success: SUB
        var reqSub = new AdjustLoyaltyPointsRequest("0901234567", "SUB", 30, "Su dung diem");
        var resSub = await controller.AdjustPoints(reqSub, CancellationToken.None);
        var okSub = Assert.IsType<OkObjectResult>(resSub);
        var apiSub = Assert.IsType<ApiResponse<LoyaltyLedgerDto>>(okSub.Value);
        Assert.True(apiSub.Success);
        Assert.Equal(40, apiSub.Data?.BalanceAfter);
    }

    [Fact]
    public async Task GetLedger_ShouldReturnOk_WithOrdersAndAdjustments()
    {
        using var context = CreateDbContext();
        var customer = new Customer
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            CustomerCode = "KH-01",
            Name = "Nguyen Van A",
            Phone = "0901234567",
            RewardPoints = 100
        };
        context.Customers.Add(customer);

        context.Orders.Add(new Order
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            OrderCode = "HD1001",
            CustomerId = customer.Id,
            Customer = customer,
            FinalAmount = 50_000m,
            PointsRedeemed = 5,
            Status = "Completed",
            CreatedAt = DateTime.UtcNow
        });

        context.LoyaltyPointAdjustments.Add(new LoyaltyPointAdjustment
        {
            Id = Guid.NewGuid(),
            CustomerId = customer.Id,
            Customer = customer,
            TransactionCode = "LP-ADJ-001",
            Type = "MANUAL_ADJUST_ADD",
            PointsChanged = 20,
            BalanceAfter = 100,
            Reason = "Tang diem",
            PerformedBy = "Owner",
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var config = new LoyaltyConfigDto(true, 10_000m, 1, 1, 1_000m, 100, false);
        _mockLoyalty.Setup(s => s.GetConfigAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(config);

        var controller = new LoyaltyConfigController(_mockLoyalty.Object, context, _mockTenant.Object)
        {
            ControllerContext = CreateContext()
        };

        var result = await controller.GetLedger(CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result);
        var apiRes = Assert.IsType<ApiResponse<IReadOnlyList<LoyaltyLedgerDto>>>(ok.Value);
        Assert.True(apiRes.Success);
        Assert.NotEmpty(apiRes.Data!);
    }
}
