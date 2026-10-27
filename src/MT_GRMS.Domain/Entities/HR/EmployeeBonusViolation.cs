using MT_GRMS.Domain.Common;

namespace MT_GRMS.Domain.Entities.HR;

/// <summary>
/// Bảng EmployeeBonusViolations (Thưởng, Phạt & Tạm ứng lương nhân viên)
/// </summary>
public class EmployeeBonusViolation : BaseTenantEntity
{
    public Guid EmployeeId { get; set; }
    public Guid? PayslipId { get; set; }
    public string Type { get; set; } = string.Empty;
    public decimal Amount { get; set; } = 0.00m;
    public DateTime IncidentDate { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? EvidenceUrl { get; set; }

    // Navigations
    public Employee? Employee { get; set; }
    public Payslip? Payslip { get; set; }
}
