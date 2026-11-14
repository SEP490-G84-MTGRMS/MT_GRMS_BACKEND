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
            var userIdClaim = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? context.User.FindFirst("sub")?.Value;
            var branchIdClaim = context.User.FindFirst("branch_id")?.Value;

            var userId = TenantContext.ResolveDeterministicGuid(userIdClaim);
            Guid? branchId = Guid.TryParse(branchIdClaim, out var parsedBranchId) ? parsedBranchId : null;
            var role = context.User.FindFirst(ClaimTypes.Role)?.Value
                ?? context.User.FindFirst("role")?.Value
                ?? context.User.FindFirst("RoleName")?.Value;

            tenantContext.SetRawUserId(userIdClaim);

            // Tenant switching is reserved for a token explicitly issued to a system admin.
            // A role/header supplied by the client must never establish or replace tenant scope.
            var isSystemAdmin = string.Equals(role, "ADMIN", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(context.User.FindFirst("is_admin")?.Value, "true", StringComparison.OrdinalIgnoreCase);
            if (isSystemAdmin)
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
            else
            {
                tenantContext.SetContext(string.Empty, userId, branchId);
                tenantContext.SetRole(role);
            }
        }

        await _next(context);
    }
}
