using MT_GRMS.Domain.Common;

namespace MT_GRMS.Domain.Entities.SaaS;

public class TenantMedia : BaseEntity<string>
{
    public TenantMedia() => Id = Uuid7.NewString();
    public string TenantId { get; set; } = string.Empty;
    public string PublicId { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public long Bytes { get; set; }
}
