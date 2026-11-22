using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using MT_GRMS.Application.PosOrders;
using MT_GRMS.Application.Promotions.Dtos;
using MT_GRMS.Domain.Entities.Sales;

namespace MT_GRMS.Infrastructure.Services.Printer;

public class ThermalPrintService : IThermalPrintService
{
    private const int LineWidth = 40; // Kho in chuan may in nhiet 58mm (POS-58) Font B: 40 ky tu can doi sat mep giay, khong bao gio bi tran le

    public bool PrintVoucher(PrintVoucherRequest request, string printerName = "POS-58")
    {
        byte[] bytes = BuildEscPosVoucher(request);
        return RawPrinterHelper.SendBytesToPrinter(printerName, bytes);
    }

    public byte[] BuildEscPosVoucher(PrintVoucherRequest request)
    {
        var buffer = new List<byte>();

        // 1. Khoi tao may in (ESC @)
        buffer.AddRange(new byte[] { 0x1B, 0x40 });

        // 2. Chon Font B nho gon (~7.5pt, 40-42 ky tu/dong) (ESC M 1)
        buffer.AddRange(new byte[] { 0x1B, 0x4D, 0x01 });

        // 3. Header cua hang (Canh giua: ESC a 1)
        buffer.AddRange(new byte[] { 0x1B, 0x61, 0x01 });
        buffer.AddRange(new byte[] { 0x1B, 0x45, 0x01 }); // In dam

        string storeName = !string.IsNullOrWhiteSpace(request.StoreName)
            ? RemoveAccents(request.StoreName).ToUpper()
            : "HE THONG CUA HANG";
        foreach (var l in WordWrap(storeName, LineWidth))
        {
            buffer.AddRange(Encoding.ASCII.GetBytes(l + "\n"));
        }
        buffer.AddRange(new byte[] { 0x1B, 0x45, 0x00 }); // Tat in dam

        if (!string.IsNullOrWhiteSpace(request.BranchName) && !request.BranchName.Contains("Toan he thong", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var l in WordWrap($"Co so: {RemoveAccents(request.BranchName)}", LineWidth))
            {
                buffer.AddRange(Encoding.ASCII.GetBytes(l + "\n"));
            }
        }

        string address = !string.IsNullOrWhiteSpace(request.StoreAddress)
            ? RemoveAccents(request.StoreAddress)
            : "120 Hoang Hoa Tham, Tay Ho, Ha Noi";
        string phone = !string.IsNullOrWhiteSpace(request.StorePhone)
            ? request.StorePhone
            : "024 3822 9988";

        foreach (var l in WordWrap($"Dia chi: {address}", LineWidth))
        {
            buffer.AddRange(Encoding.ASCII.GetBytes(l + "\n"));
        }
        buffer.AddRange(Encoding.ASCII.GetBytes($"Hotline: {phone}\n"));

        // Tieu de phieu voucher (In dam)
        buffer.AddRange(new byte[] { 0x1B, 0x45, 0x01 });
        buffer.AddRange(Encoding.ASCII.GetBytes("\n*** PHIEU VOUCHER / UU DAI ***\n"));
        buffer.AddRange(new byte[] { 0x1B, 0x45, 0x00 });

        if (!string.IsNullOrWhiteSpace(request.BatchName))
        {
            string batchText = $"Dot: {RemoveAccents(request.BatchName)}";
            foreach (var l in WordWrap(batchText, LineWidth))
            {
                buffer.AddRange(Encoding.ASCII.GetBytes(l + "\n"));
            }
        }

        string divider = new string('-', LineWidth);
        buffer.AddRange(Encoding.ASCII.GetBytes(divider + "\n"));

        // 4. Ma Voucher noi bat (Canh giua, in dam)
        buffer.AddRange(new byte[] { 0x1B, 0x61, 0x01 });
        buffer.AddRange(new byte[] { 0x1B, 0x45, 0x01 });
        buffer.AddRange(Encoding.ASCII.GetBytes($"MA VOUCHER:\n[{request.VoucherCode}]\n"));
        buffer.AddRange(new byte[] { 0x1B, 0x45, 0x00 });

        // Canh bao neu da su dung hoac het han
        if (string.Equals(request.Status, "USED", StringComparison.OrdinalIgnoreCase))
        {
            buffer.AddRange(new byte[] { 0x1B, 0x45, 0x01 });
            buffer.AddRange(Encoding.ASCII.GetBytes("[DA SU DUNG - VO HIEU HOA]\n"));
            buffer.AddRange(new byte[] { 0x1B, 0x45, 0x00 });
            if (!string.IsNullOrWhiteSpace(request.OrderCode))
            {
                buffer.AddRange(Encoding.ASCII.GetBytes($"HD: {request.OrderCode}\n"));
            }
            if (request.UsedAt.HasValue)
            {
                buffer.AddRange(Encoding.ASCII.GetBytes($"Luc: {request.UsedAt.Value:dd/MM/yyyy HH:mm}\n"));
            }
        }
        else if (string.Equals(request.Status, "EXPIRED", StringComparison.OrdinalIgnoreCase))
        {
            buffer.AddRange(new byte[] { 0x1B, 0x45, 0x01 });
            buffer.AddRange(Encoding.ASCII.GetBytes("[DA HET HAN SU DUNG]\n"));
            buffer.AddRange(new byte[] { 0x1B, 0x45, 0x00 });
        }
        else if (string.Equals(request.Status, "LOCKED", StringComparison.OrdinalIgnoreCase))
        {
            buffer.AddRange(new byte[] { 0x1B, 0x45, 0x01 });
            buffer.AddRange(Encoding.ASCII.GetBytes("[DANG KHOA]\n"));
            buffer.AddRange(new byte[] { 0x1B, 0x45, 0x00 });
        }

        buffer.AddRange(Encoding.ASCII.GetBytes(divider + "\n"));

        // 5. Chi tiet uu dai & Dieu kien (Canh trai: ESC a 0)
        buffer.AddRange(new byte[] { 0x1B, 0x61, 0x00 });

        string discountStr = string.Equals(request.DiscountType, "PERCENT", StringComparison.OrdinalIgnoreCase)
            ? $"Giam {request.DiscountValue:0.#}%"
            : $"Giam {FormatMoney(request.DiscountValue)} d";
        buffer.AddRange(Encoding.ASCII.GetBytes(FormatTwoColumns("Muc giam:", discountStr, LineWidth) + "\n"));

        if (request.MinOrderValue > 0)
        {
            buffer.AddRange(Encoding.ASCII.GetBytes(FormatTwoColumns("Don toi thieu:", $"{FormatMoney(request.MinOrderValue)} d", LineWidth) + "\n"));
        }

        if (request.MaxDiscountAmount.HasValue && request.MaxDiscountAmount.Value > 0)
        {
            buffer.AddRange(Encoding.ASCII.GetBytes(FormatTwoColumns("Giam toi da:", $"{FormatMoney(request.MaxDiscountAmount.Value)} d", LineWidth) + "\n"));
        }

        string expiryStr = $"{request.EndDate:dd/MM/yyyy}";
        buffer.AddRange(Encoding.ASCII.GetBytes(FormatTwoColumns("Han dung den:", expiryStr, LineWidth) + "\n"));

        string scopeStr = !string.IsNullOrWhiteSpace(request.AppliedBranches)
            ? RemoveAccents(request.AppliedBranches)
            : "Toan he thong chi nhanh";
        buffer.AddRange(Encoding.ASCII.GetBytes(FormatTwoColumns("Pham vi:", scopeStr, LineWidth) + "\n"));

        if (!string.IsNullOrWhiteSpace(request.CustomerName))
        {
            string custInfo = RemoveAccents(request.CustomerName);
            if (!string.IsNullOrWhiteSpace(request.CustomerPhone))
            {
                custInfo += $" ({request.CustomerPhone})";
            }
            buffer.AddRange(Encoding.ASCII.GetBytes(FormatTwoColumns("Cap rieng cho:", custInfo, LineWidth) + "\n"));
        }

        buffer.AddRange(Encoding.ASCII.GetBytes(divider + "\n"));

        // 6. Dieu kien & Ghi chu (Canh trai)
        buffer.AddRange(Encoding.ASCII.GetBytes("* Dieu kien su dung:\n"));
        buffer.AddRange(Encoding.ASCII.GetBytes($"- Ap dung: {scopeStr}.\n"));
        buffer.AddRange(Encoding.ASCII.GetBytes("- Xuat trinh ma cho thu ngan truoc khi thanh toan.\n"));
        buffer.AddRange(Encoding.ASCII.GetBytes("- Moi ma chi su dung 01 lan tai quay POS.\n"));
        buffer.AddRange(Encoding.ASCII.GetBytes("- Khong co gia tri quy doi thanh tien mat.\n"));

        buffer.AddRange(Encoding.ASCII.GetBytes(divider + "\n"));

        // 7. Chan phieu (Canh giua: ESC a 1)
        buffer.AddRange(new byte[] { 0x1B, 0x61, 0x01 });
        buffer.AddRange(Encoding.ASCII.GetBytes("CAM ON QUY KHACH & HEN GAP LAI!\n"));

        // 8. Day giay ra (Feed 4 dong)
        buffer.AddRange(Encoding.ASCII.GetBytes("\n\n\n\n"));

        // 9. Cat giay tu dong (GS V 66 0)
        buffer.AddRange(new byte[] { 0x1D, 0x56, 0x42, 0x00 });

        return buffer.ToArray();
    }

    public bool PrintOrderInvoice(Order order, string printerName = "POS-58", decimal? cashReceived = null, decimal? changeAmount = null, string? cashierName = null)

    {
        byte[] bytes = BuildEscPosReceipt(order, cashReceived, changeAmount, cashierName);
        return RawPrinterHelper.SendBytesToPrinter(printerName, bytes);
    }

    public byte[] BuildEscPosReceipt(Order order, decimal? cashReceived = null, decimal? changeAmount = null, string? cashierName = null)
    {
        var buffer = new List<byte>();

        // 1. Khởi tạo máy in (ESC @)
        buffer.AddRange(new byte[] { 0x1B, 0x40 });

        // 2. Chọn font nhỏ gọn Font B (~7.5pt, 40-42 ký tự/dòng trên giấy 58mm) (ESC M 1)
        buffer.AddRange(new byte[] { 0x1B, 0x4D, 0x01 });

        // 3. Header cửa hàng tạp hóa / siêu thị mini (Canh giữa: ESC a 1)
        buffer.AddRange(new byte[] { 0x1B, 0x61, 0x01 });
        buffer.AddRange(new byte[] { 0x1B, 0x45, 0x01 }); // In đậm

        string storeName = order.Branch?.Name != null && !string.IsNullOrWhiteSpace(order.Branch.Name)
            ? RemoveAccents(order.Branch.Name).ToUpper()
            : "SIEU THI MINI";
        foreach (var l in WordWrap(storeName, LineWidth))
        {
            buffer.AddRange(Encoding.ASCII.GetBytes(l + "\n"));
        }
        buffer.AddRange(new byte[] { 0x1B, 0x45, 0x00 }); // Tắt in đậm

        string address = !string.IsNullOrWhiteSpace(order.Branch?.Address)
            ? RemoveAccents(order.Branch.Address)
            : "120 Hoang Hoa Tham, Tay Ho, Ha Noi";
        string phone = !string.IsNullOrWhiteSpace(order.Branch?.Phone)
            ? order.Branch.Phone
            : (!string.IsNullOrWhiteSpace(order.Branch?.Tenant?.Hotline) ? order.Branch.Tenant.Hotline : "024 3822 9988");

        foreach (var l in WordWrap($"Dia chi: {address}", LineWidth))
        {
            buffer.AddRange(Encoding.ASCII.GetBytes(l + "\n"));
        }
        buffer.AddRange(Encoding.ASCII.GetBytes($"Hotline: {phone}\n"));

        // Tiêu đề hóa đơn
        buffer.AddRange(new byte[] { 0x1B, 0x45, 0x01 });
        bool isPreOrder = string.Equals(order.Status, "PreOrder", StringComparison.OrdinalIgnoreCase)
            || string.Equals(order.Status, "PendingPickup", StringComparison.OrdinalIgnoreCase)
            || (order.OrderCode != null && (order.OrderCode.StartsWith("DH", StringComparison.OrdinalIgnoreCase) || order.OrderCode.StartsWith("PO", StringComparison.OrdinalIgnoreCase)));
        string receiptTitle = isPreOrder ? "\nPHIEU DAT HANG TRUOC\n" : "\nHOA DON BAN LE\n";
        buffer.AddRange(Encoding.ASCII.GetBytes(receiptTitle));
        buffer.AddRange(new byte[] { 0x1B, 0x45, 0x00 });

        // 4. Thông tin hóa đơn (Canh trái: ESC a 0)
        buffer.AddRange(new byte[] { 0x1B, 0x61, 0x00 });
        string codePrefix = isPreOrder ? "MA DAT: " : "HD:  ";
        buffer.AddRange(Encoding.ASCII.GetBytes($"{codePrefix}{order.OrderCode ?? "HD0001"}\n"));

        string dateLabel = isPreOrder ? "Ngay tao:" : "Ngay ban:";
        string dateStr = $"{order.OrderDate:dd/MM/yyyy HH:mm}";
        buffer.AddRange(Encoding.ASCII.GetBytes(FormatTwoColumns(dateLabel, dateStr, LineWidth) + "\n"));

        string cashierText;
        if (!string.IsNullOrWhiteSpace(cashierName))
        {
            string norm = RemoveAccents(cashierName.Trim());
            if (norm.StartsWith("Chu cua hang", StringComparison.OrdinalIgnoreCase) ||
                norm.StartsWith("Owner", StringComparison.OrdinalIgnoreCase))
            {
                cashierText = norm;
            }
            else
            {
                cashierText = $"Thu ngan: {norm}";
            }
        }
        else
        {
            cashierText = "Thu ngan: Mac dinh";
        }

        foreach (var l in WordWrap(cashierText, LineWidth))
        {
            buffer.AddRange(Encoding.ASCII.GetBytes(l + "\n"));
        }

        // Đường gạch nét đứt (40 ký tự)
        string divider = new string('-', LineWidth);
        string doubleDivider = new string('=', LineWidth);

        buffer.AddRange(Encoding.ASCII.GetBytes(divider + "\n"));
        // Header bảng: STT Ten hang (trai) va Thanh tien (phai)
        string tableHeader1 = FormatTwoColumns("STT  Ten hang", "Thanh tien", LineWidth);
        string tableHeader2 = "     Don gia / So luong";
        buffer.AddRange(Encoding.ASCII.GetBytes(tableHeader1 + "\n"));
        buffer.AddRange(Encoding.ASCII.GetBytes(tableHeader2 + "\n"));
        buffer.AddRange(Encoding.ASCII.GetBytes(divider + "\n"));

        // 5. Chi tiết các món hàng: tối đa 4 từ trên 1 dòng, thụt lề 3 khoảng trắng
        int totalQuantity = 0;
        if (order.Details != null && order.Details.Count > 0)
        {
            int stt = 1;
            foreach (var item in order.Details)
            {
                string rawName = !string.IsNullOrWhiteSpace(item.ProductName)
                    ? item.ProductName
                    : (item.Product != null ? item.Product.Name : "San pham");
                rawName = RemoveAccents(rawName.Trim());
                if (item.IsGift)
                {
                    rawName = $"[Qua KM] {rawName}";
                }

                string[] words = rawName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var nameLines = new List<string>();
                for (int w = 0; w < words.Length; w += 4)
                {
                    int count = Math.Min(4, words.Length - w);
                    nameLines.Add(string.Join(" ", words.Skip(w).Take(count)));
                }
                if (nameLines.Count == 0)
                {
                    nameLines.Add("San pham");
                }

                // In các dòng của tên sản phẩm (mỗi dòng tối đa 4 từ)
                for (int i = 0; i < nameLines.Count; i++)
                {
                    if (i == 0)
                    {
                        buffer.AddRange(Encoding.ASCII.GetBytes($"{stt}. {nameLines[i]}\n"));
                    }
                    else
                    {
                        buffer.AddRange(Encoding.ASCII.GetBytes($"   {nameLines[i]}\n"));
                    }
                }

                // Dòng giá: [SL] x [Đơn giá] bên trái, [Thành tiền] bên phải
                int itemQty = (int)Math.Round(item.Qty);
                totalQuantity += itemQty;

                string qtyPriceStr = item.IsGift
                    ? $"   {item.Qty:0.##} x 0 (Tang KM)"
                    : $"   {item.Qty:0.##} x {FormatMoney(item.UnitPrice)}";
                string lineTotalStr = item.IsGift ? "0" : FormatMoney(item.UnitPrice * item.Qty);
                string priceLine = FormatTwoColumns(qtyPriceStr, lineTotalStr, LineWidth);
                buffer.AddRange(Encoding.ASCII.GetBytes(priceLine + "\n"));

                stt++;
            }
        }

        buffer.AddRange(Encoding.ASCII.GetBytes(divider + "\n"));

        // Dòng tổng cộng bảng
        string totalRow = FormatTwoColumns($"Tong cong: {totalQuantity}", FormatMoney(order.TotalAmount), LineWidth);
        buffer.AddRange(Encoding.ASCII.GetBytes(totalRow + "\n"));
        buffer.AddRange(Encoding.ASCII.GetBytes(divider + "\n"));

        // 6. Tổng kết tiền hàng và thanh toán chi tiết minh bạch
        decimal itemDiscounts = order.Details != null ? order.Details.Sum(d => d.DiscountAmount) : 0m;
        decimal promoDiscount = order.PromotionDiscountAmount > 0
            ? order.PromotionDiscountAmount
            : Math.Max(0m, order.DiscountAmount - itemDiscounts - order.VoucherDiscountAmount - order.PointsDiscountAmount);

        if (itemDiscounts > 0)
        {
            string itemDiscStr = "-" + FormatMoney(itemDiscounts) + " d";
            buffer.AddRange(Encoding.ASCII.GetBytes(FormatTwoColumns("Chiet khau sp:", itemDiscStr, LineWidth) + "\n"));
        }

        if (promoDiscount > 0 || !string.IsNullOrWhiteSpace(order.AppliedPromotionSummary))
        {
            string promoVal = promoDiscount > 0 ? $"-{FormatMoney(promoDiscount)} d" : "";
            buffer.AddRange(Encoding.ASCII.GetBytes(FormatTwoColumns("Khuyen mai don:", promoVal, LineWidth) + "\n"));
            if (!string.IsNullOrWhiteSpace(order.AppliedPromotionSummary))
            {
                foreach (var l in WordWrap($"  CTKM: {RemoveAccents(order.AppliedPromotionSummary)}", LineWidth))
                {
                    buffer.AddRange(Encoding.ASCII.GetBytes(l + "\n"));
                }
            }
        }
        else if (order.DiscountAmount > 0 && itemDiscounts <= 0)
        {
            string discStr = "-" + FormatMoney(order.DiscountAmount) + " d";
            buffer.AddRange(Encoding.ASCII.GetBytes(FormatTwoColumns("Tien chiet khau:", discStr, LineWidth) + "\n"));
        }
        if (!string.IsNullOrWhiteSpace(order.AppliedVoucherCode))
        {
            string vchVal = order.VoucherDiscountAmount > 0 ? $"-{FormatMoney(order.VoucherDiscountAmount)} d" : "Da ap dung";
            buffer.AddRange(Encoding.ASCII.GetBytes(FormatTwoColumns($"  Voucher ({RemoveAccents(order.AppliedVoucherCode)}):", vchVal, LineWidth) + "\n"));
        }
        if (order.PointsRedeemed > 0)
        {
            string ptsVal = order.PointsDiscountAmount > 0 ? $"-{FormatMoney(order.PointsDiscountAmount)} d" : "Da doi";
            buffer.AddRange(Encoding.ASCII.GetBytes(FormatTwoColumns($"  Doi {order.PointsRedeemed} diem:", ptsVal, LineWidth) + "\n"));
        }

        // TỔNG PHẢI TRẢ (In đậm)
        buffer.AddRange(new byte[] { 0x1B, 0x45, 0x01 });
        string finalTotalStr = FormatMoney(order.FinalAmount) + " d";
        buffer.AddRange(Encoding.ASCII.GetBytes(FormatTwoColumns("TONG PHAI TRA:", finalTotalStr, LineWidth) + "\n"));
        buffer.AddRange(new byte[] { 0x1B, 0x45, 0x00 });

        decimal cash = cashReceived ?? (order.PaymentMethod == "CASH" ? order.FinalAmount : 0m);
        string payMethodLabel = order.PaymentMethod switch
        {
            "CASH" => "(Tien mat)",
            "VIETQR" => "(VietQR)",
            "BANK_TRANSFER" or "BANK" => "(Chuyen khoan)",
            "CREDIT" or "DEBT" => "(Ban no)",
            _ => $"({order.PaymentMethod ?? "Tien mat"})"
        };

        buffer.AddRange(Encoding.ASCII.GetBytes(FormatTwoColumns("Tien khach tra:", payMethodLabel, LineWidth) + "\n"));

        if (order.PaymentMethod == "CASH")
        {
            buffer.AddRange(Encoding.ASCII.GetBytes(FormatTwoColumns("  Tien mat:", FormatMoney(cash) + " d", LineWidth) + "\n"));
            decimal change = changeAmount ?? Math.Max(0, cash - order.FinalAmount);
            buffer.AddRange(Encoding.ASCII.GetBytes(FormatTwoColumns("  Tien con du:", FormatMoney(change) + " d", LineWidth) + "\n"));
        }
        else if (order.PaymentMethod == "VIETQR" || order.PaymentMethod == "BANK_TRANSFER" || order.PaymentMethod == "BANK")
        {
            buffer.AddRange(Encoding.ASCII.GetBytes(FormatTwoColumns("  Chuyen khoan:", FormatMoney(order.FinalAmount) + " d", LineWidth) + "\n"));
            buffer.AddRange(Encoding.ASCII.GetBytes(FormatTwoColumns("  Tien con du:", "0 d", LineWidth) + "\n"));
        }
        else if (order.PaymentMethod == "CREDIT" || order.PaymentMethod == "DEBT")
        {
            buffer.AddRange(Encoding.ASCII.GetBytes(FormatTwoColumns("  Con no:", FormatMoney(order.FinalAmount) + " d", LineWidth) + "\n"));
        }
        else
        {
            buffer.AddRange(Encoding.ASCII.GetBytes(FormatTwoColumns("  Hinh thuc:", order.PaymentMethod ?? "Tien mat", LineWidth) + "\n"));
        }

        if (isPreOrder && order.DepositAmount > 0)
        {
            buffer.AddRange(Encoding.ASCII.GetBytes(FormatTwoColumns("  Tien dat coc:", FormatMoney(order.DepositAmount) + " d", LineWidth) + "\n"));
            decimal remaining = Math.Max(0, order.FinalAmount - order.DepositAmount);
            buffer.AddRange(Encoding.ASCII.GetBytes(FormatTwoColumns("  Con phai tra:", FormatMoney(remaining) + " d", LineWidth) + "\n"));
        }

        // 7. Khối Thông Tin Khách Hàng (chuẩn theo form ảnh)
        buffer.AddRange(Encoding.ASCII.GetBytes(divider + "\n"));
        bool isWalkIn = order.Customer == null
            || string.Equals(order.Customer.CustomerCode, "KHACHLE", StringComparison.OrdinalIgnoreCase)
            || string.Equals(order.Customer.Name?.Trim(), "Khách lẻ", StringComparison.OrdinalIgnoreCase)
            || string.Equals(order.Customer.Name?.Trim(), "Khách hàng lẻ", StringComparison.OrdinalIgnoreCase);

        if (!isWalkIn && order.Customer != null)
        {
            buffer.AddRange(new byte[] { 0x1B, 0x45, 0x01 });
            buffer.AddRange(Encoding.ASCII.GetBytes("THONG TIN KHACH HANG\n"));
            buffer.AddRange(new byte[] { 0x1B, 0x45, 0x00 });

            string custName = RemoveAccents(order.Customer.Name);
            buffer.AddRange(Encoding.ASCII.GetBytes($"Ten khach hang: {custName}\n"));
            string custAddr = !string.IsNullOrWhiteSpace(order.Customer.Address) ? RemoveAccents(order.Customer.Address) : "- - -";
            foreach (var l in WordWrap($"Dia chi: {custAddr}", LineWidth))
            {
                buffer.AddRange(Encoding.ASCII.GetBytes(l + "\n"));
            }
            string custPhone = !string.IsNullOrWhiteSpace(order.Customer.Phone) ? order.Customer.Phone : "- - -";
            buffer.AddRange(Encoding.ASCII.GetBytes($"So dien thoai: {custPhone}\n"));
            buffer.AddRange(Encoding.ASCII.GetBytes($"Tong diem tich luy: {order.Customer.RewardPoints} diem\n"));
        }
        else
        {
            buffer.AddRange(Encoding.ASCII.GetBytes("Khach hang: Khach hang le (Khong tich diem)\n"));
        }

        // 8. Chân trang siêu thị mini / tạp hóa (Canh giữa: ESC a 1)
        buffer.AddRange(Encoding.ASCII.GetBytes(divider + "\n"));
        buffer.AddRange(new byte[] { 0x1B, 0x61, 0x01 });
        buffer.AddRange(new byte[] { 0x1B, 0x45, 0x01 });
        buffer.AddRange(Encoding.ASCII.GetBytes("CAM ON QUY KHACH & HEN GAP LAI!\n"));
        buffer.AddRange(new byte[] { 0x1B, 0x45, 0x00 });

        // 9. Đẩy giấy 4 dòng và gửi lệnh cắt giấy tự động (GS V B 0)
        buffer.AddRange(Encoding.ASCII.GetBytes("\n\n\n\n"));
        buffer.AddRange(new byte[] { 0x1D, 0x56, 0x42, 0x00 });

        return buffer.ToArray();
    }

    private static List<string> WordWrap(string text, int maxWidth)
    {
        var lines = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return lines;

        string[] words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var currentLine = new StringBuilder();

        foreach (var word in words)
        {
            if (currentLine.Length == 0)
            {
                currentLine.Append(word);
            }
            else if (currentLine.Length + 1 + word.Length <= maxWidth)
            {
                currentLine.Append(' ').Append(word);
            }
            else
            {
                lines.Add(currentLine.ToString());
                currentLine.Clear();
                currentLine.Append(word);
            }
        }

        if (currentLine.Length > 0)
        {
            lines.Add(currentLine.ToString());
        }

        return lines;
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

    private static string RemoveAccents(string? text)
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
