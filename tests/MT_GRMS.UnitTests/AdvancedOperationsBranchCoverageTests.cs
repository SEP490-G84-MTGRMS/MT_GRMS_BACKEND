using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using MT_GRMS.Api.Controllers;
using MT_GRMS.Application.Admin.Dtos;
using MT_GRMS.Application.Admin.Services;
using MT_GRMS.Application.Catalog;
using MT_GRMS.Application.Catalog.Dtos;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Stocktakes;
using MT_GRMS.Application.Stocktakes.Dtos;
using MT_GRMS.Application.SupplierReturns;
using MT_GRMS.Application.Suppliers;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Entities.Inventory;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Domain.Entities.Sales;
using MT_GRMS.Domain.Entities.Supplier;
using MT_GRMS.Domain.Constants;
using MT_GRMS.Domain.Entities.Identity;
using MT_GRMS.Domain.Exceptions;
using MT_GRMS.Infrastructure.Persistence;
using Xunit;

namespace MT_GRMS.UnitTests;

public class AdvancedOperationsBranchCoverageTests
{
    private class FakeTenantContext : ITenantContext
    {
        public string CurrentTenantId { get; set; } = "tenant-adv-01";
        public Guid CurrentUserId { get; set; } = Guid.NewGuid();
        public string CurrentRole { get; set; } = "STORE_OWNER";
        public Guid? CurrentBranchId { get; set; } = Guid.NewGuid();
        public string? CustomRawUserId { get; set; } = null;
        public string CurrentRawUserId => CustomRawUserId ?? (CurrentUserId != Guid.Empty ? CurrentUserId.ToString() : string.Empty);

        public void SetContext(string tenantId, Guid userId, Guid? branchId)
        {
            CurrentTenantId = tenantId;
            CurrentUserId = userId;
            CurrentBranchId = branchId;
        }

        public void SetRole(string? role)
        {
            CurrentRole = role ?? "STORE_OWNER";
        }
    }
    // =========================================================================
    // SECTION 1: PaymentsController Qr, Revoke & Order Status Branches
    // =========================================================================

    [Fact]
    public async Task PaymentsController_GetPublicQrConfig_And_RevokeKey_And_CheckStatus_Branches()
    {
        var tenantId = "tenant-adv-pay";
        var tenantContext = new FakeTenantContext { CurrentTenantId = tenantId };

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"AdvPaymentsDb_{Guid.NewGuid()}")
            .Options;
        using var dbContext = new AppDbContext(options, tenantContext);

        var mockFactory = new Mock<IHttpClientFactory>();
        var mockConfig = new Mock<IConfiguration>();
        var mockLogger = new Mock<ILogger<PaymentsController>>();

        var controller = new PaymentsController(dbContext, mockFactory.Object, mockConfig.Object, mockLogger.Object, tenantContext);
        var claims = new List<Claim>
        {
            new(ClaimTypes.Role, RoleConstants.StoreOwner),
            new("tenant_id", tenantId)
        };
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth")) }
        };

        // 1. GetPublicQrConfig: branchId specified but branch has no payment account -> BRANCH_PAYMENT_ACCOUNT_NOT_CONFIGURED (404)
        var branchId = Guid.NewGuid();
        var resNoAcc = await controller.GetPublicQrConfig(tenantId, branchId, CancellationToken.None);
        var notFoundAcc = Assert.IsType<NotFoundObjectResult>(resNoAcc);
        Assert.NotNull(notFoundAcc.Value);

        // 2. GetPublicQrConfig: no branch, no config -> 404
        var resNoConfig = await controller.GetPublicQrConfig(tenantId, null, CancellationToken.None);
        var notFoundConfig = Assert.IsType<NotFoundObjectResult>(resNoConfig);
        Assert.NotNull(notFoundConfig.Value);

        // Seed StorePaymentConfig
        var paymentConfig = new StorePaymentConfig
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            BankCode = "MB",
            BankName = "MB Bank",
            BankAccountNumber = "1234567890",
            BankAccountName = "CONG TY TNHH ABC",
            SepayApiKeyEncrypted = "some_encrypted_key",
            SepayWebhookSecret = "whsec_test123",
            IsActive = true
        };
        dbContext.StorePaymentConfigs.Add(paymentConfig);
        await dbContext.SaveChangesAsync();

        // 3. GetPublicQrConfig: with config -> 200 Ok
        var resOk = await controller.GetPublicQrConfig(tenantId, null, CancellationToken.None);
        var okResult = Assert.IsType<OkObjectResult>(resOk);
        Assert.NotNull(okResult.Value);

        // 4. RevokeSePayApiKey: as non-owner -> 403 Forbidden
        var staffClaims = new List<Claim>
        {
            new(ClaimTypes.Role, "STAFF"),
            new("tenant_id", tenantId)
        };
        controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(staffClaims, "TestAuth"));
        var resForbidden = await controller.RevokeSePayApiKey(CancellationToken.None);
        var objectResult = Assert.IsType<ObjectResult>(resForbidden);
        Assert.Equal(StatusCodes.Status403Forbidden, objectResult.StatusCode);

        // 5. RevokeSePayApiKey: as store owner -> 200 Ok and clears key
        controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        var resRevokeOk = await controller.RevokeSePayApiKey(CancellationToken.None);
        var okRevoke = Assert.IsType<OkObjectResult>(resRevokeOk);
        Assert.NotNull(okRevoke.Value);
        Assert.Equal(string.Empty, paymentConfig.SepayApiKeyEncrypted);

        // 6. CheckOrderStatus: order does not exist -> 404
        var resOrderNotFound = await controller.CheckOrderStatus("ORD-NONEXISTENT", CancellationToken.None);
        Assert.IsType<NotFoundObjectResult>(resOrderNotFound);

        // 7. CheckOrderStatus: order exists and is PAID
        var paidOrder = new Order
        {
            Id = Guid.NewGuid(),
            OrderCode = "HD1001",
            TenantId = tenantId,
            FinalAmount = 150000m,
            PaymentStatus = "PAID",
            Status = "Completed",
            CreatedAt = DateTime.UtcNow
        };
        dbContext.Orders.Add(paidOrder);
        await dbContext.SaveChangesAsync();

        var resPaid = await controller.CheckOrderStatus("1001", CancellationToken.None);
        var okPaid = Assert.IsType<OkObjectResult>(resPaid);
        Assert.NotNull(okPaid.Value);

        // 8. CheckOrderStatus: order exists and is UNPAID
        var unpaidOrder = new Order
        {
            Id = Guid.NewGuid(),
            OrderCode = "ORD-UNPAID-01",
            TenantId = tenantId,
            FinalAmount = 250000m,
            PaymentStatus = "UNPAID",
            Status = "PendingPayment",
            CreatedAt = DateTime.UtcNow
        };
        dbContext.Orders.Add(unpaidOrder);
        await dbContext.SaveChangesAsync();

        var resUnpaid = await controller.CheckOrderStatus("ORD-UNPAID-01", CancellationToken.None);
        var okUnpaid = Assert.IsType<OkObjectResult>(resUnpaid);
        Assert.NotNull(okUnpaid.Value);
    }

    // =========================================================================
    // SECTION 2: PaymentsController HandleSePayWebhook Security Layers Branches
    // =========================================================================

    [Fact]
    public async Task PaymentsController_HandleSePayWebhook_SecurityLayers_Branches()
    {
        var tenantId = "tenant-webhook-01";
        var tenantContext = new FakeTenantContext { CurrentTenantId = tenantId };

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"AdvWebhookDb_{Guid.NewGuid()}")
            .Options;
        using var dbContext = new AppDbContext(options, tenantContext);

        var mockFactory = new Mock<IHttpClientFactory>();
        var mockConfig = new Mock<IConfiguration>();
        var mockLogger = new Mock<ILogger<PaymentsController>>();

        var controller = new PaymentsController(dbContext, mockFactory.Object, mockConfig.Object, mockLogger.Object, tenantContext);
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("10.99.88.77");
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var payload = new SePayWebhookPayload
        {
            Id = 999901,
            AmountIn = 200000m,
            AccountNumber = "1234567890",
            TransactionContent = "HD5001 chuyen tien",
            TransactionDate = DateTime.UtcNow
        };

        // 1. Missing Authorization header -> 401 Unauthorized
        var resNoAuth = await controller.HandleSePayWebhook(payload, CancellationToken.None);
        Assert.IsType<UnauthorizedObjectResult>(resNoAuth);

        // 2. Empty Authorization token -> 401
        httpContext.Request.Headers["Authorization"] = "Bearer   ";
        var resEmptyAuth = await controller.HandleSePayWebhook(payload, CancellationToken.None);
        Assert.IsType<UnauthorizedObjectResult>(resEmptyAuth);

        // 3. No order code in transaction content -> 400 BadRequest
        httpContext.Request.Headers["Authorization"] = "Apikey whsec_secret123";
        payload.TransactionContent = "a b c";
        var resNoCode = await controller.HandleSePayWebhook(payload, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(resNoCode);

        // 4. Order code extracted but order not found in DB -> 404 NotFound
        payload.TransactionContent = "HD9999 mua hang";
        var resNotFound = await controller.HandleSePayWebhook(payload, CancellationToken.None);
        Assert.IsType<NotFoundObjectResult>(resNotFound);

        // Seed order
        var order = new Order
        {
            Id = Guid.NewGuid(),
            OrderCode = "HD5001",
            TenantId = tenantId,
            FinalAmount = 200000m,
            PaymentStatus = "UNPAID",
            Status = "PendingPayment",
            CreatedAt = DateTime.UtcNow
        };
        dbContext.Orders.Add(order);

        var config = new StorePaymentConfig
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            BankAccountNumber = "1234567890",
            SepayWebhookSecret = "whsec_correct_secret",
            SepayApiKeyEncrypted = "",
            IsActive = true
        };
        dbContext.StorePaymentConfigs.Add(config);
        await dbContext.SaveChangesAsync();

        // 5. Invalid token vs store secret -> 401 Unauthorized
        payload.TransactionContent = "HD5001 thanh toan";
        httpContext.Request.Headers["Authorization"] = "Apikey whsec_wrong_secret";
        var resWrongSecret = await controller.HandleSePayWebhook(payload, CancellationToken.None);
        Assert.IsType<UnauthorizedObjectResult>(resWrongSecret);

        // 6. Wrong bank account number in payload -> 400 BadRequest
        httpContext.Request.Headers["Authorization"] = "Apikey whsec_correct_secret";
        payload.AccountNumber = "9999999999";
        var resWrongAcc = await controller.HandleSePayWebhook(payload, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(resWrongAcc);

        // 7. Replay attack: transaction ID already processed -> 200 with isIdempotentReplay = true
        payload.AccountNumber = "1234567890";
        dbContext.PaymentTransactions.Add(new PaymentTransaction
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OrderId = order.Id,
            BankTransactionId = "999901",
            AmountIn = 200000m,
            Gateway = "VIETQR",
            CreatedAt = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var resReplay = await controller.HandleSePayWebhook(payload, CancellationToken.None);
        var okReplay = Assert.IsType<OkObjectResult>(resReplay);
        Assert.NotNull(okReplay.Value);

        // 8. New transaction ID but insufficient amount -> 400 BadRequest
        payload.Id = 999902;
        payload.AmountIn = 50000m; // order requires 200,000m
        var resInsufficient = await controller.HandleSePayWebhook(payload, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(resInsufficient);

        // 9. Full payment success -> 200 Ok, marks order PAID and Completed
        payload.Id = 999903;
        payload.AmountIn = 200000m;
        var resSuccess = await controller.HandleSePayWebhook(payload, CancellationToken.None);
        var okSuccess = Assert.IsType<OkObjectResult>(resSuccess);
        Assert.NotNull(okSuccess.Value);
        Assert.Equal("PAID", order.PaymentStatus);
        Assert.Equal("Completed", order.Status);
    }

    // =========================================================================
    // SECTION 3: TenantStoreController Purchase Subscription Branches
    // =========================================================================

    [Fact]
    public async Task TenantStoreController_PurchaseSubscription_ValidationAndBonus_Branches()
    {
        var tenantId = "tenant-sub-branch-01";
        var userId = "user-sub-branch-01";
        var tenantContext = new FakeTenantContext { CurrentTenantId = tenantId, CurrentUserId = Guid.NewGuid() };

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"AdvTenantSubDb_{Guid.NewGuid()}")
            .Options;
        using var dbContext = new AppDbContext(options, tenantContext);

        var tenant = new Tenant
        {
            Id = tenantId,
            Name = "Sieu Thi Adv",
            CurrentPlan = "PRO",
            SubscriptionStatus = "ACTIVE",
            WalletBalance = 20000000m,
            Status = "ACTIVE"
        };
        var tenantUser = new TenantUser
        {
            TenantId = tenantId,
            UserId = userId,
            IsActive = true,
            Role = RoleConstants.StoreOwner
        };
        var user = new User
        {
            Id = userId,
            FullName = "Store Owner Adv",
            Email = "owneradv@test.com",
            IsEmailVerified = true
        };
        dbContext.Tenants.Add(tenant);
        dbContext.TenantUsers.Add(tenantUser);
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        var mockTenantService = new Mock<ITenantService>();
        var mockPlanService = new Mock<ISubscriptionPlanService>();

        var controller = new TenantStoreController(mockTenantService.Object, mockPlanService.Object, dbContext);
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
            new("tenant_id", tenantId),
            new(ClaimTypes.Role, RoleConstants.StoreOwner)
        };
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth")) }
        };

        // 1. ExtraStorageGb < 0 or > 100 -> 400
        var reqStorageNeg = new PurchaseSubscriptionRequest("PRO", 12, ExtraStorageGb: -1);
        var resStorageNeg = await controller.PurchaseSubscription(reqStorageNeg, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(resStorageNeg);

        var reqStorageOver = new PurchaseSubscriptionRequest("PRO", 12, ExtraStorageGb: 105);
        var resStorageOver = await controller.PurchaseSubscription(reqStorageOver, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(resStorageOver);

        // 2. PlanCode contains TRIAL -> 400 TRIAL_ENDPOINT_REQUIRED
        var reqTrial = new PurchaseSubscriptionRequest("TRIAL", 12);
        var resTrial = await controller.PurchaseSubscription(reqTrial, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(resTrial);

        // 3. PlanCode not STANDARD or PRO -> 400 INVALID_PLAN_CODE
        var reqInvalidPlan = new PurchaseSubscriptionRequest("ULTRA", 12);
        var resInvalidPlan = await controller.PurchaseSubscription(reqInvalidPlan, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(resInvalidPlan);

        // 4. Downgrade from PRO to STANDARD while active -> 400 DOWNGRADE_NOT_ALLOWED
        var reqDowngrade = new PurchaseSubscriptionRequest("STANDARD", 12);
        var resDowngrade = await controller.PurchaseSubscription(reqDowngrade, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(resDowngrade);

        // 5. Success Purchase PRO for 24 months with WALLET payment -> +27 months (24 + 3 bonus)
        var reqPro24 = new PurchaseSubscriptionRequest(
            PlanCode: "PRO",
            Months: 24,
            PaymentMethod: "WALLET",
            ExtraBranches: 2,
            ExtraStorageGb: 10
        );

        var resPro24 = await controller.PurchaseSubscription(reqPro24, CancellationToken.None);
        var okPro24 = Assert.IsType<OkObjectResult>(resPro24);
        var apiResPro24 = Assert.IsType<ApiResponse<PurchaseSubscriptionResponse>>(okPro24.Value);
        Assert.True(apiResPro24.Success);
        Assert.Equal("PRO", tenant.CurrentPlan);
        Assert.Equal(2, tenant.ExtraBranches);
        Assert.Equal(10, tenant.ExtraStorageGb);
        Assert.True(tenant.WalletBalance < 20000000m);

        // 6. Idempotency test: same RequestId -> returns existing transaction
        var reqId = Guid.NewGuid();
        var reqIdempotent = new PurchaseSubscriptionRequest(
            PlanCode: "PRO",
            Months: 12,
            PaymentMethod: "WALLET",
            RequestId: reqId
        );
        var resFirst = await controller.PurchaseSubscription(reqIdempotent, CancellationToken.None);
        Assert.IsType<OkObjectResult>(resFirst);

        var resSecond = await controller.PurchaseSubscription(reqIdempotent, CancellationToken.None);
        var okSecond = Assert.IsType<OkObjectResult>(resSecond);
        var apiResSecond = Assert.IsType<ApiResponse<PurchaseSubscriptionResponse>>(okSecond.Value);
        Assert.Contains("trước đó", apiResSecond.Message);
    }

    // =========================================================================
    // SECTION 4: TenantStoreController Activate Trial Subscription Branches
    // =========================================================================

    [Fact]
    public async Task TenantStoreController_ActivateTrialSubscription_Branches()
    {
        var tenantId = "tenant-trial-branch-01";
        var userId = "user-trial-branch-01";
        var tenantContext = new FakeTenantContext { CurrentTenantId = tenantId };

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"AdvTenantTrialDb_{Guid.NewGuid()}")
            .Options;
        using var dbContext = new AppDbContext(options, tenantContext);

        var mockTenantService = new Mock<ITenantService>();
        var mockPlanService = new Mock<ISubscriptionPlanService>();

        var controller = new TenantStoreController(mockTenantService.Object, mockPlanService.Object, dbContext);

        // 1. Missing claims -> 403 Forbidden
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) }
        };
        var resNoClaims = await controller.ActivateTrialSubscription(CancellationToken.None);
        var objNoClaims = Assert.IsType<ObjectResult>(resNoClaims);
        Assert.Equal(StatusCodes.Status403Forbidden, objNoClaims.StatusCode);

        // Seed tenant, tenantUser, user
        var tenant = new Tenant
        {
            Id = tenantId,
            Name = "Cua hang Trial",
            CurrentPlan = null,
            SubscriptionStatus = "NO_PLAN"
        };
        var tenantUser = new TenantUser
        {
            TenantId = tenantId,
            UserId = userId,
            IsActive = true,
            Role = RoleConstants.StoreOwner
        };
        var user = new User
        {
            Id = userId,
            FullName = "Store Owner Trial",
            Email = "trialowner@test.com",
            IsEmailVerified = false // not verified yet
        };
        dbContext.Tenants.Add(tenant);
        dbContext.TenantUsers.Add(tenantUser);
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
            new("tenant_id", tenantId),
            new(ClaimTypes.Role, RoleConstants.StoreOwner)
        };
        controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));

        // 2. Email not verified -> 403 Forbidden
        var resUnverified = await controller.ActivateTrialSubscription(CancellationToken.None);
        var objUnverified = Assert.IsType<ObjectResult>(resUnverified);
        Assert.Equal(StatusCodes.Status403Forbidden, objUnverified.StatusCode);

        // Verify email
        user.IsEmailVerified = true;
        await dbContext.SaveChangesAsync();

        // 3. Eligible trial activation -> 200 Ok
        var resSuccess = await controller.ActivateTrialSubscription(CancellationToken.None);
        var okSuccess = Assert.IsType<OkObjectResult>(resSuccess);
        Assert.NotNull(okSuccess.Value);
        Assert.Equal("TRIAL", tenant.CurrentPlan);
        Assert.Equal("TRIAL", tenant.SubscriptionStatus);
        Assert.NotNull(tenant.TrialStartedAt);
        Assert.NotNull(tenant.TrialExpiresAt);
        Assert.NotNull(user.TrialConsumedAt);

        // 4. Repeated trial activation when trial already started and expired -> 409 Conflict
        tenant.TrialExpiresAt = DateTime.UtcNow.AddDays(-1);
        await dbContext.SaveChangesAsync();

        var resAlready = await controller.ActivateTrialSubscription(CancellationToken.None);
        var objAlready = Assert.IsType<ObjectResult>(resAlready);
        Assert.Equal(StatusCodes.Status409Conflict, objAlready.StatusCode);
    }

    // =========================================================================
    // SECTION 5: StocktakeService Validation & Workflow Branches
    // =========================================================================

    [Fact]
    public async Task StocktakeService_Detail_Submit_Review_Validation_Branches()
    {
        var stRepoMock = new Mock<IStocktakeRepository>();
        var supRetServiceMock = new Mock<ISupplierReturnService>();
        var supRetRepoMock = new Mock<ISupplierReturnRepository>();
        var supRepoMock = new Mock<ISupplierRepository>();
        var uow = new Mock<IUnitOfWork>();
        var tenant = new FakeTenantContext
        {
            CurrentTenantId = "tenant-stk-cov",
            CurrentRole = "STORE_OWNER"
        };

        var service = new StocktakeService(
            stRepoMock.Object,
            supRetServiceMock.Object,
            supRetRepoMock.Object,
            supRepoMock.Object,
            tenant,
            uow.Object
        );

        var stId = Guid.NewGuid();

        // 1. GetByIdAsync: not found -> NotFoundException
        stRepoMock.Setup(r => r.GetDetailDtoByIdAsync("tenant-stk-cov", stId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((StocktakeDetailDto?)null);
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetByIdAsync(stId));

        // 2. CreateAsync: empty branchId -> BusinessException
        var cmdEmptyBranch = new CreateStocktakeCommand(Guid.Empty, "LOT", "PERIODIC", null, null, null, null);
        await Assert.ThrowsAsync<BusinessException>(() => service.CreateAsync(cmdEmptyBranch));

        // 3. SubmitAsync: not found -> NotFoundException
        stRepoMock.Setup(r => r.GetByIdAsync("tenant-stk-cov", stId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Stocktake?)null);
        await Assert.ThrowsAsync<NotFoundException>(() => service.SubmitAsync(stId));

        // 4. SubmitAsync: status not DRAFT or NEEDS_RECOUNT (e.g. COMPLETED) -> BusinessException
        var completedStk = new Stocktake
        {
            Id = stId,
            TenantId = "tenant-stk-cov",
            BranchId = Guid.NewGuid(),
            Status = "COMPLETED"
        };
        stRepoMock.Setup(r => r.GetByIdAsync("tenant-stk-cov", stId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(completedStk);
        await Assert.ThrowsAsync<BusinessException>(() => service.SubmitAsync(stId));

        // 5. ReviewAsync: employee role -> BusinessException
        var empTenant = new FakeTenantContext
        {
            CurrentTenantId = "tenant-stk-cov",
            CurrentRole = "EMPLOYEE"
        };
        var empService = new StocktakeService(
            stRepoMock.Object,
            supRetServiceMock.Object,
            supRetRepoMock.Object,
            supRepoMock.Object,
            empTenant,
            uow.Object
        );
        var reviewCmd = new ReviewStocktakeCommand("APPROVE", null);
        await Assert.ThrowsAsync<BusinessException>(() => empService.ReviewAsync(stId, reviewCmd));

        // 6. ReviewAsync: status not PENDING_APPROVAL -> BusinessException
        var draftStk = new Stocktake
        {
            Id = stId,
            TenantId = "tenant-stk-cov",
            BranchId = Guid.NewGuid(),
            Status = "DRAFT"
        };
        stRepoMock.Setup(r => r.GetByIdAsync("tenant-stk-cov", stId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(draftStk);
        await Assert.ThrowsAsync<BusinessException>(() => service.ReviewAsync(stId, reviewCmd));

        // 7. ReviewAsync: decision REJECT -> sets status REJECTED
        var pendingStk = new Stocktake
        {
            Id = stId,
            TenantId = "tenant-stk-cov",
            BranchId = Guid.NewGuid(),
            Status = "PENDING_APPROVAL",
            Lines = new List<StocktakeLine>()
        };
        stRepoMock.Setup(r => r.GetByIdAsync("tenant-stk-cov", stId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pendingStk);
        var branchId = pendingStk.BranchId;
        stRepoMock.Setup(r => r.GetDetailDtoByIdAsync("tenant-stk-cov", stId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StocktakeDetailDto(stId, "KK-001", branchId, "BR01", "Branch 1", "FULL", "PERIODIC", "REJECTED", "Owner", DateTime.UtcNow, null, "Owner", DateTime.UtcNow, null, null, new List<StocktakeLineDto>()));

        var rejectRes = await service.ReviewAsync(stId, new ReviewStocktakeCommand("REJECT", null));
        Assert.NotNull(rejectRes);
        Assert.Equal("REJECTED", pendingStk.Status);

        // 8. ReviewAsync: decision NEEDS_RECOUNT -> sets status NEEDS_RECOUNT
        pendingStk.Status = "PENDING_APPROVAL";
        stRepoMock.Setup(r => r.GetDetailDtoByIdAsync("tenant-stk-cov", stId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StocktakeDetailDto(stId, "KK-001", branchId, "BR01", "Branch 1", "FULL", "PERIODIC", "NEEDS_RECOUNT", "Owner", DateTime.UtcNow, null, "Owner", DateTime.UtcNow, null, null, new List<StocktakeLineDto>()));

        var recountRes = await service.ReviewAsync(stId, new ReviewStocktakeCommand("NEEDS_RECOUNT", null));
        Assert.NotNull(recountRes);
        Assert.Equal("NEEDS_RECOUNT", pendingStk.Status);

        // 9. ReviewAsync: decision APPROVE with surplus and ConfirmSurplusFound = false -> SURPLUS_CONFIRM_REQUIRED
        pendingStk.Status = "PENDING_APPROVAL";
        pendingStk.Lines = new List<StocktakeLine>
        {
            new() { Id = Guid.NewGuid(), SystemQty = 10m, GoodQty = 15m, VarianceQty = 5m }
        };
        var exSurplus = await Assert.ThrowsAsync<BusinessException>(() =>
            service.ReviewAsync(stId, new ReviewStocktakeCommand("APPROVE", null, ConfirmSurplusFound: false)));
        Assert.Equal("SURPLUS_CONFIRM_REQUIRED", exSurplus.ErrorCode);
    }

    // =========================================================================
    // SECTION 6: CatalogService Create & Update Validation Branches
    // =========================================================================

    [Fact]
    public async Task CatalogService_CreateAndUpdate_Validation_Branches()
    {
        var prodRepo = new Mock<IProductRepository>();
        var catRepo = new Mock<ICategoryRepository>();
        var uow = new Mock<IUnitOfWork>();
        var tenant = new FakeTenantContext();

        var service = new CatalogService(prodRepo.Object, catRepo.Object, uow.Object, tenant);

        var validCatId = Guid.NewGuid();

        // 1. CreateProductAsync: empty name -> ArgumentException
        var reqEmptyName = new ProductCreateRequest("", validCatId, 10000);
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateProductAsync(reqEmptyName));

        // 2. CreateProductAsync: negative selling price -> ArgumentException
        var reqNegPrice = new ProductCreateRequest("Product 1", validCatId, -500);
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateProductAsync(reqNegPrice));

        // 3. CreateProductAsync: minStock > maxStock -> ArgumentException
        var reqInvalidStock = new ProductCreateRequest("Product 1", validCatId, 10000, MinStock: 50, MaxStock: 10);
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateProductAsync(reqInvalidStock));

        // 4. CreateProductAsync: category not found -> BusinessException CATEGORY_NOT_FOUND
        catRepo.Setup(r => r.GetByIdAsync(validCatId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Category?)null);
        var reqNoCat = new ProductCreateRequest("Product 1", validCatId, 10000);
        var exNoCat = await Assert.ThrowsAsync<BusinessException>(() => service.CreateProductAsync(reqNoCat));
        Assert.Equal("CATEGORY_NOT_FOUND", exNoCat.ErrorCode);

        // 5. UpdateProductAsync: product not found -> NotFoundException
        var prodId = Guid.NewGuid();
        prodRepo.Setup(r => r.GetByIdWithCategoryAsync(prodId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Product?)null);
        var updateReq = new ProductUpdateRequest("Product Updated", validCatId, 15000);
        await Assert.ThrowsAsync<NotFoundException>(() => service.UpdateProductAsync(prodId, updateReq));

        // 6. DeleteProductAsync: product not found -> NotFoundException
        prodRepo.Setup(r => r.GetByIdAsync(prodId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Product?)null);
        await Assert.ThrowsAsync<NotFoundException>(() => service.DeleteProductAsync(prodId));

        // 7. DeleteProductAsync: product has orders -> BusinessException PRODUCT_HAS_ORDERS
        var existingProd = new Product { Id = prodId, Name = "Product Active" };
        prodRepo.Setup(r => r.GetByIdAsync(prodId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingProd);
        prodRepo.Setup(r => r.HasOrdersAsync(prodId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var exHasOrders = await Assert.ThrowsAsync<BusinessException>(() => service.DeleteProductAsync(prodId));
        Assert.Equal("PRODUCT_HAS_ORDERS", exHasOrders.ErrorCode);
    }
}
