namespace MT_GRMS.Domain.Exceptions;

public class BusinessException : Exception
{
    public string ErrorCode { get; }

    public BusinessException(string errorCode, string message) : base(message)
    {
        ErrorCode = errorCode;
    }

    public BusinessException(string message) : this("BUSINESS_ERROR", message)
    {
    }
}
