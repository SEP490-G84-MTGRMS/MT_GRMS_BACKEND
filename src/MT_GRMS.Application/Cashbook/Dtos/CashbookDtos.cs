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
    decimal Discrepancy,
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
    DateTime OccurredAtUtc
);
public record CashbookCreateRequest(
    Guid BranchId,
    CashbookEntryType Type,
    string Category,
    decimal Amount,
    string ReferenceCode,
    string Description
);