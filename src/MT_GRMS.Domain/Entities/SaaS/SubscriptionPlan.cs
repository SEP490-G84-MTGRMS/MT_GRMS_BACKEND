using MT_GRMS.Domain.Common;

namespace MT_GRMS.Domain.Entities.SaaS;

/// <summary>
/// Bảng SubscriptionPlans (Gói dịch vụ SaaS cấp hệ thống)
/// </summary>
public class SubscriptionPlan : BaseEntity<string>
{
    public SubscriptionPlan()
    {
        Id = Uuid7.NewString();
    }

    public string PlanCode { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public decimal PriceMonthly { get; set; }
    public decimal PriceYearly { get; set; }
    public int MaxBranches { get; set; } = 1;
    public int MaxStaff { get; set; } = 5;
    public string? EnabledModulesJson { get; set; }
    public bool IsActive { get; set; } = true;

    // Navigation
    public ICollection<Tenant> Tenants { get; set; } = new List<Tenant>();
}
