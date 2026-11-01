using MT_GRMS.Domain.Common;

namespace MT_GRMS.Domain.Entities.Finance;

/// <summary>
/// Record used to deduplicate financial requests and prevent double-spending via Two-Phase Idempotency.
/// </summary>
public class IdempotencyRecord : BaseTenantEntity
{
    public Guid? UserId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public string PayloadHash { get; set; } = string.Empty;
    public string ResponseJson { get; set; } = string.Empty;
    public int StatusCode { get; set; } = 200;
    public string Status { get; set; } = "PENDING"; // PENDING | COMPLETED
    public DateTime ExpiresAtUtc { get; set; } = DateTime.UtcNow.AddHours(24);
}
