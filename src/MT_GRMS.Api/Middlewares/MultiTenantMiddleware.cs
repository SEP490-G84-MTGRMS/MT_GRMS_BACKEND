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
            var userName = context.User.FindFirst("fullName")?.Value
                ?? context.User.FindFirst("full_name")?.Value
                ?? context.User.FindFirst(ClaimTypes.Name)?.Value
                ?? context.User.FindFirst("name")?.Value
                ?? context.User.Identity?.Name;

            tenantContext.SetRawUserId(userIdClaim);
            tenantContext.SetUserName(userName);

            // SaaS Admin never has tenant context. X-Tenant-Id header must never establish tenant scope for Admin.
            var isSystemAdmin = string.Equals(role, "ADMIN", StringComparison.OrdinalIgnoreCase)
                || string.Equals(context.User.FindFirst("is_admin")?.Value, "true", StringComparison.OrdinalIgnoreCase);

            if (isSystemAdmin)
            {
                tenantId = string.Empty;
                branchId = null;
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
