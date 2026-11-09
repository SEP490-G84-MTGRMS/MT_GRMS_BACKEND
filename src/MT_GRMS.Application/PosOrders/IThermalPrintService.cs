using System;
using MT_GRMS.Domain.Entities.Sales;

namespace MT_GRMS.Application.PosOrders;

public interface IThermalPrintService
{
    /// <summary>
    /// In hóa đơn bán lẻ qua máy in nhiệt POS-58 sử dụng chế độ RAW
    /// </summary>
    bool PrintOrderInvoice(Order order, string printerName = "POS-58", decimal? cashReceived = null, decimal? changeAmount = null, string? cashierName = null);

    /// <summary>
    /// Sinh mảng byte ESC/POS định dạng khổ in 43mm (Font B nhỏ gọn ~7.5pt, layout Siêu thị mini / Tạp hóa)
    /// </summary>
    byte[] BuildEscPosReceipt(Order order, decimal? cashReceived = null, decimal? changeAmount = null, string? cashierName = null);
}
