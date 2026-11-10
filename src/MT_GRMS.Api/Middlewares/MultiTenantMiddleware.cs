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
            tenantContext.SetContext(tenantId ?? string.Empty, userId, branchId);
            tenantContext.SetRole(role);

            await _next(context);
            return;
        }

        // Hỗ trợ header X-Tenant-Id hoặc fallback tenant cho môi trường dev/standalone
        if (string.IsNullOrWhiteSpace(tenantContext.CurrentTenantId) ||
            tenantContext.CurrentTenantId == "default-tenant" ||
            tenantContext.CurrentTenantId == "DEFAULT" ||
            tenantContext.CurrentTenantId == "TENANT-HCM-01")
        {
            var headerTenantId = context.Request.Headers["X-Tenant-Id"].FirstOrDefault();
            var fallbackTenant = !string.IsNullOrWhiteSpace(headerTenantId) ? headerTenantId : "tenant_mt_mart_01";
            if (fallbackTenant is "default-tenant" or "DEFAULT" or "TENANT-HCM-01")
            {
                fallbackTenant = "tenant_mt_mart_01";
            }
            tenantContext.SetContext(fallbackTenant, Guid.Empty, null);

            var headerRole = context.Request.Headers["X-User-Role"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(headerRole))
            {
                tenantContext.SetRole(headerRole);
            }
            else if (string.IsNullOrWhiteSpace(tenantContext.CurrentRole))
            {
                tenantContext.SetRole("STORE_OWNER");
            }
        }

        await _next(context);
    }
}
