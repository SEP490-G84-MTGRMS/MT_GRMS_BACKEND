using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Exceptions;

namespace MT_GRMS.Api.Middlewares;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
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
        var statusCode = (int)HttpStatusCode.InternalServerError;
        ApiResponse<object> apiResponse;

        switch (exception)
        {
            case BusinessException businessEx:
                statusCode = businessEx.ErrorCode switch
                {
                    "CONFLICT" or "DUPLICATE_KEY" or "DUPLICATE_CODE" or "DUPLICATE_BARCODE" or "DUPLICATE_CITIZEN_ID" or "DUPLICATE_PHONE" or "DUPLICATE_USERNAME" or "PRODUCT_ALREADY_ASSIGNED_TO_SUPPLIER" => (int)HttpStatusCode.Conflict,
                    "FORBIDDEN" => (int)HttpStatusCode.Forbidden,
                    _ => (int)HttpStatusCode.UnprocessableEntity
                };
                apiResponse = ApiResponse<object>.Fail(businessEx.ErrorCode, businessEx.Message);
                _logger.LogWarning(businessEx, "Business exception: {Code} - {Message}", businessEx.ErrorCode, businessEx.Message);
                break;

            case ArgumentException argEx:
                statusCode = (int)HttpStatusCode.BadRequest;
                apiResponse = ApiResponse<object>.Fail("BAD_REQUEST", argEx.Message);
                _logger.LogWarning(argEx, "Invalid argument: {Message}", argEx.Message);
                break;

            case UnauthorizedAccessException unauthEx:
                statusCode = (int)HttpStatusCode.Unauthorized;
                apiResponse = ApiResponse<object>.Fail("UNAUTHORIZED", unauthEx.Message);
                _logger.LogWarning(unauthEx, "Unauthorized access: {Message}", unauthEx.Message);
                break;

            case DbUpdateException dbUpdateEx when dbUpdateEx.InnerException is Npgsql.PostgresException pgEx && pgEx.SqlState == "23505":
                statusCode = (int)HttpStatusCode.Conflict;
                apiResponse = ApiResponse<object>.Fail("DUPLICATE_KEY", "Dữ liệu bị trùng lặp trong hệ thống (vi phạm ràng buộc duy nhất).");
                _logger.LogWarning(dbUpdateEx, "Postgres unique constraint 23505: {Constraint}", pgEx.ConstraintName);
                break;

            case NotFoundException notFoundEx:
                statusCode = (int)HttpStatusCode.NotFound;
                apiResponse = ApiResponse<object>.Fail("NOT_FOUND", notFoundEx.Message);
                _logger.LogWarning(notFoundEx, "Not found: {Message}", notFoundEx.Message);
                break;

            default:
                apiResponse = ApiResponse<object>.Fail("INTERNAL_SERVER_ERROR", "Đã xảy ra lỗi không mong muốn trên hệ thống.");
                _logger.LogError(exception, "Unhandled exception: {Message}", exception.Message);
                break;
        }

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = statusCode;

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(apiResponse, options));
    }
}
