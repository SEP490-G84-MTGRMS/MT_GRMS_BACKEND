using MT_GRMS.Domain.Common;

namespace MT_GRMS.Domain.Entities.HR;

/// <summary>
/// Bảng EmployeeSchedules (Lịch làm việc & Chấm công GPS Inline)
/// </summary>
public class EmployeeSchedule : BaseEntity
{
    public Guid EmployeeId { get; set; }
    public Guid? PayslipId { get; set; }
    public DateTime WorkDate { get; set; }
    public string ShiftCode { get; set; } = string.Empty;
    public string ShiftName { get; set; } = string.Empty;
    public TimeSpan PlannedStartTime { get; set; }
    public TimeSpan PlannedEndTime { get; set; }
    public decimal DurationHours { get; set; } = 8.00m;
    public TimeSpan AllowedCheckInStart { get; set; }
    public TimeSpan AllowedCheckInEnd { get; set; }
    public DateTime? CheckInTime { get; set; }
    public DateTime? CheckOutTime { get; set; }
    public decimal? CheckInLatitude { get; set; }
    public decimal? CheckInLongitude { get; set; }
    public bool IsGpsValid { get; set; } = true;
    public int LateMinutes { get; set; } = 0;
    public decimal ActualWorkHours { get; set; } = 0.00m;
    public string Status { get; set; } = "scheduled";
    public string? ApprovalType { get; set; }
    public string ApprovalStatus { get; set; } = "NONE";
    public string? ApprovalReason { get; set; }
    public string? EvidenceImageUrl { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }

    // Navigations
    public Employee? Employee { get; set; }
    public Payslip? Payslip { get; set; }
}
