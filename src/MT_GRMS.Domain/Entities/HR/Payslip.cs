using MT_GRMS.Domain.Common;

namespace MT_GRMS.Domain.Entities.HR;

/// <summary>
/// Bảng Payslips (Phiếu lương tháng nhân viên chi tiết)
/// </summary>
public class Payslip : BaseEntity
{
    public Guid EmployeeId { get; set; }
    public string PayslipCode { get; set; } = string.Empty;
    public string PeriodMonth { get; set; } = string.Empty;
    public decimal StandardDays { get; set; } = 26.00m;
    public decimal WorkedDays { get; set; } = 0.00m;
    public decimal OtHours { get; set; } = 0.00m;
    public decimal ContractBaseSalary { get; set; } = 0.00m;
    public decimal BaseSalaryEarned { get; set; } = 0.00m;
    public decimal OvertimePay { get; set; } = 0.00m;
    public decimal LunchAllowance { get; set; } = 0.00m;
    public decimal TravelAllowance { get; set; } = 0.00m;
    public decimal Bonus { get; set; } = 0.00m;
    public decimal TotalEarnings { get; set; } = 0.00m;
    public decimal AdvanceDeduction { get; set; } = 0.00m;
    public decimal SocialInsurance { get; set; } = 0.00m;
    public decimal ViolationDeduction { get; set; } = 0.00m;
    public decimal TotalDeductions { get; set; } = 0.00m;
    public decimal NetSalary { get; set; } = 0.00m;
    public string Status { get; set; } = "UNPAID";
    public DateTime? PaidAt { get; set; }
    public string? PaymentMethod { get; set; }

    // Navigations
    public Employee? Employee { get; set; }
    public ICollection<EmployeeSchedule> Schedules { get; set; } = new List<EmployeeSchedule>();
    public ICollection<EmployeeBonusViolation> BonusViolations { get; set; } = new List<EmployeeBonusViolation>();
}
