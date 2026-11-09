using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Constants;
using MT_GRMS.Domain.Exceptions;

namespace MT_GRMS.Api.Middlewares;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly IHostEnvironment _env;

    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<ExceptionHandlingMiddleware> logger,
        IHostEnvironment env)
    {
        _next = next;
        _logger = logger;
        _env = env;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var response = context.Response;
        response.ContentType = "application/json";

        int statusCode;
        ApiResponse<object> apiResponse;

        switch (exception)
        {
            case BusinessException businessEx:
                statusCode = businessEx.ErrorCode switch
                {
                    "PRODUCT_ALREADY_ASSIGNED_TO_SUPPLIER" => (int)HttpStatusCode.Conflict,
                    "FORBIDDEN" => (int)HttpStatusCode.Forbidden,
                    _ => (int)HttpStatusCode.UnprocessableEntity
                };
                apiResponse = ApiResponse<object>.Fail(businessEx.ErrorCode, businessEx.Message, statusCode);
                _logger.LogWarning(businessEx, "Business exception occurred: {Message}", businessEx.Message);
                break;

            case InvalidOperationException invEx:
                statusCode = (int)HttpStatusCode.UnprocessableEntity;
                apiResponse = ApiResponse<object>.Fail("INVALID_OPERATION", invEx.Message);
                _logger.LogWarning(invEx, "Invalid operation occurred: {Message}", invEx.Message);
                break;

            case ArgumentException argEx:
                statusCode = (int)HttpStatusCode.BadRequest;
                apiResponse = ApiResponse<object>.Fail("BAD_REQUEST", argEx.Message, statusCode);
                _logger.LogWarning(argEx, "Invalid argument: {Message}", argEx.Message);
                break;

            case UnauthorizedAccessException unauthEx:
                statusCode = (int)HttpStatusCode.Unauthorized;
                apiResponse = ApiResponse<object>.Fail("UNAUTHORIZED", unauthEx.Message, statusCode);
                _logger.LogWarning(unauthEx, "Unauthorized access: {Message}", unauthEx.Message);
                break;

            case Microsoft.EntityFrameworkCore.DbUpdateException dbUpdateEx when dbUpdateEx.InnerException is Npgsql.PostgresException pgEx && pgEx.SqlState == "23505":
                statusCode = (int)HttpStatusCode.Conflict;
                var constraintName = pgEx.ConstraintName ?? string.Empty;
                var friendlyMsg = constraintName.Contains("ProductCode", StringComparison.OrdinalIgnoreCase)
                    ? "Mã sản phẩm đã tồn tại trong hệ thống. Vui lòng nhập mã khác."
                    : constraintName.Contains("Barcode", StringComparison.OrdinalIgnoreCase)
                        ? "Mã vạch đã được sử dụng bởi sản phẩm khác."
                        : "Dữ liệu bị trùng lặp trong hệ thống (vi phạm ràng buộc duy nhất).";
                apiResponse = ApiResponse<object>.Fail("DUPLICATE_KEY", friendlyMsg);
                _logger.LogWarning(dbUpdateEx, "Unique constraint violation: {ConstraintName}", constraintName);
                break;

            case NotFoundException notFoundEx:
                statusCode = (int)HttpStatusCode.NotFound;
                apiResponse = ApiResponse<object>.Fail(ErrorCodes.NotFound, notFoundEx.Message, statusCode);
                _logger.LogWarning(notFoundEx, "Not found exception occurred: {Message}", notFoundEx.Message);
                break;

            default:
                statusCode = (int)HttpStatusCode.InternalServerError;
                // SEC-06: Ngăn chặn Information Disclosure trên môi trường Production/Staging
                var errorMessage = _env.IsDevelopment()
                    ? $"Lỗi: {exception.Message} | {exception.InnerException?.Message}"
                    : "Đã xảy ra lỗi hệ thống. Vui lòng liên hệ quản trị viên.";
                apiResponse = ApiResponse<object>.Fail(ErrorCodes.UnhandledError, errorMessage, statusCode);
                _logger.LogError(exception, "Unhandled exception occurred");
                break;
        }

        response.StatusCode = statusCode;
        var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        await response.WriteAsync(JsonSerializer.Serialize(apiResponse, jsonOptions));
    }
}
