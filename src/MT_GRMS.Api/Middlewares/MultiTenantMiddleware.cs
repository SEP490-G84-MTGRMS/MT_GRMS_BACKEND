using System.Security.Claims;
using MT_GRMS.Application.Common;

namespace MT_GRMS.Api.Middlewares;

public class MultiTenantMiddleware
{
    private readonly RequestDelegate _next;

    public MultiTenantMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ITenantContext tenantContext)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var tenantId = context.User.FindFirst("tenant_id")?.Value;
            var userIdClaim = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var branchIdClaim = context.User.FindFirst("branch_id")?.Value;

            var userId = Guid.TryParse(userIdClaim, out var parsedUserId) ? parsedUserId : Guid.Empty;
            Guid? branchId = Guid.TryParse(branchIdClaim, out var parsedBranchId) ? parsedBranchId : null;

            if (!string.IsNullOrWhiteSpace(tenantId))
            {
                tenantContext.SetContext(tenantId, userId, branchId);
                var role = context.User.FindFirst(ClaimTypes.Role)?.Value
                    ?? context.User.FindFirst("role")?.Value
                    ?? context.User.FindFirst("RoleName")?.Value;
                tenantContext.SetRole(role);
            }
        }

        // Header X-Tenant-Id hoặc tenant seed local (tenant_mt_mart_01)
        if (string.IsNullOrWhiteSpace(tenantContext.CurrentTenantId)
            || tenantContext.CurrentTenantId is "DEFAULT" or "default-tenant")
        {
            var headerTenantId = context.Request.Headers["X-Tenant-Id"].FirstOrDefault();
            var fallbackTenant = !string.IsNullOrWhiteSpace(headerTenantId) && headerTenantId is not ("DEFAULT" or "default-tenant")
                ? headerTenantId
                : "tenant_mt_mart_01";
            tenantContext.SetContext(
                fallbackTenant,
                tenantContext.CurrentUserId == Guid.Empty ? Guid.Empty : tenantContext.CurrentUserId,
                tenantContext.CurrentBranchId);
        }

        await _next(context);
    }
}
