using MT_GRMS.Domain.Common;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Domain.Entities.HR;

/// <summary>
/// Bảng EmployeeSchedules: Lịch phân ca & Chấm công thực tế của nhân viên
/// </summary>
public class EmployeeSchedule : BaseTenantEntity
{
    public Guid EmployeeId { get; set; }
    public Guid BranchId { get; set; }                        // Chi nhánh làm việc thực tế của ca này
    public Guid? ShiftConfigId { get; set; }                  // Ca làm việc mẫu áp dụng
    public Guid? PayslipId { get; set; }                      // Phiếu lương gắn liền (nếu đã chốt công)

    public DateTime WorkDate { get; set; }                    // Ngày làm việc (chỉ tính ngày)
    
    // Snapshot thông tin ca tại thời điểm xếp lịch (tránh bị ảnh hưởng nếu ShiftConfig bị sửa đổi sau này)
    public string ShiftCode { get; set; } = string.Empty;
    public string ShiftName { get; set; } = string.Empty;
    public TimeSpan PlannedStartTime { get; set; }
    public TimeSpan PlannedEndTime { get; set; }
    public decimal DurationHours { get; set; } = 4.00m;
    public TimeSpan AllowedCheckInStart { get; set; }
    public TimeSpan AllowedCheckInEnd { get; set; }
    public bool IsCheckInNextDay { get; set; } = false;       // Ca kết thúc vào ngày hôm sau

    // Dữ liệu chấm công thực tế
    public DateTime? CheckInTime { get; set; }
    public DateTime? CheckOutTime { get; set; }

    // Chấm công AI FaceID & Phương thức điểm danh
    public string? CheckInMethod { get; set; }                // "FACE_ID", "MANUAL"
    public string? CheckInFaceImageUrl { get; set; }
    public decimal? FaceMatchScore { get; set; }
    public string? CheckOutMethod { get; set; }
    public string? CheckOutFaceImageUrl { get; set; }

    // Thống kê giờ công & Dung sai đi muộn / về sớm / OT
    public int LateMinutes { get; set; } = 0;
    public int EarlyLeaveMinutes { get; set; } = 0;
    public int OvertimeMinutes { get; set; } = 0;
    public decimal ActualWorkHours { get; set; } = 0.00m;

    // Trạng thái ca & Quy trình duyệt của Quản lý / Chủ cửa hàng
    public ScheduleStatus Status { get; set; } = ScheduleStatus.Scheduled;
    public bool IsLeave { get; set; } = false;
    public bool IsPaidLeave { get; set; } = false;
    public string? ApprovalType { get; set; }
    public ScheduleApprovalStatus ApprovalStatus { get; set; } = ScheduleApprovalStatus.None;
    public string? ApprovalReason { get; set; }
    public string? EvidenceImageUrl { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }

    // Navigations
    public Employee? Employee { get; set; }
    public Branch? Branch { get; set; }
    public ShiftConfig? ShiftConfig { get; set; }
    public Payslip? Payslip { get; set; }

    // Quan hệ 1 - N: Danh sách tất cả các yêu cầu phát sinh của ca này
    public ICollection<ScheduleApprovalRequest> ApprovalRequests { get; set; } = new List<ScheduleApprovalRequest>();
}
