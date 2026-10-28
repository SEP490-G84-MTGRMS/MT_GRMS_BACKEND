namespace MT_GRMS.Application.Common;

public interface ITenantContext
{
    string CurrentTenantId { get; }
    Guid CurrentUserId { get; }
    Guid? CurrentBranchId { get; }
    string CurrentRole { get; }
    void SetContext(string tenantId, Guid userId, Guid? branchId);
    void SetRole(string? role);
}
