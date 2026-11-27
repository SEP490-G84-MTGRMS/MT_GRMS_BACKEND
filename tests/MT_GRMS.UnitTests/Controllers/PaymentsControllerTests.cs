using System;
using System.Collections.Generic;
using System.Reflection;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using MT_GRMS.Api.Controllers;
using MT_GRMS.Api.Hubs;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Common.Services;
using MT_GRMS.Application.Notifications;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Domain.Entities.Sales;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Infrastructure.Persistence;
using Npgsql;
using Xunit;

namespace MT_GRMS.UnitTests.Controllers;

public class PaymentsControllerTests
{
    private readonly Mock<ITenantContext> _mockTenant;
    private readonly Mock<IHttpClientFactory> _mockHttpClientFactory;
    private readonly Mock<IConfiguration> _mockConfig;
    private readonly Mock<ILogger<PaymentsController>> _mockLogger;
    private const string TenantId = "tenant-pay-01";

    public PaymentsControllerTests()
    {
        _mockTenant = new Mock<ITenantContext>();
        _mockTenant.SetupGet(t => t.CurrentTenantId).Returns(TenantId);

        _mockHttpClientFactory = new Mock<IHttpClientFactory>();
        _mockConfig = new Mock<IConfiguration>();
        _mockLogger = new Mock<ILogger<PaymentsController>>();
    }

    private AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"PaymentsDb_{Guid.NewGuid()}")
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new AppDbContext(options, _mockTenant.Object);
    }

    private PaymentsController CreateController(AppDbContext context)
        => PaymentTestSupport.CreateController(context, _mockHttpClientFactory.Object, _mockConfig.Object, _mockLogger.Object, _mockTenant.Object);

    private static int _ipCounter = 1;
    private static ControllerContext CreateContext(string role = "STORE_OWNER", string? tenant = TenantId, string? ip = null)
    {
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.Role, role),
            new Claim("role", role),
            new Claim(ClaimTypes.Name, "TestUser")
        };
        if (!string.IsNullOrEmpty(tenant))
        {
            claims.Add(new Claim("tenant_id", tenant));
            claims.Add(new Claim("TenantId", tenant));
        }

        var identity = new ClaimsIdentity(claims, "TestAuth");
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(identity)
        };
        var number = Interlocked.Increment(ref _ipCounter);
        var assignedIp = ip ?? $"10.0.{number / 250}.{number % 250}";
        httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(assignedIp);

        return new ControllerContext { HttpContext = httpContext };
    }

    [Fact]
    public async Task GetPublicQrConfig_ShouldHandleNotFound_AndSuccess()
    {
        using var context = CreateDbContext();
        var controller = CreateController(context);
        controller.ControllerContext = CreateContext();

        // 1. Not found when nothing configured
        var resNotFound = await controller.GetPublicQrConfig(TenantId, null, null, CancellationToken.None);
        Assert.IsType<NotFoundObjectResult>(resNotFound);

        // 2. Branch requested but no branch payment account
        var resBranchNotFound = await controller.GetPublicQrConfig(TenantId, Guid.NewGuid(), null, CancellationToken.None);
        Assert.IsType<NotFoundObjectResult>(resBranchNotFound);

        // 3. Config exists -> Success
        PaymentTestSupport.AddTenant(context, TenantId, enabled: true);
        var bank = PaymentTestSupport.AddBank(context, TenantId, "0987654321");
        await context.SaveChangesAsync();

        var resSuccess = await controller.GetPublicQrConfig(TenantId, bank.BranchId, null, CancellationToken.None);
        Assert.IsType<OkObjectResult>(resSuccess);
    }

    [Fact]
    public async Task GetPaymentConfig_ShouldHandleForbidden_AndSuccess()
    {
        using var context = CreateDbContext();
        var controller = CreateController(context);

        // 1. Forbidden for STAFF
        controller.ControllerContext = CreateContext("STAFF");
        var resForbid = await controller.GetPaymentConfig(CancellationToken.None);
        Assert.IsType<ForbidResult>(resForbid);

        // 2. Success for STORE_OWNER
        controller.ControllerContext = CreateContext("STORE_OWNER");
        PaymentTestSupport.AddTenant(context, TenantId, enabled: true);
        PaymentTestSupport.AddBank(context, TenantId, "1234567890", "VCB");
        await context.SaveChangesAsync();

        var resOk = await controller.GetPaymentConfig(CancellationToken.None);
        Assert.IsType<OkObjectResult>(resOk);
    }

    [Fact]
    public async Task SavePaymentConfig_ShouldHandleForbidden_Validation_AndSuccess()
    {
        using var context = CreateDbContext();
        var controller = CreateController(context);

        // 1. Forbidden for STAFF
        controller.ControllerContext = CreateContext("STAFF");
        var req = new SavePaymentConfigRequest { BankCode = "MB", BankAccountNumber = "12345", BankAccountName = "TEST" };
        var resForbid = await controller.SavePaymentConfig(req, CancellationToken.None);
        Assert.IsType<ForbidResult>(resForbid);

        // 2. Validation error: empty bank account
        controller.ControllerContext = CreateContext("STORE_OWNER");
        PaymentTestSupport.AddTenant(context, TenantId);
        var bank = PaymentTestSupport.AddBank(context, TenantId, "1234567890");
        await context.SaveChangesAsync();
        var branchId = bank.BranchId!.Value;
        var reqEmpty = new SavePaymentConfigRequest { BankCode = "MB", BankAccountNumber = "", BankAccountName = "TEST", BranchIds = [branchId] };
        var resEmpty = await controller.SavePaymentConfig(reqEmpty, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(resEmpty);

        // 2b. Bank account number too long
        var reqLongNumber = new SavePaymentConfigRequest { BankCode = "MB", BankAccountNumber = new string('0', 31), BankAccountName = "TEST", BranchIds = [branchId] };
        Assert.IsType<BadRequestObjectResult>(await controller.SavePaymentConfig(reqLongNumber, CancellationToken.None));

        // 2c. Invalid branch id
        var reqBadBranch = new SavePaymentConfigRequest
        {
            BankCode = "MB",
            BankAccountNumber = "123456789",
            BankAccountName = "TEST",
            BranchIds = new List<Guid> { Guid.NewGuid() }
        };
        Assert.IsType<BadRequestObjectResult>(await controller.SavePaymentConfig(reqBadBranch, CancellationToken.None));

        // 2d. Invalid payment account id
        var reqBadPaymentAcc = new SavePaymentConfigRequest
        {
            BankCode = "MB",
            BankAccountNumber = "123456789",
            BankAccountName = "TEST",
            PaymentAccountId = Guid.NewGuid(),
            BranchIds = [branchId],
            IsActive = false
        };
        Assert.IsType<BadRequestObjectResult>(await controller.SavePaymentConfig(reqBadPaymentAcc, CancellationToken.None));

        // 2e. Short Sepay API key
        var reqShortKey = new SavePaymentConfigRequest
        {
            BankCode = "MB",
            BankAccountNumber = "123456789",
            BankAccountName = "TEST",
            SepayApiKey = "short_key",
            BranchIds = [branchId]
        };
        Assert.IsType<BadRequestObjectResult>(await controller.SavePaymentConfig(reqShortKey, CancellationToken.None));

        // 3. Success with valid Branch
        var reqSuccess = new SavePaymentConfigRequest
        {
            BankCode = "MB",
            BankName = "MBBank",
            BankAccountNumber = "1234567890",
            BankAccountName = "NGUYEN VAN C",
            SepayApiKey = "SEPAY_API_KEY_1234567890_TEST",
            BranchIds = [branchId],
            IsActive = true
        };
        var resSuccess = await controller.SavePaymentConfig(reqSuccess, CancellationToken.None);
        Assert.IsType<OkObjectResult>(resSuccess);

        // 4. Update existing config and retain branches
        reqSuccess.BankAccountName = "NGUYEN VAN C UPDATED";
        var resUpdate = await controller.SavePaymentConfig(reqSuccess, CancellationToken.None);
        Assert.IsType<OkObjectResult>(resUpdate);
    }

    [Fact]
    public async Task RevokeSePayApiKey_ShouldHandleForbidden_AndSuccess()
    {
        using var context = CreateDbContext();
        var controller = CreateController(context);

        // Forbidden
        controller.ControllerContext = CreateContext("STAFF");
        var resForbid = await controller.RevokeSePayApiKey(CancellationToken.None);
        var objRes = Assert.IsType<ObjectResult>(resForbid);
        Assert.Equal(StatusCodes.Status403Forbidden, objRes.StatusCode);

        // Success when config exists
        controller.ControllerContext = CreateContext("STORE_OWNER");
        var tenant = PaymentTestSupport.AddTenant(context, TenantId, enabled: true, apiKey: "test-api-key", secret: "test-webhook-secret");
        await context.SaveChangesAsync();

        var resSuccess = await controller.RevokeSePayApiKey(CancellationToken.None);
        Assert.IsType<OkObjectResult>(resSuccess);
        Assert.Empty(tenant.SepayApiKeyEncrypted);
        Assert.False(tenant.IsSepayEnabled);
        Assert.NotEqual("test-webhook-secret", tenant.SepayWebhookSecret);
    }

    [Fact]
    public async Task CheckOrderStatus_ShouldHandleNotFound_AndSuccess()
    {
        using var context = CreateDbContext();
        var controller = CreateController(context);
        controller.ControllerContext = CreateContext();

        // NotFound
        var resNotFound = await controller.CheckOrderStatus("ORD-NONEXISTENT", CancellationToken.None);
        Assert.IsType<NotFoundObjectResult>(resNotFound);

        // Found
        context.Orders.Add(new Order
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            OrderCode = "HD1001",
            TotalAmount = 100_000m,
            FinalAmount = 100_000m,
            PaymentMethod = "VIETQR",
            PaymentStatus = "PAID",
            Status = "COMPLETED",
            OrderDate = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var resFound = await controller.CheckOrderStatus("HD1001", CancellationToken.None);
        Assert.IsType<OkObjectResult>(resFound);
    }

    [Fact]
    public async Task TestSecurityLayers_ShouldHandleForbidden_AndSuccess()
    {
        using var context = CreateDbContext();
        var controller = CreateController(context);

        // Forbidden
        controller.ControllerContext = CreateContext("STAFF");
        var resForbid = await controller.TestSecurityLayers(CancellationToken.None);
        var objRes = Assert.IsType<ObjectResult>(resForbid);
        Assert.Equal(StatusCodes.Status403Forbidden, objRes.StatusCode);

        // Success
        controller.ControllerContext = CreateContext("STORE_OWNER");
        var resSuccess = await controller.TestSecurityLayers(CancellationToken.None);
        Assert.IsType<OkObjectResult>(resSuccess);
    }

    [Fact]
    public async Task GetSePayBankAccounts_ShouldHandleForbidden_NoConfig_AndMockKey()
    {
        using var context = CreateDbContext();
        var controller = CreateController(context);

        // 1. Forbidden for STAFF
        controller.ControllerContext = CreateContext("STAFF");
        var resForbid = await controller.GetSePayBankAccounts(CancellationToken.None);
        var objRes = Assert.IsType<ObjectResult>(resForbid);
        Assert.Equal(StatusCodes.Status403Forbidden, objRes.StatusCode);

        // 2. STORE_OWNER without config -> 502 Bad Gateway
        controller.ControllerContext = CreateContext("STORE_OWNER");
        var resNoConfig = await controller.GetSePayBankAccounts(CancellationToken.None);
        var objBadGateway = Assert.IsType<ObjectResult>(resNoConfig);
        Assert.Equal(StatusCodes.Status502BadGateway, objBadGateway.StatusCode);

        // 3. STORE_OWNER with mock key -> 502 Bad Gateway
        PaymentTestSupport.AddTenant(context, TenantId, enabled: true, apiKey: "mock_key_123");
        await context.SaveChangesAsync();

        var resMock = await controller.GetSePayBankAccounts(CancellationToken.None);
        var objMock = Assert.IsType<ObjectResult>(resMock);
        Assert.Equal(StatusCodes.Status502BadGateway, objMock.StatusCode);
    }

    [Fact]
    public async Task HandleSePayWebhook_ShouldRejectInvalidRequests_AndAcknowledgeReplay()
    {
        using var context = CreateDbContext();
        var controller = CreateController(context);

        var payload = new SePayWebhookPayload
        {
            Id = 12345,
            Gateway = "VietQR",
            TransactionDate = DateTime.UtcNow.ToString("O"),
            AccountNumber = "12345678",
            AmountIn = 100_000m,
            TransactionContent = "Thanh toan HD1001",
            ReferenceNumber = "REF-999"
        };

        var validContext = CreateContext();
        controller.ControllerContext = validContext;

        // 2. Missing Authorization header -> 401 Unauthorized
        var resNoAuth = await controller.HandleSePayWebhook(payload, CancellationToken.None);
        Assert.IsType<UnauthorizedObjectResult>(resNoAuth);

        // 3. Empty Authorization token -> 401 Unauthorized
        validContext.HttpContext.Request.Headers["Authorization"] = "Bearer   ";
        var resEmptyAuth = await controller.HandleSePayWebhook(payload, CancellationToken.None);
        Assert.IsType<UnauthorizedObjectResult>(resEmptyAuth);

        // Set valid header
        validContext.HttpContext.Request.Headers["Authorization"] = "Bearer mock_apikey";

        // 4. Unknown key on an orderless ping -> 401 Unauthorized
        var payloadBadContent = new SePayWebhookPayload
        {
            Id = 12345,
            TransactionContent = ""
        };
        var resBadContent = await controller.HandleSePayWebhook(payloadBadContent, CancellationToken.None);
        Assert.IsType<UnauthorizedObjectResult>(resBadContent);

        // 5. An unknown store token cannot be used to probe order codes.
        payload.TransactionContent = "HD999999";
        var resNotFound = await controller.HandleSePayWebhook(payload, CancellationToken.None);
        Assert.IsType<UnauthorizedObjectResult>(resNotFound);

        // Seed order
        var order = new Order
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            OrderCode = "HD1001",
            TotalAmount = 100_000m,
            FinalAmount = 100_000m,
            PaymentMethod = "VIETQR",
            PaymentStatus = "PENDING",
            Status = "PendingPayment",
            OrderDate = DateTime.UtcNow
        };
        context.Orders.Add(order);

        // Seed config with webhook secret
        PaymentTestSupport.AddTenant(context, TenantId, enabled: true, secret: "SECRET_WEBHOOK_123");
        var bank = PaymentTestSupport.AddBank(context, TenantId, "12345678");
        order.BranchId = bank.BranchId!.Value;
        order.FinanceAccountId = bank.Id;
        await context.SaveChangesAsync();

        payload.TransactionContent = "HD1001";

        // 6. Invalid API key / secret -> 401 Unauthorized
        validContext.HttpContext.Request.Headers["Authorization"] = "Bearer WRONG_KEY";
        var resWrongKey = await controller.HandleSePayWebhook(payload, CancellationToken.None);
        Assert.IsType<UnauthorizedObjectResult>(resWrongKey);

        // Use correct secret
        validContext.HttpContext.Request.Headers["Authorization"] = "Bearer SECRET_WEBHOOK_123";

        // 7. Account number mismatch -> 400 BadRequest
        payload.AccountNumber = "88888888";
        var resWrongAcc = await controller.HandleSePayWebhook(payload, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(resWrongAcc);
        payload.AccountNumber = "12345678";

        // 8. Idempotency replay check -> 200 Ok with isIdempotentReplay = true
        context.PaymentTransactions.Add(new PaymentTransaction
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            BankTransactionId = "12345",
            OrderId = order.Id,
            Gateway = "VietQR",
            AmountIn = 100_000m,
            TransactionDate = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var resIdempotent = await controller.HandleSePayWebhook(payload, CancellationToken.None);
                Assert.True(PaymentTestSupport.Json(Assert.IsType<OkObjectResult>(resIdempotent)).GetProperty("isIdempotentReplay").GetBoolean());
    }

    [Fact]
    public async Task GetSePayBankAccounts_ShouldHandleForbidden_NoConfig_AndSuccess()
    {
        using var context = CreateDbContext();

        // 1. Forbidden when not store owner or admin
        var staffContext = CreateContext(role: "STAFF");
        var controllerStaff = CreateController(context);
        controllerStaff.ControllerContext = staffContext;
        var resForbidden = await controllerStaff.GetSePayBankAccounts(CancellationToken.None);
        var obj403 = Assert.IsType<ObjectResult>(resForbidden);
        Assert.Equal(403, obj403.StatusCode);

        // 2. Owner context, but no config / no key -> 502 Bad Gateway
        var ownerContext = CreateContext(role: "STORE_OWNER");
        var controllerOwner = CreateController(context);
        controllerOwner.ControllerContext = ownerContext;
        var resNoConfig = await controllerOwner.GetSePayBankAccounts(CancellationToken.None);
        var obj502 = Assert.IsType<ObjectResult>(resNoConfig);
        Assert.Equal(502, obj502.StatusCode);

        // 3. Config with real key and mock HTTP client returning SePay bankaccounts
        PaymentTestSupport.AddTenant(context, TenantId, enabled: true, apiKey: "REAL_SEPAY_API_KEY_12345");
        await context.SaveChangesAsync();

        var jsonResponse = @"{
            ""bankaccounts"": [
                {
                    ""account_number"": ""0987654321"",
                    ""account_holder_name"": ""NGUYEN VAN A"",
                    ""bank_short_name"": ""MB"",
                    ""bank_full_name"": ""Military Commercial Joint Stock Bank"",
                    ""label"": ""TK Chinh""
                }
            ]
        }";

        var mockHandler = new MockHttpMessageHandler(_ => new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new System.Net.Http.StringContent(jsonResponse, System.Text.Encoding.UTF8, "application/json")
        });
        var httpClient = new System.Net.Http.HttpClient(mockHandler);
        _mockHttpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);

        var resSuccess = await controllerOwner.GetSePayBankAccounts(CancellationToken.None);
        var obj200 = Assert.IsType<OkObjectResult>(resSuccess);
        Assert.NotNull(obj200.Value);

        // 4. HTTP client throws exception -> fallback to 502 Bad Gateway
        var mockThrowingHandler = new MockHttpMessageHandler(_ => throw new System.Net.Http.HttpRequestException("Connection timeout"));
        var throwingHttpClient = new System.Net.Http.HttpClient(mockThrowingHandler);
        _mockHttpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(throwingHttpClient);

        var resThrows = await controllerOwner.GetSePayBankAccounts(CancellationToken.None);
        var objThrows502 = Assert.IsType<ObjectResult>(resThrows);
        Assert.Equal(502, objThrows502.StatusCode);
    }

    [Fact]
    public async Task HandleSePayWebhook_SecurityBranches_ShouldWork()
    {
        using var context = CreateDbContext();
        var controller = CreateController(context);

        var localContext = CreateContext();
        controller.ControllerContext = localContext;

        var payload = new SePayWebhookPayload
        {
            Id = 55555,
            AccountNumber = "12345678",
            AmountIn = 100_000m,
            TransactionContent = "HD1001"
        };
        // Missing Authorization header
        var resMissingAuth = await controller.HandleSePayWebhook(payload, CancellationToken.None);
        Assert.IsType<UnauthorizedObjectResult>(resMissingAuth);

        // 3. Empty Authorization token -> 401 Unauthorized
        localContext.HttpContext.Request.Headers["Authorization"] = "Bearer   ";
        var resEmptyAuth = await controller.HandleSePayWebhook(payload, CancellationToken.None);
        Assert.IsType<UnauthorizedObjectResult>(resEmptyAuth);

        // 4. Double check failure when SePay API returns 404
        localContext.HttpContext.Request.Headers["Authorization"] = "Bearer REAL_KEY_999";
        var order = new Order
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            OrderCode = "HD1001",
            Status = "PendingPayment",
            PaymentStatus = "UNPAID",
            FinalAmount = 100_000m,
            CreatedAt = DateTime.UtcNow
        };
        context.Orders.Add(order);
        PaymentTestSupport.AddTenant(context, TenantId, enabled: true, apiKey: "REAL_KEY_999", secret: "SECRET_WEBHOOK_123");
        var bank = PaymentTestSupport.AddBank(context, TenantId, "12345678");
        order.BranchId = bank.BranchId!.Value;
        order.FinanceAccountId = bank.Id;
        await context.SaveChangesAsync();

        var failHandler = new MockHttpMessageHandler(_ => new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
        _mockHttpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new System.Net.Http.HttpClient(failHandler));

        var resDoubleCheckFail = await controller.HandleSePayWebhook(payload, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(resDoubleCheckFail);

        // 5. Double check returns transaction with mismatched order code
        var mismatchJson = @"{
            ""transaction"": {
                ""amount_in"": 100000,
                ""transaction_content"": ""HD8888"",
                ""account_number"": ""12345678""
            }
        }";
        var mismatchHandler = new MockHttpMessageHandler(_ => new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new System.Net.Http.StringContent(mismatchJson, System.Text.Encoding.UTF8, "application/json")
        });
        _mockHttpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new System.Net.Http.HttpClient(mismatchHandler));

        var resMismatchCode = await controller.HandleSePayWebhook(payload, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(resMismatchCode);
    }

    [PaymentPostgresFact]
    public async Task HandleSePayWebhook_SandboxHappyPath_AndDuplicate_ShouldWork()
    {
        await using var database = await PaymentTestSupport.OpenPostgresAsync(_mockTenant.Object);
        var context = database.Context;
        var controller = CreateController(context);

        var localContext = CreateContext();
        localContext.HttpContext.Request.Headers["Authorization"] = "Bearer mock_sepay_key_123";
        controller.ControllerContext = localContext;

        var order = new Order
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            OrderCode = "HD2001",
            Status = "PendingPayment",
            PaymentStatus = "UNPAID",
            FinalAmount = 150_000m,
            CreatedAt = DateTime.UtcNow
        };
        context.Orders.Add(order);
        PaymentTestSupport.AddTenant(context, TenantId, enabled: true, apiKey: "mock_sepay_key_123", secret: "SECRET_WEBHOOK_123");
        var bank = PaymentTestSupport.AddBank(context, TenantId, "999888777");
        order.BranchId = bank.BranchId!.Value;
        order.FinanceAccountId = bank.Id;
        await context.SaveChangesAsync();

        var payload = new SePayWebhookPayload
        {
            Id = 99999,
            AccountNumber = "999888777",
            AmountIn = 150_000m,
            TransactionContent = "HD2001",
            Gateway = "VietQR"
        };

        // 1. Success payment
        var resSuccess = await controller.HandleSePayWebhook(payload, CancellationToken.None);
        var okObj = Assert.IsType<OkObjectResult>(resSuccess);
        Assert.Equal("PAID", order.PaymentStatus);
        Assert.Equal("Completed", order.Status);

        // 2. Duplicate transaction -> idempotent Ok
        var resDup = await controller.HandleSePayWebhook(payload, CancellationToken.None);
        Assert.True(PaymentTestSupport.Json(Assert.IsType<OkObjectResult>(resDup)).GetProperty("isIdempotentReplay").GetBoolean());
        Assert.Single(await context.PaymentTransactions.ToListAsync());
        Assert.Single(await context.CashbookEntries.ToListAsync());
        Assert.Equal(150_000m, bank.CurrentBalance);
    }

    [PaymentPostgresFact]
    public async Task HandleSePayWebhook_TestMode_VerifiesSandboxLedgerBeforePostingOnce()
    {
        await using var database = await PaymentTestSupport.OpenPostgresAsync(_mockTenant.Object);
        var context = database.Context;
        var config = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["SePay:UseTestMode"] = "true" }).Build();
        var controller = PaymentTestSupport.CreateController(context, _mockHttpClientFactory.Object,
            config, _mockLogger.Object, _mockTenant.Object);
        controller.ControllerContext = CreateContext();
        const string token = "sandbox-access-token-fixture";
        const string webhookKey = "sandbox-webhook-key-fixture";
        controller.Request.Headers["Authorization"] = $"Bearer {webhookKey}";
        PaymentTestSupport.AddTenant(context, TenantId, enabled: true, apiKey: token, secret: webhookKey);
        var bank = PaymentTestSupport.AddBank(context, TenantId, "999888777");
        var order = new Order
        {
            Id = Guid.NewGuid(), TenantId = TenantId, BranchId = bank.BranchId!.Value,
            FinanceAccountId = bank.Id, OrderCode = "HD2001", Status = "PendingPayment",
            PaymentStatus = "UNPAID", FinalAmount = 150_000m, CreatedAt = DateTime.UtcNow
        };
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var ledgerId = Guid.NewGuid();
        var ledgerAmount = 149_000;
        var lookups = 0;
        var handler = new MockHttpMessageHandler(request =>
        {
            lookups++;
            Assert.Equal("userapi-sandbox.sepay.vn", request.RequestUri!.Host);
            Assert.Equal("/v2/transactions", request.RequestUri.AbsolutePath);
            Assert.Contains("reference_number=QAFT2001", request.RequestUri.Query);
            Assert.Equal(token, request.Headers.Authorization?.Parameter);
            return new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new System.Net.Http.StringContent(
                    $$"""{"status":"success","data":[{"id":"{{ledgerId}}","reference_number":"QAFT2001","account_number":"999888777","transfer_type":"in","amount_in":{{ledgerAmount}},"code":"HD2001","transaction_content":"HD2001"}]}""")
            };
        });
        _mockHttpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(() => new System.Net.Http.HttpClient(handler, disposeHandler: false));
        var payload = new SePayWebhookPayload
        {
            Id = 99001, AccountNumber = "999888777", AmountIn = 150_000m,
            TransactionContent = "HD2001", ReferenceNumber = "QAFT2001", Gateway = "MB"
        };

        Assert.IsType<BadRequestObjectResult>(await controller.HandleSePayWebhook(payload, CancellationToken.None));
        Assert.Empty(await context.PaymentTransactions.ToListAsync());
        Assert.Empty(await context.CashbookEntries.ToListAsync());
        Assert.Equal("UNPAID", order.PaymentStatus);

        ledgerAmount = 150_000;
        Assert.IsType<OkObjectResult>(await controller.HandleSePayWebhook(payload, CancellationToken.None));
        var duplicate = Assert.IsType<OkObjectResult>(await controller.HandleSePayWebhook(payload, CancellationToken.None));
        Assert.True(PaymentTestSupport.Json(duplicate).GetProperty("isIdempotentReplay").GetBoolean());
        Assert.Equal(3, lookups);
        Assert.Equal($"test:{ledgerId:D}", (await context.PaymentTransactions.SingleAsync()).BankTransactionId);
        Assert.Equal(150_000m, (await context.CashbookEntries.SingleAsync()).Amount);
        Assert.Equal(150_000m, bank.CurrentBalance);
        Assert.Equal("PAID", order.PaymentStatus);
        Assert.Equal("Completed", order.Status);
    }

    [PaymentPostgresFact]
    public async Task HandleSePayWebhook_SameOrderCodeAcrossStores_PaysAuthenticatedStoreOnly()
    {
        await using var database = await PaymentTestSupport.OpenPostgresAsync(_mockTenant.Object);
        var context = database.Context;
        var otherTenantId = "tenant-other-store";
        var otherBank = PaymentTestSupport.AddBank(context, otherTenantId, "111222333");
        var targetBank = PaymentTestSupport.AddBank(context, TenantId, "999888777");
        PaymentTestSupport.AddTenant(context, otherTenantId, enabled: true, apiKey: "mock_other_key", secret: "OTHER_SECRET");
        PaymentTestSupport.AddTenant(context, TenantId, enabled: true, apiKey: "mock_sepay_key_123", secret: "TARGET_SECRET");
        var otherOrder = new Order
        {
            Id = Guid.NewGuid(), TenantId = otherTenantId, BranchId = otherBank.BranchId!.Value,
            FinanceAccountId = otherBank.Id, OrderCode = "HD2002", Status = "PendingPayment",
            PaymentStatus = "UNPAID", FinalAmount = 150_000m, CreatedAt = DateTime.UtcNow
        };
        var targetOrder = new Order
        {
            Id = Guid.NewGuid(), TenantId = TenantId, BranchId = targetBank.BranchId!.Value,
            FinanceAccountId = targetBank.Id, OrderCode = "HD2002", Status = "PendingPayment",
            PaymentStatus = "UNPAID", FinalAmount = 150_000m, CreatedAt = DateTime.UtcNow
        };
        context.Orders.AddRange(otherOrder, targetOrder);
        await context.SaveChangesAsync();

        var controller = CreateController(context);
        var requestContext = CreateContext();
        requestContext.HttpContext.Request.Headers["Authorization"] = "Bearer TARGET_SECRET";
        controller.ControllerContext = requestContext;
        var result = await controller.HandleSePayWebhook(new SePayWebhookPayload
        {
            Id = 2002002, AccountNumber = "999888777", AmountIn = 150_000m,
            TransactionContent = "HD2002", Gateway = "VietQR"
        }, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal("PAID", targetOrder.PaymentStatus);
        Assert.Equal("UNPAID", otherOrder.PaymentStatus);
        Assert.Equal(targetOrder.Id, (await context.PaymentTransactions.SingleAsync()).OrderId);
    }

    [PaymentPostgresFact]
    public async Task HandleSePayWebhook_Underpaid_RecordsPartialPayment()
    {
        await using var database = await PaymentTestSupport.OpenPostgresAsync(_mockTenant.Object);
        var context = database.Context;
        var controller = CreateController(context);

        var localContext = CreateContext();
        localContext.HttpContext.Request.Headers["Authorization"] = "Bearer mock_sepay_key_123";
        controller.ControllerContext = localContext;

        var order = new Order
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            OrderCode = "HD3001",
            Status = "PendingPayment",
            PaymentStatus = "UNPAID",
            FinalAmount = 200_000m,
            CreatedAt = DateTime.UtcNow
        };
        context.Orders.Add(order);
        PaymentTestSupport.AddTenant(context, TenantId, enabled: true, apiKey: "mock_sepay_key_123", secret: "SECRET_WEBHOOK_123");
        var bank = PaymentTestSupport.AddBank(context, TenantId, "999888777");
        order.BranchId = bank.BranchId!.Value;
        order.FinanceAccountId = bank.Id;
        await context.SaveChangesAsync();

        var payload = new SePayWebhookPayload
        {
            Id = 88888,
            AccountNumber = "999888777",
            AmountIn = 50_000m, // underpaid
            TransactionContent = "HD3001"
        };

        var res = await controller.HandleSePayWebhook(payload, CancellationToken.None);
        var result = PaymentTestSupport.Json(Assert.IsType<OkObjectResult>(res));
        Assert.False(result.GetProperty("isFullPayment").GetBoolean());
        Assert.Equal("PARTIALLY_PAID", order.PaymentStatus);
        Assert.Equal(50_000m, order.DepositAmount);
        Assert.Equal(50_000m, bank.CurrentBalance);
        Assert.Equal(50_000m, (await context.CashbookEntries.SingleAsync()).Amount);
    }

    [Fact]
    public async Task CheckOrderStatus_AndTestSecurityLayers_ShouldWork()
    {
        using var context = CreateDbContext();
        var controller = CreateController(context);
        controller.ControllerContext = CreateContext();

        var order = new Order
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            OrderCode = "HD4001",
            Status = "Completed",
            PaymentStatus = "PAID",
            FinalAmount = 50_000m
        };
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        // Check empty orderCode -> NotFound
        var resEmpty = await controller.CheckOrderStatus(" ", CancellationToken.None);
        Assert.IsType<NotFoundObjectResult>(resEmpty);

        // Check not found
        var resNotFound = await controller.CheckOrderStatus("HD9999", CancellationToken.None);
        Assert.IsType<NotFoundObjectResult>(resNotFound);

        // Check found
        var resFound = await controller.CheckOrderStatus("HD4001", CancellationToken.None);
        var okFound = Assert.IsType<OkObjectResult>(resFound);

        // TestSecurityLayers
        var resSec = await controller.TestSecurityLayers(CancellationToken.None);
        Assert.IsType<OkObjectResult>(resSec);
    }

    [Fact]
    public void NormalizeBankCode_AllBankVariants_ShouldMapCorrectly()
    {
        var method = typeof(PaymentsController).GetMethod("NormalizeBankCode", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        string Normalize(string brand) => (string)method.Invoke(null, new object[] { brand })!;

        Assert.Equal("MB", Normalize("MB"));
        Assert.Equal("MB", Normalize("Ngân hàng MB"));
        Assert.Equal("VCB", Normalize("Vietcom"));
        Assert.Equal("VCB", Normalize("VCB"));
        Assert.Equal("TCB", Normalize("Techcom"));
        Assert.Equal("TCB", Normalize("TCB"));
        Assert.Equal("ACB", Normalize("ACB"));
        Assert.Equal("TPB", Normalize("TPB"));
        Assert.Equal("VPB", Normalize("VPB"));
        Assert.Equal("CTG", Normalize("VietinBank"));
        Assert.Equal("CTG", Normalize("CTG"));
        Assert.Equal("CTG", Normalize("ICB"));
        Assert.Equal("BIDV", Normalize("BIDV"));
        Assert.Equal("STB", Normalize("Sacom"));
        Assert.Equal("STB", Normalize("STB"));
        Assert.Equal("VIB", Normalize("VIB"));
        Assert.Equal("OCB", Normalize("OCB"));
        Assert.Equal("SHB", Normalize("SHB"));
        Assert.Equal("OTHER_BANK", Normalize("OTHER_BANK"));
    }

    [Fact]
    public void ExtractTenantPrefixAndOrderCode_Variants_ShouldParseProperly()
    {
        var method = typeof(PaymentsController).GetMethod("ExtractTenantPrefixAndOrderCode", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        (string, string) Extract(string content) => (((string, string))method.Invoke(null, new object[] { content })!);

        Assert.Equal(("", ""), Extract(""));
        Assert.Equal(("", ""), Extract("   "));
        Assert.Equal(("AN02", "HD1005"), Extract("AN02_HD1005"));
        Assert.Equal(("AN02", "HD1005"), Extract("AN02 1005"));
        Assert.Equal(("MT01", "HD9999"), Extract("MT01_HD9999"));
        Assert.Equal(("", "HD1234"), Extract("HD1234"));
        Assert.Equal(("", "TEST"), Extract("test content"));
    }

    [Fact]
    public void GetAuthenticatedTenantId_Variants_ShouldReadCorrectClaim()
    {
        var method = typeof(PaymentsController).GetMethod("GetAuthenticatedTenantId", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);

        using var context = CreateDbContext();
        var controller = CreateController(context);

        // 1. tenant_id claim
        var userWithTenantId = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("tenant_id", "t-01")
        }));
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = userWithTenantId } };
        Assert.Equal("t-01", (string?)method.Invoke(controller, null));

        // 2. TenantId claim
        var userWithAltClaim = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("TenantId", "t-02")
        }));
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = userWithAltClaim } };
        Assert.Equal("t-02", (string?)method.Invoke(controller, null));

        // 3. No claim
        var emptyUser = new ClaimsPrincipal(new ClaimsIdentity());
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = emptyUser } };
        Assert.Null((string?)method.Invoke(controller, null));
    }

    [Fact]
    public async Task GetPaymentConfig_WhenNoConfig_ReturnsDefaultConfig()
    {
        using var context = CreateDbContext();
        var controller = CreateController(context);
        controller.ControllerContext = CreateContext();

        PaymentTestSupport.AddTenant(context, TenantId);
        await context.SaveChangesAsync();
        var result = await controller.GetPaymentConfig(CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);
    }

    [Fact]
    public async Task SavePaymentConfig_InvalidAccountNumber_ReturnsBadRequest()
    {
        using var context = CreateDbContext();
        var controller = CreateController(context);
        controller.ControllerContext = CreateContext();

        PaymentTestSupport.AddTenant(context, TenantId);
        var bank = PaymentTestSupport.AddBank(context, TenantId, "1234567890");
        await context.SaveChangesAsync();

        var req = new SavePaymentConfigRequest
        {
            BranchIds = [bank.BranchId!.Value],
            BankCode = "MB",
            BankName = "MBBank",
            BankAccountNumber = "123",
            BankAccountName = "NGUYEN VAN A",
            SepayApiKey = "sepay-key-123",
            IsActive = true
        };
        var result = await controller.SavePaymentConfig(req, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task SavePaymentConfig_FirstTimeNoApiKey_ReturnsBadRequest()
    {
        using var context = CreateDbContext();
        var controller = CreateController(context);
        controller.ControllerContext = CreateContext();

        PaymentTestSupport.AddTenant(context, TenantId);
        var bank = PaymentTestSupport.AddBank(context, TenantId, "1234567890");
        await context.SaveChangesAsync();

        var req = new SavePaymentConfigRequest
        {
            BranchIds = [bank.BranchId!.Value],
            BankCode = "MB",
            BankName = "MBBank",
            BankAccountNumber = "1234567890",
            BankAccountName = "NGUYEN VAN A",
            SepayApiKey = "",
            IsActive = true
        };
        var result = await controller.SavePaymentConfig(req, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task SavePaymentConfig_WithApprovalThresholds_AndUnencryptedKey_SavesSuccessfully()
    {
        using var context = CreateDbContext();
        var controller = CreateController(context);
        controller.ControllerContext = CreateContext();

        PaymentTestSupport.AddTenant(context, TenantId);
        var bank = PaymentTestSupport.AddBank(context, TenantId, "1234567890");
        await context.SaveChangesAsync();

        var req = new SavePaymentConfigRequest
        {
            BranchIds = [bank.BranchId!.Value],
            BankCode = "MB",
            BankName = "MBBank",
            BankAccountNumber = "1234567890",
            BankAccountName = "NGUYEN VAN A",
            SepayApiKey = "raw-sepay-api-key-1234567890",
            IsActive = true,
            CashOutApprovalThreshold = 500000m,
            CashOutShiftTotalLimit = 2000000m
        };

        var result = await controller.SavePaymentConfig(req, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);

        var saved = await context.Tenants.SingleAsync(x => x.Id == TenantId);
        Assert.NotNull(saved);
        Assert.Equal(500000m, saved.CashOutApprovalThreshold);
        Assert.Equal(2000000m, saved.CashOutShiftTotalLimit);
    }

    [Fact]
    public async Task HandleSePayWebhook_RateLimitExceeded_Returns429()
    {
        using var context = CreateDbContext();
        var controller = CreateController(context);

        var ip = $"127.0.0.{(_ipCounter++ % 200) + 10}";
        controller.ControllerContext = CreateContext(role: "ANONYMOUS", tenant: null, ip: ip);

        var payload = new SePayWebhookPayload
        {
            Id = 1,
            Gateway = "MB",
            AccountNumber = "1234567890",
            TransactionDate = DateTime.UtcNow.ToString("O"),
            TransactionContent = "DH-001",
            AmountIn = 10000m,
            ReferenceNumber = "REF"
        };

        IActionResult? lastResult = null;
        for (int i = 0; i < 15; i++)
        {
            lastResult = await controller.HandleSePayWebhook(payload, CancellationToken.None);
        }

        var objResult = Assert.IsType<ObjectResult>(lastResult);
        Assert.Equal(StatusCodes.Status429TooManyRequests, objResult.StatusCode);
    }

    [Fact]
    public async Task HandleSePayWebhook_SePayApiAccountMismatch_ReturnsBadRequest()
    {
        using var context = CreateDbContext();
        var order = new Order
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            OrderCode = "HD1005",
            TotalAmount = 50000m,
            PaymentStatus = "PENDING",
            Status = "COMPLETED"
        };
        context.Orders.Add(order);

        PaymentTestSupport.AddTenant(context, TenantId, enabled: true, apiKey: "real-production-api-key-12345", secret: "SECRET_WEBHOOK_123");
        var bank = PaymentTestSupport.AddBank(context, TenantId, "1234567890");
        order.BranchId = bank.BranchId!.Value;
        order.FinanceAccountId = bank.Id;
        await context.SaveChangesAsync();

        var jsonResponse = "{\"transaction\": {\"amount_in\": 50000, \"transaction_content\": \"HD1005\", \"account_number\": \"99999999\"}}";
        var handler = new MockHttpMessageHandler(_ => new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new System.Net.Http.StringContent(jsonResponse, System.Text.Encoding.UTF8, "application/json")
        });
        var client = new System.Net.Http.HttpClient(handler);
        _mockHttpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);

        var controller = CreateController(context);
        controller.ControllerContext = CreateContext(role: "ANONYMOUS", tenant: null, ip: "127.0.0.1");
        controller.ControllerContext.HttpContext.Request.Headers["Authorization"] = "Bearer SECRET_WEBHOOK_123";

        var payload = new SePayWebhookPayload
        {
            Id = 1,
            Gateway = "MB",
            AccountNumber = "1234567890",
            TransactionDate = DateTime.UtcNow.ToString("O"),
            TransactionContent = "HD1005",
            AmountIn = 50000m,
            ReferenceNumber = "REF"
        };
        var result = await controller.HandleSePayWebhook(payload, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.NotNull(badRequest.Value);
    }

    [Fact]
    public async Task HandleSePayWebhook_SePayApiNetworkError_Returns502()
    {
        using var context = CreateDbContext();
        var order = new Order
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            OrderCode = "HD1005",
            TotalAmount = 50000m,
            PaymentStatus = "PENDING",
            Status = "COMPLETED"
        };
        context.Orders.Add(order);

        PaymentTestSupport.AddTenant(context, TenantId, enabled: true, apiKey: "real-production-api-key-12345", secret: "SECRET_WEBHOOK_123");
        var bank = PaymentTestSupport.AddBank(context, TenantId, "1234567890");
        order.BranchId = bank.BranchId!.Value;
        order.FinanceAccountId = bank.Id;
        await context.SaveChangesAsync();

        var handler = new MockHttpMessageHandler(_ => throw new System.Net.Http.HttpRequestException("Connection timeout"));
        var client = new System.Net.Http.HttpClient(handler);
        _mockHttpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);

        var controller = CreateController(context);
        controller.ControllerContext = CreateContext(role: "ANONYMOUS", tenant: null, ip: "127.0.0.1");
        controller.ControllerContext.HttpContext.Request.Headers["Authorization"] = "Bearer SECRET_WEBHOOK_123";

        var payload = new SePayWebhookPayload
        {
            Id = 1,
            Gateway = "MB",
            AccountNumber = "1234567890",
            TransactionDate = DateTime.UtcNow.ToString("O"),
            TransactionContent = "HD1005",
            AmountIn = 50000m,
            ReferenceNumber = "REF"
        };
        var result = await controller.HandleSePayWebhook(payload, CancellationToken.None);

        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status502BadGateway, objResult.StatusCode);
    }

    [Fact]
    public void GetClientIpAddress_UsesRemoteAddress_And_ResolveTenantIdHeader()
    {
        using var context = CreateDbContext();
        var controller = CreateController(context);

        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("70.41.3.18");
        httpContext.Request.Headers["X-Forwarded-For"] = "203.0.113.195";
        httpContext.Request.Headers["X-Tenant-Id"] = "header-tenant-99";
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var ipMethod = typeof(PaymentsController).GetMethod("GetClientIpAddress", BindingFlags.NonPublic | BindingFlags.Instance);
        var ip = (string?)ipMethod!.Invoke(controller, null);
        Assert.Equal("70.41.3.18", ip);

        var tenantMethod = typeof(PaymentsController).GetMethod("ResolveTenantId", BindingFlags.NonPublic | BindingFlags.Instance);
        var tenant = (string?)tenantMethod!.Invoke(controller, new object?[] { null });
        Assert.Equal("header-tenant-99", tenant);
    }

    private class MockHttpMessageHandler : System.Net.Http.HttpMessageHandler
    {
        private readonly Func<System.Net.Http.HttpRequestMessage, System.Net.Http.HttpResponseMessage> _responseFactory;

        public MockHttpMessageHandler(Func<System.Net.Http.HttpRequestMessage, System.Net.Http.HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(
            System.Net.Http.HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_responseFactory(request));
        }
    }
}

public sealed class PaymentPostgresFactAttribute : FactAttribute
{
    internal const string ConnectionKey = "MT_GRMS_PAYMENT_TEST_POSTGRES";

    public PaymentPostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionKey)))
            Skip = $"Requires an explicit {ConnectionKey} connection.";
    }
}

internal static class PaymentTestSupport
{
    public static PaymentsController CreateController(AppDbContext context, IHttpClientFactory clients, IConfiguration configuration,
        ILogger<PaymentsController> logger, ITenantContext tenant, string? environmentName = null)
    {
        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(value => value.EnvironmentName).Returns(environmentName ?? Environments.Development);
        var hub = new Mock<IHubContext<NotificationHub>>();
        var hubClients = new Mock<IHubClients>();
        hubClients.Setup(clients => clients.Group(It.IsAny<string>())).Returns(Mock.Of<IClientProxy>());
        hub.SetupGet(value => value.Clients).Returns(hubClients.Object);
        return new PaymentsController(context, clients, configuration, logger, tenant, new EphemeralDataProtectionProvider(),
            environment.Object, new AuditHashService(configuration), Mock.Of<INotificationService>(), hub.Object);
    }

    public static Tenant AddTenant(AppDbContext context, string id, bool enabled = false, string apiKey = "", string secret = "")
    {
        var tenant = new Tenant
        {
            Id = id, Name = "Payment test store", Subdomain = id, Code = id,
            SepayApiKeyEncrypted = apiKey, SepayWebhookSecret = secret, IsSepayEnabled = enabled
        };
        context.Tenants.Add(tenant);
        return tenant;
    }

    public static FinanceAccount AddBank(AppDbContext context, string tenantId, string accountNumber, string bankCode = "MB")
    {
        var branch = new Branch { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Main branch", BranchCode = "MAIN" };
        var bank = new FinanceAccount
        {
            Id = Guid.NewGuid(), BranchId = branch.Id, TenantId = tenantId, AccountType = FinanceAccountType.Bank,
            AccountName = "Payment account", AccountHolderName = "TEST STORE", AccountNumber = accountNumber, BankCode = bankCode
        };
        branch.CurrentBankFinanceAccountId = bank.Id;
        context.Branches.Add(branch);
        context.FinanceAccounts.Add(bank);
        return bank;
    }

    public static System.Text.Json.JsonElement Json(OkObjectResult result)
        => System.Text.Json.JsonSerializer.SerializeToElement(result.Value);

    public static async Task<PostgresDatabase> OpenPostgresAsync(ITenantContext tenant)
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        var schema = $"payment_test_{Guid.NewGuid():N}";
        var connection = Environment.GetEnvironmentVariable(PaymentPostgresFactAttribute.ConnectionKey)!;
        var admin = new NpgsqlConnection(connection);
        var isolated = new NpgsqlConnectionStringBuilder(connection) { SearchPath = schema, Pooling = false };
        var database = new PostgresDatabase(admin, schema,
            new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(isolated.ConnectionString).Options, tenant));
        try
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", admin);
            await create.ExecuteNonQueryAsync();
            await database.Context.Database.OpenConnectionAsync();
            await database.Context.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('search_path', {schema}, false)");
            Assert.Equal(schema, await database.Context.Database.SqlQueryRaw<string>("SELECT current_schema() AS \"Value\"").SingleAsync());
            await using var createTables = new NpgsqlCommand(
                database.Context.Database.GenerateCreateScript(),
                (NpgsqlConnection)database.Context.Database.GetDbConnection());
            await createTables.ExecuteNonQueryAsync();
            return database;
        }
        catch
        {
            await database.DisposeAsync();
            throw;
        }
    }

    public sealed class PostgresDatabase(NpgsqlConnection admin, string schema, AppDbContext context) : IAsyncDisposable
    {
        public AppDbContext Context { get; } = context;

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await using (admin)
            {
                if (admin.State != System.Data.ConnectionState.Open) return;
                await using var drop = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE", admin);
                await drop.ExecuteNonQueryAsync();
            }
        }
    }
}
