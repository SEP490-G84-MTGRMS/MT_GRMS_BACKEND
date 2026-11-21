using System;
using System.Collections.Generic;
using System.Reflection;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using MT_GRMS.Api.Controllers;
using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Domain.Entities.Sales;
using MT_GRMS.Infrastructure.Persistence;
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
            .Options;

        return new AppDbContext(options, _mockTenant.Object);
    }

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
        var assignedIp = ip ?? $"10.0.{(System.Threading.Interlocked.Increment(ref _ipCounter) / 250) % 250}.{System.Threading.Interlocked.Increment(ref _ipCounter) % 250}";
        httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(assignedIp);

        return new ControllerContext { HttpContext = httpContext };
    }

    [Fact]
    public async Task GetPublicQrConfig_ShouldHandleNotFound_AndSuccess()
    {
        using var context = CreateDbContext();
        var controller = new PaymentsController(
            context, _mockHttpClientFactory.Object, _mockConfig.Object, _mockLogger.Object, _mockTenant.Object)
        {
            ControllerContext = CreateContext()
        };

        // 1. Not found when nothing configured
        var resNotFound = await controller.GetPublicQrConfig(TenantId, null, CancellationToken.None);
        Assert.IsType<NotFoundObjectResult>(resNotFound);

        // 2. Branch requested but no branch payment account
        var resBranchNotFound = await controller.GetPublicQrConfig(TenantId, Guid.NewGuid(), CancellationToken.None);
        Assert.IsType<NotFoundObjectResult>(resBranchNotFound);

        // 3. Config exists -> Success
        context.StorePaymentConfigs.Add(new StorePaymentConfig
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            BankCode = "MB",
            BankName = "MBBank",
            BankAccountNumber = "0987654321",
            BankAccountName = "NGUYEN VAN A",
            IsActive = true
        });
        await context.SaveChangesAsync();

        var resSuccess = await controller.GetPublicQrConfig(TenantId, null, CancellationToken.None);
        Assert.IsType<OkObjectResult>(resSuccess);
    }

    [Fact]
    public async Task GetPaymentConfig_ShouldHandleForbidden_AndSuccess()
    {
        using var context = CreateDbContext();
        var controller = new PaymentsController(
            context, _mockHttpClientFactory.Object, _mockConfig.Object, _mockLogger.Object, _mockTenant.Object);

        // 1. Forbidden for STAFF
        controller.ControllerContext = CreateContext("STAFF");
        var resForbid = await controller.GetPaymentConfig(CancellationToken.None);
        var objRes = Assert.IsType<ObjectResult>(resForbid);
        Assert.Equal(StatusCodes.Status403Forbidden, objRes.StatusCode);

        // 2. Success for STORE_OWNER
        controller.ControllerContext = CreateContext("STORE_OWNER");
        context.StorePaymentConfigs.Add(new StorePaymentConfig
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            BankCode = "VCB",
            BankName = "Vietcombank",
            BankAccountNumber = "1234567890",
            BankAccountName = "NGUYEN VAN B",
            IsActive = true
        });
        await context.SaveChangesAsync();

        var resOk = await controller.GetPaymentConfig(CancellationToken.None);
        Assert.IsType<OkObjectResult>(resOk);
    }

    [Fact]
    public async Task SavePaymentConfig_ShouldHandleForbidden_Validation_AndSuccess()
    {
        using var context = CreateDbContext();
        var controller = new PaymentsController(
            context, _mockHttpClientFactory.Object, _mockConfig.Object, _mockLogger.Object, _mockTenant.Object);

        // 1. Forbidden for STAFF
        controller.ControllerContext = CreateContext("STAFF");
        var req = new SavePaymentConfigRequest { BankCode = "MB", BankAccountNumber = "12345", BankAccountName = "TEST" };
        var resForbid = await controller.SavePaymentConfig(req, CancellationToken.None);
        var objRes = Assert.IsType<ObjectResult>(resForbid);
        Assert.Equal(StatusCodes.Status403Forbidden, objRes.StatusCode);

        // 2. Validation error: empty bank account
        controller.ControllerContext = CreateContext("STORE_OWNER");
        var reqEmpty = new SavePaymentConfigRequest { BankCode = "MB", BankAccountNumber = "", BankAccountName = "TEST" };
        var resEmpty = await controller.SavePaymentConfig(reqEmpty, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(resEmpty);

        // 2b. Name too long
        var reqLongName = new SavePaymentConfigRequest { BankCode = "MB", BankAccountNumber = "123456789", BankAccountName = new string('A', 151) };
        Assert.IsType<BadRequestObjectResult>(await controller.SavePaymentConfig(reqLongName, CancellationToken.None));

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
            PaymentAccountId = Guid.NewGuid()
        };
        Assert.IsType<BadRequestObjectResult>(await controller.SavePaymentConfig(reqBadPaymentAcc, CancellationToken.None));

        // 2e. Short Sepay API key
        var reqShortKey = new SavePaymentConfigRequest
        {
            BankCode = "MB",
            BankAccountNumber = "123456789",
            BankAccountName = "TEST",
            SepayApiKey = "short_key"
        };
        Assert.IsType<BadRequestObjectResult>(await controller.SavePaymentConfig(reqShortKey, CancellationToken.None));

        // 3. Success with valid Branch
        var branch = new Branch
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            Name = "Chi nhanh chinh",
            BranchCode = "CN01",
            Address = "123 Street",
            Phone = "0987654321",
            IsActive = true
        };
        await context.Branches.AddAsync(branch);
        await context.SaveChangesAsync();

        var reqSuccess = new SavePaymentConfigRequest
        {
            BankCode = "MB",
            BankName = "MBBank",
            BankAccountNumber = "1234567890",
            BankAccountName = "NGUYEN VAN C",
            SepayApiKey = "SEPAY_API_KEY_1234567890_TEST",
            BranchIds = new List<Guid> { branch.Id },
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
        var controller = new PaymentsController(
            context, _mockHttpClientFactory.Object, _mockConfig.Object, _mockLogger.Object, _mockTenant.Object);

        // Forbidden
        controller.ControllerContext = CreateContext("STAFF");
        var resForbid = await controller.RevokeSePayApiKey(CancellationToken.None);
        var objRes = Assert.IsType<ObjectResult>(resForbid);
        Assert.Equal(StatusCodes.Status403Forbidden, objRes.StatusCode);

        // Success when config exists
        controller.ControllerContext = CreateContext("STORE_OWNER");
        context.StorePaymentConfigs.Add(new StorePaymentConfig
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            BankCode = "MB",
            BankAccountNumber = "1234567890",
            BankAccountName = "TEST",
            SepayApiKeyEncrypted = "AES256:dummy",
            IsActive = true
        });
        await context.SaveChangesAsync();

        var resSuccess = await controller.RevokeSePayApiKey(CancellationToken.None);
        Assert.IsType<OkObjectResult>(resSuccess);
    }

    [Fact]
    public async Task CheckOrderStatus_ShouldHandleNotFound_AndSuccess()
    {
        using var context = CreateDbContext();
        var controller = new PaymentsController(
            context, _mockHttpClientFactory.Object, _mockConfig.Object, _mockLogger.Object, _mockTenant.Object)
        {
            ControllerContext = CreateContext()
        };

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
        var controller = new PaymentsController(
            context, _mockHttpClientFactory.Object, _mockConfig.Object, _mockLogger.Object, _mockTenant.Object);

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
        var controller = new PaymentsController(
            context, _mockHttpClientFactory.Object, _mockConfig.Object, _mockLogger.Object, _mockTenant.Object);

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
        context.StorePaymentConfigs.Add(new StorePaymentConfig
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            BankCode = "MB",
            BankAccountNumber = "12345678",
            BankAccountName = "STORE",
            SepayApiKeyEncrypted = "AES256:mock_key_123",
            IsActive = true
        });
        await context.SaveChangesAsync();

        var resMock = await controller.GetSePayBankAccounts(CancellationToken.None);
        var objMock = Assert.IsType<ObjectResult>(resMock);
        Assert.Equal(StatusCodes.Status502BadGateway, objMock.StatusCode);
    }

    [Fact]
    public async Task HandleSePayWebhook_SecurityLayers_AndSuccessFlow_ShouldExecute()
    {
        using var context = CreateDbContext();
        var controller = new PaymentsController(
            context, _mockHttpClientFactory.Object, _mockConfig.Object, _mockLogger.Object, _mockTenant.Object);

        var payload = new SePayWebhookPayload
        {
            Id = 12345,
            Gateway = "VietQR",
            TransactionDate = DateTime.UtcNow,
            AccountNumber = "12345678",
            AmountIn = 100_000m,
            TransactionContent = "Thanh toan HD1001",
            ReferenceNumber = "REF-999"
        };

        // 1. IP not whitelisted -> 403 Forbidden
        var notWhitelistedContext = CreateContext();
        notWhitelistedContext.HttpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("8.8.8.8");
        controller.ControllerContext = notWhitelistedContext;
        var resIp = await controller.HandleSePayWebhook(payload, CancellationToken.None);
        var objIp = Assert.IsType<ObjectResult>(resIp);
        Assert.Equal(StatusCodes.Status403Forbidden, objIp.StatusCode);

        // Whitelist IP from now on
        var validContext = CreateContext();
        validContext.HttpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("10.1.1.1");
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

        // 4. Order code cannot be extracted -> 400 BadRequest
        var payloadBadContent = new SePayWebhookPayload
        {
            Id = 12345,
            TransactionContent = ""
        };
        var resBadContent = await controller.HandleSePayWebhook(payloadBadContent, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(resBadContent);

        // 5. Order not found -> 404 NotFound
        payload.TransactionContent = "HD999999";
        var resNotFound = await controller.HandleSePayWebhook(payload, CancellationToken.None);
        Assert.IsType<NotFoundObjectResult>(resNotFound);

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
        var config = new StorePaymentConfig
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            BankCode = "MB",
            BankAccountNumber = "12345678",
            BankAccountName = "STORE OWNER",
            SepayWebhookSecret = "SECRET_WEBHOOK_123",
            IsActive = true
        };
        context.StorePaymentConfigs.Add(config);
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
        Assert.IsType<OkObjectResult>(resIdempotent);

        // 9. Insufficient amount -> 400 BadRequest
        payload.Id = 99999;
        payload.AmountIn = 50_000m;
        var resInsufficient = await controller.HandleSePayWebhook(payload, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(resInsufficient);

        // 10. Valid webhook execution -> Success (200 Ok), marks order PAID
        payload.AmountIn = 100_000m;
        var resSuccess = await controller.HandleSePayWebhook(payload, CancellationToken.None);
        var objSuccess = Assert.IsType<OkObjectResult>(resSuccess);
        Assert.NotNull(objSuccess.Value);

        var updatedOrder = await context.Orders.FindAsync(order.Id);
        Assert.Equal("PAID", updatedOrder!.PaymentStatus);
        Assert.Equal("Completed", updatedOrder.Status);
    }

    [Fact]
    public async Task GetSePayBankAccounts_ShouldHandleForbidden_NoConfig_AndSuccess()
    {
        using var context = CreateDbContext();

        // 1. Forbidden when not store owner or admin
        var staffContext = CreateContext(role: "STAFF");
        var controllerStaff = new PaymentsController(
            context, _mockHttpClientFactory.Object, _mockConfig.Object, _mockLogger.Object, _mockTenant.Object)
        {
            ControllerContext = staffContext
        };
        var resForbidden = await controllerStaff.GetSePayBankAccounts(CancellationToken.None);
        var obj403 = Assert.IsType<ObjectResult>(resForbidden);
        Assert.Equal(403, obj403.StatusCode);

        // 2. Owner context, but no config / no key -> 502 Bad Gateway
        var ownerContext = CreateContext(role: "STORE_OWNER");
        var controllerOwner = new PaymentsController(
            context, _mockHttpClientFactory.Object, _mockConfig.Object, _mockLogger.Object, _mockTenant.Object)
        {
            ControllerContext = ownerContext
        };
        var resNoConfig = await controllerOwner.GetSePayBankAccounts(CancellationToken.None);
        var obj502 = Assert.IsType<ObjectResult>(resNoConfig);
        Assert.Equal(502, obj502.StatusCode);

        // 3. Config with real key and mock HTTP client returning SePay bankaccounts
        var config = new StorePaymentConfig
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            SepayApiKeyEncrypted = "REAL_SEPAY_API_KEY_12345",
            IsActive = true
        };
        context.StorePaymentConfigs.Add(config);
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
        var controller = new PaymentsController(
            context, _mockHttpClientFactory.Object, _mockConfig.Object, _mockLogger.Object, _mockTenant.Object);

        // 1. Non-whitelisted IP -> 403 Forbidden
        var forbiddenIpContext = CreateContext();
        forbiddenIpContext.HttpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("198.51.100.1");
        controller.ControllerContext = forbiddenIpContext;

        var payload = new SePayWebhookPayload
        {
            Id = 55555,
            AccountNumber = "12345678",
            AmountIn = 100_000m,
            TransactionContent = "HD1001"
        };
        var resIpForbidden = await controller.HandleSePayWebhook(payload, CancellationToken.None);
        var objIpForbidden = Assert.IsType<ObjectResult>(resIpForbidden);
        Assert.Equal(403, objIpForbidden.StatusCode);

        // 2. Missing Authorization header -> 401 Unauthorized
        var localContext = CreateContext(); // 127.0.0.1 is whitelisted
        controller.ControllerContext = localContext;
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
        context.StorePaymentConfigs.Add(new StorePaymentConfig
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            SepayApiKeyEncrypted = "REAL_KEY_999",
            BankAccountNumber = "12345678",
            IsActive = true
        });
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

    [Fact]
    public async Task HandleSePayWebhook_SandboxHappyPath_AndDuplicate_ShouldWork()
    {
        using var context = CreateDbContext();
        var controller = new PaymentsController(
            context, _mockHttpClientFactory.Object, _mockConfig.Object, _mockLogger.Object, _mockTenant.Object);

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
        context.StorePaymentConfigs.Add(new StorePaymentConfig
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            SepayApiKeyEncrypted = "mock_sepay_key_123",
            BankAccountNumber = "999888777",
            IsActive = true
        });
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
        Assert.IsType<OkObjectResult>(resDup);
    }

    [Fact]
    public async Task HandleSePayWebhook_Underpaid_ReturnsBadRequest()
    {
        using var context = CreateDbContext();
        var controller = new PaymentsController(
            context, _mockHttpClientFactory.Object, _mockConfig.Object, _mockLogger.Object, _mockTenant.Object);

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
        context.StorePaymentConfigs.Add(new StorePaymentConfig
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            SepayApiKeyEncrypted = "mock_sepay_key_123",
            BankAccountNumber = "999888777",
            IsActive = true
        });
        await context.SaveChangesAsync();

        var payload = new SePayWebhookPayload
        {
            Id = 88888,
            AccountNumber = "999888777",
            AmountIn = 50_000m, // underpaid
            TransactionContent = "HD3001"
        };

        var res = await controller.HandleSePayWebhook(payload, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(res);
    }

    [Fact]
    public async Task CheckOrderStatus_AndTestSecurityLayers_ShouldWork()
    {
        using var context = CreateDbContext();
        var controller = new PaymentsController(
            context, _mockHttpClientFactory.Object, _mockConfig.Object, _mockLogger.Object, _mockTenant.Object)
        {
            ControllerContext = CreateContext()
        };

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
        var controller = new PaymentsController(
            context, _mockHttpClientFactory.Object, _mockConfig.Object, _mockLogger.Object, _mockTenant.Object);

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
    public void IsIpWhitelisted_Variants_ShouldValidateCorrectly()
    {
        var method = typeof(PaymentsController).GetMethod("IsIpWhitelisted", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);

        using var context = CreateDbContext();
        var controller = new PaymentsController(
            context, _mockHttpClientFactory.Object, _mockConfig.Object, _mockLogger.Object, _mockTenant.Object);

        bool CheckIp(string ip) => (bool)method.Invoke(controller, new object[] { ip })!;

        Assert.False(CheckIp(""));
        Assert.False(CheckIp("   "));
        Assert.True(CheckIp("127.0.0.1"));
        Assert.True(CheckIp("::1"));
        Assert.False(CheckIp("198.51.100.22"));

        // Custom config whitelist
        _mockConfig.Setup(c => c["SePaySettings:AllowedWebhookIps"]).Returns("203.0.113.1, 203.0.113.2");
        Assert.True(CheckIp("203.0.113.1"));
        Assert.True(CheckIp("203.0.113.2"));
        Assert.False(CheckIp("203.0.113.3"));
    }

    [Fact]
    public async Task GetPaymentConfig_WhenNoConfig_ReturnsDefaultConfig()
    {
        using var context = CreateDbContext();
        var controller = new PaymentsController(
            context, _mockHttpClientFactory.Object, _mockConfig.Object, _mockLogger.Object, _mockTenant.Object);
        controller.ControllerContext = CreateContext();

        var result = await controller.GetPaymentConfig(CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);
    }

    [Fact]
    public async Task SavePaymentConfig_InvalidAccountNumber_ReturnsBadRequest()
    {
        using var context = CreateDbContext();
        var controller = new PaymentsController(
            context, _mockHttpClientFactory.Object, _mockConfig.Object, _mockLogger.Object, _mockTenant.Object);
        controller.ControllerContext = CreateContext();

        var req = new SavePaymentConfigRequest
        {
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
        var controller = new PaymentsController(
            context, _mockHttpClientFactory.Object, _mockConfig.Object, _mockLogger.Object, _mockTenant.Object);
        controller.ControllerContext = CreateContext();

        var req = new SavePaymentConfigRequest
        {
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
        var controller = new PaymentsController(
            context, _mockHttpClientFactory.Object, _mockConfig.Object, _mockLogger.Object, _mockTenant.Object);
        controller.ControllerContext = CreateContext();

        var req = new SavePaymentConfigRequest
        {
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

        var saved = await context.StorePaymentConfigs.FirstOrDefaultAsync(x => x.TenantId == TenantId);
        Assert.NotNull(saved);
        Assert.Equal(500000m, saved.CashOutApprovalThreshold);
        Assert.Equal(2000000m, saved.CashOutShiftTotalLimit);
    }

    [Fact]
    public async Task HandleSePayWebhook_RateLimitExceeded_Returns429()
    {
        using var context = CreateDbContext();
        var controller = new PaymentsController(
            context, _mockHttpClientFactory.Object, _mockConfig.Object, _mockLogger.Object, _mockTenant.Object);

        var ip = $"127.0.0.{(_ipCounter++ % 200) + 10}";
        controller.ControllerContext = CreateContext(role: "ANONYMOUS", tenant: null, ip: ip);

        var payload = new SePayWebhookPayload
        {
            Id = 1,
            Gateway = "MB",
            AccountNumber = "1234567890",
            TransactionDate = DateTime.UtcNow,
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

        var config = new StorePaymentConfig
        {
            TenantId = TenantId,
            BankCode = "MB",
            BankAccountNumber = "1234567890",
            BankAccountName = "TEST STORE",
            SepayApiKeyEncrypted = "real-production-api-key-12345",
            SepayWebhookSecret = "SECRET_WEBHOOK_123",
            IsActive = true
        };
        context.StorePaymentConfigs.Add(config);
        await context.SaveChangesAsync();

        var jsonResponse = "{\"transaction\": {\"amount_in\": 50000, \"transaction_content\": \"HD1005\", \"account_number\": \"99999999\"}}";
        var handler = new MockHttpMessageHandler(_ => new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new System.Net.Http.StringContent(jsonResponse, System.Text.Encoding.UTF8, "application/json")
        });
        var client = new System.Net.Http.HttpClient(handler);
        _mockHttpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);

        var controller = new PaymentsController(
            context, _mockHttpClientFactory.Object, _mockConfig.Object, _mockLogger.Object, _mockTenant.Object);
        controller.ControllerContext = CreateContext(role: "ANONYMOUS", tenant: null, ip: "127.0.0.1");
        controller.ControllerContext.HttpContext.Request.Headers["Authorization"] = "Bearer SECRET_WEBHOOK_123";

        var payload = new SePayWebhookPayload
        {
            Id = 1,
            Gateway = "MB",
            AccountNumber = "1234567890",
            TransactionDate = DateTime.UtcNow,
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

        var config = new StorePaymentConfig
        {
            TenantId = TenantId,
            BankCode = "MB",
            BankAccountNumber = "1234567890",
            BankAccountName = "TEST STORE",
            SepayApiKeyEncrypted = "real-production-api-key-12345",
            SepayWebhookSecret = "SECRET_WEBHOOK_123",
            IsActive = true
        };
        context.StorePaymentConfigs.Add(config);
        await context.SaveChangesAsync();

        var handler = new MockHttpMessageHandler(_ => throw new System.Net.Http.HttpRequestException("Connection timeout"));
        var client = new System.Net.Http.HttpClient(handler);
        _mockHttpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);

        var controller = new PaymentsController(
            context, _mockHttpClientFactory.Object, _mockConfig.Object, _mockLogger.Object, _mockTenant.Object);
        controller.ControllerContext = CreateContext(role: "ANONYMOUS", tenant: null, ip: "127.0.0.1");
        controller.ControllerContext.HttpContext.Request.Headers["Authorization"] = "Bearer SECRET_WEBHOOK_123";

        var payload = new SePayWebhookPayload
        {
            Id = 1,
            Gateway = "MB",
            AccountNumber = "1234567890",
            TransactionDate = DateTime.UtcNow,
            TransactionContent = "HD1005",
            AmountIn = 50000m,
            ReferenceNumber = "REF"
        };
        var result = await controller.HandleSePayWebhook(payload, CancellationToken.None);

        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status502BadGateway, objResult.StatusCode);
    }

    [Fact]
    public void GetClientIpAddress_ForwardedFor_And_ResolveTenantIdHeader()
    {
        using var context = CreateDbContext();
        var controller = new PaymentsController(
            context, _mockHttpClientFactory.Object, _mockConfig.Object, _mockLogger.Object, _mockTenant.Object);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Forwarded-For"] = "203.0.113.195, 70.41.3.18";
        httpContext.Request.Headers["X-Tenant-Id"] = "header-tenant-99";
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var ipMethod = typeof(PaymentsController).GetMethod("GetClientIpAddress", BindingFlags.NonPublic | BindingFlags.Instance);
        var ip = (string?)ipMethod!.Invoke(controller, null);
        Assert.Equal("203.0.113.195", ip);

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
