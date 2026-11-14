using MT_GRMS.Domain.Common;
using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Domain.Entities.Finance;

/// <summary>
/// Bản ghi chốt kỳ tính thuế bất biến của Hộ kinh doanh theo TT 152/2025/TT-BTC & NĐ 141/2026/NĐ-CP (FE-19).
/// </summary>
public class HouseholdTaxSnapshot : BaseTenantEntity
{
    public Guid? BranchId { get; set; }
    public TaxPeriodType PeriodType { get; set; }
    public int Year { get; set; }
    public int PeriodNumber { get; set; }

    public decimal AnnualAccumulatedRevenue { get; set; }
    public HouseholdTaxGroup HouseholdGroup { get; set; }
    public bool IsTaxExempt { get; set; }
    public decimal TaxableRevenueInPeriod { get; set; }

    public decimal VatRate { get; set; }
    public decimal VatAmount { get; set; }
    public decimal PitRate { get; set; }
    public decimal PitAmount { get; set; }
    public decimal TotalTaxAmount { get; set; }

    /// <summary>
    /// Phương pháp tính TNCN được áp dụng trong kỳ này (chỉ ghi nhận khi !IsTaxExempt).
    /// </summary>
    public TaxCalculationMethod? AppliedPitMethod { get; set; }

    /// <summary>
    /// Lưu trữ snapshot chi tiết 4 sổ kế toán (S2b, S2c, S2d, S2e) dạng JSON tại thời điểm chốt.
    /// </summary>
    public string BookDataJson { get; set; } = "{}";

    /// <summary>
    /// Chữ ký số HMAC-SHA256 bảo đảm tính toàn vẹn và chống giả mạo số liệu của snapshot chốt kỳ.
    /// </summary>
    public string AuditSignatureHash { get; set; } = string.Empty;

    public string FinalizedBy { get; set; } = string.Empty;
    public DateTime FinalizedAtUtc { get; set; } = DateTime.UtcNow;
    public string? Notes { get; set; }

    /// <summary>
    /// Trạng thái của snapshot: Finalized (đang khóa kỳ) hoặc Superseded (lịch sử đã bị sửa đổi).
    /// </summary>
    public TaxSnapshotStatus Status { get; set; } = TaxSnapshotStatus.Finalized;

    /// <summary>
    /// Phiên bản điều chỉnh (0: bản gốc, 1, 2...: các bản sửa đổi hồi tố).
    /// </summary>
    public int RevisionNumber { get; set; } = 0;

    /// <summary>
    /// Id của snapshot trước đó mà bản ghi này thay thế (nếu có).
    /// </summary>
    public Guid? SupersededSnapshotId { get; set; }

    /// <summary>
    /// Lý do thay thế / số biên bản điều chỉnh khi chốt lại kỳ tính thuế đã khóa.
    /// </summary>
    public string? RectificationReason { get; set; }
}
