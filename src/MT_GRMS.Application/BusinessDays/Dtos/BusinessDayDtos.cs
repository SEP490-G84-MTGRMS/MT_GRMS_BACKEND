namespace MT_GRMS.Application.BusinessDays.Dtos;

public record BusinessDayFundCount(Guid FundId, decimal ActualCashCounted);

public record CloseBusinessDayRequest(
    Guid BranchId,
    DateOnly BusinessDate,
    IReadOnlyList<BusinessDayFundCount> FundCounts,
    string PreviewToken,
    string? Note = null);

public record BusinessDayClosingResponse(
    Guid Id,
    Guid BranchId,
    DateOnly BusinessDate,
    string Status,
    int TotalShifts,
    int TotalOrders,
    decimal TotalCashSales,
    decimal TotalBankSales,
    decimal OwnerShiftlessCashSales,
    decimal OwnerShiftlessBankSales,
    decimal TotalCashIn,
    decimal TotalCashOut,
    decimal TotalRetainedFloat,
    decimal TotalDroppedToSafe,
    decimal ExpectedCash,
    decimal ActualCashCounted,
    decimal CashDiscrepancy,
    string ReferenceCode,
    DateTime ClosedAtUtc);

public record BusinessDateResponse(
    Guid BranchId,
    DateOnly BusinessDate,
    TimeSpan BusinessDayStartTime);

public record ReopenBusinessDayRequest(string Reason);

public record BusinessDayClosingVersionResponse(
    Guid Id,
    Guid BranchId,
    DateOnly BusinessDate,
    int Version,
    string Status,
    bool IsActive,
    Guid? SupersedesClosingId,
    string ReferenceCode,
    decimal TotalCashSales,
    decimal TotalBankSales,
    decimal TotalCashIn,
    decimal TotalCashOut,
    decimal TotalDiscrepancy,
    DateTime? ClosedAtUtc,
    DateTime? ReopenedAtUtc,
    Guid? ReopenedByUserId,
    string? ReopenReason);

public record BusinessDayFundPreview(
    Guid FundId,
    string FundName,
    string FundType,
    decimal ExpectedCash,
    bool RequiresPhysicalCount,
    bool IsInTransit = false);

public record BusinessDayBlocker(
    string Code,
    string EntityType,
    string EntityId,
    string DisplayName,
    decimal? Amount,
    string Status,
    string? ActionPath = null);

public record BusinessDayClosingPreview(
    Guid BranchId,
    DateOnly BusinessDate,
    DateTime PeriodStartUtc,
    DateTime PeriodEndUtc,
    decimal TotalCashSales,
    decimal TotalBankSales,
    decimal TotalCashIn,
    decimal TotalCashOut,
    decimal TotalRetainedFloat,
    decimal TotalDroppedToSafe,
    IReadOnlyList<BusinessDayFundPreview> Funds,
    IReadOnlyList<BusinessDayBlocker> Blockers,
    string PreviewToken);
