using MT_GRMS.Domain.Common;
using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Domain.Entities.Finance;

/// <summary>
/// Khoá phương pháp tính TNCN cho một năm thuế của hộ kinh doanh.
/// Căn cứ: Điểm d Khoản 5 Điều 4 Nghị định 68/2026/NĐ-CP —
/// hộ thực hiện ổn định phương pháp tính thuế trong 2 năm liên tiếp
/// từ năm đầu tiên áp dụng.
/// </summary>
public class HouseholdTaxYearSetting : BaseTenantEntity
{
    public int Year { get; set; }
    public TaxCalculationMethod PitMethod { get; set; }

    /// <summary>Năm cuối bắt buộc dùng PitMethod (ổn định 2 năm).</summary>
    public int LockedUntilYear { get; set; }

    /// <summary>"REVENUE_EXCEEDED_3B" | "START_OF_YEAR_ABOVE_3B" | "MANUAL" | "FIRST_FINALIZED_PERIOD"</summary>
    public string LockReason { get; set; } = string.Empty;

    public DateTime LockedAtUtc { get; set; } = DateTime.UtcNow;
    public string LockedBySystem { get; set; } = "AUTO";
}

