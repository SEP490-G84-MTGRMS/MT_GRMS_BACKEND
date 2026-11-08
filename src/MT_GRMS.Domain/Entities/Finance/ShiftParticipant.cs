using MT_GRMS.Domain.Common;

namespace MT_GRMS.Domain.Entities.Finance;

/// <summary>
/// Nhân viên tham gia bán hàng hoặc phát sinh giao dịch trong ca bán hàng chung của chi nhánh.
/// </summary>
public class ShiftParticipant : BaseTenantEntity
{
    public Guid ShiftId { get; set; }
    public Guid UserId { get; set; }
    public string? UserName { get; set; }
    public string? FullName { get; set; }
    public DateTime JoinedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastActiveAtUtc { get; set; } = DateTime.UtcNow;

    // Navigation
    public ShiftZReport? Shift { get; set; }
}

