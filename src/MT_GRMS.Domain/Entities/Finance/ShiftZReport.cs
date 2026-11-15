using MT_GRMS.Domain.Common;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Domain.Entities.Finance;

/// <summary>
/// Cashier shift and end-of-shift Z-Report.
/// </summary>
public class ShiftZReport : BaseTenantEntity
{
    public Guid BranchId { get; set; }
    public Guid TillId { get; set; }
    public string ShiftCode { get; set; } = string.Empty;
    public string ShiftName { get; set; } = string.Empty;
    public Guid CashierUserId { get; set; }
    public Guid? OpenedScheduleId { get; set; }
    public Guid? ClosedScheduleId { get; set; }
    public string? ReceiverUserId { get; set; }
    public Guid? PrevShiftId { get; set; }

    public DateTime OpenedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ClosedAtUtc { get; set; }
    public DateOnly BusinessDate { get; set; }

    public decimal OpeningFloatExpected { get; set; } = 0.00m; // Số tiền ca trước để lại (prevShift.RetainedFloatAmount)
    public decimal OpeningFloat { get; set; } = 0.00m;         // Số tiền thực đếm khi mở ca
    public decimal OpeningDiscrepancy { get; set; } = 0.00m;   // Chênh lệch mở ca = OpeningFloat - OpeningFloatExpected

    public decimal TotalCashIn { get; set; } = 0.00m;          // Tổng tiền vào két trong ca (nạp tiền lẻ, thu nợ...)
    public decimal TotalCashOut { get; set; } = 0.00m;         // Tổng tiền ra két trong ca (chi vặt, rút nộp chủ...)
    public decimal TotalBankRevenue { get; set; } = 0.00m;     // Doanh thu ngân hàng/VietQR (không tính vào két tiền mặt)
    public int TotalOrdersCount { get; set; } = 0;             // Tổng số hóa đơn phát sinh trong ca

    public decimal ExpectedCash { get; set; } = 0.00m;         // Tiền lý thuyết theo sổ sách (G)
    public decimal? ActualCashCounted { get; set; }            // Tiền mặt thực tế đếm cuối ca (H)
    public decimal? Discrepancy { get; set; }                  // Chênh lệch chốt ca = H - G (null khi ca đang mở)
    public string? DiscrepancyReason { get; set; }             // Giải trình riêng, không trộn vào ghi chú
    public long ClosingVersion { get; set; } = 1;
    public Guid? DestinationFundId { get; set; }

    public decimal RetainedFloatAmount { get; set; } = 0.00m;  // Số tiền giữ lại két cho ca kế tiếp
    public decimal DroppedToSafeAmount { get; set; } = 0.00m;  // Số tiền rút nộp vào két an toàn (BranchSafe)

    public decimal TotalCashSales { get; set; } = 0.00m;
    public HandoverStatus HandoverStatus { get; set; } = HandoverStatus.PENDING;
    public ShiftState Status { get; set; } = ShiftState.Open;
    public string? Notes { get; set; }

    public Guid? ClosingStartedByUserId { get; set; }
    public DateTime? ClosingStartedAtUtc { get; set; }
    public Guid? ClosedByUserId { get; set; }
    public string? ForceCloseReason { get; set; }
    public bool SnapshotLocked { get; set; }
    public CashbookPostingStatus CashbookPostingStatus { get; set; } = CashbookPostingStatus.NotPosted;
    public Guid? BusinessDayClosingId { get; set; }

    // Navigations
    public Branch? Branch { get; set; }
    public FinanceAccount? DestinationFund { get; set; }
    public ShiftZReport? PrevShift { get; set; }
    public ICollection<ShiftCashMovement> CashMovements { get; set; } = new List<ShiftCashMovement>();
    public ICollection<ShiftParticipant> Participants { get; set; } = new List<ShiftParticipant>();
    public ICollection<ShiftCloseConfirmation> CloseConfirmations { get; set; } = new List<ShiftCloseConfirmation>();
}
