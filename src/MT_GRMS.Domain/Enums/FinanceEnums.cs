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
    Open = 1,   // Đang mở ca
    Closed = 2  // Đã chốt ca Z-report
}