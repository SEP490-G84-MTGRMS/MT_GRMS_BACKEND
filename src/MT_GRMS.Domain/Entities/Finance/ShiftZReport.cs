using MT_GRMS.Domain.Common;
using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Domain.Entities.Finance;

/// <summary>
/// Cashier shift and end-of-shift Z-Report.
/// </summary>
public class ShiftZReport : BaseTenantEntity
{
    public Guid TillId { get; set; }
    public Guid CashierUserId { get; set; }
    public DateTime OpenedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ClosedAtUtc { get; set; }
    public decimal OpeningFloat { get; set; } // Inherited from previous shift's RetainedFloat
    public decimal TotalCashIn { get; set; }
    public decimal TotalCashOut { get; set; }
    public decimal ExpectedCash { get; set; }
    public decimal? ActualCashCounted { get; set; }
    public decimal Discrepancy { get; set; }
    public decimal RetainedFloatAmount { get; set; } // Left for next shift
    public decimal DroppedToSafeAmount { get; set; } // Transferred to BranchSafe via Cash Drop
    public ShiftState Status { get; set; } = ShiftState.Open;
    public string? Notes { get; set; }
}