using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MT_GRMS.Api.Middlewares;
using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Constants;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Infrastructure.Persistence;
using Xunit;

namespace MT_GRMS.UnitTests.Middlewares;

public class SubscriptionAccessMiddlewareTests
{
    private class FakeTenantContext : ITenantContext
    {
        public string CurrentTenantId { get; set; } = "tenant-01";
        public Guid CurrentUserId { get; set; } = Guid.NewGuid();
        public Guid? CurrentBranchId { get; set; }
        public string CurrentRole { get; set; } = "STORE_OWNER";

        public void SetContext(string tenantId, Guid userId, Guid? branchId)
        {
            CurrentTenantId = tenantId;
            CurrentUserId = userId;
            CurrentBranchId = branchId;
        }

        public void SetRole(string? role) => CurrentRole = role ?? string.Empty;
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"SubAccessDb_{Guid.NewGuid()}")
            .Options;
        return new AppDbContext(options, new FakeTenantContext());
    }

    private static DefaultHttpContext CreateHttpContext(string? tenantId = "tenant-01", bool authenticated = true, string method = "GET", string path = "/api/orders")
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddProblemDetails();
        context.RequestServices = services.BuildServiceProvider();

        if (authenticated)
        {
            var claims = new List<Claim>();
            if (tenantId != null)
            {
                claims.Add(new Claim("tenant_id", tenantId));
            }
            context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        }

        return context;
    }

    [Theory]
    [InlineData("OPTIONS", "/api/orders", true)]
    [InlineData("GET", "/api/auth/login", true)]
    [InlineData("GET", "/api/tenant/pricing-plans", true)]
    [InlineData("POST", "/api/tenant/subscriptions/checkout", true)]
    [InlineData("POST", "/api/webhooks/sepay", true)]
    public async Task AllowedRequests_BypassMiddleware(string method, string path, bool authenticated)
    {
        using var db = CreateDbContext();
        var nextCalled = false;
        var middleware = new SubscriptionAccessMiddleware(ctx =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var context = CreateHttpContext("tenant-01", authenticated, method, path);
        await middleware.InvokeAsync(context, db);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task StaffManagementEndpoint_UsesCurrentAddonState_AndKeepsOtherEndpointsAvailable()
    {
        using var db = CreateDbContext();
        var tenant = new Tenant
        {
            Id = "tenant-01", CurrentPlan = "STANDARD", SubscriptionStatus = "ACTIVE",
            SubscriptionExpiresAt = DateTime.UtcNow.AddDays(10)
        };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        var calls = 0;
        var middleware = new SubscriptionAccessMiddleware(_ =>
        {
            calls++;
            return Task.CompletedTask;
        });
        var context = CreateHttpContext(path: "/api/employees");
        context.User.AddIdentity(new ClaimsIdentity([new Claim(ClaimTypes.Role, SystemRoles.StoreOwner)]));
        using var body = new MemoryStream();
        context.Response.Body = body;
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask,
            new EndpointMetadataCollection(new RequiresStaffManagementAttribute()), "Employees"));

        await middleware.InvokeAsync(context, db);

        Assert.Equal(403, context.Response.StatusCode);
        body.Position = 0;
        using var reader = new StreamReader(body);
        Assert.Contains("STAFF_MANAGEMENT_ADDON_REQUIRED", await reader.ReadToEndAsync());
        Assert.Equal(0, calls);

        context.SetEndpoint(null);
        await middleware.InvokeAsync(context, db);
        Assert.Equal(1, calls);

        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask,
            new EndpointMetadataCollection(new RequiresStaffManagementAttribute()), "Employees"));
        tenant.HasStaffManagementAddon = true;
        await db.SaveChangesAsync();
        await middleware.InvokeAsync(context, db);
        Assert.Equal(2, calls);

        tenant.CurrentPlan = "PRO";
        tenant.HasStaffManagementAddon = false;
        await db.SaveChangesAsync();
        await middleware.InvokeAsync(context, db);
        Assert.Equal(3, calls);

        tenant.CurrentPlan = "TRIAL";
        tenant.SubscriptionStatus = "TRIAL";
        await db.SaveChangesAsync();
        await middleware.InvokeAsync(context, db);
        Assert.Equal(4, calls);
    }

    [Fact]
    public async Task Unauthenticated_BypassesMiddleware()
    {
        using var db = CreateDbContext();
        var nextCalled = false;
        var middleware = new SubscriptionAccessMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var context = CreateHttpContext(null, authenticated: false);
        await middleware.InvokeAsync(context, db);

        Assert.True(nextCalled);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("DEFAULT")]
    public async Task DefaultOrNullTenant_BypassesMiddleware(string? tenantId)
    {
        using var db = CreateDbContext();
        var nextCalled = false;
        var middleware = new SubscriptionAccessMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var context = CreateHttpContext(tenantId, authenticated: true);
        await middleware.InvokeAsync(context, db);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task TenantNotFound_ReturnsForbidden()
    {
        using var db = CreateDbContext();
        var nextCalled = false;
        var middleware = new SubscriptionAccessMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var context = CreateHttpContext("tenant-unknown", authenticated: true);
        await middleware.InvokeAsync(context, db);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Theory]
    [InlineData("NO_PLAN")]
    [InlineData("REVIEW_REQUIRED")]
    [InlineData("EXPIRED")]
    public async Task BlockedSubscriptionStatuses_ReturnForbidden(string status)
    {
        using var db = CreateDbContext();
        db.Tenants.Add(new Tenant
        {
            Id = "tenant-blocked",
            Name = "Blocked Store",
            SubscriptionStatus = status
        });
        await db.SaveChangesAsync();

        var nextCalled = false;
        var middleware = new SubscriptionAccessMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var context = CreateHttpContext("tenant-blocked", authenticated: true);
        await middleware.InvokeAsync(context, db);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Fact]
    public async Task ExpiredTrial_ReturnsForbidden_ActiveTrial_Allows()
    {
        using var db = CreateDbContext();
        db.Tenants.AddRange(
            new Tenant
            {
                Id = "tenant-trial-expired",
                Name = "Expired Trial",
                SubscriptionStatus = "TRIAL",
                TrialExpiresAt = DateTime.UtcNow.AddMinutes(-10)
            },
            new Tenant
            {
                Id = "tenant-trial-valid",
                Name = "Valid Trial",
                SubscriptionStatus = "TRIAL",
                TrialExpiresAt = DateTime.UtcNow.AddDays(7)
            }
        );
        await db.SaveChangesAsync();

        var middleware = new SubscriptionAccessMiddleware(_ => Task.CompletedTask);

        // Expired trial -> 403
        var expiredCtx = CreateHttpContext("tenant-trial-expired");
        await middleware.InvokeAsync(expiredCtx, db);
        Assert.Equal(StatusCodes.Status403Forbidden, expiredCtx.Response.StatusCode);

        // Valid trial -> next called
        var validNext = false;
        var validMiddleware = new SubscriptionAccessMiddleware(_ =>
        {
            validNext = true;
            return Task.CompletedTask;
        });
        var validCtx = CreateHttpContext("tenant-trial-valid");
        await validMiddleware.InvokeAsync(validCtx, db);
        Assert.True(validNext);
    }

    [Fact]
    public async Task ExpiredActive_ReturnsForbidden_ValidActive_Allows()
    {
        using var db = CreateDbContext();
        db.Tenants.AddRange(
            new Tenant
            {
                Id = "tenant-active-expired",
                Name = "Expired Active",
                SubscriptionStatus = "ACTIVE",
                SubscriptionExpiresAt = DateTime.UtcNow.AddMinutes(-5)
            },
            new Tenant
            {
                Id = "tenant-active-valid",
                Name = "Valid Active",
                SubscriptionStatus = "ACTIVE",
                SubscriptionExpiresAt = DateTime.UtcNow.AddMonths(1)
            }
        );
        await db.SaveChangesAsync();

        var middleware = new SubscriptionAccessMiddleware(_ => Task.CompletedTask);

        // Expired active -> 403
        var expiredCtx = CreateHttpContext("tenant-active-expired");
        await middleware.InvokeAsync(expiredCtx, db);
        Assert.Equal(StatusCodes.Status403Forbidden, expiredCtx.Response.StatusCode);

        // Valid active -> next called
        var validNext = false;
        var validMiddleware = new SubscriptionAccessMiddleware(_ =>
        {
            validNext = true;
            return Task.CompletedTask;
        });
        var validCtx = CreateHttpContext("tenant-active-valid");
        await validMiddleware.InvokeAsync(validCtx, db);
        Assert.True(validNext);
    }
}
