using MT_GRMS.Domain.Common;
using MT_GRMS.Domain.Entities.SaaS;

namespace MT_GRMS.Domain.Entities.System;

/// <summary>
/// Bảng TaxSnapshots (Kê khai & Lịch sử Thuế định kỳ của Tenant)
/// </summary>
public class TaxSnapshot : BaseTenantEntity
{
    public int TaxYear { get; set; }
    public int? TaxQuarter { get; set; }
    public decimal TotalRevenue { get; set; } = 0.00m;
    public decimal? TaxRateVat { get; set; }
    public decimal VatAmount { get; set; } = 0.00m;
    public decimal? TaxRatePit { get; set; }
    public decimal PitPayable { get; set; } = 0.00m;
    public string VatMethod { get; set; } = "DIRECT";
    public string PitMethod { get; set; } = "PROGRESSIVE";
    public string Status { get; set; } = "DRAFT";
    public DateTime? SubmittedAt { get; set; }
    public DateTime? PaidAt { get; set; }
    public DateTime? GeneratedAt { get; set; }
    public bool IsLocked { get; set; } = false;

    // Navigation
    public Tenant? Tenant { get; set; }
}
