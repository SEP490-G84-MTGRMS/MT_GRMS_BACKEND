namespace MT_GRMS.Domain.Constants;

public static class ErrorCodes
{
    public const string UnhandledError = "UNHANDLED_ERROR";
    public const string NotFound = "NOT_FOUND";
    public const string ValidationError = "VALIDATION_ERROR";
    public const string Unauthorized = "UNAUTHORIZED";
    public const string InvalidCredentials = "INVALID_CREDENTIALS";
    public const string Forbidden = "FORBIDDEN";
    public const string TenantLocked = "TENANT_LOCKED";
    public const string InsufficientStock = "INSUFFICIENT_STOCK";
    public const string BatchExpired = "BATCH_EXPIRED";
    public const string OrderDuplicated = "ORDER_DUPLICATED";
}
