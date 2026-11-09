using MT_GRMS.Domain.Common;

namespace MT_GRMS.Domain.Entities.System;

/// <summary>
/// Outbox event để BackgroundService xử lý sau khi transaction commit.
/// </summary>
public class OutboxMessage : BaseTenantEntity
{
    public string Type { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    public string IdempotencyKey { get; set; } = string.Empty;

    /// <summary>PENDING | PROCESSING | PROCESSED | FAILED | DEAD</summary>
    public string Status { get; set; } = "PENDING";

    public int AttemptCount { get; set; }
    public DateTime? NextAttemptAtUtc { get; set; }
    public DateTime? ProcessedAtUtc { get; set; }
    public DateTime? LockedUntilUtc { get; set; }
    public string? LockedBy { get; set; }
    public string? LastError { get; set; }
}
