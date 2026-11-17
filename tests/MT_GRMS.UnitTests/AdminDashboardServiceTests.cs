using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using MT_GRMS.Application.Admin.Repositories;
using MT_GRMS.Application.Admin.Services;
using MT_GRMS.Domain.Entities.Identity;
using MT_GRMS.Domain.Entities.SaaS;
using Xunit;

namespace MT_GRMS.UnitTests;

public class AdminDashboardServiceTests
{
    private readonly Mock<ITenantRepository> _mockTenantRepo;
    private readonly Mock<ISubscriptionPlanRepository> _mockPlanRepo;
    private readonly AdminDashboardService _service;

    public AdminDashboardServiceTests()
    {
        _mockTenantRepo = new Mock<ITenantRepository>();
        _mockPlanRepo = new Mock<ISubscriptionPlanRepository>();
        _service = new AdminDashboardService(_mockTenantRepo.Object, _mockPlanRepo.Object);
    }

    [Fact]
    public async Task GetKpiMetricsAsync_ShouldReturn4Metrics()
    {
        _mockTenantRepo.Setup(r => r.GetTotalCountAsync(It.IsAny<CancellationToken>())).ReturnsAsync(10);
        _mockTenantRepo.Setup(r => r.GetActiveCountAsync(It.IsAny<CancellationToken>())).ReturnsAsync(8);
        _mockTenantRepo.Setup(r => r.GetExpiringSoonCountAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(2);
        _mockTenantRepo.Setup(r => r.GetGracePeriodCountAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _mockTenantRepo.Setup(r => r.GetLockedCountAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _mockTenantRepo.Setup(r => r.GetTotalBranchesCountAsync(It.IsAny<CancellationToken>())).ReturnsAsync(16);

        var plan = new SubscriptionPlan { Id = "PRO", PriceMonthly = 249_000m };
        var activeTenants = new List<Tenant>
        {
            new() { Id = "t1", Status = "ACTIVE", SubscriptionPlan = plan },
            new() { Id = "t2", Status = "ACTIVE", SubscriptionPlan = plan }
        };
        _mockTenantRepo.Setup(r => r.GetAllActiveWithPlanAsync(It.IsAny<CancellationToken>())).ReturnsAsync(activeTenants);

        var metrics = await _service.GetKpiMetricsAsync();
        Assert.Equal(4, metrics.Count);
        Assert.Equal("mrr", metrics[0].Id);
        Assert.Equal("active_tenants", metrics[1].Id);
        Assert.Equal("total_branches", metrics[2].Id);
        Assert.Equal("urgent_alerts", metrics[3].Id);
    }

    [Fact]
    public async Task GetPlanDistributionAsync_ShouldReturnDistributions()
    {
        var plans = new List<SubscriptionPlan>
        {
            new() { Id = "TRIAL", PlanCode = "TRIAL", PlanName = "Trial", PriceMonthly = 0m, MaxBranches = 1, MaxStaff = 3 },
            new() { Id = "PRO", PlanCode = "PRO", PlanName = "Pro", PriceMonthly = 249_000m, MaxBranches = 5, MaxStaff = 20 }
        };
        var counts = new Dictionary<string, int>
        {
            ["TRIAL"] = 5,
            ["PRO"] = 10
        };

        _mockPlanRepo.Setup(r => r.GetAllAsync(null, null, It.IsAny<CancellationToken>())).ReturnsAsync(plans);
        _mockPlanRepo.Setup(r => r.GetActiveTenantCountsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(counts);

        var dist = await _service.GetPlanDistributionAsync();
        Assert.Equal(2, dist.Count);
        Assert.Equal(5, dist[0].TenantCount);
        Assert.Equal(10, dist[1].TenantCount);
    }

    [Fact]
    public async Task GetUrgentAlertsAsync_ShouldHandleVariousTenantStatuses()
    {
        var user = new User { Id = "u1", FullName = "Chu Cua Hang", Phone = "0901234567" };
        var tu = new TenantUser { UserId = "u1", User = user, Role = "STORE_OWNER", IsActive = true };

        var lockedTenant = new Tenant
        {
            Subdomain = "locked-sub",
            Name = "Locked Shop",
            Status = "LOCKED",
            LockType = "HARD",
            LockReason = "Vi pham chinh sach",
            TenantUsers = new List<TenantUser> { tu }
        };

        var graceTenant = new Tenant
        {
            Subdomain = "grace-sub",
            Name = "Grace Shop",
            Status = "GRACE_PERIOD",
            GracePeriodExpiresAt = DateTime.UtcNow.AddDays(3),
            TenantUsers = new List<TenantUser> { tu }
        };

        var expiringTenant = new Tenant
        {
            Subdomain = "exp-sub",
            Name = "Expiring Shop",
            Status = "ACTIVE",
            SubscriptionExpiresAt = DateTime.UtcNow.AddDays(5),
            TenantUsers = new List<TenantUser> { tu }
        };

        _mockTenantRepo.Setup(r => r.GetUrgentAlertTenantsAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Tenant> { lockedTenant, graceTenant, expiringTenant });

        var alerts = await _service.GetUrgentAlertsAsync();
        Assert.Equal(3, alerts.Count);
        Assert.Equal("error", alerts[0].StatusType);
        Assert.Equal("error", alerts[1].StatusType);
        Assert.Equal("warning", alerts[2].StatusType);
    }

    [Fact]
    public async Task GetMonthlyAnalyticsAsync_ShouldReturnAnalytics()
    {
        var plans = new List<SubscriptionPlan>
        {
            new() { Id = "PRO", PriceMonthly = 249_000m }
        };
        var activeTenants = new List<Tenant>
        {
            new() { Id = "t1", Status = "ACTIVE", SubscriptionPlan = plans[0] }
        };

        _mockPlanRepo.Setup(r => r.GetAllAsync(null, null, It.IsAny<CancellationToken>())).ReturnsAsync(plans);
        _mockTenantRepo.Setup(r => r.GetActiveCountAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _mockTenantRepo.Setup(r => r.GetAllActiveWithPlanAsync(It.IsAny<CancellationToken>())).ReturnsAsync(activeTenants);

        var analytics = await _service.GetMonthlyAnalyticsAsync();
        Assert.NotEmpty(analytics);
    }
}
