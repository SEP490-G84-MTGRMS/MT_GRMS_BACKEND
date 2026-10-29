namespace MT_GRMS.Domain.Common;

/// <summary>
/// Thực thể cơ sở cho các bảng toàn cục/nền tảng hệ thống (Global Root Entities),
/// không thuộc phạm vi sở hữu của riêng một Tenant nào (ví dụ: SubscriptionPlan, Tenant, User).
/// </summary>
public abstract class BaseEntity<TKey>
{
    public TKey Id { get; set; } = default!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public bool IsDeleted { get; set; } = false;
}

public abstract class BaseEntity : BaseEntity<Guid>
{
    protected BaseEntity()
    {
        Id = Uuid7.NewGuid();
    }
}
