using Microsoft.AspNetCore.Mvc;
using MT_GRMS.Api.Controllers;
using MT_GRMS.Application.Admin.Dtos;
using MT_GRMS.Application.Admin.Services;
using MT_GRMS.Application.Common;
using Xunit;

namespace MT_GRMS.UnitTests.Admin;

public class MockDashboardService : IAdminDashboardService
{
    public Task<IReadOnlyList<KpiMetricResponse>> GetKpiMetricsAsync(CancellationToken cancellationToken = default)
    {
        var list = new List<KpiMetricResponse>
        {
            new("mrr", "Doanh thu tháng (MRR)", "45,000,000", "đ/tháng", "ARR: 540,000,000 đ", "+12.5%", "up", "DollarOutlined", "#e6f7ff", "#1890ff", "#1890ff"),
            new("active_tenants", "Khách thuê hoạt động", "18", "doanh nghiệp", "Trên tổng số 20 gian hàng", "+2 tháng này", "up", "ShopOutlined", "#f6ffed", "#52c41a", "#52c41a"),
            new("total_branches", "Tổng số chi nhánh", "42", "điểm bán", "Trung bình 2.3 CN / khách", "+5 điểm bán", "up", "ApartmentOutlined", "#fff7e6", "#fa8c16", "#fa8c16"),
            new("urgent_alerts", "Cần xử lý gấp", "3", "sự việc", "1 ân hạn, 2 sắp hết hạn", "Cần can thiệp", "down", "AlertOutlined", "#fff1f0", "#f5222d", "#f5222d")
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
    public async Task GetKpis_Returns200Ok_WithFourStandardCards()
    {
        var result = await _controller.GetKpis(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<IReadOnlyList<KpiMetricResponse>>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Equal(4, resp.Data!.Count);
        Assert.Equal("mrr", resp.Data[0].Id);
        Assert.Equal("active_tenants", resp.Data[1].Id);
        Assert.Equal("total_branches", resp.Data[2].Id);
        Assert.Equal("urgent_alerts", resp.Data[3].Id);
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

