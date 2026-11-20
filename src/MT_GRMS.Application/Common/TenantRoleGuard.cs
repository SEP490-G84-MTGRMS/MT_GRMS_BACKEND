using MT_GRMS.Domain.Constants;

namespace MT_GRMS.Application.Common;

public static class TenantRoleGuard
{
    public static void RequireStoreOwner(ITenantContext tenantContext, string area)
    {
        if (string.IsNullOrWhiteSpace(tenantContext.CurrentTenantId) ||
            tenantContext.CurrentRole != SystemRoles.StoreOwner)
        {
            throw new UnauthorizedAccessException($"Chỉ chủ cửa hàng được truy cập {area}.");
        }
    }
}
