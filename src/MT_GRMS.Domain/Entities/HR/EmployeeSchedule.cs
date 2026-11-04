using MT_GRMS.Domain.Common;
using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Domain.Entities.HR;

/// <summary>
/// Bảng EmployeeSchedules (Lịch làm việc & Chấm công nhận diện khuôn mặt AI FaceID / Chủ cửa hàng duyệt)
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

    // Chấm công nhận diện khuôn mặt (AI FaceID) & Phương thức điểm danh
    public string? CheckInMethod { get; set; }       // "FACE_ID", "MANUAL"
    public string? CheckInFaceImageUrl { get; set; } // URL ảnh chụp camera khi check-in
    public decimal? FaceMatchScore { get; set; }     // Độ khớp khuôn mặt (0.00 - 100.00%)
    public string? CheckOutMethod { get; set; }      // "FACE_ID", "MANUAL"
    public string? CheckOutFaceImageUrl { get; set; } // URL ảnh chụp camera khi check-out

    public int LateMinutes { get; set; } = 0;
    public decimal ActualWorkHours { get; set; } = 0.00m;
    public ScheduleStatus Status { get; set; } = ScheduleStatus.Scheduled;
    public string? ApprovalType { get; set; }
    public ScheduleApprovalStatus ApprovalStatus { get; set; } = ScheduleApprovalStatus.None;
    public string? ApprovalReason { get; set; }
    public string? EvidenceImageUrl { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }

    // Navigations
    public Employee? Employee { get; set; }
    public Payslip? Payslip { get; set; }
}
