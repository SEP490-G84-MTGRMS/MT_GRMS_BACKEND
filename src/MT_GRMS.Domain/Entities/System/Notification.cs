using MT_GRMS.Domain.Common;

namespace MT_GRMS.Domain.Entities.System;

/// <summary>
/// Bảng Notifications: Lưu trữ thông báo In-App cô lập theo Tenant và User nhận.
/// </summary>
public class Notification : BaseTenantEntity
{
    public string RecipientUserId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? ActionUrl { get; set; }
    public bool IsRead { get; set; } = false;
    public DateTime? ReadAtUtc { get; set; }
}
