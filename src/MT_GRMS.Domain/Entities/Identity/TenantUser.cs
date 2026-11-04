using MT_GRMS.Domain.Entities.SaaS;

namespace MT_GRMS.Domain.Entities.Identity;

/// <summary>
/// Bảng TenantUsers (Phân quyền Tenant - Composite Key TenantID + UserID)
/// </summary>
public class TenantUser
{
    public string TenantId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Role { get; set; } = "STAFF";
    public bool IsActive { get; set; } = true;
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;

    // Navigations
    public Tenant? Tenant { get; set; }
    public User? User { get; set; }
}
