using MT_GRMS.Application.Admin.Dtos;

namespace MT_GRMS.Application.Admin.Services;

public interface IAdminDashboardService
{
    Task<IReadOnlyList<KpiMetricResponse>> GetKpiMetricsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PlanDistributionResponse>> GetPlanDistributionAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UrgentAlertResponse>> GetUrgentAlertsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MonthlyAnalyticsResponse>> GetMonthlyAnalyticsAsync(CancellationToken cancellationToken = default);
}

