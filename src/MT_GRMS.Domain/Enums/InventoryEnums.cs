namespace MT_GRMS.Domain.Enums;

/// <summary>
/// Trạng thái phiếu điều chuyển hàng hóa liên chi nhánh (Transfers)
/// Theo quy trình chuyển hàng 2 pha UC-15 / BR-TRF:
/// - Draft: Phiếu tạm / Lưu tạm (chưa trừ kho gửi)
/// - InTransit: Đang chuyển (đã trừ kho chi nhánh gửi, hàng đang trên đường đi)
/// - Received: Đã nhận (chi nhánh nhận đã kiểm đếm thực tế và cộng tồn kho đích)
/// - Cancelled: Đã hủy (hoàn nguyên 100% tồn kho về lại chi nhánh gửi)
/// </summary>
public enum TransferStatus
{
    Draft = 1,
    InTransit = 2,
    Received = 3,
    Cancelled = 4
}

/// <summary>
/// Trạng thái dòng kiểm hàng chi tiết phiếu điều chuyển (TransferDetails)
/// Phản ánh kết quả đối soát giữa số lượng xuất gửi (SentQty) và số lượng nhận thực tế (ReceivedQty)
/// </summary>
public enum TransferLineStatus
{
    Pending = 0,    // Chưa kiểm đếm (phiếu ở trạng thái Draft hoặc InTransit)
    Matched = 1,    // Khớp đủ 100% (SentQty == ReceivedQty và không bể vỡ)
    Deficit = 2,    // Nhận thiếu (ReceivedQty < SentQty)
    Surplus = 3,    // Nhận thừa (ReceivedQty > SentQty)
    Damaged = 4     // Hư hỏng / Bể vỡ trong quá trình vận chuyển (DamagedQty > 0)
}

/// <summary>
/// Bí danh cho TransferLineStatus
/// </summary>
public enum TransferDetailStatus
{
    Pending = 0,
    Matched = 1,
    Deficit = 2,
    Surplus = 3,
    Damaged = 4
}

