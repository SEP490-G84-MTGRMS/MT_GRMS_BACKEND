namespace MT_GRMS.Domain.Common;

/// <summary>
/// Thực thể cơ sở cho các bảng thuộc phạm vi một Tenant (Tenant-scoped Entities),
/// được tự động phân lập qua Global Query Filter và tự động gán thông tin Audit.
/// </summary>
public abstract class BaseTenantEntity<TKey> : BaseEntity<TKey>
{
    public string TenantId { get; set; } = string.Empty;
    public Guid? CreatedBy { get; set; }
}

public abstract class BaseTenantEntity : BaseTenantEntity<Guid>
{
    protected BaseTenantEntity()
    {
        Id = Guid.NewGuid();
    }
}
