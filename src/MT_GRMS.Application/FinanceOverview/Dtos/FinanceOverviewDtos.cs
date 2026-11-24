namespace MT_GRMS.Application.FinanceOverview.Dtos;

public record FinanceOverviewPeriodDto(
    DateTime FromUtc,
    DateTime ToUtc,
    string TimeZone
);

public record FinanceOverviewScopeDto(
    Guid? BranchId,
    string BranchName,
    int BranchCount
);

public record FinanceAttentionDto(
    decimal ReceivableAmount,
    int DebtorCount,
    decimal PendingPayableAmount,
    int PendingPayableCount,
    decimal CashInTransit,
    int PendingCashMovementCount,
    bool IsCurrentSnapshot
);

public record BranchCashHealthDto(
    Guid BranchId,
    string BranchCode,
    string BranchName,
    decimal OpeningBalance,
    decimal CashIn,
    decimal CashOut,
    decimal NetCashFlow,
    decimal InternalTransferNet,
    decimal ClosingBalance,
    decimal CashAtRegisters,
    decimal SafeDeposit,
    decimal CashInTransit,
    decimal ReceivableAmount,
    decimal PendingPayableAmount,
    int PendingPayableCount,
    string Status,
    decimal BankBalance = 0
);

public record FinanceOverviewTotalsDto(
    decimal OpeningBalance,
    decimal CashIn,
    decimal CashOut,
    decimal NetCashFlow,
    decimal InternalTransferNet,
    decimal ClosingBalance,
    decimal AvailableCash,
    decimal CashAtRegisters,
    decimal SafeDeposit,
    decimal BankBalance,
    decimal UnallocatedBankBalance,
    decimal CashInTransit
);

public record FinanceOverviewResponse(
    FinanceOverviewScopeDto Scope,
    FinanceOverviewPeriodDto Period,
    DateTime PositionAsOfUtc,
    DateTime GeneratedAtUtc,
    FinanceOverviewTotalsDto Totals,
    FinanceAttentionDto Attention,
    IReadOnlyList<BranchCashHealthDto> Branches
);
