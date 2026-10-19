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
            }
        }

        // Hỗ trợ header X-Tenant-Id hoặc fallback tenant cho môi trường dev/standalone
        if (string.IsNullOrWhiteSpace(tenantContext.CurrentTenantId))
        {
            var headerTenantId = context.Request.Headers["X-Tenant-Id"].FirstOrDefault();
            var fallbackTenant = !string.IsNullOrWhiteSpace(headerTenantId) ? headerTenantId : "default-tenant";
            tenantContext.SetContext(fallbackTenant, Guid.Empty, null);
        }

        await _next(context);
    }
}
