using MT_GRMS.Domain.Common;

namespace MT_GRMS.Domain.Entities.SaaS;

/// <summary>
/// Bảng PlatformSettings (Cấu hình Nền tảng Toàn cục SaaS - Singleton Record ID = 1)
/// </summary>
public class PlatformSetting : BaseEntity<int>
{
    // 1. Chính sách thuê bao dùng thử & Ân hạn
    public int TrialDays { get; set; } = 3;
    public int GracePeriodDays { get; set; } = 7;
    public decimal BranchAddonPriceMonthly { get; set; } = 80000m;
    public decimal StorageAddonPricePerGbMonthly { get; set; } = 15000m;

    // 2. Tài khoản thụ hưởng Master VietQR
    public string MasterBankCode { get; set; } = "MBBank";
    public string MasterBankName { get; set; } = "MB Bank (Ngân hàng Quân Đội)";
    public string MasterAccountNumber { get; set; } = "190065228888";
    public string MasterAccountName { get; set; } = "CONG TY TNHH PHAN MEM MT-GRMS";

    // 3. Cấu hình liên hệ & CSKH
    public string SupportHotline { get; set; } = "1900 6522";
    public string SupportEmail { get; set; } = "support@thviet.vn";
    public string SupportZalo { get; set; } = "0912345678";

    // 4. Bật/Tắt chế độ bảo trì toàn hệ thống
    public bool IsMaintenanceMode { get; set; } = false;
    public string? MaintenanceMessage { get; set; } = "Hệ thống đang bảo trì nâng cấp. Vui lòng quay lại sau ít phút.";
    public DateTime? MaintenanceExpectedEndTime { get; set; }

    // Audit
    public string? UpdatedBy { get; set; }
}
