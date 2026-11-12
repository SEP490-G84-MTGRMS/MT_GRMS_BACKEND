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
    public string? EvidenceImageUrl { get; set; }           // Ảnh bằng chứng chính (hỗ trợ tương thích ngược)
    public List<string> EvidenceImageUrls { get; set; } = new(); // Danh sách nhiều ảnh minh chứng đính kèm

    // ================= 1. DÀNH CHO ĐƠN XIN NGHỈ (LeaveRequest) =================
    public LeaveType? LeaveType { get; set; }               // Loại nghỉ: AnnualLeave, SickLeave, UnpaidLeave...
    public bool IsPaidLeave { get; set; } = false;          // Nghỉ có hưởng lương hay không lương

    // ================= 2. DÀNH CHO ĐƠN ĐỔI CA (ShiftSwap) =================
    public ShiftSwapType? SwapType { get; set; }            // CrossSwap (Đổi chéo) hoặc Cover (Làm thay)
    public Guid? TargetEmployeeId { get; set; }             // Đồng nghiệp nhận ca / đổi ca
    public Guid? TargetScheduleId { get; set; }             // Ca của đồng nghiệp muốn đổi chéo
    public bool? PeerConfirmed { get; set; }                // Đồng nghiệp đã xác nhận đồng ý chưa
    public DateTime? PeerConfirmedAt { get; set; }          // Thời điểm đồng nghiệp xác nhận

    // ================= 3. DÀNH CHO ĐƠN SỰ CỐ CHẤM CÔNG (CheckInIncident) =================
    public DateTime? RequestedCheckInTime { get; set; }     // Giờ vào thực tế xin ghi nhận
    public DateTime? RequestedCheckOutTime { get; set; }    // Giờ ra thực tế xin ghi nhận
    public DateTime? ApprovedCheckInTime { get; set; }      // Giờ vào được duyệt
    public DateTime? ApprovedCheckOutTime { get; set; }     // Giờ ra được duyệt

    // ================= 4. DÀNH CHO ĐƠN LÀM THÊM GIỜ (Overtime) =================
    public int? RequestedOvertimeMinutes { get; set; }      // Số phút xin làm thêm (ví dụ: 60)
    public int? ApprovedOvertimeMinutes { get; set; }       // Số phút OT Quản lý duyệt

    // ================= THÔNG TIN PHÊ DUYỆT (CHỦ QUÁN / QUẢN LÝ) =================
    public string? ReviewNote { get; set; }                 // Ý kiến / Lý do từ chối của Quản lý
    public string? ApprovedBy { get; set; }                 // Người duyệt (UserId / Username)
    public DateTime? ApprovedAt { get; set; }               // Thời điểm duyệt

    // Navigations
    public EmployeeSchedule? Schedule { get; set; }
    public Employee? Employee { get; set; }
    public Employee? TargetEmployee { get; set; }
    public EmployeeSchedule? TargetSchedule { get; set; }
}
