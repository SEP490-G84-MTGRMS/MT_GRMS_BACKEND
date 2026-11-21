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
    public string? Email { get; set; }
    public string? ManagerName { get; set; }
    public bool IsHeadquarter { get; set; } = false;
    public bool IsActive { get; set; } = true;
    public Guid? CurrentBankFinanceAccountId { get; set; }
    public TimeSpan BusinessDayStartTime { get; set; } = new(6, 0, 0);

    // Chính sách chấm công theo chi nhánh (gộp từ BranchAttendancePolicy)
    public decimal StandardDayHours { get; set; } = 8.00m;
    public bool IsConvertHoursToDays { get; set; } = true;
    public string? ConversionRulesJson { get; set; }
    public bool EnableLateThreshold { get; set; } = true;
    public int LateThresholdMinutes { get; set; } = 0;
    public bool EnableEarlyLeaveThreshold { get; set; } = true;
    public int EarlyLeaveThresholdMinutes { get; set; } = 0;
    public bool IsAttendancePolicyCustomized { get; set; } = false;

    // Backward compatibility aliases
    [NotMapped]
    public string Code { get => BranchCode; set => BranchCode = value; }
    [NotMapped]
    public string Status { get => IsActive ? "Active" : "Inactive"; set => IsActive = value == "Active"; }
    [NotMapped]
    public int PendingOrdersCount { get; set; } = 0;
    [NotMapped]
    public DateTime? LastOrderDate { get; set; }

    // Navigation
    public Tenant? Tenant { get; set; }
}
