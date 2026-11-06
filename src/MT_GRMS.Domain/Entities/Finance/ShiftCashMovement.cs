using MT_GRMS.Domain.Common;
using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Domain.Entities.Finance;

/// <summary>
/// Chi tiết phiếu thu/chi tiền mặt tại quầy trong ca làm việc.
/// </summary>
public class ShiftCashMovement : BaseTenantEntity
{
    public Guid ShiftId { get; set; }
    public CashMovementType MovementType { get; set; } // IN hoặc OUT
    public CashMovementCategory Category { get; set; } // EXPENSE, CASH_DRAW, CASH_DEPOSIT, DEBT_COLLECT
    public decimal Amount { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? CreatedByName { get; set; }
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;

    // Navigation
    public ShiftZReport? Shift { get; set; }
}
