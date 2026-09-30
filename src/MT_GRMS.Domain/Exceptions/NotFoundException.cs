namespace MT_GRMS.Domain.Exceptions;

public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message) { }
    public NotFoundException(string entityName, object key) : base($"Không tìm thấy {entityName} với mã khóa '{key}'.") { }
}
