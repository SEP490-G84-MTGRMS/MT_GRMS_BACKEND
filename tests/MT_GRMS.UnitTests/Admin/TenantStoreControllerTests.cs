using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MT_GRMS.Api.Controllers;
using MT_GRMS.Application.Admin.Dtos;
using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Infrastructure.Persistence;
using Xunit;

namespace MT_GRMS.UnitTests.Admin;

public class TenantStoreControllerTests
{
    private readonly AppDbContext _dbContext;
    private readonly MockTenantService _tenantService;
    private readonly MockPlanService _planService;
    private readonly TenantStoreController _controller;

    public TenantStoreControllerTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"MT_GRMS_TenantStoreTest_{Guid.NewGuid()}")
            .Options;

        _dbContext = new AppDbContext(options);

        // Seed demo tenant
        _dbContext.Tenants.Add(new Tenant
        {
            Id = "tenant-01",
            Name = "Hà Nội Mart",
            Subdomain = "hanoi-mart",
            Code = "HN-MART",
            Status = "ACTIVE",
            CurrentPlan = "PRO",
            WalletBalance = 20000000m
        });
        _dbContext.SaveChanges();

        _tenantService = new MockTenantService();
        _planService = new MockPlanService();

        _planService.Plans.Add(new PlanSummaryResponse(
            Id: "plan-pro",
            Code: "PRO",
            Name: "Gói Chuyên Nghiệp",
            Description: "Dành cho chuỗi cửa hàng",
            MonthlyPrice: 330000m,
            YearlyPrice: 3300000m,
            Status: "ACTIVE",
            ActiveTenantCount: 5,
            Quotas: new PlanQuotaDto(5, 20, 10, "Daily"),
            Features: new PlanFeatureDto(),
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: null
        ));

        _controller = new TenantStoreController(_tenantService, _planService, _dbContext);

        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, "StoreOwner"),
            new Claim("tenant_id", "tenant-01"),
            new Claim(ClaimTypes.Role, "STORE_OWNER")
        }, "mock_auth"));

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };
    }

    [Fact]
    public async Task GetStoreInfo_WithTenantIdClaim_Returns200Ok_WithDetail()
    {
        var result = await _controller.GetStoreInfo(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<TenantDetailResponse>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Equal("hanoi-mart", resp.Data!.Subdomain);
        Assert.Equal("HN-MART", resp.Data!.Code);
        Assert.Equal("Gói Pro", resp.Data!.PlanName);
    }

    [Fact]
    public async Task UpdateStoreInfo_ValidRequest_Returns200Ok_WithUpdated()
    {
        var req = new UpdateTenantRequest("Hà Nội Mart Cập Nhật", "010999888", "19001122", "456 Cầu Giấy", "Hà Nội");
        var result = await _controller.UpdateStoreInfo(req, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<TenantDetailResponse>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Equal("Hà Nội Mart Cập Nhật", resp.Data!.Name);
    }

    [Fact]
    public async Task GetPricingPlans_Returns200Ok_WithActivePlans()
    {
        var result = await _controller.GetPricingPlans(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<IReadOnlyList<PlanSummaryResponse>>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Single(resp.Data!);
        Assert.Equal("PRO", resp.Data![0].Code);
        Assert.Equal(330000m, resp.Data![0].MonthlyPrice);
    }

    [Fact]
    public async Task PurchaseSubscription_ValidRequest_ExtendsSubscriptionAndReturns200Ok()
    {
        var tenant = await _dbContext.Tenants.FindAsync("tenant-01");
        if (tenant != null)
        {
            tenant.WalletBalance = 10000000m;
        }

        _dbContext.SubscriptionPlans.Add(new SubscriptionPlan
        {
            Id = "plan-pro",
            PlanCode = "PRO",
            PlanName = "Gói Chuyên Nghiệp",
            PriceMonthly = 330000m,
            PriceYearly = 3300000m,
            IsActive = true
        });
        await _dbContext.SaveChangesAsync();

        var req = new PurchaseSubscriptionRequest(
            PlanCode: "PRO",
            Months: 12,
            ExtraBranches: 1,
            ExtraWarehouses: 0,
            PaymentMethod: "Wallet"
        );

        var result = await _controller.PurchaseSubscription(req, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<PurchaseSubscriptionResponse>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Equal("PRO", resp.Data!.PlanCode);
        Assert.True(resp.Data.DaysRemaining > 300);
        Assert.True(resp.Data.TotalAmount > 0);

        var updatedTenant = await _dbContext.Tenants.FindAsync("tenant-01");
        Assert.NotNull(updatedTenant);
        Assert.Equal("PRO", updatedTenant!.CurrentPlan);
        Assert.NotNull(updatedTenant.SubscriptionExpiresAt);
    }
}
