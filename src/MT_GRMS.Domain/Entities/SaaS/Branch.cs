using System.ComponentModel.DataAnnotations.Schema;
using MT_GRMS.Domain.Common;

namespace MT_GRMS.Domain.Entities.SaaS;

/// <summary>
/// Bảng Branches (Chi nhánh / Cửa hàng siêu thị)
/// </summary>
public class Branch : BaseTenantEntity
{
    public string BranchCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public int CheckinRadiusMeters { get; set; } = 100;
    public bool IsActive { get; set; } = true;

    // Backward compatibility aliases
    [NotMapped]
    public string Code { get => BranchCode; set => BranchCode = value; }
    [NotMapped]
    public string? ManagerName { get; set; }
    [NotMapped]
    public string Status { get => IsActive ? "Active" : "Inactive"; set => IsActive = value == "Active"; }
    [NotMapped]
    public int PendingOrdersCount { get; set; } = 0;
    [NotMapped]
    public DateTime? LastOrderDate { get; set; }

    // Navigation
    public Tenant? Tenant { get; set; }
}
