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
    /// Lưu trữ snapshot chi tiết 4 sổ kế toán (S2b, S2c, S2d, S2e) dạng JSON tại thời điểm chốt.
    /// </summary>
    public string BookDataJson { get; set; } = "{}";

    public string FinalizedBy { get; set; } = string.Empty;
    public DateTime FinalizedAtUtc { get; set; } = DateTime.UtcNow;
    public string? Notes { get; set; }
}
