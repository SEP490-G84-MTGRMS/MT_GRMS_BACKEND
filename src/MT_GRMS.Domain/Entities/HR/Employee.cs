using MT_GRMS.Domain.Common;
using MT_GRMS.Domain.Entities.Identity;
using MT_GRMS.Domain.Entities.SaaS;

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
    public string? AvatarUrl { get; set; }
    public string? JobTitle { get; set; }
    public string SalaryType { get; set; } = "monthly";
    public decimal BaseRate { get; set; } = 0.00m;
    public string? BankAccountNumber { get; set; }
    public string? BankName { get; set; }
    public string? BankAccountHolder { get; set; }
    public decimal LunchAllowance { get; set; } = 0.00m;
    public decimal TravelAllowance { get; set; } = 0.00m;
    public decimal DebtAmount { get; set; } = 0.00m;
    public string Status { get; set; } = "active";

    // Navigations
    public Branch? Branch { get; set; }
    public User? User { get; set; }
    public ICollection<Payslip> Payslips { get; set; } = new List<Payslip>();
    public ICollection<EmployeeSchedule> Schedules { get; set; } = new List<EmployeeSchedule>();
    public ICollection<EmployeeBonusViolation> BonusViolations { get; set; } = new List<EmployeeBonusViolation>();
}
