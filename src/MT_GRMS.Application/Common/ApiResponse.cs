namespace MT_GRMS.Application.Common;

public class ApiResponse<T>
{
    public bool Success { get; init; }
    public int StatusCode { get; init; } = 200;
    public int Status => StatusCode;
    public string? Message { get; init; }
    public string? ErrorCode { get; init; }
    public T? Data { get; init; }

    public static ApiResponse<T> Ok(T data, string? message = null, int statusCode = 200) =>
        new() { Success = true, StatusCode = statusCode, Data = data, Message = message };

    public static ApiResponse<T> Fail(string errorCode, string message, int statusCode = 400) =>
        new() { Success = false, StatusCode = statusCode, ErrorCode = errorCode, Message = message };
}

public class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; init; } = [];
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalItems { get; init; }
}
