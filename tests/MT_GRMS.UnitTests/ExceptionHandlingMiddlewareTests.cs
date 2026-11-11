using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using MT_GRMS.Api.Middlewares;
using MT_GRMS.Application.Common;
using Xunit;

namespace MT_GRMS.UnitTests;

public class ExceptionHandlingMiddlewareTests
{
    private class FakeHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "MT_GRMS.Api";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    [Fact]
    public async Task HandleUnhandledException_InProduction_ShouldMaskRawDetails()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        var env = new FakeHostEnvironment { EnvironmentName = Environments.Production };
        var middleware = new ExceptionHandlingMiddleware(
            next: _ => throw new Exception("Sensitive internal database connection string leaked!"),
            logger: NullLogger<ExceptionHandlingMiddleware>.Instance,
            env: env
        );

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal((int)HttpStatusCode.InternalServerError, context.Response.StatusCode);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body);
        var responseBody = await reader.ReadToEndAsync();

        var json = JsonSerializer.Deserialize<ApiResponse<object>>(responseBody, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(json);
        Assert.False(json.Success);
        Assert.Equal("Đã xảy ra lỗi hệ thống. Vui lòng liên hệ quản trị viên.", json.Message);
        Assert.DoesNotContain("Sensitive internal database connection string leaked!", responseBody);
    }

    [Fact]
    public async Task HandleUnhandledException_InDevelopment_ShouldIncludeRawDetails()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        var env = new FakeHostEnvironment { EnvironmentName = Environments.Development };
        var middleware = new ExceptionHandlingMiddleware(
            next: _ => throw new Exception("Debug internal error details"),
            logger: NullLogger<ExceptionHandlingMiddleware>.Instance,
            env: env
        );

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal((int)HttpStatusCode.InternalServerError, context.Response.StatusCode);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body);
        var responseBody = await reader.ReadToEndAsync();

        var json = JsonSerializer.Deserialize<ApiResponse<object>>(responseBody, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(json);
        Assert.False(json.Success);
        Assert.Contains("Debug internal error details", json.Message);
    }
}
