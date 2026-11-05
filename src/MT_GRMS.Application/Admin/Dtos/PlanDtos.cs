namespace MT_GRMS.Application.Admin.Dtos;

public record PlanQuotaDto(
    int MaxBranches = 1,
    int MaxEmployees = 3,
    int MaxStorageGb = 5,
    string BackupFrequency = "Weekly"
);

public record PlanFeatureDto(
    bool FefoExpiryAlert = true,
    bool MultiUnitConversion = true,
    bool VietQrDynamic = true,
    bool DebtZaloBook = false,
    bool BarcodeScanning = true,
    bool FinancialReporting = false
);

public record CreatePlanRequest(
    string Code,
    string Name,
    string? Description,
    decimal MonthlyPrice,
    decimal YearlyPrice,
    PlanQuotaDto? Quotas,
    PlanFeatureDto? Features
);

public record UpdatePlanRequest(
    string Name,
    string? Description,
    decimal MonthlyPrice,
    decimal YearlyPrice,
    PlanQuotaDto? Quotas,
    PlanFeatureDto? Features
);

public record UpdatePlanStatusRequest(
    bool IsActive
);

public record PlanFilterRequest(
    string? Keyword = null,
    string? Status = null
);

public record PlanTenantSummaryDto(
    string Id,
    string Name,
    string Subdomain,
    int ActiveBranches,
    int ActiveEmployees,
    DateTime JoinedDate,
    DateTime? ExpiryDate
);

public record PlanSummaryResponse(
    string Id,
    string Code,
    string Name,
    string Description,
    decimal MonthlyPrice,
    decimal YearlyPrice,
    string Status,
    int ActiveTenantCount,
    PlanQuotaDto Quotas,
    PlanFeatureDto Features,
    DateTime CreatedAt,
    DateTime? UpdatedAt
);

public record PlanDetailResponse(
    string Id,
    string Code,
    string Name,
    string Description,
    decimal MonthlyPrice,
    decimal YearlyPrice,
    string Status,
    int ActiveTenantCount,
    PlanQuotaDto Quotas,
    PlanFeatureDto Features,
    List<PlanTenantSummaryDto> Tenants,
    DateTime CreatedAt,
    DateTime? UpdatedAt
);

public record PlanKpiBannerResponse(
    int TotalPlans,
    int ActivePlans,
    int TotalActiveTenants,
    decimal EstimatedMrr
);

