using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MT_GRMS.Api.Controllers;
using MT_GRMS.Api.Filters;
using MT_GRMS.Api.Middlewares;
using MT_GRMS.API.Controllers;
using MT_GRMS.Application.Admin.Dtos;
using MT_GRMS.Application.Admin.Services;
using MT_GRMS.Application.Auth;
using MT_GRMS.Application.Auth.Dtos;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Common.Interfaces;
using MT_GRMS.Domain.Entities.Identity;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Domain.Entities.System;
using MT_GRMS.Infrastructure.Persistence;
using MT_GRMS.Infrastructure.Repositories;
using MT_GRMS.Infrastructure.Services;
using Xunit;

namespace MT_GRMS.IntegrationTests;

public class TrialOnboardingIntegrationTests
{
    private sealed class TestTenantContext : ITenantContext
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
        public void SetRole(string? role) => CurrentRole = role ?? string.Empty;
    }

    private sealed class FailingRegisterUserRepository(IUserRepository inner) : IUserRepository
    {
        public Task<User?> GetByIdAsync(string id, CancellationToken cancellationToken = default) => inner.GetByIdAsync(id, cancellationToken);
        public Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default) => inner.GetByUsernameAsync(username, cancellationToken);
        public Task<IReadOnlyList<User>> FindAsync(System.Linq.Expressions.Expression<Func<User, bool>> predicate, CancellationToken cancellationToken = default) => inner.FindAsync(predicate, cancellationToken);
        public Task<IReadOnlyList<User>> GetAllAsync(CancellationToken cancellationToken = default) => inner.GetAllAsync(cancellationToken);
        public Task AddAsync(User user, CancellationToken cancellationToken = default) => inner.AddAsync(user, cancellationToken);
        public void Update(User user) => inner.Update(user);
        public void Delete(User user) => inner.Delete(user);
        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => inner.SaveChangesAsync(cancellationToken);
        public Task<bool> ExistsByPhoneOrEmailAsync(string phone, string? email, CancellationToken cancellationToken = default) => inner.ExistsByPhoneOrEmailAsync(phone, email, cancellationToken);
        public Task<User> RegisterStoreOwnerAsync(User user, Tenant tenant, Branch branch, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Simulated DB failure");
        public Task<User> RegisterStoreOwnerAsync(User user, Tenant tenant, Branch branch, AuditLog? auditLog, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Simulated DB failure");
    }

    private sealed class FakeEmailService : IEmailService
    {
        public Task SendPasswordResetOtpAsync(string toEmail, string fullName, string otpCode, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> SendLoginOtpAsync(string toEmail, string fullName, string otpCode, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> SendRegistrationOtpAsync(string toEmail, string fullName, string storeName, string otpCode, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private sealed class StubTenantService(AppDbContext db) : ITenantService
    {
        public Task<PagedResult<TenantSummaryResponse>> GetTenantsAsync(TenantFilterRequest filter, CancellationToken cancellationToken = default)
            => Task.FromResult(new PagedResult<TenantSummaryResponse>());
        public async Task<TenantDetailResponse> GetByIdAsync(string tenantId, CancellationToken cancellationToken = default)
        {
            var t = await db.Tenants.AsNoTracking().FirstAsync(x => x.Id == tenantId, cancellationToken);
            return new TenantDetailResponse(
                t.Id, t.Subdomain, t.Name, t.Code, t.TaxCode, t.Hotline, t.Address, t.City,
                t.Status, t.CurrentPlan, t.CurrentPlan ?? "Chưa chọn gói", t.SubscriptionExpiresAt,
                null, null, null, null, null, 1, 1, 1, 1,
                new List<TenantDetailBranchSummaryDto>(), t.CreatedAt, t.UpdatedAt, t.WalletBalance);
        }
        public Task<TenantDetailResponse> UpdateTenantAsync(string id, UpdateTenantRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task LockTenantAsync(string id, LockTenantRequest request, string lockedBy, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task UnlockTenantAsync(string id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task ExtendGracePeriodAsync(string id, ExtendGraceRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private sealed class StubPlanService : ISubscriptionPlanService
    {
        public Task<IReadOnlyList<PlanSummaryResponse>> GetPlansAsync(PlanFilterRequest filter, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<PlanSummaryResponse>>(Array.Empty<PlanSummaryResponse>());
        public Task<PlanKpiBannerResponse> GetKpisAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<PlanDetailResponse> GetByIdAsync(string planId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<PlanDetailResponse> CreatePlanAsync(CreatePlanRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<PlanDetailResponse> UpdatePlanAsync(string planId, UpdatePlanRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> ToggleStatusAsync(string planId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task DeletePlanAsync(string planId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private static AppDbContext CreateDbContext(string dbName, ITenantContext? tenantContext = null)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return tenantContext == null ? new AppDbContext(options) : new AppDbContext(options, tenantContext);
    }

    private static ClaimsPrincipal CreateOwnerPrincipal(string userId, string tenantId) =>
        new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim("tenant_id", tenantId),
            new Claim(ClaimTypes.Role, "STORE_OWNER")
        ], "test_auth"));

    [Fact]
    public async Task Register_WhenDbRollbackOccurs_ReleasesOtpReservationForSafeRetry_AndSucceeds()
    {
        var dbName = Guid.NewGuid().ToString();
        using var db = CreateDbContext(dbName);
        var otpStore = new RegistrationOtpStore(db);
        var devConfig = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Development"
        }).Build();

        var realRepo = new UserRepository(db);
        var failingRepo = new FailingRegisterUserRepository(realRepo);
        var failingAuth = new AuthService(failingRepo, devConfig, registrationOtpStore: otpStore);

        var otp = await failingAuth.SendRegistrationOtpAsync(new SendRegistrationOtpRequest(
            "0981112233", "Tạp Hóa Retry", "Chủ Retry", "retry-owner@mtgrms.vn", StartTrial: true));

        var req = new RegisterTenantRequest(
            "Chủ Retry", "Tạp Hóa Retry", "0981112233", "retry-owner@mtgrms.vn",
            "Password@123", true, otp.DevOtpCode, StartTrial: true,
            IdempotencyKey: "idem-retry-1", ChallengeId: otp.ChallengeId);

        await Assert.ThrowsAsync<InvalidOperationException>(() => failingAuth.RegisterTenantAsync(req));

        var afterRollback = await otpStore.GetAsync(otp.ChallengeId, "retry-owner@mtgrms.vn", CancellationToken.None);
        Assert.NotNull(afterRollback);
        Assert.Equal(RegistrationOtpStates.Pending, afterRollback!.State);

        var realAuth = new AuthService(realRepo, devConfig, registrationOtpStore: otpStore);
        var success = await realAuth.RegisterTenantAsync(req);

        Assert.NotNull(success.User);
        var afterSuccess = await otpStore.GetAsync(otp.ChallengeId, "retry-owner@mtgrms.vn", CancellationToken.None);
        Assert.NotNull(afterSuccess);
        Assert.Equal(RegistrationOtpStates.Consumed, afterSuccess!.State);

        var tenant = await db.Tenants.SingleAsync();
        Assert.Equal("TRIAL", tenant.SubscriptionStatus);
        var audit = await db.AuditLogs.SingleAsync(a => a.ActionType == "TRIAL_ACTIVATED");
        Assert.Equal(tenant.Id, audit.TenantId);
    }

    [Fact]
    public async Task Register_TwoInstancesConcurrent_SameChallenge_CreatesOnlyOneTenant()
    {
        var dbName = Guid.NewGuid().ToString();
        using var seedDb = CreateDbContext(dbName);
        var seedOtpStore = new RegistrationOtpStore(seedDb);
        var devConfig = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Development"
        }).Build();
        var seederAuth = new AuthService(new UserRepository(seedDb), devConfig, registrationOtpStore: seedOtpStore);
        var otp = await seederAuth.SendRegistrationOtpAsync(new SendRegistrationOtpRequest(
            "0982223344", "Tạp Hóa Song Song", "Chủ Song Song", "parallel@mtgrms.vn", StartTrial: false));

        var req = new RegisterTenantRequest(
            "Chủ Song Song", "Tạp Hóa Song Song", "0982223344", "parallel@mtgrms.vn",
            "Password@123", true, otp.DevOtpCode, StartTrial: false,
            IdempotencyKey: "idem-parallel-1", ChallengeId: otp.ChallengeId);

        using var db1 = CreateDbContext(dbName);
        using var db2 = CreateDbContext(dbName);
        var auth1 = new AuthService(new UserRepository(db1), devConfig, registrationOtpStore: new RegistrationOtpStore(db1));
        var auth2 = new AuthService(new UserRepository(db2), devConfig, registrationOtpStore: new RegistrationOtpStore(db2));

        var task1 = auth1.RegisterTenantAsync(req);
        var task2 = auth2.RegisterTenantAsync(req);

        var completed = await Task.WhenAll(
            task1.ContinueWith(t => t.IsCompletedSuccessfully),
            task2.ContinueWith(t => t.IsCompletedSuccessfully));

        Assert.Equal(1, completed.Count(ok => ok));
        Assert.Equal(1, await seedDb.Users.CountAsync());
        Assert.Equal(1, await seedDb.Tenants.CountAsync());
        Assert.Equal(1, await seedDb.Branches.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task Register_IdempotencyFilter_SameKeyDifferentPayload_Returns409ProblemDetails()
    {
        var dbName = Guid.NewGuid().ToString();
        var tenantCtx = new TestTenantContext();
        using var db = CreateDbContext(dbName, tenantCtx);
        var filter = new IdempotencyFilter(db, tenantCtx, useProblemDetails: true);

        var req1 = new RegisterTenantRequest("A", "Store A", "0983334455", "idem@mtgrms.vn", "Password@123", true, "123456", StartTrial: false, ChallengeId: "ch-1");
        var http1 = new DefaultHttpContext();
        http1.Request.Method = "POST";
        http1.Request.Path = "/api/auth/register";
        http1.Request.Headers["Idempotency-Key"] = "key-reg-001";
        var exec1 = new ActionExecutingContext(
            new ActionContext(http1, new RouteData(), new ActionDescriptor(), new ModelStateDictionary()),
            new List<IFilterMetadata>(),
            new Dictionary<string, object?> { ["request"] = req1 },
            new object());

        await filter.OnActionExecutionAsync(exec1, () => Task.FromResult(new ActionExecutedContext(
            exec1, new List<IFilterMetadata>(), exec1.Controller)
        {
            Result = new OkObjectResult(ApiResponse<string>.Ok("ok"))
        }));

        var req2 = req1 with { StartTrial = true };
        var http2 = new DefaultHttpContext();
        http2.Request.Method = "POST";
        http2.Request.Path = "/api/auth/register";
        http2.Request.Headers["Idempotency-Key"] = "key-reg-001";
        var exec2 = new ActionExecutingContext(
            new ActionContext(http2, new RouteData(), new ActionDescriptor(), new ModelStateDictionary()),
            new List<IFilterMetadata>(),
            new Dictionary<string, object?> { ["request"] = req2 },
            new object());

        await filter.OnActionExecutionAsync(exec2, () => Task.FromResult<ActionExecutedContext>(null!));

        var conflict = Assert.IsType<ObjectResult>(exec2.Result);
        Assert.Equal(409, conflict.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal("IDEMPOTENCY_PAYLOAD_MISMATCH", problem.Extensions["code"]);
        Assert.NotNull(problem.Extensions["traceId"]);
    }

    [Fact]
    public async Task ActivateTrial_ConcurrentRequests_And_AcrossTenants_And_SpoofedHeader()
    {
        var dbName = Guid.NewGuid().ToString();
        using var db1 = CreateDbContext(dbName);
        using var db2 = CreateDbContext(dbName);

        db1.Users.Add(new User
        {
            Id = "usr-trial-1", UserName = "0984445566", FullName = "Owner",
            Email = "owner-trial@mtgrms.vn", NormalizedEmail = "owner-trial@mtgrms.vn",
            PasswordHash = "hash", IsEmailVerified = true
        });
        db1.Tenants.AddRange(
            new Tenant { Id = "tenant-A", Name = "Store A", Subdomain = "store-a", Code = "SA", SubscriptionStatus = "NO_PLAN" },
            new Tenant { Id = "tenant-B", Name = "Store B", Subdomain = "store-b", Code = "SB", SubscriptionStatus = "NO_PLAN" });
        db1.TenantUsers.AddRange(
            new TenantUser { TenantId = "tenant-A", UserId = "usr-trial-1", Role = "STORE_OWNER", IsActive = true },
            new TenantUser { TenantId = "tenant-B", UserId = "usr-trial-1", Role = "STORE_OWNER", IsActive = true });
        await db1.SaveChangesAsync();

        var controller1 = new TenantStoreController(new StubTenantService(db1), new StubPlanService(), db1)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = CreateOwnerPrincipal("usr-trial-1", "tenant-A") }
            }
        };
        controller1.Request.Headers["X-Tenant-Id"] = "tenant-B";

        var controller2 = new TenantStoreController(new StubTenantService(db2), new StubPlanService(), db2)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = CreateOwnerPrincipal("usr-trial-1", "tenant-A") }
            }
        };

        var results = await Task.WhenAll(
            controller1.ActivateTrialSubscription(CancellationToken.None),
            controller2.ActivateTrialSubscription(CancellationToken.None));

        Assert.All(results, r => Assert.IsType<OkObjectResult>(r));

        using var verifyDb = CreateDbContext(dbName);
        var tenantA = await verifyDb.Tenants.FindAsync("tenant-A");
        var tenantB = await verifyDb.Tenants.FindAsync("tenant-B");
        Assert.Equal("TRIAL", tenantA!.SubscriptionStatus);
        Assert.Equal(TimeSpan.FromHours(72), tenantA.TrialExpiresAt!.Value - tenantA.TrialStartedAt!.Value);
        Assert.Equal("NO_PLAN", tenantB!.SubscriptionStatus);
        Assert.Null(tenantB.TrialStartedAt);
        Assert.Equal(1, await verifyDb.AuditLogs.CountAsync(a => a.ActionType == "TRIAL_ACTIVATED"));

        // Account đã dùng Trial trên tenant A -> thử kích hoạt trên tenant B bị 409 TRIAL_ALREADY_USED
        var controllerB = new TenantStoreController(new StubTenantService(verifyDb), new StubPlanService(), verifyDb)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = CreateOwnerPrincipal("usr-trial-1", "tenant-B") }
            }
        };
        var conflictResult = Assert.IsType<ObjectResult>(await controllerB.ActivateTrialSubscription(CancellationToken.None));
        Assert.Equal(409, conflictResult.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(conflictResult.Value);
        Assert.Equal("TRIAL_ALREADY_USED", problem.Extensions["code"]);
        Assert.Equal("NO_PLAN", (await verifyDb.Tenants.FindAsync("tenant-B"))!.SubscriptionStatus);
    }

    [Fact]
    public async Task SubscriptionAccessMiddleware_BlocksNoPlanAndLiveExpiredTrial_AllowsOnboardingEndpoints()
    {
        var dbName = Guid.NewGuid().ToString();
        using var db = CreateDbContext(dbName);
        db.Tenants.AddRange(
            new Tenant { Id = "t-noplan", Name = "NP", Subdomain = "np", Code = "NP", SubscriptionStatus = "NO_PLAN" },
            new Tenant
            {
                Id = "t-expired", Name = "EX", Subdomain = "ex", Code = "EX",
                SubscriptionStatus = "TRIAL",
                TrialStartedAt = DateTime.UtcNow.AddHours(-80),
                TrialExpiresAt = DateTime.UtcNow.AddHours(-8)
            });
        await db.SaveChangesAsync();

        var nextCalled = false;
        var middleware = new SubscriptionAccessMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });

        var services = new ServiceCollection().AddLogging().BuildServiceProvider();

        // 1. NO_PLAN gọi API nghiệp vụ -> 403 SUBSCRIPTION_REQUIRED
        var blockedNoPlanCtx = new DefaultHttpContext
        {
            User = CreateOwnerPrincipal("usr-1", "t-noplan"),
            RequestServices = services,
            Response = { Body = new MemoryStream() }
        };
        blockedNoPlanCtx.Request.Path = "/api/products";
        await middleware.InvokeAsync(blockedNoPlanCtx, db);
        Assert.False(nextCalled);
        Assert.Equal(403, blockedNoPlanCtx.Response.StatusCode);
        blockedNoPlanCtx.Response.Body.Position = 0;
        var problemJson = await JsonDocument.ParseAsync(blockedNoPlanCtx.Response.Body);
        Assert.Equal("SUBSCRIPTION_REQUIRED", problemJson.RootElement.GetProperty("code").GetString());

        // 2. Trial hết hạn live UTC -> 403 SUBSCRIPTION_REQUIRED
        var blockedExpiredCtx = new DefaultHttpContext
        {
            User = CreateOwnerPrincipal("usr-2", "t-expired"),
            RequestServices = services,
            Response = { Body = new MemoryStream() }
        };
        blockedExpiredCtx.Request.Path = "/api/orders";
        await middleware.InvokeAsync(blockedExpiredCtx, db);
        Assert.False(nextCalled);
        Assert.Equal(403, blockedExpiredCtx.Response.StatusCode);

        // 3. Allowlist onboarding endpoints được phép đi qua
        foreach (var allowedPath in new[]
        {
            "/api/auth/me",
            "/api/tenant/store-info",
            "/api/tenant/pricing-plans",
            "/api/tenant/subscriptions/trial-status",
            "/api/tenant/wallet/balance",
            "/api/webhooks/sepay"
        })
        {
            nextCalled = false;
            var allowCtx = new DefaultHttpContext { User = CreateOwnerPrincipal("usr-1", "t-noplan") };
            allowCtx.Request.Path = allowedPath;
            await middleware.InvokeAsync(allowCtx, db);
            Assert.True(nextCalled, $"Expected {allowedPath} to pass allowlist");
        }
    }

    [Fact]
    public async Task NoPlan_PurchaseStandard_SetsFirstPaidAt_DoesNotFabricateTrialHistory_AndBlocksTrial()
    {
        var dbName = Guid.NewGuid().ToString();
        using var db = CreateDbContext(dbName);
        db.Users.Add(new User
        {
            Id = "usr-paid", UserName = "0985556677", FullName = "Paid Owner",
            Email = "paid@mtgrms.vn", NormalizedEmail = "paid@mtgrms.vn",
            PasswordHash = "hash", IsEmailVerified = true
        });
        db.Tenants.Add(new Tenant
        {
            Id = "t-paid", Name = "Paid Mart", Subdomain = "paid-mart", Code = "PM",
            SubscriptionStatus = "NO_PLAN", WalletBalance = 5_000_000m
        });
        db.TenantUsers.Add(new TenantUser { TenantId = "t-paid", UserId = "usr-paid", Role = "STORE_OWNER", IsActive = true });
        await db.SaveChangesAsync();

        var controller = new TenantStoreController(new StubTenantService(db), new StubPlanService(), db)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = CreateOwnerPrincipal("usr-paid", "t-paid") }
            }
        };

        var purchaseRes = Assert.IsType<OkObjectResult>(await controller.PurchaseSubscription(
            new PurchaseSubscriptionRequest("STANDARD", 3, 0, false, "WALLET", RequestId: Guid.NewGuid()),
            CancellationToken.None));
        Assert.NotNull(purchaseRes.Value);

        var tenant = await db.Tenants.FindAsync("t-paid");
        var user = await db.Users.FindAsync("usr-paid");
        Assert.Equal("ACTIVE", tenant!.SubscriptionStatus);
        Assert.Equal("STANDARD", tenant.CurrentPlan);
        Assert.NotNull(tenant.FirstPaidAt);
        Assert.Null(tenant.TrialStartedAt);
        Assert.Null(user!.TrialConsumedAt);

        // History không được bịa gói Trial
        var historyOk = Assert.IsType<OkObjectResult>(await controller.GetSubscriptionOrderHistory(CancellationToken.None));
        var historyResp = Assert.IsType<ApiResponse<IReadOnlyList<SubscriptionOrderHistoryDto>>>(historyOk.Value);
        Assert.Single(historyResp.Data!);
        Assert.DoesNotContain(historyResp.Data!, h => h.PlanName.Contains("Dùng Thử", StringComparison.OrdinalIgnoreCase));

        // Thử kích hoạt Trial sau khi đã paid -> 409 TRIAL_NOT_ELIGIBLE
        var trialBlocked = Assert.IsType<ObjectResult>(await controller.ActivateTrialSubscription(CancellationToken.None));
        Assert.Equal(409, trialBlocked.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(trialBlocked.Value);
        Assert.Equal("TRIAL_NOT_ELIGIBLE", problem.Extensions["code"]);
    }

    [Fact]
    public async Task SepayWebhook_ValidatesApiKey_AndPreventsReplay()
    {
        var dbName = Guid.NewGuid().ToString();
        using var db = CreateDbContext(dbName);
        db.Tenants.Add(new Tenant
        {
            Id = "t-sepay", Name = "Sepay Mart", Subdomain = "sepay", Code = "SP",
            SubscriptionStatus = "NO_PLAN", WalletBalance = 0m
        });
        db.WalletTopupOrders.Add(new WalletTopupOrder
        {
            TenantId = "t-sepay", OrderCode = "778899", Amount = 300_000m,
            TransferContent = "WAL 778899", BankCode = "MB", BankName = "MB",
            AccountNumber = "1900", AccountName = "MT", QrUrl = "https://qr", Status = "PENDING",
            ExpiresAt = DateTime.UtcNow.AddMinutes(10)
        });
        await db.SaveChangesAsync();

        var settings = Options.Create(new SePaySettings { ApiKey = "LIVE_SECRET_KEY_123" });
        var walletService = new WalletService(db, settings);
        var controller = new SepayWebhookController(walletService, settings, NullLogger<SepayWebhookController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var payload = new MT_GRMS.Application.SaaS.Dtos.SepayWebhookPayload
        {
            Id = 554433,
            TransferType = "in",
            TransferAmount = 300_000m,
            Content = "WAL 778899",
            ReferenceCode = ""
        };

        // 1. Sai API key -> 401
        controller.Request.Headers.Authorization = "Apikey WRONG";
        Assert.IsType<UnauthorizedObjectResult>(await controller.ReceiveSepayWebhook(payload, CancellationToken.None));

        // 2. Đúng API key -> cộng tiền 1 lần
        controller.Request.Headers.Authorization = "Apikey LIVE_SECRET_KEY_123";
        Assert.IsType<OkObjectResult>(await controller.ReceiveSepayWebhook(payload, CancellationToken.None));
        Assert.Equal(300_000m, (await db.Tenants.FindAsync("t-sepay"))!.WalletBalance);

        // 3. Replay lại cùng payload (dù ReferenceCode rỗng) -> không cộng tiền lần 2
        Assert.IsType<OkObjectResult>(await controller.ReceiveSepayWebhook(payload, CancellationToken.None));
        Assert.Equal(300_000m, (await db.Tenants.FindAsync("t-sepay"))!.WalletBalance);
    }

    [Fact]
    public async Task ForwardedHeaders_OnlyAcceptsConfiguredTrustedProxy()
    {
        var builder = WebApplication.CreateBuilder();
        var app = builder.Build();
        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor
        };
        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();
        options.KnownProxies.Add(IPAddress.Parse("10.0.0.10"));

        app.UseForwardedHeaders(options);

        var untrustedCtx = new DefaultHttpContext();
        untrustedCtx.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.5");
        untrustedCtx.Request.Headers["X-Forwarded-For"] = "1.2.3.4";
        await ((RequestDelegate)(ctx => Task.CompletedTask))(untrustedCtx);

        // Kiểm tra cấu hình KnownProxies không chứa untrusted IP
        Assert.Contains(IPAddress.Parse("10.0.0.10"), options.KnownProxies);
        Assert.DoesNotContain(IPAddress.Parse("203.0.113.5"), options.KnownProxies);
    }

    [Fact]
    public async Task BackfillService_DryRunAndApply_ClassifiesDeterministically_AndOmitsPii()
    {
        var dbName = Guid.NewGuid().ToString();
        using var db = CreateDbContext(dbName);
        var now = DateTime.UtcNow;

        db.Users.AddRange(
            new User { Id = "usr-1", UserName = "0901111111", FullName = "U1", Email = "secret1@example.com", NormalizedEmail = "secret1@example.com", Phone = "0901111111", PasswordHash = "x" },
            new User { Id = "usr-2", UserName = "0902222222", FullName = "U2", Email = "secret2@example.com", NormalizedEmail = "secret2@example.com", Phone = "0902222222", PasswordHash = "x" },
            new User { Id = "usr-3", UserName = "0903333333", FullName = "U3", Email = "secret3@example.com", NormalizedEmail = "secret3@example.com", Phone = "0903333333", PasswordHash = "x" },
            new User { Id = "usr-4", UserName = "0904444444", FullName = "U4", Email = "secret4@example.com", NormalizedEmail = "secret4@example.com", Phone = "0904444444", PasswordHash = "x" });

        db.Tenants.AddRange(
            new Tenant { Id = "t-trial", Name = "T1", Subdomain = "t1", Code = "T1", CurrentPlan = "TRIAL", SubscriptionStatus = "TRIAL", SubscriptionExpiresAt = now.AddHours(24) },
            new Tenant { Id = "t-paid", Name = "T2", Subdomain = "t2", Code = "T2", CurrentPlan = "STANDARD", SubscriptionStatus = "ACTIVE", SubscriptionExpiresAt = now.AddDays(30) },
            new Tenant { Id = "t-both", Name = "T3", Subdomain = "t3", Code = "T3", CurrentPlan = "PRO", SubscriptionStatus = "ACTIVE", TrialExpiresAt = now.AddDays(-5), SubscriptionExpiresAt = now.AddDays(60) },
            new Tenant { Id = "t-ambiguous", Name = "T4", Subdomain = "t4", Code = "T4", CurrentPlan = null, SubscriptionStatus = "ACTIVE", SubscriptionExpiresAt = null });

        db.TenantUsers.AddRange(
            new TenantUser { TenantId = "t-trial", UserId = "usr-1", Role = "STORE_OWNER", IsActive = true },
            new TenantUser { TenantId = "t-paid", UserId = "usr-2", Role = "STORE_OWNER", IsActive = true },
            new TenantUser { TenantId = "t-both", UserId = "usr-3", Role = "STORE_OWNER", IsActive = true },
            new TenantUser { TenantId = "t-ambiguous", UserId = "usr-4", Role = "STORE_OWNER", IsActive = true });

        db.WalletTransactions.Add(new WalletTransaction
        {
            TenantId = "t-paid", TransactionCode = "SUB-PAID-1", Type = "PURCHASE_PLAN",
            Amount = 297_000m, BalanceBefore = 500_000m, BalanceAfter = 203_000m,
            Description = "STANDARD", PaymentMethod = "WALLET", Status = "SUCCESS", CreatedAt = now.AddDays(-10)
        });
        await db.SaveChangesAsync();

        var backfill = new TrialOnboardingBackfillService(db);

        // 1. Dry-run không ghi DB
        var dryReport = await backfill.DryRunAsync();
        Assert.True(dryReport.IsDryRun);
        Assert.Equal(1, dryReport.TrialBackfilledCount);
        Assert.Equal(1, dryReport.PaidBackfilledCount);
        Assert.Equal(1, dryReport.BothHistoryCount);
        Assert.Equal(1, dryReport.ReviewRequiredCount);
        Assert.Null((await db.Tenants.FindAsync("t-paid"))!.FirstPaidAt);
        Assert.Equal("ACTIVE", (await db.Tenants.FindAsync("t-ambiguous"))!.SubscriptionStatus);

        // Report không chứa email hay SĐT đầy đủ
        var reportJson = JsonSerializer.Serialize(dryReport);
        Assert.DoesNotContain("secret1@example.com", reportJson);
        Assert.DoesNotContain("0901111111", reportJson);

        // 2. Apply ghi deterministic
        var applyReport = await backfill.ApplyAsync();
        Assert.False(applyReport.IsDryRun);

        var tTrial = await db.Tenants.FindAsync("t-trial");
        var tPaid = await db.Tenants.FindAsync("t-paid");
        var tBoth = await db.Tenants.FindAsync("t-both");
        var tAmbiguous = await db.Tenants.FindAsync("t-ambiguous");

        Assert.NotNull(tTrial!.TrialStartedAt);
        Assert.NotNull((await db.Users.FindAsync("usr-1"))!.TrialConsumedAt);

        Assert.NotNull(tPaid!.FirstPaidAt);
        Assert.Null(tPaid.TrialStartedAt);
        Assert.Null((await db.Users.FindAsync("usr-2"))!.TrialConsumedAt);

        Assert.NotNull(tBoth!.TrialStartedAt);
        Assert.NotNull(tBoth.FirstPaidAt);
        Assert.NotNull((await db.Users.FindAsync("usr-3"))!.TrialConsumedAt);

        Assert.Equal("REVIEW_REQUIRED", tAmbiguous!.SubscriptionStatus);
        Assert.Null(tAmbiguous.TrialStartedAt);
    }

    [Fact]
    public async Task SubscriptionGate_BlocksExpiredPaidPlan_AndAccessStatusSupportsStaffWithoutHeaderSpoofing()
    {
        var dbName = Guid.NewGuid().ToString();
        using var db = CreateDbContext(dbName);
        var now = DateTime.UtcNow;

        db.Users.AddRange(
            new User { Id = "usr-staff-exp", UserName = "0905551111", FullName = "Staff Exp", Phone = "0905551111", Email = "staff-exp@mtgrms.vn", PasswordHash = "x" },
            new User { Id = "usr-other", UserName = "0905552222", FullName = "Owner B", Phone = "0905552222", Email = "owner-b@mtgrms.vn", PasswordHash = "x" });
        db.Tenants.AddRange(
            new Tenant
            {
                Id = "t-paid-exp", Name = "Expired Paid Mart", Subdomain = "exp-paid", Code = "EP",
                CurrentPlan = "STANDARD", SubscriptionStatus = "ACTIVE",
                FirstPaidAt = now.AddDays(-35), SubscriptionExpiresAt = now.AddMinutes(-5)
            },
            new Tenant
            {
                Id = "t-active-b", Name = "Active B Mart", Subdomain = "active-b", Code = "AB",
                CurrentPlan = "PRO", SubscriptionStatus = "ACTIVE",
                FirstPaidAt = now.AddDays(-5), SubscriptionExpiresAt = now.AddDays(25)
            });
        db.TenantUsers.Add(new TenantUser { TenantId = "t-paid-exp", UserId = "usr-staff-exp", Role = "CASHIER", IsActive = true });
        await db.SaveChangesAsync();

        var staffPrincipal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "usr-staff-exp"),
            new Claim("tenant_id", "t-paid-exp"),
            new Claim(ClaimTypes.Role, "CASHIER")
        ], "test_auth"));

        var nextCalled = false;
        var middleware = new SubscriptionAccessMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var services = new ServiceCollection().AddLogging().BuildServiceProvider();

        // 1. Middleware blocks business API when paid plan SubscriptionExpiresAt <= UtcNow
        var businessCtx = new DefaultHttpContext
        {
            User = staffPrincipal,
            RequestServices = services,
            Response = { Body = new MemoryStream() }
        };
        businessCtx.Request.Path = "/api/products";
        await middleware.InvokeAsync(businessCtx, db);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, businessCtx.Response.StatusCode);
        businessCtx.Response.Body.Position = 0;
        var problemBody = await new StreamReader(businessCtx.Response.Body).ReadToEndAsync();
        Assert.Contains("SUBSCRIPTION_REQUIRED", problemBody);

        // 2. Middleware allows /api/tenant/subscriptions/access-status & controller ignores spoofed X-Tenant-Id
        var accessCtx = new DefaultHttpContext { User = staffPrincipal };
        accessCtx.Request.Path = "/api/tenant/subscriptions/access-status";
        accessCtx.Request.Headers["X-Tenant-Id"] = "t-active-b";
        await middleware.InvokeAsync(accessCtx, db);
        Assert.True(nextCalled);

        var controller = new TenantStoreController(new StubTenantService(db), new StubPlanService(), db)
        {
            ControllerContext = new ControllerContext { HttpContext = accessCtx }
        };
        var okResult = Assert.IsType<OkObjectResult>(await controller.GetSubscriptionAccessStatus(CancellationToken.None));
        var payloadJson = JsonSerializer.Serialize(okResult.Value);
        using var doc = JsonDocument.Parse(payloadJson);
        var data = doc.RootElement.GetProperty("Data");
        Assert.Equal("EXPIRED", data.GetProperty("state").GetString());
        Assert.True(data.GetProperty("isExpired").GetBoolean());
        Assert.True(data.GetProperty("isLocked").GetBoolean());
        Assert.Equal("SUBSCRIPTIONS", data.GetProperty("destination").GetString());
    }
}
