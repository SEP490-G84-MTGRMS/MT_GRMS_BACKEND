using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using MT_GRMS.Application.PosOrders;
using MT_GRMS.Domain.Entities.Sales;

namespace MT_GRMS.Infrastructure.Services.Printer;

public class ThermalPrintService : IThermalPrintService
{
    private const int LineWidth = 32; // Khổ in hiệu dụng 43mm trên giấy 58mm chuẩn 32 ký tự

    public bool PrintOrderInvoice(Order order, string printerName = "POS-58", decimal? cashReceived = null, decimal? changeAmount = null)
    {
        byte[] bytes = BuildEscPosReceipt(order, cashReceived, changeAmount);
        return RawPrinterHelper.SendBytesToPrinter(printerName, bytes);
    }

    public byte[] BuildEscPosReceipt(Order order, decimal? cashReceived = null, decimal? changeAmount = null)
    {
        var buffer = new List<byte>();

        // 1. Khởi tạo máy in (ESC @)
        buffer.AddRange(new byte[] { 0x1B, 0x40 });

        // 2. Chọn font nhỏ gọn Font B (~7.5pt) để vừa vặn khổ 43mm không bị tràn lề (ESC M 1)
        buffer.AddRange(new byte[] { 0x1B, 0x4D, 0x01 });

        // 3. Header cửa hàng tạp hóa / siêu thị mini (Canh giữa: ESC a 1)
        buffer.AddRange(new byte[] { 0x1B, 0x61, 0x01 });
        buffer.AddRange(new byte[] { 0x1B, 0x45, 0x01 }); // In đậm

        string storeName = order.Branch?.Name != null && !string.IsNullOrWhiteSpace(order.Branch.Name)
            ? RemoveAccents(order.Branch.Name).ToUpper()
            : "SIEU THI MINI MT-GROCERY";
        buffer.AddRange(Encoding.ASCII.GetBytes(storeName + "\n"));
        buffer.AddRange(new byte[] { 0x1B, 0x45, 0x00 }); // Tắt in đậm

        string address = !string.IsNullOrWhiteSpace(order.Branch?.Address)
            ? RemoveAccents(order.Branch.Address)
            : "123 Cau Giay, Q. Cau Giay, Ha Noi";
        string phone = !string.IsNullOrWhiteSpace(order.Branch?.Phone)
            ? order.Branch.Phone
            : "0987.654.321";

        buffer.AddRange(Encoding.ASCII.GetBytes(address + "\n"));
        buffer.AddRange(Encoding.ASCII.GetBytes($"Hotline: {phone}\n"));
        buffer.AddRange(Encoding.ASCII.GetBytes("Wifi: MT-GROCERY | Pass: 68686868\n"));

        // Tiêu đề hóa đơn
        buffer.AddRange(new byte[] { 0x1B, 0x45, 0x01 });
        buffer.AddRange(Encoding.ASCII.GetBytes("\nHOA DON BAN LE (POS)\n"));
        buffer.AddRange(new byte[] { 0x1B, 0x45, 0x00 });

        // Đường gạch nét đứt (32 ký tự)
        buffer.AddRange(Encoding.ASCII.GetBytes("--------------------------------\n"));

        // 4. Canh trái thông tin đơn hàng (ESC a 0)
        buffer.AddRange(new byte[] { 0x1B, 0x61, 0x00 });
        buffer.AddRange(Encoding.ASCII.GetBytes($"So HD: {order.OrderCode ?? "HD0001"}\n"));
        buffer.AddRange(Encoding.ASCII.GetBytes($"Ngay: {order.OrderDate:dd/MM/yyyy HH:mm:ss}\n"));
        
        string customerName = order.Customer != null ? RemoveAccents(order.Customer.Name) : "Khach le";
        buffer.AddRange(Encoding.ASCII.GetBytes(FormatTwoColumns("Khach hang:", customerName, LineWidth) + "\n"));

        buffer.AddRange(Encoding.ASCII.GetBytes("--------------------------------\n"));
        buffer.AddRange(Encoding.ASCII.GetBytes(FormatTwoColumns("Ten hang hoa", "SL   T.Tien", LineWidth) + "\n"));
        buffer.AddRange(Encoding.ASCII.GetBytes("--------------------------------\n"));

        // 5. Chi tiết các món hàng theo mẫu tạp hóa (Tên món dòng 1, Số lượng x Đơn giá và Thành tiền dòng 2)
        if (order.Details != null && order.Details.Count > 0)
        {
            foreach (var item in order.Details)
            {
                string productName = !string.IsNullOrWhiteSpace(item.ProductName)
                    ? item.ProductName
                    : (item.Product != null ? item.Product.Name : "San pham");
                productName = RemoveAccents(productName);

                buffer.AddRange(Encoding.ASCII.GetBytes(productName + "\n"));

                string qtyUnit = $"{item.Qty:0.##}";
                string unitPriceStr = FormatMoney(item.UnitPrice);
                string lineTotalStr = FormatMoney(item.ActualPrice * item.Qty);

                string leftCol = $"  {qtyUnit} x {unitPriceStr}";
                buffer.AddRange(Encoding.ASCII.GetBytes(FormatTwoColumns(leftCol, lineTotalStr, LineWidth) + "\n"));
            }
        }

        buffer.AddRange(Encoding.ASCII.GetBytes("--------------------------------\n"));

        // 6. Tổng kết tiền và thuế GTGT
        string subtotalStr = FormatMoney(order.TotalAmount);
        buffer.AddRange(Encoding.ASCII.GetBytes(FormatTwoColumns("Tong tien hang:", subtotalStr + " d", LineWidth) + "\n"));

        if (order.DiscountAmount > 0)
        {
            string discStr = "-" + FormatMoney(order.DiscountAmount) + " d";
            buffer.AddRange(Encoding.ASCII.GetBytes(FormatTwoColumns("Chiet khau / Giam:", discStr, LineWidth) + "\n"));
        }

        // Tính VAT nếu có
        decimal totalVat = 0m;
        if (order.Details != null)
        {
            foreach (var d in order.Details)
            {
                totalVat += d.VatAmount;
            }
        }
        if (totalVat > 0)
        {
            buffer.AddRange(Encoding.ASCII.GetBytes(FormatTwoColumns("Thue VAT:", FormatMoney(totalVat) + " d", LineWidth) + "\n"));
        }

        buffer.AddRange(Encoding.ASCII.GetBytes("================================\n"));

        // TỔNG CỘNG THANH TOÁN (In đậm)
        buffer.AddRange(new byte[] { 0x1B, 0x45, 0x01 });
        string finalTotalStr = FormatMoney(order.FinalAmount) + " d";
        buffer.AddRange(Encoding.ASCII.GetBytes(FormatTwoColumns("TONG THANH TOAN:", finalTotalStr, LineWidth) + "\n"));
        buffer.AddRange(new byte[] { 0x1B, 0x45, 0x00 });

        // Hình thức thanh toán
        string pMethod = order.PaymentMethod?.ToUpper() switch
        {
            "CASH" => "Tien mat",
            "VIETQR" or "BANK_TRANSFER" => "Chuyen khoan VietQR",
            "DEBT" => "Ban no (Ghi so)",
            _ => order.PaymentMethod ?? "Tien mat"
        };
        buffer.AddRange(Encoding.ASCII.GetBytes(FormatTwoColumns("Hinh thuc:", pMethod, LineWidth) + "\n"));

        if (order.PaymentMethod == "CASH")
        {
            decimal cash = cashReceived ?? order.FinalAmount;
            decimal change = changeAmount ?? Math.Max(0, cash - order.FinalAmount);
            buffer.AddRange(Encoding.ASCII.GetBytes(FormatTwoColumns("Tien khach dua:", FormatMoney(cash) + " d", LineWidth) + "\n"));
            buffer.AddRange(Encoding.ASCII.GetBytes(FormatTwoColumns("Tien tra lai:", FormatMoney(change) + " d", LineWidth) + "\n"));
        }

        if (order.Customer != null)
        {
            int earnedPoints = (int)(order.FinalAmount / 10000);
            buffer.AddRange(Encoding.ASCII.GetBytes(FormatTwoColumns("Diem tich luy:", $"+{earnedPoints} | Tong: {order.Customer.RewardPoints}", LineWidth) + "\n"));
        }

        buffer.AddRange(Encoding.ASCII.GetBytes("--------------------------------\n"));

        // 7. Chân trang siêu thị mini / tạp hóa (Canh giữa: ESC a 1)
        buffer.AddRange(new byte[] { 0x1B, 0x61, 0x01 });
        buffer.AddRange(Encoding.ASCII.GetBytes("Quy khach kiem tra ky hoa don\n"));
        buffer.AddRange(Encoding.ASCII.GetBytes("va hang hoa truoc khi roi quay!\n"));
        buffer.AddRange(Encoding.ASCII.GetBytes("Doi tra trong 24h kem hoa don.\n"));
        buffer.AddRange(new byte[] { 0x1B, 0x45, 0x01 });
        buffer.AddRange(Encoding.ASCII.GetBytes("CAM ON QUY KHACH & HEN GAP LAI!\n"));
        buffer.AddRange(new byte[] { 0x1B, 0x45, 0x00 });

        // 8. Đẩy giấy 4 dòng và gửi lệnh cắt giấy tự động (GS V B 0)
        buffer.AddRange(Encoding.ASCII.GetBytes("\n\n\n\n"));
        buffer.AddRange(new byte[] { 0x1D, 0x56, 0x42, 0x00 });

        return buffer.ToArray();
    }

    private static string FormatTwoColumns(string left, string right, int width = LineWidth)
    {
        left = RemoveAccents(left ?? string.Empty);
        right = RemoveAccents(right ?? string.Empty);

        if (left.Length + right.Length >= width)
        {
            int maxLeft = Math.Max(1, width - right.Length - 1);
            if (left.Length > maxLeft)
            {
                left = left.Substring(0, maxLeft);
            }
        }

        int spaces = Math.Max(1, width - left.Length - right.Length);
        return left + new string(' ', spaces) + right;
    }

    private static string FormatMoney(decimal amount) => amount.ToString("#,##0", new CultureInfo("vi-VN"));

    private static string RemoveAccents(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        string normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (char c in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c);
            }
        }
        return sb.ToString().Normalize(NormalizationForm.FormC)
            .Replace("đ", "d").Replace("Đ", "D")
            .Replace("–", "-").Replace("—", "-");
    }
}
