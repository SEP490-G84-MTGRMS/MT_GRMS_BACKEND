using MT_GRMS.Domain.Common;
using MT_GRMS.Domain.Entities.Identity;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Domain.Entities.HR;

/// <summary>
/// Bảng Employees (Hồ sơ nhân viên gắn với Chi nhánh & Tenant)
/// </summary>
public class Employee : BaseTenantEntity
{
    public Guid BranchId { get; set; }
    public string? UserId { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string CitizenId { get; set; } = string.Empty;
    public DateTime? DateOfBirth { get; set; }
    public string? Gender { get; set; }
    public string Phone { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Address { get; set; }
    public DateTime? StartDate { get; set; }
    public string? Note { get; set; }
    public string? AvatarUrl { get; set; }
    public SalaryType SalaryType { get; set; } = SalaryType.Monthly;
    public decimal BaseRate { get; set; } = 0.00m;
    public string? OtWeekday { get; set; }
    public string? OtSunday { get; set; }
    public string? BankAccountNumber { get; set; }
    public string? BankName { get; set; }
    public string? BankAccountHolder { get; set; }
    public decimal DebtAmount { get; set; } = 0.00m;
    public EmployeeStatus Status { get; set; } = EmployeeStatus.Active;

    // Navigations
    public Branch? Branch { get; set; }
    public User? User { get; set; }
    public ICollection<EmployeeAllowance> Allowances { get; set; } = new List<EmployeeAllowance>();
    public ICollection<Payslip> Payslips { get; set; } = new List<Payslip>();
    public ICollection<EmployeeSchedule> Schedules { get; set; } = new List<EmployeeSchedule>();
    public ICollection<EmployeeBonusViolation> BonusViolations { get; set; } = new List<EmployeeBonusViolation>();
}
