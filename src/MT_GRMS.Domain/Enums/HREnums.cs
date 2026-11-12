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

public enum EmployeeStatus
{
    Active = 1,
    Inactive = 2
}

public enum ScheduleStatus
{
    Draft = 0,
    Scheduled = 1,
    Published = 2,
    InProgress = 3,
    Completed = 4,
    Cancelled = 5,
    OnLeave = 6
}

public enum ScheduleApprovalStatus
{
    None = 0,
    Pending = 1,
    Approved = 2,
    Rejected = 3,
    Cancelled = 4
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

public enum PayslipStatus
{
    Unpaid = 1,
    Partial = 2,
    Paid = 3
}
