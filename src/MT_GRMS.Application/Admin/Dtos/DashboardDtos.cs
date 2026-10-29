namespace MT_GRMS.Application.Admin.Dtos;

public record KpiMetricResponse(
    string Id,
    string Title,
    string Value,
    string? Unit,
    string SubText,
    string? TrendText,
    string? TrendType,
    string Icon,
    string IconBg,
    string IconColor,
    string? ValueColor
);

public record PlanDistributionResponse(
    string Id,
    string PlanName,
    string Subtitle,
    int TenantCount,
    double Percentage,
    string RevenueText,
    string Color
);

public record UrgentAlertResponse(
    string Subdomain,
    string Name,
    string OwnerName,
    string Hotline,
    string PlanText,
    string ExpiryNotice,
    string StatusText,
    string StatusType
);

public record RevenueBreakdownDto(
    decimal Basic,
    decimal Advanced,
    decimal Enterprise
);

public record MonthlyAnalyticsResponse(
    string Month,
    string ShortMonth,
    decimal Mrr,
    string MrrFormatted,
    decimal Arr,
    string ArrFormatted,
    decimal Arpu,
    string ArpuFormatted,
    RevenueBreakdownDto RevenueBreakdown,
    int NewTenants,
    int ActiveTenants,
    int TotalOrders,
    string OrdersFormatted,
    string GrowthRate,
    string ChurnRate
);

