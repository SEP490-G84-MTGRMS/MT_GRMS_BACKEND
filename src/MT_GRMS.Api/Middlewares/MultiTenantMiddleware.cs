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

            var role = context.User.FindFirst(ClaimTypes.Role)?.Value
                ?? context.User.FindFirst("role")?.Value
                ?? context.User.FindFirst("RoleName")?.Value;

            // INV-1 & TEN-05: Chỉ System Admin mới có quyền switch tenant qua header X-Tenant-Id
            if (string.Equals(role, "ADMIN", StringComparison.OrdinalIgnoreCase))
            {
                var headerTenantId = context.Request.Headers["X-Tenant-Id"].FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(headerTenantId))
                {
                    tenantId = headerTenantId;
                }
            }

            if (!string.IsNullOrWhiteSpace(tenantId))
            {
                tenantContext.SetContext(tenantId, userId, branchId);
                tenantContext.SetRole(role);
            }
        }

        await _next(context);
    }
}
