using MT_GRMS.Domain.Common;
using MT_GRMS.Domain.Entities.Identity;
using MT_GRMS.Domain.Entities.SaaS;

namespace MT_GRMS.Domain.Entities.System;

/// <summary>
/// Bảng AuditLogs (Nhật ký kiểm toán thao tác hệ thống)
/// </summary>
public class AuditLog : BaseEntity
{
    public string? TenantId { get; set; }
    public string? ActorId { get; set; }
    public string? ActorName { get; set; }
    public string? ActorRole { get; set; }
    public string ActionType { get; set; } = string.Empty;
    public string TargetEntity { get; set; } = string.Empty;
    public string? TargetId { get; set; }
    public string? OldValuesJson { get; set; }
    public string? NewValuesJson { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }

    // Navigations
    public Tenant? Tenant { get; set; }
    public User? Actor { get; set; }
}
