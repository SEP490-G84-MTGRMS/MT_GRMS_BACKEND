using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using MT_GRMS.Api.Middlewares;
using MT_GRMS.Application.Common;

namespace MT_GRMS.UnitTests;

public class MultiTenantMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_WhenAnonymousHeadersSpoofTenantAndRole_DoesNotSetTenantContext()
    {
        var tenantContext = new TenantContext();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Tenant-Id"] = "tenant-spoofed";
        httpContext.Request.Headers["X-User-Role"] = "STORE_OWNER";

        var middleware = new MultiTenantMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(httpContext, tenantContext);

        Assert.Equal(string.Empty, tenantContext.CurrentTenantId);
        Assert.Equal(Guid.Empty, tenantContext.CurrentUserId);
        Assert.Equal(string.Empty, tenantContext.CurrentRole);
    }

    [Fact]
    public async Task InvokeAsync_WhenAuthenticated_BindsContextOnlyFromClaims()
    {
        var userId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var tenantContext = new TenantContext();
        var httpContext = CreateAuthenticatedContext(
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim("tenant_id", "tenant-claims"),
            new Claim("branch_id", branchId.ToString()),
            new Claim(ClaimTypes.Role, "STORE_OWNER"));
        httpContext.Request.Headers["X-Tenant-Id"] = "tenant-spoofed";
        httpContext.Request.Headers["X-User-Role"] = "ADMIN";

        var middleware = new MultiTenantMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(httpContext, tenantContext);

        Assert.Equal("tenant-claims", tenantContext.CurrentTenantId);
        Assert.Equal(userId, tenantContext.CurrentUserId);
        Assert.Equal(branchId, tenantContext.CurrentBranchId);
        Assert.Equal("STORE_OWNER", tenantContext.CurrentRole);
    }

    [Fact]
    public async Task InvokeAsync_WhenAdminClaimLacksSystemAdminFlag_IgnoresTenantSwitchHeader()
    {
        var tenantContext = new TenantContext();
        var httpContext = CreateAuthenticatedContext(
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim("tenant_id", "tenant-claims"),
            new Claim(ClaimTypes.Role, "ADMIN"));
        httpContext.Request.Headers["X-Tenant-Id"] = "tenant-other";

        var middleware = new MultiTenantMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(httpContext, tenantContext);

        Assert.Equal("tenant-claims", tenantContext.CurrentTenantId);
    }

    [Fact]
    public async Task InvokeAsync_WhenSystemAdminSwitchesTenant_UsesTenantSwitchHeader()
    {
        var tenantContext = new TenantContext();
        var httpContext = CreateAuthenticatedContext(
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim("tenant_id", "tenant-claims"),
            new Claim(ClaimTypes.Role, "ADMIN"),
            new Claim("is_admin", "true"));
        httpContext.Request.Headers["X-Tenant-Id"] = "tenant-other";

        var middleware = new MultiTenantMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(httpContext, tenantContext);

        Assert.Equal("tenant-other", tenantContext.CurrentTenantId);
        Assert.Equal("ADMIN", tenantContext.CurrentRole);
    }

    private static DefaultHttpContext CreateAuthenticatedContext(params Claim[] claims)
    {
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuthentication"));
        return context;
    }
}