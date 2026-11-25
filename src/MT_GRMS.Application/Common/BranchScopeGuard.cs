using System;
using MT_GRMS.Domain.Exceptions;

namespace MT_GRMS.Application.Common;

/// <summary>
/// Chuẩn hóa phân quyền phạm vi chi nhánh (RBAC):
/// - StoreOwner / Admin: xem toàn bộ chi nhánh (branchId = null) hoặc lọc theo chi nhánh bất kỳ.
/// - Employee / Staff / Cashier: chỉ được phép xem và thao tác dữ liệu thuộc đúng chi nhánh được phân công.
/// </summary>
public static class BranchScopeGuard
{
    public static bool IsBranchScopedEmployee(ITenantContext tenantContext)
    {
        var role = (tenantContext.CurrentRole ?? string.Empty)
            .Trim()
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToUpperInvariant();

        return role is "EMPLOYEE" or "STAFF" or "CASHIER";
    }

    public static Guid? ResolveEffectiveBranchId(ITenantContext tenantContext, Guid? requestedBranchId)
    {
        if (!IsBranchScopedEmployee(tenantContext))
        {
            return requestedBranchId.HasValue && requestedBranchId.Value != Guid.Empty
                ? requestedBranchId
                : null;
        }

        var assignedBranchId = RequireAssignedBranch(tenantContext);
        if (requestedBranchId.HasValue &&
            requestedBranchId.Value != Guid.Empty &&
            requestedBranchId.Value != assignedBranchId)
        {
            throw new BusinessException(
                "FORBIDDEN",
                "Nhân viên chỉ được phép xem và thao tác dữ liệu thuộc chi nhánh được phân công.");
        }

        return assignedBranchId;
    }

    public static Guid ResolveRequiredBranchId(ITenantContext tenantContext, Guid requestedBranchId)
    {
        if (!IsBranchScopedEmployee(tenantContext))
        {
            return requestedBranchId;
        }

        var assignedBranchId = RequireAssignedBranch(tenantContext);
        if (requestedBranchId != Guid.Empty && requestedBranchId != assignedBranchId)
        {
            throw new BusinessException(
                "FORBIDDEN",
                "Nhân viên chỉ được phép xem và thao tác dữ liệu thuộc chi nhánh được phân công.");
        }

        return assignedBranchId;
    }

    public static void EnsureCanAccessBranch(ITenantContext tenantContext, Guid resourceBranchId)
    {
        if (!IsBranchScopedEmployee(tenantContext))
        {
            return;
        }

        var assignedBranchId = RequireAssignedBranch(tenantContext);
        if (resourceBranchId != assignedBranchId)
        {
            throw new BusinessException(
                "FORBIDDEN",
                "Nhân viên chỉ được phép xem và thao tác dữ liệu thuộc chi nhánh được phân công.");
        }
    }

    private static Guid RequireAssignedBranch(ITenantContext tenantContext)
    {
        if (!tenantContext.CurrentBranchId.HasValue || tenantContext.CurrentBranchId.Value == Guid.Empty)
        {
            throw new BusinessException(
                "FORBIDDEN",
                "Tài khoản nhân viên chưa được gán chi nhánh.");
        }

        return tenantContext.CurrentBranchId.Value;
    }
}

