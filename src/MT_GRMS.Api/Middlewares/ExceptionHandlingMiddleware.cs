using System.Net;
using System.Text.Json;
using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Constants;
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
                apiResponse = ApiResponse<object>.Fail(businessEx.ErrorCode, businessEx.Message);
                _logger.LogWarning(businessEx, "Business exception occurred: {Message}", businessEx.Message);
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

            case NotFoundException notFoundEx:
                statusCode = (int)HttpStatusCode.NotFound;
                apiResponse = ApiResponse<object>.Fail(ErrorCodes.NotFound, notFoundEx.Message);
                _logger.LogWarning(notFoundEx, "Not found exception occurred: {Message}", notFoundEx.Message);
                break;

            default:
                statusCode = (int)HttpStatusCode.InternalServerError;
                apiResponse = ApiResponse<object>.Fail(ErrorCodes.UnhandledError, $"Lỗi: {exception.Message} | {exception.InnerException?.Message}");
                _logger.LogError(exception, "Unhandled exception occurred");
                break;
        }

        response.StatusCode = statusCode;
        var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        await response.WriteAsync(JsonSerializer.Serialize(apiResponse, jsonOptions));
    }
}
