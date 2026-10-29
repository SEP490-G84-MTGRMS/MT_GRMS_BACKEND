using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Application.Expenses.Dtos;

public record CreateExpenseRequest(
    Guid? BranchId,
    string BeneficiaryName,
    string Category,
    decimal Amount,
    FinanceAccountType PaymentSourceType,
    Guid? PaymentSourceAccountId,
    bool IsTaxDeductible,
    string? InvoiceNumber,
    string? SellerTaxCode,
    string? EvidenceAttachmentUrl,
    string? Note
);

public record ApproveExpenseRequest(
    Guid ExpenseId,
    string? Note
);

public record DisburseExpenseRequest(
    Guid ExpenseId,
    FinanceAccountType PaymentSourceType,
    Guid PaymentSourceAccountId,
    string? Note
);

public record ExpenseVoucherResponse(
    Guid Id,
    string VoucherCode,
    Guid? BranchId,
    string BeneficiaryName,
    string Category,
    decimal Amount,
    string Status,
    string PaymentSourceType,
    Guid? PaymentSourceAccountId,
    bool IsTaxDeductible,
    string? InvoiceNumber,
    string? SellerTaxCode,
    string? EvidenceAttachmentUrl,
    bool IsFlaggedAsSuspiciousSplit,
    string? SuspiciousReason,
    Guid? ApprovedByUserId,
    DateTime? ApprovedAtUtc,
    DateTime? PaidAtUtc,
    DateTime ExpenseDate,
    string? Note
);