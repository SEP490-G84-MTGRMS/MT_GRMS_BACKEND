namespace MT_GRMS.Application.Common;

public interface ITenantContext
{
    string CurrentTenantId { get; }
    Guid CurrentUserId { get; }
    string CurrentRawUserId => CurrentUserId != Guid.Empty ? CurrentUserId.ToString() : string.Empty;
    Guid? CurrentBranchId { get; }
    string CurrentRole { get; }
    void SetContext(string tenantId, Guid userId, Guid? branchId);
    void SetRawUserId(string? rawUserId) { }
    void SetRole(string? role);
}
