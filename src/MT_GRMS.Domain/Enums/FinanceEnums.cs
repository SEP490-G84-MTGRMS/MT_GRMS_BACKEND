namespace MT_GRMS.Domain.Enums;

/// <summary>
/// Classification of financial accounts.
/// </summary>
public enum FinanceAccountType
{
    Till = 1,      // Két tiền mặt tại quầy
    Safe = 2,      // Két sắt chi nhánh
    Bank = 3,      // Tài khoản ngân hàng
    Transit = 4,   // Tiền đang chuyển (Cash in Transit)
    Suspense = 5   // Tài khoản treo chờ xử lý
}

/// <summary>
/// Cashbook entry type (Receipt vs Disbursement).
/// </summary>
public enum CashbookEntryType
{
    Receipt = 1, // Thu tiền (Cash In)
    Payment = 2  // Chi tiền (Cash Out)
}

/// <summary>
/// 5-step lifecycle for operational expense vouchers.
/// </summary>
public enum ExpenseVoucherStatus
{
    Draft = 1,             // Bản nháp
    PendingApproval = 2,   // Chờ duyệt
    Approved = 3,          // Đã duyệt (chưa xuất quỹ)
    Paid = 4,              // Đã xuất quỹ thành công
    PaymentFailed = 5,     // Xuất quỹ thất bại (thiếu tiền mặt)
    Cancelled = 6,         // Đã hủy trước khi duyệt
    Expired = 7,           // Quá hạn duyệt
    Reversed = 8           // Đã bị đảo bút toán
}

/// <summary>
/// Cash in transit status for 2-phase bank deposits.
/// </summary>
public enum CashInTransitStatus
{
    PendingBankConfirmation = 1, // Đang mang đi nộp
    Completed = 2,               // Đã vào tài khoản ngân hàng
    Discrepancy = 3,             // Tiền thực nhận bị lệch
    Cancelled = 4,               // Đã hủy trước khi nộp
    LossInTransit = 5            // Thất thoát trên đường mang tiền
}

/// <summary>
/// Cashier shift status.
/// </summary>
public enum ShiftState
{
    Open = 1,         // Đang mở ca
    Closed = 2,       // Đã chốt ca Z-report
    Closing = 3,      // Đang tiến hành chốt ca
    ForceClosed = 4   // Buộc đóng ca bởi Chủ cửa hàng
}

/// <summary>
/// Loại biến động tiền mặt trong két ca làm việc.
/// </summary>
public enum CashMovementType
{
    IN = 1,  // Tiền vào két (nạp tiền lẻ, thu tiền...)
    OUT = 2  // Tiền ra khỏi két (chi vặt, rút nộp chủ...)
}

/// <summary>
/// Danh mục phân loại thu/chi tiền mặt tại quầy.
/// </summary>
public enum CashMovementCategory
{
    EXPENSE = 1,       // Chi vặt (mua đá, văn phòng phẩm...)
    CASH_DRAW = 2,     // Rút két nộp chủ cửa hàng / chuyển két an toàn
    CASH_DEPOSIT = 3,  // Nạp thêm tiền lẻ vào két
    DEBT_COLLECT = 4   // Thu nợ khách hàng bằng tiền mặt
}

/// <summary>
/// Trạng thái xác nhận bàn giao két giữa 2 ca liên tiếp.
/// </summary>
public enum HandoverStatus
{
    PENDING = 1,    // Chờ ca sau xác nhận
    CONFIRMED = 2,  // Ca sau xác nhận khớp tiền
    DISPUTED = 3    // Ca sau khiếu nại thiếu/sai tiền
}
