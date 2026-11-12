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
    DEBT_COLLECT = 4,  // Thu nợ khách hàng bằng tiền mặt
    PREORDER_PICKUP = 5, // Thu tiền còn lại khi giao hàng đơn đặt trước
    RETURN_REFUND = 6   // Hoàn tiền mặt trả hàng cho khách tại quầy
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

public enum CashbookPostingStatus
{
    NotPosted = 1,
    Posted = 2,
    Failed = 3
}

public enum BusinessDayClosingStatus
{
    Closing = 1,
    Closed = 2,
    Failed = 3
}

/// <summary>
/// Trạng thái phê duyệt của phiếu thu/chi ca POS.
/// </summary>
public enum CashMovementStatus
{
    Recorded = 1,        // Đã ghi nhận hiệu lực ngay (phiếu thu hoặc phiếu chi <= hạn mức, hoặc sau khi chủ duyệt)
    PendingApproval = 2, // Đang chờ chủ duyệt (phiếu chi > hạn mức)
    Rejected = 3,        // Chủ từ chối duyệt
    Cancelled = 4        // Nhân viên tự hủy khi đang pending
}
