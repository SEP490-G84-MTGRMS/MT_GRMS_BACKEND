using MT_GRMS.Domain.Common;
using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Domain.Entities.HR;

/// <summary>
/// Bảng ScheduleApprovalRequests: Lưu từng phiếu yêu cầu OT, Sự cố chấm công, Đổi ca, hoặc Xin nghỉ
/// </summary>
public class ScheduleApprovalRequest : BaseTenantEntity
{
    public Guid ScheduleId { get; set; }                    // Ca gốc của nhân viên gửi yêu cầu
    public Guid EmployeeId { get; set; }                    // Nhân viên tạo đơn (Người yêu cầu)
    public Guid BranchId { get; set; }                      // Chi nhánh diễn ra ca
    
    // Loại yêu cầu & Trạng thái duyệt
    public ScheduleRequestType RequestType { get; set; } 
    public ScheduleApprovalStatus Status { get; set; } = ScheduleApprovalStatus.Pending;

    // Dữ liệu giải trình chung
    public string Reason { get; set; } = string.Empty;      // Lý do làm đơn
    public List<string> EvidenceImageUrls { get; set; } = new(); // Danh sách nhiều ảnh minh chứng đính kèm

    // ================= 1. DÀNH CHO ĐƠN XIN NGHỈ (LeaveRequest) =================
    public LeaveType? LeaveType { get; set; }               // Loại nghỉ: AnnualLeave, SickLeave, UnpaidLeave...
    public bool IsPaidLeave { get; set; } = false;          // Nghỉ có hưởng lương hay không lương

    // ================= 2. DÀNH CHO ĐƠN SỰ CỐ CHẤM CÔNG (CheckInIncident) =================
    public DateTime? RequestedCheckInTime { get; set; }     // Giờ vào thực tế xin ghi nhận
    public DateTime? RequestedCheckOutTime { get; set; }    // Giờ ra thực tế xin ghi nhận
    public DateTime? ApprovedCheckInTime { get; set; }      // Giờ vào được duyệt
    public DateTime? ApprovedCheckOutTime { get; set; }     // Giờ ra được duyệt

    // ================= 3. DÀNH CHO ĐƠN LÀM THÊM GIỜ (Overtime) =================
    public int? RequestedOvertimeMinutes { get; set; }      // Số phút xin làm thêm (ví dụ: 60)
    public int? ApprovedOvertimeMinutes { get; set; }       // Số phút OT Quản lý duyệt

    // ================= THÔNG TIN PHÊ DUYỆT (CHỦ QUÁN / QUẢN LÝ) =================
    public string? ReviewNote { get; set; }                 // Ý kiến / Lý do từ chối của Quản lý
    public string? ApprovedBy { get; set; }                 // Người duyệt (UserId / Username)
    public DateTime? ApprovedAt { get; set; }               // Thời điểm duyệt

    // Navigations
    public EmployeeSchedule? Schedule { get; set; }
    public Employee? Employee { get; set; }
}
