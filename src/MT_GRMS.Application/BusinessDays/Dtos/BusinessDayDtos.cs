namespace MT_GRMS.Application.BusinessDays.Dtos;

public record CloseBusinessDayRequest(
    Guid BranchId,
    DateOnly BusinessDate,
    decimal ActualCashCounted,
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
