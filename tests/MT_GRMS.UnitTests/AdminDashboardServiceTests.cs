using System;
using System.Collections.Generic;
using System.Globalization;
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
    public async Task GetKpiMetricsAsync_ShouldReturnMrrTenantCountAndSuccessfulPurchases()
    {
        _mockTenantRepo.Setup(r => r.GetTotalCountAsync(It.IsAny<CancellationToken>())).ReturnsAsync(10);
        _mockTenantRepo.Setup(r => r.GetSuccessfulUpgradeCountAsync(It.IsAny<CancellationToken>())).ReturnsAsync(4);

        var plan = new SubscriptionPlan { Id = "PRO", PriceMonthly = 249_000m };
        var activeTenants = new List<Tenant>
        {
            new() { Id = "t1", Status = "ACTIVE", SubscriptionPlan = plan },
            new() { Id = "t2", Status = "ACTIVE", SubscriptionPlan = plan, SubscriptionExpiresAt = DateTime.UtcNow.AddDays(1) },
            new() { Id = "t3", Status = "ACTIVE", SubscriptionPlan = plan, SubscriptionExpiresAt = DateTime.UtcNow.AddDays(-1) },
            new() { Id = "t4", Status = "ACTIVE", SubscriptionPlan = plan, LockType = "HARD" }
        };
        _mockTenantRepo.Setup(r => r.GetAllActiveWithPlanAsync(It.IsAny<CancellationToken>())).ReturnsAsync(activeTenants);

        var metrics = await _service.GetKpiMetricsAsync();
        Assert.Collection(metrics,
            metric =>
            {
                Assert.Equal("mrr", metric.Id);
                Assert.Equal(498_000m.ToString("N0", CultureInfo.GetCultureInfo("vi-VN")), metric.Value);
            },
            metric =>
            {
                Assert.Equal("total_tenants", metric.Id);
                Assert.Equal("10", metric.Value);
            },
            metric =>
            {
                Assert.Equal("successful_upgrades", metric.Id);
                Assert.Equal("4", metric.Value);
            });
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
        Assert.Equal(lockedTenant.Id, alerts[0].Id);
        Assert.Equal("locked", alerts[0].StatusType);
        Assert.Contains("Vi pham chinh sach", alerts[0].ExpiryNotice);
        Assert.Equal(graceTenant.Id, alerts[1].Id);
        Assert.Equal("grace", alerts[1].StatusType);
        Assert.Equal(expiringTenant.Id, alerts[2].Id);
        Assert.Equal("expiring", alerts[2].StatusType);
    }

    [Fact]
    public async Task GetMonthlyAnalyticsAsync_DoesNotEstimatePastMonthsFromCurrentSubscriptions()
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
        Assert.Empty(analytics);
    }
}
