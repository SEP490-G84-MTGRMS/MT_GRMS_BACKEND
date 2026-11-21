using Microsoft.AspNetCore.Mvc;
using MT_GRMS.Api.Controllers;
using MT_GRMS.Application.Admin.Dtos;
using MT_GRMS.Application.Admin.Repositories;
using MT_GRMS.Application.Admin.Services;
using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Entities.SaaS;
using Moq;
using Xunit;

namespace MT_GRMS.UnitTests.Admin;

public class MockDashboardService : IAdminDashboardService
{
    public Task<IReadOnlyList<KpiMetricResponse>> GetKpiMetricsAsync(CancellationToken cancellationToken = default)
    {
        var list = new List<KpiMetricResponse>
        {
            new("mrr", "Doanh thu tháng (MRR)", "45,000,000", "đ/tháng", "ARR: 540,000,000 đ", null, "neutral", "DollarOutlined", "#EFF6FF", "#0066FF", "#0066FF"),
            new("total_tenants", "Tổng số tenant", "20", "tenant", "Tất cả tenant đã đăng ký trên hệ thống", null, "neutral", "ShopOutlined", "#F1F5F9", "#475569", "#0F172A"),
            new("successful_upgrades", "Nâng cấp gói thành công", "3", "giao dịch", "Chỉ tính giao dịch chuyển lên gói cao hơn", null, "neutral", "ApartmentOutlined", "#F1F5F9", "#475569", "#0F172A")
        };
        return Task.FromResult<IReadOnlyList<KpiMetricResponse>>(list);
    }

    public Task<IReadOnlyList<PlanDistributionResponse>> GetPlanDistributionAsync(CancellationToken cancellationToken = default)
    {
        var list = new List<PlanDistributionResponse>
        {
            new("plan-1", "Gói Cơ Bản", "1 CN, 3 NV", 8, 44.4, "3,920,000 đ/tháng", "#1890ff"),
            new("plan-2", "Gói Nâng Cao", "5 CN, 15 NV", 7, 38.9, "6,930,000 đ/tháng", "#52c41a"),
            new("plan-3", "Gói Doanh Nghiệp", "20 CN, 50 NV", 3, 16.7, "5,970,000 đ/tháng", "#722ed1")
        };
        return Task.FromResult<IReadOnlyList<PlanDistributionResponse>>(list);
    }

    public Task<IReadOnlyList<UrgentAlertResponse>> GetUrgentAlertsAsync(CancellationToken cancellationToken = default)
    {
        var list = new List<UrgentAlertResponse>
        {
            new("bachhoa-hn", "Bách Hóa Hà Nội", "Anh Tuấn", "0912345678", "Gói Cơ Bản", "Còn 2 ngày", "Sắp hết hạn", "warning"),
            new("sieuthi-sg", "Siêu Thị Sài Gòn", "Chị Mai", "0987654321", "Gói Pro", "Còn 5 ngày ân hạn", "Đang ân hạn", "error")
        };
        return Task.FromResult<IReadOnlyList<UrgentAlertResponse>>(list);
    }

    public Task<IReadOnlyList<MonthlyAnalyticsResponse>> GetMonthlyAnalyticsAsync(CancellationToken cancellationToken = default)
    {
        var list = new List<MonthlyAnalyticsResponse>
        {
            new(
                Month: "2026-10",
                ShortMonth: "T10",
                Mrr: 45000000,
                MrrFormatted: "45,000,000 đ",
                Arr: 540000000,
                ArrFormatted: "540,000,000 đ",
                Arpu: 2500000,
                ArpuFormatted: "2,500,000 đ",
                RevenueBreakdown: new RevenueBreakdownDto(11250000, 20250000, 13500000),
                NewTenants: 3,
                ActiveTenants: 18,
                TotalOrders: 8100,
                OrdersFormatted: "8,100",
                GrowthRate: "8.5%",
                ChurnRate: "1.1%"
            )
        };
        return Task.FromResult<IReadOnlyList<MonthlyAnalyticsResponse>>(list);
    }
}

public class AdminDashboardApiControllerTests
{
    private readonly MockDashboardService _service;
    private readonly AdminDashboardController _controller;

    public AdminDashboardApiControllerTests()
    {
        _service = new MockDashboardService();
        _controller = new AdminDashboardController(_service);
    }

    [Fact]
    public async Task GetKpis_Returns200Ok_WithThreeAdminCards()
    {
        var result = await _controller.GetKpis(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<IReadOnlyList<KpiMetricResponse>>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Equal(3, resp.Data!.Count);
        Assert.Equal("mrr", resp.Data[0].Id);
        Assert.Equal("total_tenants", resp.Data[1].Id);
        Assert.Equal("successful_upgrades", resp.Data[2].Id);
    }

    [Fact]
    public async Task GetPlanDistribution_Returns200Ok_WithItems()
    {
        var result = await _controller.GetPlanDistribution(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<IReadOnlyList<PlanDistributionResponse>>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Equal(3, resp.Data!.Count);
        Assert.Equal("Gói Cơ Bản", resp.Data[0].PlanName);
        Assert.Equal(44.4, resp.Data[0].Percentage);
    }

    [Fact]
    public async Task GetUrgentAlerts_Returns200Ok_WithAlerts()
    {
        var result = await _controller.GetUrgentAlerts(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<IReadOnlyList<UrgentAlertResponse>>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Equal(2, resp.Data!.Count);
        Assert.Equal("bachhoa-hn", resp.Data[0].Subdomain);
        Assert.Equal("warning", resp.Data[0].StatusType);
    }

    [Fact]
    public async Task GetMonthlyAnalytics_Returns200Ok_WithAnalytics()
    {
        var result = await _controller.GetMonthlyAnalytics(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<IReadOnlyList<MonthlyAnalyticsResponse>>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Single(resp.Data!);
        Assert.Equal("2026-10", resp.Data[0].Month);
        Assert.Equal(45000000, resp.Data[0].Mrr);
    }
}

public class AdminDashboardServiceTests
{
    [Fact]
    public async Task GetKpiMetrics_UsesActiveUnlockedPlansAndSuccessfulUpgrades()
    {
        var tenantRepository = new Mock<ITenantRepository>();
        var planRepository = new Mock<ISubscriptionPlanRepository>();
        tenantRepository.Setup(repository => repository.GetTotalCountAsync(It.IsAny<CancellationToken>())).ReturnsAsync(4);
        tenantRepository.Setup(repository => repository.GetSuccessfulUpgradeCountAsync(It.IsAny<CancellationToken>())).ReturnsAsync(2);
        tenantRepository.Setup(repository => repository.GetAllActiveWithPlanAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Tenant>
        {
            new() { Status = "ACTIVE", SubscriptionPlan = new SubscriptionPlan { PriceMonthly = 99000m } },
            new() { Status = "ACTIVE", SubscriptionPlan = new SubscriptionPlan { PriceMonthly = 249000m } },
            new() { Status = "ACTIVE", LockType = "SOFT", SubscriptionPlan = new SubscriptionPlan { PriceMonthly = 249000m } },
            new() { Status = "INACTIVE", SubscriptionPlan = new SubscriptionPlan { PriceMonthly = 249000m } }
        });

        var service = new AdminDashboardService(tenantRepository.Object, planRepository.Object);

        var metrics = await service.GetKpiMetricsAsync();

        Assert.Equal(3, metrics.Count);
        Assert.Equal("348.000", metrics.Single(metric => metric.Id == "mrr").Value);
        Assert.Equal("4", metrics.Single(metric => metric.Id == "total_tenants").Value);
        Assert.Equal("2", metrics.Single(metric => metric.Id == "successful_upgrades").Value);
    }

    [Fact]
    public async Task GetMonthlyAnalytics_WhenNoHistoricalSource_ReturnsEmptyInsteadOfMockData()
    {
        var service = new AdminDashboardService(
            Mock.Of<ITenantRepository>(),
            Mock.Of<ISubscriptionPlanRepository>());

        var analytics = await service.GetMonthlyAnalyticsAsync();

        Assert.Empty(analytics);
    }
}

