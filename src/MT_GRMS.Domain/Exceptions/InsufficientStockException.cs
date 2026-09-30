using MT_GRMS.Domain.Constants;

namespace MT_GRMS.Domain.Exceptions;

public class InsufficientStockException : BusinessException
{
    public InsufficientStockException(string message = "Mặt hàng trong lô đã hết tồn kho khả dụng.")
        : base(ErrorCodes.InsufficientStock, message)
    {
    }

    public InsufficientStockException(string productName, decimal requested, decimal available)
        : base(ErrorCodes.InsufficientStock, $"Sản phẩm '{productName}' không đủ tồn kho (yêu cầu: {requested}, hiện có: {available}).")
    {
    }
}
