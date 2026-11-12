using MT_GRMS.Domain.Common;
using MT_GRMS.Domain.Entities.SaaS;

namespace MT_GRMS.Domain.Entities.HR;

/// <summary>
/// Bảng BranchAttendancePolicies: Chính sách chấm công, quy đổi ngày công, dung sai đi muộn và làm thêm giờ (OT) theo Chi nhánh
/// </summary>
public class BranchAttendancePolicy : BaseTenantEntity
{
    public Guid BranchId { get; set; }                                   // Chi nhánh áp dụng chính sách

    public decimal StandardDayHours { get; set; } = 8.00m;               // Số giờ của 1 ngày công chuẩn (VD: 8.0)
    public bool IsConvertHoursToDays { get; set; } = true;               // Quy đổi số giờ làm thành số ngày công
    public string? ConversionRulesJson { get; set; }                     // JSON danh sách các mức quy đổi giờ -> ngày công

    public bool EnableLateThreshold { get; set; } = true;                // Bật tính đi muộn
    public int LateThresholdMinutes { get; set; } = 0;                   // Tính đi muộn sau X phút
    public bool EnableEarlyLeaveThreshold { get; set; } = true;          // Bật tính về sớm
    public int EarlyLeaveThresholdMinutes { get; set; } = 0;             // Tính về sớm trước X phút

    // Navigation
    public Branch? Branch { get; set; }
}

