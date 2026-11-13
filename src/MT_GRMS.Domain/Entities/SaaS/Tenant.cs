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
        Id = Uuid7.NewString();
    }

    public string Name { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? TaxCode { get; set; }
    public string? Hotline { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string Status { get; set; } = "ACTIVE";
    public string SubscriptionStatus { get; set; } = "NO_PLAN";
    public decimal WalletBalance { get; set; } = 0;
    public string? CurrentPlan { get; set; }
    public int ExtraBranches { get; set; } = 0;
    public int ExtraStorageGb { get; set; } = 0;
    public DateTime? StorageAddonExpiresAt { get; set; }
    public long StorageUsedBytes { get; set; } = 0;
    public bool HasStaffManagementAddon { get; set; } = false;
    public DateTime? SubscriptionExpiresAt { get; set; }
    public DateTime? TrialStartedAt { get; set; }
    public DateTime? TrialExpiresAt { get; set; }
    public DateTime? FirstPaidAt { get; set; }
    public DateTime? GracePeriodExpiresAt { get; set; }
    public string? LockType { get; set; }
    public string? LockReason { get; set; }
    public DateTime? LockedAt { get; set; }
    public string? LockedBy { get; set; }

    // Navigations
    public SubscriptionPlan? SubscriptionPlan { get; set; }
    public ICollection<TenantUser> TenantUsers { get; set; } = new List<TenantUser>();
    public ICollection<Branch> Branches { get; set; } = new List<Branch>();
    public ICollection<WalletTransaction> WalletTransactions { get; set; } = new List<WalletTransaction>();
    public ICollection<WalletTopupOrder> WalletTopupOrders { get; set; } = new List<WalletTopupOrder>();
}
