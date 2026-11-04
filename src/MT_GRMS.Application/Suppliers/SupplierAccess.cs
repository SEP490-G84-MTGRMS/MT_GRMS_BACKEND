using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Entities.Supplier;
using MT_GRMS.Domain.Exceptions;

namespace MT_GRMS.Application.Suppliers;

internal static class SupplierAccess
{
    public static bool IsStoreOwner(ITenantContext tenantContext)
    {
        var role = (tenantContext.CurrentRole ?? string.Empty).Trim().ToUpperInvariant();
        return string.IsNullOrEmpty(role) || role is "STORE_OWNER" or "ADMIN";
    }

    public static void EnsureStoreOwner(ITenantContext tenantContext)
    {
        if (!IsStoreOwner(tenantContext))
        {
            throw new BusinessException("FORBIDDEN", "Chỉ Store Owner được thay đổi thông tin nhà cung cấp.");
        }
    }

    public static bool CanViewSupplier(ITenantContext tenantContext, Supplier supplier)
    {
        if (IsStoreOwner(tenantContext))
        {
            return true;
        }

        if (tenantContext.CurrentBranchId is not Guid branchId)
        {
            return false;
        }

        if (supplier.BranchAssignments == null || supplier.BranchAssignments.Count == 0)
        {
            return true;
        }

        return supplier.BranchAssignments.Any(a => a.BranchId == branchId);
    }
}
