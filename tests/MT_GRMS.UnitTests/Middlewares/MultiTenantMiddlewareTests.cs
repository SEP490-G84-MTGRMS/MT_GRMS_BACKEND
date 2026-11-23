using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using MT_GRMS.Api.Middlewares;
using MT_GRMS.Application.Common;
using Xunit;

namespace MT_GRMS.UnitTests.Middlewares;

public class MultiTenantMiddlewareTests
{
    private class FakeTenantContext : ITenantContext
    {
        public string CurrentTenantId { get; set; } = string.Empty;
        public Guid CurrentUserId { get; set; } = Guid.Empty;
        public Guid? CurrentBranchId { get; set; }
        public string CurrentRole { get; set; } = string.Empty;

        public void SetContext(string tenantId, Guid userId, Guid? branchId)
        {
            CurrentTenantId = tenantId;
            CurrentUserId = userId;
            CurrentBranchId = branchId;
        }

        public void SetRole(string? role)
        {
            CurrentRole = role ?? string.Empty;
        }
    }

    [Fact]
    public async Task AuthenticatedUser_SetsTenantContext()
    {
        var tenantContext = new FakeTenantContext();
        var nextCalled = false;
        var middleware = new MultiTenantMiddleware(ctx =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var httpContext = new DefaultHttpContext();
        var claims = new List<Claim>
        {
            new("tenant_id", "tenant-test-01"),
            new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new("branch_id", Guid.NewGuid().ToString()),
            new(ClaimTypes.Role, "STORE_OWNER")
        };
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));

        await middleware.InvokeAsync(httpContext, tenantContext);

        Assert.True(nextCalled);
        Assert.Equal("tenant-test-01", tenantContext.CurrentTenantId);
        Assert.Equal("STORE_OWNER", tenantContext.CurrentRole);
        Assert.NotNull(tenantContext.CurrentBranchId);
    }

    [Fact]
    public async Task AdminRole_DoesNotAcquireTenantOrBranchScope()
    {
        var tenantContext = new FakeTenantContext();
        var middleware = new MultiTenantMiddleware(_ => Task.CompletedTask);

        var httpContext = new DefaultHttpContext();
        var claims = new List<Claim>
        {
            new("tenant_id", "tenant-admin-orig"),
            new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new("branch_id", Guid.NewGuid().ToString()),
            new(ClaimTypes.Role, "ADMIN")
        };
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        httpContext.Request.Headers["X-Tenant-Id"] = "tenant-switched";

        await middleware.InvokeAsync(httpContext, tenantContext);

        Assert.Empty(tenantContext.CurrentTenantId);
        Assert.Null(tenantContext.CurrentBranchId);
        Assert.Equal("ADMIN", tenantContext.CurrentRole);
    }

    [Fact]
    public async Task NonAdminRole_CannotSwitchTenantViaHeader()
    {
        var tenantContext = new FakeTenantContext();
        var middleware = new MultiTenantMiddleware(_ => Task.CompletedTask);

        var httpContext = new DefaultHttpContext();
        var claims = new List<Claim>
        {
            new("tenant_id", "tenant-staff-orig"),
            new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new(ClaimTypes.Role, "STAFF")
        };
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        httpContext.Request.Headers["X-Tenant-Id"] = "tenant-hacked";

        await middleware.InvokeAsync(httpContext, tenantContext);

        Assert.Equal("tenant-staff-orig", tenantContext.CurrentTenantId);
    }

    [Fact]
    public async Task RegistrationEndpoint_SkipsFallbackTenant()
    {
        var tenantContext = new FakeTenantContext();
        var middleware = new MultiTenantMiddleware(_ => Task.CompletedTask);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/api/auth/register-tenant";

        await middleware.InvokeAsync(httpContext, tenantContext);

        Assert.Equal(string.Empty, tenantContext.CurrentTenantId);
    }

    [Fact]
    public async Task UnauthenticatedUser_CannotSetTenantOrRoleViaHeaders()
    {
        var tenantContext = new FakeTenantContext();
        var middleware = new MultiTenantMiddleware(_ => Task.CompletedTask);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Tenant-Id"] = "tenant_custom_fallback";
        httpContext.Request.Headers["X-User-Role"] = "CASHIER";

        await middleware.InvokeAsync(httpContext, tenantContext);

        Assert.Empty(tenantContext.CurrentTenantId);
        Assert.Empty(tenantContext.CurrentRole);
        Assert.Equal(Guid.Empty, tenantContext.CurrentUserId);
    }

    [Fact]
    public async Task DefaultTenantHeader_DoesNotGrantStoreOwnerScope()
    {
        var tenantContext = new FakeTenantContext();
        var middleware = new MultiTenantMiddleware(_ => Task.CompletedTask);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Tenant-Id"] = "DEFAULT";

        await middleware.InvokeAsync(httpContext, tenantContext);

        Assert.Empty(tenantContext.CurrentTenantId);
        Assert.Empty(tenantContext.CurrentRole);
    }
}
