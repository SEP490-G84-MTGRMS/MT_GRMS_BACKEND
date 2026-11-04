using MT_GRMS.Domain.Common;
using MT_GRMS.Domain.Entities.SaaS;

namespace MT_GRMS.Domain.Entities.HR;

/// <summary>
/// Bảng ShiftConfigs: Cấu hình danh mục ca làm việc chuẩn của Chi nhánh (Master Data)
/// </summary>
public class ShiftConfig : BaseTenantEntity
{
    public Guid BranchId { get; set; }
    public string? BranchIds { get; set; } // Danh sách JSON các ID chi nhánh áp dụng: ["guid1", "guid2"]
    public string ShiftCode { get; set; } = string.Empty; // Mã ca: Tự sinh hoặc tùy chỉnh
    public string ShiftName { get; set; } = string.Empty; // Tên ca: "Tối", "Sáng", "Chiều"
    public TimeSpan StartTime { get; set; }               // Giờ bắt đầu chuẩn (VD: 17:00:00)
    public TimeSpan EndTime { get; set; }                 // Giờ kết thúc chuẩn (VD: 21:00:00)
    public decimal DurationHours { get; set; } = 4.00m;   // Tổng giờ làm việc (VD: 4.00)
    public TimeSpan AllowedCheckInStart { get; set; }     // Khung check-in sớm (VD: 14:00:00)
    public TimeSpan AllowedCheckInEnd { get; set; }       // Khung check-in trễ (VD: 00:00:00)
    public bool IsCheckInNextDay { get; set; } = false;   // Kết thúc chấm công vào hôm sau
    public string? Description { get; set; }              // Ghi chú ca làm việc
    public bool IsActive { get; set; } = true;            // Trạng thái hoạt động (true: đang dùng, false: ngừng hoạt động)

    // Navigation
    public Branch? Branch { get; set; }
}
