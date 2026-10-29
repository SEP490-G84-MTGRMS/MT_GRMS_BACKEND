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
        var activeTenants = await _tenantRepository.GetActiveCountAsync(cancellationToken);
        var expiringSoon = await _tenantRepository.GetExpiringSoonCountAsync(7, cancellationToken);
        var inGrace = await _tenantRepository.GetGracePeriodCountAsync(cancellationToken);
        var lockedCount = await _tenantRepository.GetLockedCountAsync(cancellationToken);
        var totalBranches = await _tenantRepository.GetTotalBranchesCountAsync(cancellationToken);

        // Tính MRR từ danh sách Tenant Active
        var activeTenantsWithPlans = await _tenantRepository.GetAllActiveWithPlanAsync(cancellationToken);
        decimal mrr = activeTenantsWithPlans
            .Where(t => t.Status == "ACTIVE" && t.LockType == null)
            .Sum(t => t.SubscriptionPlan?.PriceMonthly ?? 0);

        var urgentCount = expiringSoon + inGrace + lockedCount;
        var avgBranches = activeTenants > 0 ? (double)totalBranches / activeTenants : 0;

        return new List<KpiMetricResponse>
        {
            new(
                Id: "mrr",
                Title: "Doanh thu tháng (MRR)",
                Value: mrr.ToString("N0", ViCulture),
                Unit: "đ/tháng",
                SubText: "ARR tương đương: " + (mrr * 12).ToString("N0", ViCulture) + " đ",
                TrendText: "+8.5% so với tháng trước",
                TrendType: "up",
                Icon: "DollarOutlined",
                IconBg: "#e6f7ff",
                IconColor: "#1890ff",
                ValueColor: "#1890ff"
            ),
            new(
                Id: "active_tenants",
                Title: "Khách thuê hoạt động",
                Value: activeTenants.ToString(),
                Unit: "doanh nghiệp",
                SubText: $"Trên tổng số {totalTenants} gian hàng đăng ký",
                TrendText: "+2 tháng này",
                TrendType: "up",
                Icon: "ShopOutlined",
                IconBg: "#f6ffed",
                IconColor: "#52c41a",
                ValueColor: "#52c41a"
            ),
            new(
                Id: "total_branches",
                Title: "Tổng số chi nhánh",
                Value: totalBranches.ToString(),
                Unit: "điểm bán",
                SubText: $"Trung bình {avgBranches:F1} CN / khách thuê",
                TrendText: "+5 điểm bán mới",
                TrendType: "up",
                Icon: "ApartmentOutlined",
                IconBg: "#fff7e6",
                IconColor: "#fa8c16",
                ValueColor: "#fa8c16"
            ),
            new(
                Id: "urgent_alerts",
                Title: "Cần xử lý gấp",
                Value: urgentCount.ToString(),
                Unit: "sự việc",
                SubText: $"{inGrace} ân hạn, {expiringSoon} sắp hết hạn, {lockedCount} đang khóa",
                TrendText: urgentCount > 0 ? "Cần can thiệp" : "Ổn định",
                TrendType: urgentCount > 0 ? "down" : "up",
                Icon: "AlertOutlined",
                IconBg: "#fff1f0",
                IconColor: "#f5222d",
                ValueColor: urgentCount > 0 ? "#f5222d" : "#52c41a"
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
                statusType = "error";
                expiryNotice = !string.IsNullOrEmpty(t.LockReason) ? $"Lý do: {t.LockReason}" : "Đang bị tạm khóa";
            }
            else if (t.Status == "GRACE_PERIOD" || (t.SubscriptionExpiresAt != null && t.SubscriptionExpiresAt <= now))
            {
                var remainingDays = t.GracePeriodExpiresAt != null && t.GracePeriodExpiresAt > now
                    ? (int)Math.Ceiling((t.GracePeriodExpiresAt.Value - now).TotalDays)
                    : 0;
                statusText = "Đang ân hạn";
                statusType = "error";
                expiryNotice = remainingDays > 0 ? $"Còn {remainingDays} ngày ân hạn" : "Hết hạn hôm nay";
            }
            else
            {
                var daysLeft = t.SubscriptionExpiresAt != null
                    ? (int)Math.Ceiling((t.SubscriptionExpiresAt.Value - now).TotalDays)
                    : 0;
                statusText = "Sắp hết hạn";
                statusType = "warning";
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
        // Trả về dữ liệu 6 tháng gần nhất (mock theo quy mô doanh thu hiện tại)
        var plans = await _planRepository.GetAllAsync(cancellationToken: cancellationToken);
        var activeTenants = await _tenantRepository.GetActiveCountAsync(cancellationToken);
        var activeTenantsWithPlans = await _tenantRepository.GetAllActiveWithPlanAsync(cancellationToken);

        decimal currentMrr = activeTenantsWithPlans
            .Where(t => t.Status == "ACTIVE" && t.LockType == null)
            .Sum(t => t.SubscriptionPlan?.PriceMonthly ?? 0);

        if (currentMrr <= 0) currentMrr = 45000000m; // baseline nếu DB chưa có tenant gán plan

        var now = DateTime.UtcNow;
        var list = new List<MonthlyAnalyticsResponse>();

        for (int i = 5; i >= 0; i--)
        {
            var date = now.AddMonths(-i);
            var monthStr = date.ToString("yyyy-MM");
            var shortMonthStr = "T" + date.Month;

            // Tính tăng trưởng lũy tiến lùi
            var factor = (decimal)(1.0 - (i * 0.08));
            var mrr = Math.Max(10000000m, currentMrr * factor);
            var arr = mrr * 12;
            var tenantCount = Math.Max(1, (int)(activeTenants * factor));
            var arpu = tenantCount > 0 ? mrr / tenantCount : 0;

            var breakdown = new RevenueBreakdownDto(
                Basic: mrr * 0.25m,
                Advanced: mrr * 0.45m,
                Enterprise: mrr * 0.30m
            );

            list.Add(new MonthlyAnalyticsResponse(
                Month: monthStr,
                ShortMonth: shortMonthStr,
                Mrr: Math.Round(mrr, 0),
                MrrFormatted: mrr.ToString("N0", ViCulture) + " đ",
                Arr: Math.Round(arr, 0),
                ArrFormatted: arr.ToString("N0", ViCulture) + " đ",
                Arpu: Math.Round(arpu, 0),
                ArpuFormatted: arpu.ToString("N0", ViCulture) + " đ",
                RevenueBreakdown: breakdown,
                NewTenants: Math.Max(1, (int)(tenantCount * 0.15)),
                ActiveTenants: tenantCount,
                TotalOrders: tenantCount * 450,
                OrdersFormatted: (tenantCount * 450).ToString("N0", ViCulture),
                GrowthRate: (8.0 + (i % 3)).ToString("F1") + "%",
                ChurnRate: "1.2%"
            ));
        }

        return await Task.FromResult(list);
    }
}
