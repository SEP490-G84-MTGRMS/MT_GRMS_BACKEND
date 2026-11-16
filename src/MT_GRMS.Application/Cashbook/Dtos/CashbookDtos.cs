using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Application.Cashbook.Dtos;

public record OpenShiftRequest(
    Guid TillId,
    decimal CountedFloat,
    string? Notes
);

public record CloseShiftRequest(
    Guid ShiftId,
    decimal ActualCashCounted,
    decimal RetainedFloatAmount,
    string? Notes
);

public record CashDropRequest(
    Guid ShiftId,
    decimal Amount,
    Guid ManagerUserId,
    string? Notes
);

public record DepositToBankRequest(
    Guid SourceAccountId,
    Guid TargetBankAccountId,
    decimal Amount,
    Guid CarrierUserId,
    string? Note
);

public record ConfirmBankDepositRequest(
    Guid TransitId,
    decimal ActualReceivedAmount,
    string? Note
);

public record ShiftResponse(
    Guid Id,
    Guid TillId,
    Guid CashierUserId,
    decimal OpeningFloat,
    decimal TotalCashIn,
    decimal TotalCashOut,
    decimal ExpectedCash,
    decimal? ActualCashCounted,
    decimal? Discrepancy,
    decimal RetainedFloatAmount,
    decimal DroppedToSafeAmount,
    string Status,
    DateTime OpenedAtUtc,
    DateTime? ClosedAtUtc
);

public record CashInTransitResponse(
    Guid Id,
    string TransitCode,
    Guid SourceAccountId,
    Guid TargetBankAccountId,
    decimal AmountSent,
    decimal? AmountReceived,
    decimal DiscrepancyAmount,
    Guid CarrierUserId,
    string Status,
    DateTime CreatedAtUtc
);

public record CashbookEntryResponse(
    Guid Id,
    string EntryCode,
    Guid AccountId,
    string AccountType,
    Guid? BranchId,
    string EntryType,
    decimal Amount,
    decimal BalanceAfter,
    string SourceType,
    Guid? SourceId,
    string ReferenceCode,
    string AuditHash,
    string Description,
    DateTime OccurredAtUtc,
    string CategoryCode = "REVIEW_REQUIRED",
    FinancialNature FinancialNature = FinancialNature.Suspense,
    CashbookTaxTreatment TaxTreatment = CashbookTaxTreatment.ReviewRequired,
    string? TaxTreatmentReason = null
);
public record CashbookCreateRequest(
    Guid BranchId,
    CashbookEntryType Type,
    string Category,
    decimal Amount,
    string ReferenceCode,
    string Description
);
public record CashAccountResponse(
    Guid Id,
    Guid? BranchId,
    string BranchName,
    string AccountName,
    string AccountType,
    string? BankName,
    string? AccountNumber,
    decimal CurrentBalance,
    decimal InitialBalance,
    bool IsActive,
    string Currency,
    bool IsSafeDeposit
);

public record CashbookSummaryResponse(
    decimal TotalOpeningBalance,
    decimal TotalReceipts,
    decimal TotalPayments,
    decimal TotalClosingBalance,
    decimal TotalCashInDrawers,
    decimal TotalBankDeposits,
    decimal TotalSafeDepositBalance
);

public record CreateReceiptRequest(
    Guid AccountId,
    decimal Amount,
    string Category,
    string? Description,
    string? ReferenceCode,
    string CategoryCode = "OTHER_INCOME",
    CashbookTaxTreatment? TaxTreatment = null,
    string? TaxTreatmentReason = null
);

public record CreatePaymentRequest(
    Guid AccountId,
    decimal Amount,
    string Category,
    string? Description,
    string? ReferenceCode,
    string CategoryCode = "OTHER_PAYMENT",
    CashbookTaxTreatment? TaxTreatment = null,
    string? TaxTreatmentReason = null
);

/// <summary>
/// Records an MT-GRMS cashbook payment as tax-payment evidence. It is not an eTax/treasury settlement assertion.
/// </summary>
public record RecordTaxPaymentRequest(
    Guid SourceAccountId,
    decimal Amount,
    string BeneficiaryName,
    string PaymentReference,
    string? EvidenceAttachmentUrl,
    Guid? HouseholdTaxSnapshotId,
    string? Note
);

public record TaxPaymentResponse(
    Guid VoucherId,
    Guid? HouseholdTaxSnapshotId,
    CashbookEntryResponse Entry
);

public record TransferFundRequest(
    Guid FromAccountId,
    Guid ToAccountId,
    decimal Amount,
    string? Note
);

public record ReconcileAccountRequest(
    Guid AccountId,
    decimal ActualCountedBalance,
    string Reason
);

public record VoidEntryRequest(
    string Reason
);

public record TransferFundResponse(
    CashbookEntryResponse Payment,
    CashbookEntryResponse Receipt
);
