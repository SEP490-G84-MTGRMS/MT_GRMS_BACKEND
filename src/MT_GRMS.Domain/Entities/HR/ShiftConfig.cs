using MT_GRMS.Domain.Common;
using MT_GRMS.Domain.Entities.SaaS;

namespace MT_GRMS.Domain.Entities.HR;

/// <summary>
/// Bảng ShiftConfigs: Cấu hình danh mục ca làm việc chuẩn của từng Chi nhánh (Master Data)
/// Chủ cửa hàng có thể cấu hình các khung giờ ca mẫu (Ca sáng, Ca chiều, Ca tối) mà không cần tạo bảng WorkShifts riêng.
/// </summary>
public class ShiftConfig : BaseTenantEntity
{
    public Guid BranchId { get; set; }
    public string ShiftCode { get; set; } = string.Empty; // Mã ca: SANG, CHIEU, TOI, FULL
    public string ShiftName { get; set; } = string.Empty; // Tên ca: Ca sáng, Ca chiều
    public TimeSpan StartTime { get; set; }               // Giờ bắt đầu chuẩn (VD: 06:00:00)
    public TimeSpan EndTime { get; set; }                 // Giờ kết thúc chuẩn (VD: 14:00:00)
    public decimal DurationHours { get; set; } = 8.00m;   // Thời lượng chuẩn (VD: 8.00 giờ)
    public TimeSpan AllowedCheckInStart { get; set; }     // Khung check-in sớm (VD: 05:30:00)
    public TimeSpan AllowedCheckInEnd { get; set; }       // Khung check-in trễ tối đa (VD: 07:00:00)
    public string? Description { get; set; }              // Ghi chú ca làm việc
    public bool IsActive { get; set; } = true;            // Trạng thái hoạt động (true: đang dùng, false: ngừng hoạt động)

    // Navigation
    public Branch? Branch { get; set; }
}

