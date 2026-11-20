namespace MT_GRMS.Domain.Enums;

/// <summary>
/// Hình thức trả lương của nhân viên
/// </summary>
public enum SalaryType
{
    Monthly = 1, // Lương cố định theo tháng
    Hourly = 2,  // Lương theo giờ làm việc
    Shift = 3    // Lương theo ca làm việc
}

/// <summary>
/// Trạng thái làm việc của nhân viên
/// </summary>
public enum EmployeeStatus
{
    Active = 1,   // Đang làm việc
    Inactive = 2  // Ngừng làm việc / Đã nghỉ
}

/// <summary>
/// Trạng thái vòng đời của ca làm việc / bản ghi chấm công
/// </summary>
public enum ScheduleStatus
{
    Draft = 0,       // Nháp (chưa ban hành lịch cho nhân viên)
    Scheduled = 1,   // Đã xếp lịch (mặc định khi tạo ca)
    Published = 2,   // Đã ban hành lịch (sẵn sàng để chấm công)
    InProgress = 3,  // Đang trong ca làm việc (đã Check-in, chưa Check-out)
    Completed = 4,   // Đã hoàn thành ca (đã Check-out hoặc chấm công đi làm / nghỉ có lương)
    Cancelled = 5,   // Đã hủy ca hoặc nghỉ không lương
    OnLeave = 6      // Đang nghỉ phép (đã duyệt đơn xin nghỉ)
}

/// <summary>
/// Trạng thái phê duyệt đơn yêu cầu gắn với ca làm việc
/// </summary>
public enum ScheduleApprovalStatus
{
    None = 0,      // Không có đơn / Chưa gửi yêu cầu
    Pending = 1,   // Đang chờ quản lý phê duyệt
    Approved = 2,  // Đã được phê duyệt
    Rejected = 3,  // Đã bị từ chối
    Cancelled = 4  // Đã hủy yêu cầu (dự phòng / chưa dùng trực tiếp trong logic hiện tại)
}

/// <summary>
/// Loại yêu cầu phê duyệt ca làm việc
/// </summary>
public enum ScheduleRequestType
{
    Overtime = 1,         // Làm thêm giờ
    CheckInIncident = 2,  // Sự cố Check-in / Vào ca (Mở ca khẩn cấp)
    ShiftSwap = 3,        // Đổi ca làm việc
    LeaveRequest = 4,     // Xin nghỉ ca làm việc
    CheckOutIncident = 5  // Sự cố Check-out / Ra ca
}

/// <summary>
/// Hình thức đổi ca làm việc
/// </summary>
public enum ShiftSwapType
{
    CrossSwap = 1, // Đổi chéo ca (Ca A đổi lấy Ca B của đồng nghiệp)
    Cover = 2      // Nhờ làm thay (Chuyển ca của A cho đồng nghiệp)
}

/// <summary>
/// Loại lý do xin nghỉ phép
/// </summary>
public enum LeaveType
{
    AnnualLeave = 1, // Nghỉ phép năm (hưởng lương)
    SickLeave = 2,   // Nghỉ ốm / Khám bệnh
    UnpaidLeave = 3, // Nghỉ việc riêng không lương
    Maternity = 4,   // Nghỉ thai sản
    Other = 5        // Khác
}

/// <summary>
/// Trạng thái thanh toán của phiếu lương
/// </summary>
public enum PayslipStatus
{
    Unpaid = 1,   // Chưa thanh toán (có thể tính lại, tạm ứng hoặc xóa)
    Partial = 2,  // Thanh toán một phần (dự phòng khóa ca tương tự Paid, hiện chưa gán trực tiếp)
    Paid = 3      // Đã thanh toán (khóa phiếu lương và khóa toàn bộ ca trong kỳ)
}
