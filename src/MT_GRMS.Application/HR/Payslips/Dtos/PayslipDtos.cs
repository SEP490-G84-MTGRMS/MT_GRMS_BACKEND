using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Application.HR.Payslips.Dtos;

public record GeneratePayrollBatchRequest(
    string PeriodMonth,
    Guid? BranchId = null,
    SalaryType? SalaryTypeFilter = null,
    string? Note = null
);

public record PayPayslipRequest(
    string PaymentMethod,
    string? Note = null,
    Guid? CashbookAccountId = null
);

public record BatchPayPayslipsRequest(
    List<Guid> PayslipIds,
    string PaymentMethod,
    string? Note = null
);

public record PayslipQueryFilter(
    string? Keyword = null,
    string? PeriodMonth = null,
    Guid? BranchId = null,
    PayslipStatus? Status = null,
    int Page = 1,
    int PageSize = 20
);

public record BatchPayResultDto(
    int TotalRequested,
    int SuccessCount,
    int FailedCount,
    IReadOnlyList<string> PaidPayslipCodes,
    IReadOnlyList<string> Errors
);

public record PayslipSummaryDto(
    Guid Id,
    string PayslipCode,
    string PeriodMonth,
    Guid BranchId,
    string BranchName,
    Guid EmployeeId,
    string EmployeeCode,
    string EmployeeName,
    SalaryType SalaryType,
    decimal WorkedDays,
    decimal TotalWorkHours,
    decimal TotalEarnings,
    decimal TotalDeductions,
    decimal NetSalary,
    PayslipStatus Status,
    DateTime? PaidAt,
    string? PaymentMethod,
    string? CashbookVoucherCode
);

public record PayslipResponse(
    Guid Id,
    string PayslipCode,
    string PeriodMonth,
    Guid BranchId,
    string BranchName,
    Guid EmployeeId,
    string EmployeeCode,
    string EmployeeName,
    SalaryType SalaryType,
    decimal BaseRate,
    decimal StandardDays,
    decimal StandardDayHours,
    decimal WorkedDays,
    decimal TotalWorkHours,
    int TotalShifts,
    decimal OtHours,
    decimal PaidLeaveDays,
    decimal UnpaidLeaveDays,
    int TotalLateMinutes,
    int TotalEarlyLeaveMinutes,
    decimal BaseSalaryEarned,
    decimal OvertimePay,
    decimal TotalAllowance,
    string? AllowancesDetailJson,
    decimal TotalEarnings,
    decimal AdvanceDeduction,
    decimal RemainingDebt,
    decimal SocialInsurance,
    decimal BhxhAmount,
    decimal BhytAmount,
    decimal BhtnAmount,
    decimal TotalDeductions,
    decimal NetSalary,
    PayslipStatus Status,
    DateTime? PaidAt,
    string? PaymentMethod,
    string? PaidBy,
    string? BankAccountNumber,
    string? BankName,
    string? BankAccountHolder,
    Guid? CashbookTransactionId,
    string? CashbookVoucherCode,
    string? Note,
    string? ApprovedBy,
    DateTime? ApprovedAt,
    DateTime CreatedAt,
    string? UserId = null
);

