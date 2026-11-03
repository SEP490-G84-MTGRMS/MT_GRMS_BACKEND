using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MT_GRMS.Application.Admin.Dtos;
using MT_GRMS.Application.Admin.Services;
using MT_GRMS.Application.Common;

namespace MT_GRMS.Api.Controllers;

[ApiController]
[Route("api/v1/admin/dashboard")]
[Authorize(Roles = "ADMIN")]
public class AdminDashboardController : BaseApiController
{
    private readonly IAdminDashboardService _dashboardService;

    public AdminDashboardController(IAdminDashboardService dashboardService)
    {
        _dashboardService = dashboardService;
    }

    /// <summary>
    /// Lấy các chỉ số KPI thống kê tổng quan Master Dashboard (MRR, Active Tenants, Total Branches, Urgent Alerts) (REQ-ADMIN-BE-03)
    /// </summary>
    [HttpGet("kpis")]
    public async Task<IActionResult> GetKpis(CancellationToken cancellationToken)
    {
        var kpis = await _dashboardService.GetKpiMetricsAsync(cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<KpiMetricResponse>>.Ok(kpis));
    }

    /// <summary>
    /// Lấy tỷ lệ phân bổ gói cước thuê của các khách hàng toàn hệ thống (REQ-ADMIN-BE-03)
    /// </summary>
    [HttpGet("plan-distribution")]
    public async Task<IActionResult> GetPlanDistribution(CancellationToken cancellationToken)
    {
        var distribution = await _dashboardService.GetPlanDistributionAsync(cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<PlanDistributionResponse>>.Ok(distribution));
    }

    /// <summary>
    /// Lấy danh sách các sự việc cần xử lý khẩn cấp (Hết hạn, Đang trong ân hạn, Bị khóa) (REQ-ADMIN-BE-03)
    /// </summary>
    [HttpGet("urgent-alerts")]
    public async Task<IActionResult> GetUrgentAlerts(CancellationToken cancellationToken)
    {
        var alerts = await _dashboardService.GetUrgentAlertsAsync(cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<UrgentAlertResponse>>.Ok(alerts));
    }

    /// <summary>
    /// Lấy dữ liệu phân tích doanh thu MRR/ARR và xu hướng tăng trưởng khách thuê các tháng gần nhất (REQ-ADMIN-BE-03)
    /// </summary>
    [HttpGet("analytics")]
    public async Task<IActionResult> GetMonthlyAnalytics(CancellationToken cancellationToken)
    {
        var analytics = await _dashboardService.GetMonthlyAnalyticsAsync(cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<MonthlyAnalyticsResponse>>.Ok(analytics));
    }
}

