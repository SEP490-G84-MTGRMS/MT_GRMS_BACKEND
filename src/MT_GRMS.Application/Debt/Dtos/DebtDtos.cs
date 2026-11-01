using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Application.Debt.Dtos;

public record RecordSaleDebtRequest(
    Guid CustomerId,
    Guid OrderId,
    string OrderCode,
    Guid BranchId,
    decimal DebtAmount,
    string? ManagerBypassCode,
    string? Notes
);

public record RepayDebtRequest(
    Guid CustomerId,
    decimal Amount,
    FinanceAccountType PaymentSourceType,
    Guid PaymentSourceAccountId,
    string? ReferenceCode,
    string? Notes
);

public record OffsetGoodsReturnDebtRequest(
    Guid CustomerId,
    Guid OriginalOrderId,
    Guid ReturnOrderId,
    decimal ReturnAmount,
    bool RefundExcessInCash,
    Guid? RefundAccountId,
    string? Notes
);

public record AdjustDebtRequest(
    Guid CustomerId,
    decimal AdjustmentAmount,
    bool IsIncrease,
    string Reason,
    string ManagerApprovalCode
);

public record WriteOffBadDebtRequest(
    Guid CustomerId,
    decimal WriteOffAmount,
    string Reason,
    string EvidenceDocumentUrl
);

public record CustomerDebtInvoiceResponse(
    Guid Id,
    Guid CustomerId,
    Guid OrderId,
    string OrderCode,
    decimal TotalDebtAmount,
    decimal RemainingDebtAmount,
    DateTime DueDate,
    string Status,
    bool IsOverdue
);

public record CustomerDebtProfileResponse(
    Guid Id,
    Guid CustomerId,
    decimal CreditLimit,
    int MaxDebtDays,
    decimal CurrentDebtBalance,
    decimal PrepaymentBalance,
    string DebtStatus,
    DateTime? LastDebtIncurredAtUtc,
    DateTime? LastRepaymentAtUtc,
    List<CustomerDebtInvoiceResponse> OpenInvoices
);

public record CustomerDebtEntryResponse(
    Guid Id,
    string EntryCode,
    Guid CustomerId,
    string EntryType,
    decimal Amount,
    decimal BalanceAfter,
    decimal PrepaymentAfter,
    string ReferenceCode,
    string Description,
    DateTime OccurredAtUtc
);

public record AllocationDetailResponse(
    Guid InvoiceId,
    string OrderCode,
    decimal AllocatedAmount,
    decimal NewRemainingDebt
);

public record RepaymentResultResponse(
    Guid DebtEntryId,
    string EntryCode,
    decimal RepaidAmount,
    decimal RemainingDebt,
    decimal ExcessPrepayment,
    List<AllocationDetailResponse> Allocations
);
