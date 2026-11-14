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
    string? Note,
    FinancialNature Nature = FinancialNature.OperatingExpense,
    string? ReferenceCode = null
);

public record ApproveExpenseRequest(
    Guid ExpenseId,
    string? Note
);

public record RejectExpenseRequest(
    Guid ExpenseId,
    string? Reason
);

public record CancelExpenseRequest(
    Guid ExpenseId,
    string Reason
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
    string? Note,
    FinancialNature Nature = FinancialNature.OperatingExpense,
    string? ReferenceCode = null,
    decimal RefundedAmount = 0,
    bool IsSettled = false
);

public record ExpenseSummaryResponse(
    decimal TotalAmount,
    int TotalCount,
    decimal PaidAmount,
    int PaidCount,
    decimal PendingApprovalAmount,
    int PendingApprovalCount,
    decimal SuspiciousAmount,
    int SuspiciousCount,
    decimal TaxDeductibleAmount,
    int TaxDeductibleCount,
    decimal NonTaxDeductibleAmount,
    int NonTaxDeductibleCount
);

public record ResolveTriageRequest(
    FinancialNature Nature,
    bool IsTaxDeductible,
    string? Category,
    string ResolutionNote
);
