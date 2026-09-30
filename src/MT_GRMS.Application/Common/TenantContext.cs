namespace MT_GRMS.Application.Common;

public class TenantContext : ITenantContext
{
    public string CurrentTenantId { get; private set; } = string.Empty;
    public Guid CurrentUserId { get; private set; } = Guid.Empty;
    public Guid? CurrentBranchId { get; private set; }

    public void SetContext(string tenantId, Guid userId, Guid? branchId)
    {
        CurrentTenantId = tenantId;
        CurrentUserId = userId;
        CurrentBranchId = branchId;
    }
}
