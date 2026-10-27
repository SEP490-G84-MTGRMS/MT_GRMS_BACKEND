using MT_GRMS.Domain.Common;
using MT_GRMS.Domain.Entities.Identity;

namespace MT_GRMS.Domain.Entities.SaaS;

/// <summary>
/// Bảng Tenants (Chủ chuỗi / Doanh nghiệp thuê SaaS toàn hệ thống)
/// </summary>
public class Tenant : BaseEntity<string>
{
    public Tenant()
    {
        Id = Guid.NewGuid().ToString();
    }

    public string Name { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? TaxCode { get; set; }
    public string? Hotline { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string Status { get; set; } = "ACTIVE";
    public string? CurrentPlan { get; set; }
    public DateTime? SubscriptionExpiresAt { get; set; }
    public DateTime? GracePeriodExpiresAt { get; set; }
    public string? LockType { get; set; }
    public string? LockReason { get; set; }
    public DateTime? LockedAt { get; set; }
    public string? LockedBy { get; set; }

    // Navigations
    public SubscriptionPlan? SubscriptionPlan { get; set; }
    public ICollection<TenantUser> TenantUsers { get; set; } = new List<TenantUser>();
    public ICollection<Branch> Branches { get; set; } = new List<Branch>();
}
