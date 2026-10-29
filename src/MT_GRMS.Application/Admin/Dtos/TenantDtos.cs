namespace MT_GRMS.Application.Admin.Dtos;

public record TenantFilterRequest(
    string? Keyword = null,
    string? Status = null,
    string? PlanCode = null,
    int Page = 1,
    int PageSize = 10
);

public record TenantSummaryResponse(
    string Id,
    string Subdomain,
    string Name,
    string? TaxCode,
    string? City,
    string OwnerName,
    string OwnerEmail,
    string OwnerPhone,
    string Plan,
    int BranchCount,
    int MaxBranches,
    int StaffCount,
    int MaxStaff,
    DateTime? ExpiryDate,
    string Status,
    string? LockType,
    int? GraceDaysRemaining,
    int? DaysUntilExpiry
);

public record TenantDetailBranchSummaryDto(
    Guid Id,
    string BranchCode,
    string Name,
    string? Address,
    string? Phone,
    bool IsActive
);

public record TenantDetailResponse(
    string Id,
    string Subdomain,
    string Name,
    string Code,
    string? TaxCode,
    string? Hotline,
    string? Address,
    string? City,
    string Status,
    string? CurrentPlan,
    string PlanName,
    DateTime? SubscriptionExpiresAt,
    DateTime? GracePeriodExpiresAt,
    string? LockType,
    string? LockReason,
    DateTime? LockedAt,
    string? LockedBy,
    int BranchCount,
    int MaxBranches,
    int StaffCount,
    int MaxStaff,
    List<TenantDetailBranchSummaryDto> Branches,
    DateTime CreatedAt,
    DateTime? UpdatedAt
);

public record UpdateTenantRequest(
    string Name,
    string? TaxCode,
    string? Hotline,
    string? Address,
    string? City
);

public record LockTenantRequest(
    string LockType,
    string Reason
);

public record ExtendGraceRequest(
    int AdditionalDays,
    string Reason
);

