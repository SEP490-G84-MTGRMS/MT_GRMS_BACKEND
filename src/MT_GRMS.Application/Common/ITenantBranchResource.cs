namespace MT_GRMS.Application.Common;

/// <summary>
/// Interface đại diện cho tài nguyên hoặc phạm vi gắn liền với Tenant và Branch.
/// </summary>
public interface ITenantBranchResource
{
    string TenantId { get; }
    Guid? BranchId { get; }
}

/// <summary>
/// Đại diện cho phạm vi ủy quyền (Scope) trước khi truy vấn hoặc ghi dữ liệu.
/// </summary>
public class TenantBranchScope : ITenantBranchResource
{
    public string TenantId { get; set; }
    public Guid? BranchId { get; set; }

    public TenantBranchScope(string tenantId, Guid? branchId)
    {
        TenantId = tenantId ?? string.Empty;
        BranchId = branchId;
    }
}
