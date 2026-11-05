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
    Reversed = 8,          // Đã bị đảo bút toán
    Unresolved = 9         // Treo tạm chờ phân loại (Triage Inbox)
}

/// <summary>
/// Financial nature of non-POS transactions for P&L and Cashbook classification.
/// </summary>
public enum FinancialNature
{
    OperatingExpense = 1, // Chi phí vận hành OPEX (Điện, nước, sửa chữa...)
    Deposit = 2,          // Tiền cọc vỏ/bình (0% P&L, 0% Thuế)
    RefundDeposit = 3,    // Hoàn trả cọc vỏ/bình (0% P&L, 0% Thuế)
    OtherIncome = 4,      // Thu nhập khác (Ve chai, phế liệu...)
    OwnerDraw = 5,        // Rút vốn chủ sở hữu (0% P&L, 0% Thuế)
    OwnerDeposit = 6,     // Nạp vốn chủ sở hữu (0% P&L, 0% Thuế)
    InternalTransfer = 7, // Chuyển quỹ nội bộ (0% P&L)
    TaxPayment = 8,       // Nộp thuế NSNN (Xóa nghĩa vụ thuế, 0% OPEX)
    Suspense = 9          // Treo tạm chờ phân loại (0% P&L, 0% Thuế)
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