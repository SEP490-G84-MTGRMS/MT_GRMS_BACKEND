using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MT_GRMS.Api.Common;
using MT_GRMS.Api.Middlewares;
using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Exceptions;
using Xunit;

namespace MT_GRMS.UnitTests.Middlewares;

public class MiddlewaresAndEnvLoaderTests
{
    [Fact]
    public void EnvLoader_Load_ShouldParseValidFile_AndHandleMissingFile()
    {
        // 1. Missing file should do nothing
        EnvLoader.Load("non_existent_env_file_12345.env");

        // 2. Real temporary env file
        var tempFile = Path.Combine(Directory.GetCurrentDirectory(), ".env.test");
        try
        {
            File.WriteAllLines(tempFile, new[]
            {
                "# Comment line",
                "",
                "TEST_KEY1=SimpleValue",
                "TEST_KEY2=\"QuotedValue\"",
                "TEST_KEY3='SingleQuoted'",
                "INVALID_LINE_NO_EQUALS",
                "=NoKey"
            });

            EnvLoader.Load(".env.test");

            Assert.Equal("SimpleValue", Environment.GetEnvironmentVariable("TEST_KEY1"));
            Assert.Equal("QuotedValue", Environment.GetEnvironmentVariable("TEST_KEY2"));
            Assert.Equal("SingleQuoted", Environment.GetEnvironmentVariable("TEST_KEY3"));
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task MultiTenantMiddleware_AuthenticatedAdmin_CanSwitchTenant()
    {
        var tenantContext = new TenantContext();
        var context = new DefaultHttpContext();
        var claims = new[]
        {
            new Claim("tenant_id", "original-tenant"),
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Role, "ADMIN")
        };
        context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        context.Request.Headers["X-Tenant-Id"] = "switched-tenant";

        var middleware = new MultiTenantMiddleware(next: (ctx) => Task.CompletedTask);
        await middleware.InvokeAsync(context, tenantContext);

        Assert.Equal("switched-tenant", tenantContext.CurrentTenantId);
        Assert.Equal("ADMIN", tenantContext.CurrentRole);
    }

    [Fact]
    public async Task MultiTenantMiddleware_AuthenticatedStaff_CannotSwitchTenant()
    {
        var tenantContext = new TenantContext();
        var context = new DefaultHttpContext();
        var claims = new[]
        {
            new Claim("tenant_id", "staff-tenant"),
            new Claim("sub", Guid.NewGuid().ToString()),
            new Claim("branch_id", Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Role, "STAFF")
        };
        context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        context.Request.Headers["X-Tenant-Id"] = "hacked-tenant";

        var middleware = new MultiTenantMiddleware(next: (ctx) => Task.CompletedTask);
        await middleware.InvokeAsync(context, tenantContext);

        Assert.Equal("staff-tenant", tenantContext.CurrentTenantId);
        Assert.Equal("STAFF", tenantContext.CurrentRole);
    }

    [Fact]
    public async Task MultiTenantMiddleware_RegistrationPath_DoesNotForceFallback()
    {
        var tenantContext = new TenantContext();
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/auth/register";

        var middleware = new MultiTenantMiddleware(next: (ctx) => Task.CompletedTask);
        await middleware.InvokeAsync(context, tenantContext);

        Assert.True(string.IsNullOrEmpty(tenantContext.CurrentTenantId));
    }

    [Fact]
    public async Task MultiTenantMiddleware_Unauthenticated_AppliesFallbackTenant()
    {
        var tenantContext = new TenantContext();
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/products";
        context.Request.Headers["X-Tenant-Id"] = "custom-fallback";
        context.Request.Headers["X-User-Role"] = "CASHIER";

        var middleware = new MultiTenantMiddleware(next: (ctx) => Task.CompletedTask);
        await middleware.InvokeAsync(context, tenantContext);

        Assert.Equal("custom-fallback", tenantContext.CurrentTenantId);
        Assert.Equal("CASHIER", tenantContext.CurrentRole);
    }

    [Theory]
    [InlineData("CONFLICT", StatusCodes.Status409Conflict)]
    [InlineData("FORBIDDEN", StatusCodes.Status403Forbidden)]
    [InlineData("OTHER_ERROR", StatusCodes.Status422UnprocessableEntity)]
    public async Task ExceptionHandlingMiddleware_HandlesBusinessException(string code, int expectedStatus)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var envMock = new Mock<IHostEnvironment>();

        var middleware = new ExceptionHandlingMiddleware(
            next: (ctx) => throw new BusinessException(code, "Test business error"),
            logger: NullLogger<ExceptionHandlingMiddleware>.Instance,
            env: envMock.Object);

        await middleware.InvokeAsync(context);

        Assert.Equal(expectedStatus, context.Response.StatusCode);
    }

    [Fact]
    public async Task ExceptionHandlingMiddleware_HandlesVariousExceptions()
    {
        var envMock = new Mock<IHostEnvironment>();
        envMock.Setup(e => e.EnvironmentName).Returns(Environments.Development);

        // 1. InvalidOperationException -> 422
        var ctx1 = new DefaultHttpContext { Response = { Body = new MemoryStream() } };
        var m1 = new ExceptionHandlingMiddleware(
            next: (ctx) => throw new InvalidOperationException("Invalid op"),
            logger: NullLogger<ExceptionHandlingMiddleware>.Instance,
            env: envMock.Object);
        await m1.InvokeAsync(ctx1);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, ctx1.Response.StatusCode);

        // 2. ArgumentException -> 400
        var ctx2 = new DefaultHttpContext { Response = { Body = new MemoryStream() } };
        var m2 = new ExceptionHandlingMiddleware(
            next: (ctx) => throw new ArgumentException("Bad arg"),
            logger: NullLogger<ExceptionHandlingMiddleware>.Instance,
            env: envMock.Object);
        await m2.InvokeAsync(ctx2);
        Assert.Equal(StatusCodes.Status400BadRequest, ctx2.Response.StatusCode);

        // 3. UnauthorizedAccessException -> 401
        var ctx3 = new DefaultHttpContext { Response = { Body = new MemoryStream() } };
        var m3 = new ExceptionHandlingMiddleware(
            next: (ctx) => throw new UnauthorizedAccessException("No access"),
            logger: NullLogger<ExceptionHandlingMiddleware>.Instance,
            env: envMock.Object);
        await m3.InvokeAsync(ctx3);
        Assert.Equal(StatusCodes.Status401Unauthorized, ctx3.Response.StatusCode);

        // 4. NotFoundException -> 404
        var ctx4 = new DefaultHttpContext { Response = { Body = new MemoryStream() } };
        var m4 = new ExceptionHandlingMiddleware(
            next: (ctx) => throw new NotFoundException("Not found resource"),
            logger: NullLogger<ExceptionHandlingMiddleware>.Instance,
            env: envMock.Object);
        await m4.InvokeAsync(ctx4);
        Assert.Equal(StatusCodes.Status404NotFound, ctx4.Response.StatusCode);

        // 5. Default Exception -> 500 (Production mode)
        var prodEnv = new Mock<IHostEnvironment>();
        prodEnv.Setup(e => e.EnvironmentName).Returns(Environments.Production);
        var ctx5 = new DefaultHttpContext { Response = { Body = new MemoryStream() } };
        var m5 = new ExceptionHandlingMiddleware(
            next: (ctx) => throw new Exception("Unexpected error"),
            logger: NullLogger<ExceptionHandlingMiddleware>.Instance,
            env: prodEnv.Object);
        await m5.InvokeAsync(ctx5);
        Assert.Equal(StatusCodes.Status500InternalServerError, ctx5.Response.StatusCode);
    }
}
