namespace MT_GRMS.Application.Common;

public interface ITenantContext
{
    string CurrentTenantId { get; }
    Guid CurrentUserId { get; }
    Guid? CurrentBranchId { get; }
    void SetContext(string tenantId, Guid userId, Guid? branchId);
}
