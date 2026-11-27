using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using MT_GRMS.Api.Common;
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

    [Fact]
    public async Task HandleInvalidOperationException_ShouldReturnHttp422AndMatchingBodyStatusCode()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        var middleware = new ExceptionHandlingMiddleware(
            next: _ => throw new InvalidOperationException("Phiếu điều chuyển đã hoàn tất."),
            logger: NullLogger<ExceptionHandlingMiddleware>.Instance,
            env: new FakeHostEnvironment { EnvironmentName = Environments.Production }
        );

        await middleware.InvokeAsync(context);

        Assert.Equal((int)HttpStatusCode.UnprocessableEntity, context.Response.StatusCode);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body);
        var responseBody = await reader.ReadToEndAsync();
        var json = JsonSerializer.Deserialize<ApiResponse<object>>(responseBody, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(json);
        Assert.False(json.Success);
        Assert.Equal(422, json.StatusCode);
        Assert.Equal("INVALID_OPERATION", json.ErrorCode);
    }

    [Fact]
    public void UtcDateTimeJsonConverter_ShouldSerializeUnspecifiedKindWithTrailingZ()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new UtcDateTimeJsonConverter());
        options.Converters.Add(new UtcNullableDateTimeJsonConverter());

        var unspecified = new DateTime(2026, 11, 24, 10, 2, 44, DateTimeKind.Unspecified);
        DateTime? nullableUnspecified = unspecified;

        var serialized = JsonSerializer.Serialize(unspecified, options);
        var serializedNullable = JsonSerializer.Serialize(nullableUnspecified, options);

        Assert.Equal("\"2026-11-24T10:02:44Z\"", serialized);
        Assert.Equal("\"2026-11-24T10:02:44Z\"", serializedNullable);
    }
}
