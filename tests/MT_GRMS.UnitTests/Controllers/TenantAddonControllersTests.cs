using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MT_GRMS.Api.Controllers;
using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Infrastructure.Persistence;
using Xunit;

namespace MT_GRMS.UnitTests.Controllers;

public class TenantAddonControllersTests
{
    private class TestTenantContext : ITenantContext
    {
        public string CurrentTenantId { get; set; } = "tenant-01";
        public Guid CurrentUserId { get; set; } = Guid.NewGuid();
        public Guid? CurrentBranchId { get; set; } = null;
        public void SetContext(string tenantId, Guid userId, Guid? branchId)
        {
            CurrentTenantId = tenantId;
            CurrentUserId = userId;
            CurrentBranchId = branchId;
        }
        public string CurrentRole { get; set; } = "STORE_OWNER";
        public void SetRole(string? role) => CurrentRole = role ?? string.Empty;
    }

    private AppDbContext CreateDbContext()
    {
        var tenant = new TestTenantContext();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"TenantAddons_{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new AppDbContext(options, tenant);
    }

    private static ControllerContext CreateContextWithTenant(string? tenantId)
    {
        var claims = tenantId != null
            ? new[] { new Claim("tenant_id", tenantId) }
            : Array.Empty<Claim>();

        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));

        return new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };
    }

    [Fact]
    public async Task BranchAddon_Quote_ShouldHandleVariousCases()
    {
        using var context = CreateDbContext();
        var tenant = new Tenant
        {
            Id = "tenant-01",
            Name = "Shop 1",
            CurrentPlan = "PRO",
            SubscriptionStatus = "ACTIVE",
            SubscriptionExpiresAt = DateTime.UtcNow.AddMonths(1)
        };
        context.Tenants.Add(tenant);
        await context.SaveChangesAsync();

        var controller = new TenantBranchAddonController(context)
        {
            ControllerContext = CreateContextWithTenant("tenant-01")
        };

        // 1. Extra branches out of range (< 1 or > 100)
        var badResult1 = await controller.Quote(0, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(badResult1);

        var badResult2 = await controller.Quote(101, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(badResult2);

        // 2. Empty Tenant Claim
        controller.ControllerContext = CreateContextWithTenant(null);
        var noTenantResult = await controller.Quote(2, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(noTenantResult);

        // 3. Valid Quote with PRO plan
        controller.ControllerContext = CreateContextWithTenant("tenant-01");
        var okResult = await controller.Quote(2, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(okResult);
        Assert.NotNull(ok.Value);
    }

    [Fact]
    public async Task BranchAddon_Purchase_ShouldHandleAllScenarios()
    {
        using var context = CreateDbContext();
        var tenant = new Tenant
        {
            Id = "tenant-01",
            Name = "Shop 1",
            CurrentPlan = "PRO",
            SubscriptionStatus = "ACTIVE",
            SubscriptionExpiresAt = DateTime.UtcNow.AddMonths(1),
            WalletBalance = 500000m
        };
        context.Tenants.Add(tenant);
        await context.SaveChangesAsync();

        var controller = new TenantBranchAddonController(context)
        {
            ControllerContext = CreateContextWithTenant("tenant-01")
        };

        // 1. Invalid request
        var bad1 = await controller.Purchase(new BranchAddonPurchaseRequest(0, Guid.NewGuid()), CancellationToken.None);
        Assert.IsType<BadRequestResult>(bad1);

        var bad2 = await controller.Purchase(new BranchAddonPurchaseRequest(2, Guid.Empty), CancellationToken.None);
        Assert.IsType<BadRequestResult>(bad2);

        // 2. Insufficient balance
        tenant.WalletBalance = 0m;
        await context.SaveChangesAsync();
        var badBalance = await controller.Purchase(new BranchAddonPurchaseRequest(2, Guid.NewGuid()), CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(badBalance);

        // 3. Successful purchase
        tenant.WalletBalance = 5000000m;
        await context.SaveChangesAsync();
        var reqId = Guid.NewGuid();
        var successResult = await controller.Purchase(new BranchAddonPurchaseRequest(2, reqId), CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(successResult);
        Assert.NotNull(ok.Value);

        // 4. Idempotent replay
        var replayResult = await controller.Purchase(new BranchAddonPurchaseRequest(2, reqId), CancellationToken.None);
        var replayOk = Assert.IsType<OkObjectResult>(replayResult);
        Assert.NotNull(replayOk.Value);
    }

    [Fact]
    public async Task StorageController_Get_ShouldHandleCases()
    {
        using var context = CreateDbContext();
        var tenant = new Tenant
        {
            Id = "tenant-01",
            Name = "Shop 1",
            CurrentPlan = "STANDARD",
            SubscriptionStatus = "ACTIVE",
            SubscriptionExpiresAt = DateTime.UtcNow.AddMonths(1),
            ExtraStorageGb = 5,
            StorageAddonExpiresAt = DateTime.UtcNow.AddMonths(1),
            StorageUsedBytes = 1024
        };
        context.Tenants.Add(tenant);
        await context.SaveChangesAsync();

        var controller = new TenantStorageController(context)
        {
            ControllerContext = CreateContextWithTenant("tenant-01")
        };

        // 1. Success
        var result = await controller.Get(CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);

        // 2. No tenant claim -> Unauthorized
        controller.ControllerContext = CreateContextWithTenant(null);
        var unauth = await controller.Get(CancellationToken.None);
        Assert.IsType<UnauthorizedResult>(unauth);

        // 3. Tenant not found in db -> NotFound
        controller.ControllerContext = CreateContextWithTenant("non-existent-tenant");
        var notFound = await controller.Get(CancellationToken.None);
        Assert.IsType<NotFoundResult>(notFound);
    }

    [Fact]
    public async Task StorageController_Quote_And_Purchase_ShouldHandleAllScenarios()
    {
        using var context = CreateDbContext();
        var tenant = new Tenant
        {
            Id = "tenant-01",
            Name = "Shop 1",
            CurrentPlan = "STANDARD",
            SubscriptionStatus = "ACTIVE",
            SubscriptionExpiresAt = DateTime.UtcNow.AddMonths(1),
            WalletBalance = 5000000m
        };
        context.Tenants.Add(tenant);
        await context.SaveChangesAsync();

        var controller = new TenantStorageController(context)
        {
            ControllerContext = CreateContextWithTenant("tenant-01")
        };

        // 1. Quote out of range
        var badQuote = await controller.Quote(0, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(badQuote);

        // 2. Valid quote
        var validQuote = await controller.Quote(10, CancellationToken.None);
        var okQuote = Assert.IsType<OkObjectResult>(validQuote);
        Assert.NotNull(okQuote.Value);

        // 3. Purchase invalid arguments
        var badPurchase1 = await controller.Purchase(new StoragePurchaseRequest(0, Guid.NewGuid()), CancellationToken.None);
        Assert.IsType<BadRequestResult>(badPurchase1);

        var badPurchase2 = await controller.Purchase(new StoragePurchaseRequest(10, Guid.Empty), CancellationToken.None);
        Assert.IsType<BadRequestResult>(badPurchase2);

        // 4. Insufficient wallet balance
        tenant.WalletBalance = 0m;
        await context.SaveChangesAsync();
        var badBal = await controller.Purchase(new StoragePurchaseRequest(10, Guid.NewGuid()), CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(badBal);

        // 5. Successful purchase
        tenant.WalletBalance = 5000000m;
        await context.SaveChangesAsync();
        var reqId = Guid.NewGuid();
        var purchaseOk = await controller.Purchase(new StoragePurchaseRequest(10, reqId), CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(purchaseOk);
        Assert.NotNull(ok.Value);

        // 6. Idempotent replay
        var replay = await controller.Purchase(new StoragePurchaseRequest(10, reqId), CancellationToken.None);
        var replayOk = Assert.IsType<OkObjectResult>(replay);
        Assert.NotNull(replayOk.Value);
    }
}
