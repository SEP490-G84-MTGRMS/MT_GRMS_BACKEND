using System.Reflection;
using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MT_GRMS.Api.Controllers;
using MT_GRMS.Application.Admin.Dtos;
using MT_GRMS.Application.Admin.Services;
using MT_GRMS.Application.Admin.Validators;
using MT_GRMS.Application.Common;
using Xunit;

namespace MT_GRMS.IntegrationTests.Admin;

public class AdminEndpointAuthorizationTests
{
    [Theory]
    [InlineData(typeof(AdminPlansController))]
    [InlineData(typeof(AdminTenantsController))]
    [InlineData(typeof(AdminDashboardController))]
    public void REQ_ADMIN_BE04_AdminControllers_MustHaveAuthorizeRoleAdminAttribute(Type controllerType)
    {
        // Act
        var authAttr = controllerType.GetCustomAttribute<AuthorizeAttribute>();

        // Assert
        Assert.NotNull(authAttr);
        Assert.Equal("ADMIN", authAttr.Roles);
    }

    [Fact]
    public async Task REQ_ADMIN_BE01_AdminPlansController_GetPlans_ReturnsOk()
    {
        // Arrange
        var fakePlanService = new FakePlanService();
        var createValidator = new CreatePlanRequestValidator();
        var updateValidator = new UpdatePlanRequestValidator();
        var controller = new AdminPlansController(fakePlanService, createValidator, updateValidator);

        // Act
        var result = await controller.GetPlans(new PlanFilterRequest(), CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<ApiResponse<IReadOnlyList<PlanSummaryResponse>>>(okResult.Value);
        Assert.True(response.Success);
        Assert.NotEmpty(response.Data!);
    }

    [Fact]
    public async Task REQ_ADMIN_BE01_AdminPlansController_DeleteInUsePlan_ReturnsBadRequest()
    {
        // Arrange
        var fakePlanService = new FakePlanService();
        var createValidator = new CreatePlanRequestValidator();
        var updateValidator = new UpdatePlanRequestValidator();
        var controller = new AdminPlansController(fakePlanService, createValidator, updateValidator);

        // Act
        var result = await controller.DeletePlan("in-use-plan", CancellationToken.None);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        var response = Assert.IsType<ApiResponse<bool>>(badRequestResult.Value);
        Assert.False(response.Success);
        Assert.Equal("PLAN_IN_USE", response.ErrorCode);
    }

    [Fact]
    public async Task REQ_ADMIN_BE02_AdminTenantsController_LockTenant_Success()
    {
        // Arrange
        var fakeTenantService = new FakeTenantService();
        var lockValidator = new LockTenantRequestValidator();
        var graceValidator = new ExtendGraceRequestValidator();
        var controller = new AdminTenantsController(fakeTenantService, lockValidator, graceValidator);

        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, "super_admin"),
            new Claim(ClaimTypes.Role, "ADMIN")
        }, "mock"));

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };

        // Act
        var result = await controller.LockTenant("tenant-01", new LockTenantRequest("SOFT", "Quá hạn thanh toán"), CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<ApiResponse<bool>>(okResult.Value);
        Assert.True(response.Success);
    }

    [Fact]
    public async Task REQ_ADMIN_BE03_AdminDashboardController_GetKpis_ReturnsAllFourMetrics()
    {
        // Arrange
        var fakeDashboardService = new FakeDashboardService();
        var controller = new AdminDashboardController(fakeDashboardService);

        // Act
        var result = await controller.GetKpis(CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<ApiResponse<IReadOnlyList<KpiMetricResponse>>>(okResult.Value);
        Assert.True(response.Success);
        Assert.Equal(4, response.Data!.Count);
        Assert.Contains(response.Data, k => k.Id == "mrr");
        Assert.Contains(response.Data, k => k.Id == "active_tenants");
        Assert.Contains(response.Data, k => k.Id == "total_branches");
        Assert.Contains(response.Data, k => k.Id == "urgent_alerts");
    }
}

public class FakePlanService : ISubscriptionPlanService
{
    public Task<IReadOnlyList<PlanSummaryResponse>> GetPlansAsync(PlanFilterRequest filter, CancellationToken cancellationToken = default)
    {
        var sample = new PlanSummaryResponse(
            Id: "plan-1",
            Code: "STARTER",
            Name: "Gói Khởi Đầu",
            Description: "Mô tả",
            MonthlyPrice: 500000,
            YearlyPrice: 5000000,
            Status: "ACTIVE",
            ActiveTenantCount: 2,
            Quotas: new PlanQuotaDto(),
            Features: new PlanFeatureDto(),
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: null
        );
        return Task.FromResult<IReadOnlyList<PlanSummaryResponse>>(new List<PlanSummaryResponse> { sample });
    }

    public Task<PlanKpiBannerResponse> GetKpisAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(new PlanKpiBannerResponse(3, 3, 10, 50000000));

    public Task<PlanDetailResponse> GetByIdAsync(string id, CancellationToken cancellationToken = default)
        => Task.FromResult(new PlanDetailResponse(id, "STARTER", "Tên", "Mô tả", 500000, 5000000, "ACTIVE", 2, new PlanQuotaDto(), new PlanFeatureDto(), new List<PlanTenantSummaryDto>(), DateTime.UtcNow, null));

    public Task<PlanDetailResponse> CreatePlanAsync(CreatePlanRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(new PlanDetailResponse("new-id", request.Code, request.Name, request.Description ?? "", request.MonthlyPrice, request.YearlyPrice, "ACTIVE", 0, new PlanQuotaDto(), new PlanFeatureDto(), new List<PlanTenantSummaryDto>(), DateTime.UtcNow, null));

    public Task<PlanDetailResponse> UpdatePlanAsync(string id, UpdatePlanRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(new PlanDetailResponse(id, "STARTER", request.Name, request.Description ?? "", request.MonthlyPrice, request.YearlyPrice, "ACTIVE", 0, new PlanQuotaDto(), new PlanFeatureDto(), new List<PlanTenantSummaryDto>(), DateTime.UtcNow, null));

    public Task<bool> ToggleStatusAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult(true);

    public Task DeletePlanAsync(string id, CancellationToken cancellationToken = default)
    {
        if (id == "in-use-plan")
        {
            throw new InvalidOperationException("Không thể xóa gói dịch vụ đang có 5 cửa hàng sử dụng.");
        }
        return Task.CompletedTask;
    }
}

public class FakeTenantService : ITenantService
{
    public Task<PagedResult<TenantSummaryResponse>> GetTenantsAsync(TenantFilterRequest filter, CancellationToken cancellationToken = default)
        => Task.FromResult(new PagedResult<TenantSummaryResponse>());

    public Task<TenantDetailResponse> GetByIdAsync(string id, CancellationToken cancellationToken = default)
        => Task.FromResult(new TenantDetailResponse(id, "sub", "Tên", "CODE", null, null, null, null, "ACTIVE", "PRO", "Gói Pro", null, null, null, null, null, null, 1, 5, 3, 10, new List<TenantDetailBranchSummaryDto>(), DateTime.UtcNow, null));

    public Task<TenantDetailResponse> UpdateTenantAsync(string id, UpdateTenantRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(new TenantDetailResponse(id, "sub", request.Name, "CODE", request.TaxCode, request.Hotline, request.Address, request.City, "ACTIVE", "PRO", "Gói Pro", null, null, null, null, null, null, 1, 5, 3, 10, new List<TenantDetailBranchSummaryDto>(), DateTime.UtcNow, null));

    public Task LockTenantAsync(string id, LockTenantRequest request, string lockedBy, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task UnlockTenantAsync(string id, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task ExtendGracePeriodAsync(string id, ExtendGraceRequest request, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public class FakeDashboardService : IAdminDashboardService
{
    public Task<IReadOnlyList<KpiMetricResponse>> GetKpiMetricsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<KpiMetricResponse>>(new List<KpiMetricResponse>
        {
            new("mrr", "MRR", "10,000,000", "đ", "", "", "", "", "", "", ""),
            new("active_tenants", "Active Tenants", "5", "", "", "", "", "", "", "", ""),
            new("total_branches", "Total Branches", "12", "", "", "", "", "", "", "", ""),
            new("urgent_alerts", "Urgent Alerts", "1", "", "", "", "", "", "", "", "")
        });
    }

    public Task<IReadOnlyList<PlanDistributionResponse>> GetPlanDistributionAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<PlanDistributionResponse>>(new List<PlanDistributionResponse>());

    public Task<IReadOnlyList<UrgentAlertResponse>> GetUrgentAlertsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<UrgentAlertResponse>>(new List<UrgentAlertResponse>());

    public Task<IReadOnlyList<MonthlyAnalyticsResponse>> GetMonthlyAnalyticsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<MonthlyAnalyticsResponse>>(new List<MonthlyAnalyticsResponse>());
}

