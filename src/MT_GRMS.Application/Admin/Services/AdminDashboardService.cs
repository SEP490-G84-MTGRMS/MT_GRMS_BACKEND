using System.Globalization;
using MT_GRMS.Application.Admin.Dtos;
using MT_GRMS.Application.Admin.Repositories;

namespace MT_GRMS.Application.Admin.Services;

public class AdminDashboardService : IAdminDashboardService
{
    private readonly ITenantRepository _tenantRepository;
    private readonly ISubscriptionPlanRepository _planRepository;
    private static readonly CultureInfo ViCulture = new("vi-VN");

    public AdminDashboardService(
        ITenantRepository tenantRepository,
        ISubscriptionPlanRepository planRepository)
    {
        _tenantRepository = tenantRepository;
        _planRepository = planRepository;
    }

    public async Task<IReadOnlyList<KpiMetricResponse>> GetKpiMetricsAsync(CancellationToken cancellationToken = default)
    {
        var totalTenants = await _tenantRepository.GetTotalCountAsync(cancellationToken);
        var successfulUpgrades = await _tenantRepository.GetSuccessfulUpgradeCountAsync(cancellationToken);

        // Tính MRR từ danh sách Tenant Active
        var activeTenantsWithPlans = await _tenantRepository.GetAllActiveWithPlanAsync(cancellationToken);
        decimal mrr = activeTenantsWithPlans
            .Where(t => t.Status == "ACTIVE" && t.LockType == null && (t.SubscriptionExpiresAt == null || t.SubscriptionExpiresAt > DateTime.UtcNow))
            .Sum(t => t.SubscriptionPlan?.PriceMonthly ?? 0);

        return new List<KpiMetricResponse>
        {
            new(
                Id: "mrr",
                Title: "Doanh thu tháng (MRR)",
                Value: mrr.ToString("N0", ViCulture),
                Unit: "đ/tháng",
                SubText: "ARR tương đương: " + (mrr * 12).ToString("N0", ViCulture) + " đ",
                TrendText: null,
                TrendType: "neutral",
                Icon: "DollarOutlined",
                IconBg: "#EFF6FF",
                IconColor: "#0066FF",
                ValueColor: "#0066FF"
            ),
            new(
                Id: "total_tenants",
                Title: "Tổng số tenant",
                Value: totalTenants.ToString(),
                Unit: "tenant",
                SubText: "Tất cả tenant đã đăng ký trên hệ thống",
                TrendText: null,
                TrendType: "neutral",
                Icon: "ShopOutlined",
                IconBg: "#F1F5F9",
                IconColor: "#475569",
                ValueColor: "#0F172A"
            ),
            new(
                Id: "successful_upgrades",
                Title: "Lượt mua gói thành công",
                Value: successfulUpgrades.ToString(),
                Unit: "giao dịch",
                SubText: "Tất cả giao dịch mua gói thành công, không gồm mua thêm",
                TrendText: null,
                TrendType: "neutral",
                Icon: "ApartmentOutlined",
                IconBg: "#F1F5F9",
                IconColor: "#475569",
                ValueColor: "#0F172A"
            )
        };
    }

    public async Task<IReadOnlyList<PlanDistributionResponse>> GetPlanDistributionAsync(CancellationToken cancellationToken = default)
    {
        var plans = await _planRepository.GetAllAsync(cancellationToken: cancellationToken);
        var tenantCounts = await _planRepository.GetActiveTenantCountsAsync(cancellationToken);

        var totalActive = tenantCounts.Values.Sum();
        var colors = new[] { "#1890ff", "#52c41a", "#faad14", "#722ed1", "#13c2c2", "#eb2f96" };
        var index = 0;

        var result = new List<PlanDistributionResponse>();
        foreach (var p in plans)
        {
            var count = 0;
            if (tenantCounts.TryGetValue(p.PlanCode, out var c1)) count = c1;
            else if (tenantCounts.TryGetValue(p.Id, out var c2)) count = c2;

            var percentage = totalActive > 0 ? Math.Round((double)count / totalActive * 100, 1) : 0;
            var monthlyRev = count * p.PriceMonthly;
            var color = colors[index % colors.Length];
            index++;

            result.Add(new PlanDistributionResponse(
                Id: p.Id,
                PlanName: p.PlanName,
                Subtitle: $"Tối đa {p.MaxBranches} CN, {p.MaxStaff} NV",
                TenantCount: count,
                Percentage: percentage,
                RevenueText: monthlyRev.ToString("N0", ViCulture) + " đ/tháng",
                Color: color
            ));
        }

        return result;
    }

    public async Task<IReadOnlyList<UrgentAlertResponse>> GetUrgentAlertsAsync(CancellationToken cancellationToken = default)
    {
        var tenants = await _tenantRepository.GetUrgentAlertTenantsAsync(10, cancellationToken);
        var now = DateTime.UtcNow;

        var alerts = new List<UrgentAlertResponse>();
        foreach (var t in tenants)
        {
            var owner = t.TenantUsers.FirstOrDefault(tu => tu.Role == "STORE_OWNER" || tu.Role == "TENANT_ADMIN")?.User
                        ?? t.TenantUsers.FirstOrDefault()?.User;
            var ownerName = !string.IsNullOrWhiteSpace(owner?.FullName) ? owner.FullName : (owner?.UserName ?? t.Name);
            var hotline = !string.IsNullOrWhiteSpace(owner?.Phone) ? owner.Phone : (t.Hotline ?? "N/A");

            string expiryNotice;
            string statusText;
            string statusType;

            if (t.LockType != null || t.Status == "LOCKED")
            {
                statusText = $"Đã khóa ({t.LockType ?? "SOFT"})";
                statusType = "locked";
                expiryNotice = !string.IsNullOrEmpty(t.LockReason) ? $"Lý do: {t.LockReason}" : "Đang bị tạm khóa";
            }
            else if (t.Status == "GRACE_PERIOD" || (t.SubscriptionExpiresAt != null && t.SubscriptionExpiresAt <= now))
            {
                var remainingDays = t.GracePeriodExpiresAt != null && t.GracePeriodExpiresAt > now
                    ? (int)Math.Ceiling((t.GracePeriodExpiresAt.Value - now).TotalDays)
                    : 0;
                statusText = "Đang ân hạn";
                statusType = "grace";
                expiryNotice = remainingDays > 0 ? $"Còn {remainingDays} ngày ân hạn" : "Hết hạn hôm nay";
            }
            else
            {
                var daysLeft = t.SubscriptionExpiresAt != null
                    ? (int)Math.Ceiling((t.SubscriptionExpiresAt.Value - now).TotalDays)
                    : 0;
                statusText = "Sắp hết hạn";
                statusType = "expiring";
                expiryNotice = $"Còn {daysLeft} ngày (hết hạn: {t.SubscriptionExpiresAt:dd/MM/yyyy})";
            }

            alerts.Add(new UrgentAlertResponse(
                Subdomain: t.Subdomain,
                Name: t.Name,
                OwnerName: ownerName,
                Hotline: hotline,
                PlanText: t.SubscriptionPlan?.PlanName ?? t.CurrentPlan ?? "Gói cước",
                ExpiryNotice: expiryNotice,
                StatusText: statusText,
                StatusType: statusType
            ));
        }

        return alerts;
    }

    public async Task<IReadOnlyList<MonthlyAnalyticsResponse>> GetMonthlyAnalyticsAsync(CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        return Array.Empty<MonthlyAnalyticsResponse>();
    }
}
