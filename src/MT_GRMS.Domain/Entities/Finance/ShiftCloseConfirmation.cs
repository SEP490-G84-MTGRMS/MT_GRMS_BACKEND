using MT_GRMS.Domain.Common;

namespace MT_GRMS.Domain.Entities.Finance;

/// <summary>
/// Bản ghi xác nhận số tiền kiểm đếm két khi chốt ca chung giữa các nhân viên.
/// </summary>
public class ShiftCloseConfirmation : BaseTenantEntity
{
    public Guid ShiftId { get; set; }
    public Guid UserId { get; set; }
    public string? UserName { get; set; }
    public decimal ConfirmedCountedCash { get; set; }
    public DateTime ConfirmedAtUtc { get; set; } = DateTime.UtcNow;

    // Navigation
    public ShiftZReport? Shift { get; set; }
}

