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
using MT_GRMS.Application.Reports;
using MT_GRMS.Application.Reports.Dtos;
using Xunit;

namespace MT_GRMS.UnitTests.Controllers;

public class StoreOwnerDashboardControllerTests
{
    private readonly Mock<IStoreOwnerDashboardService> _mockDashboard;
    private readonly Mock<ITenantContext> _mockTenant;
    private readonly StoreOwnerDashboardController _controller;

    public StoreOwnerDashboardControllerTests()
    {
        _mockDashboard = new Mock<IStoreOwnerDashboardService>();
        _mockTenant = new Mock<ITenantContext>();
        _mockTenant.SetupGet(t => t.CurrentTenantId).Returns("tenant-01");

        _controller = new StoreOwnerDashboardController(_mockDashboard.Object, _mockTenant.Object);

        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("tenant_id", "tenant-01")
        }, "TestAuth"));

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };
    }

    private static StoreOwnerDashboardResponse CreateTestResponse()
    {
        return new StoreOwnerDashboardResponse(
            Scope: new DashboardScopeDto(DateTime.UtcNow.Date, DateTime.UtcNow, DateTime.UtcNow, null, "Toan chuoi"),
            Branches: new List<DashboardBranchDto>(),
            Sales: new DashboardSalesDto(10_000_000m, 50, 200_000m, null, new List<DashboardRevenueTrendPointDto>(), new List<TopProductPnLDto>()),
            Finance: new DashboardFinanceDto(4_000_000m, 1_000_000m, 3_000_000m, "ACTUAL", null),
            Procurement: new DashboardProcurementDto(1, 2, 5_000_000m),
            Inventory: new DashboardInventoryDto(0, 0, 0, new List<DashboardStockItemDto>(), new List<DashboardExpiryItemDto>()),
            Employees: new DashboardEmployeesDto(5, 5));
    }

    [Fact]
    public async Task Get_ShouldReturnBadRequest_WhenInvalidPeriod()
    {
        var result = await _controller.Get("invalid_period", null, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Get_ShouldReturnForbid_WhenTenantMismatch()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("tenant_id", "different-tenant")
        }, "TestAuth"));

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };

        var result = await _controller.Get("today", null, CancellationToken.None);
        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task Get_ShouldReturnForbid_WhenResultNull()
    {
        _mockDashboard.Setup(d => d.GetAsync("today", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((StoreOwnerDashboardResponse?)null);

        var result = await _controller.Get("today", null, CancellationToken.None);
        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task Get_ShouldReturnOk_WhenSuccess()
    {
        var response = CreateTestResponse();
        _mockDashboard.Setup(d => d.GetAsync("today", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var result = await _controller.Get("today", null, CancellationToken.None);
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var apiRes = Assert.IsType<ApiResponse<StoreOwnerDashboardResponse>>(okResult.Value);
        Assert.True(apiRes.Success);
        Assert.Equal(10_000_000m, apiRes.Data?.Sales.NetRevenue);
    }
}
