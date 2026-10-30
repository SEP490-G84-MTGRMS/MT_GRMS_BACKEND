namespace MT_GRMS.Domain.Enums;

public enum CustomerDebtType
{
    DebtIncurred = 1,      // Bán chịu đơn hàng POS (+ Nợ)
    DebtRepayment = 2,     // Khách thanh toán nợ (- Nợ)
    GoodsReturnOffset = 3, // Cấn trừ nợ khi trả lại hàng mua chịu (- Nợ)
    DebtAdjustment = 4,    // Điều chỉnh nợ kế toán (+/- Nợ)
    BadDebtWriteOff = 5    // Xóa sổ nợ xấu (- Nợ)
}

public enum DebtInvoiceStatus
{
    Unpaid = 1,
    PartiallyPaid = 2,
    FullyPaid = 3,
    Cancelled = 4
}

public enum CustomerDebtStatus
{
    GoodStanding = 1,
    Warning = 2,
    OverdueBlocked = 3,
    LimitExceededBlocked = 4
}
