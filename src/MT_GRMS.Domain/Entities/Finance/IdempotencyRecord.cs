using MT_GRMS.Domain.Common;

namespace MT_GRMS.Domain.Entities.Finance;

/// <summary>
/// Record used to deduplicate requests within a 24-hour window.
/// </summary>
public class IdempotencyRecord : BaseTenantEntity
{
    public string Key { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public string PayloadHash { get; set; } = string.Empty;
    public string ResponseJson { get; set; } = string.Empty;
    public int StatusCode { get; set; } = 200;
    public DateTime ExpiresAtUtc { get; set; } = DateTime.UtcNow.AddHours(24);
}