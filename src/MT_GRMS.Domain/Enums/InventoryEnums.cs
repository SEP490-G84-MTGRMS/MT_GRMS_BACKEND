namespace MT_GRMS.Domain.Enums;

/// <summary>
/// Trạng thái phiếu điều chuyển hàng hóa liên chi nhánh (Transfers)
/// Theo quy trình chuyển hàng 2 pha UC-15 / BR-TRF:
/// - Draft: Phiếu đang soạn, chưa gửi cho chi nhánh nguồn.
/// - Pending: Đã gửi, chờ chi nhánh nguồn xác nhận xuất.
/// - InTransit: Đã trừ kho nguồn, hàng đang vận chuyển.
/// - Completed: Chi nhánh đích đã kiểm nhận hoặc hàng đã được hoàn trả.
/// - Cancelled: Đã hủy trước khi xuất; không dùng để hoàn tồn hàng đang vận chuyển.
/// </summary>
public enum TransferStatus
{
    Draft = 1,
    Pending = 2,
    InTransit = 3,
    Completed = 4,
    [Obsolete("Use Completed. Kept temporarily for source compatibility during the workflow migration.")]
    Received = Completed,
    Cancelled = 5
}

public enum TransferCompletionResult
{
    Received = 1,
    ReceivedWithDiscrepancy = 2,
    Returned = 3,
    Partial = 4
}

public enum TransferOrigin
{
    OwnerAllocation = 1,
    StaffUrgentRequest = 2
}

public enum TransferPriority
{
    Normal = 1,
    Urgent = 2
}

public enum TransferRequestStatus
{
    Open = 1,
    Completed = 2,
    Cancelled = 3
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

