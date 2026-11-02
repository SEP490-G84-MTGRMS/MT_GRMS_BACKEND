namespace MT_GRMS.Application.HR.Employees.Dtos;

public record EmployeeAllowanceDto(string Name, decimal Amount);

public record CreateEmployeeRequest(
    string FullName,
    string CitizenId,
    string Phone,
    Guid BranchId,
    string? Email = null,
    string? JobTitle = null,
    DateTime? DateOfBirth = null,
    string? Gender = null,
    string SalaryType = "monthly",
    decimal BaseRate = 0.00m,
    List<EmployeeAllowanceDto>? Allowances = null,
    string? BankAccountNumber = null,
    string? BankName = null,
    string? BankAccountHolder = null,
    string? AvatarUrl = null
);

public record UpdateEmployeeRequest(
    string FullName,
    string CitizenId,
    string Phone,
    Guid BranchId,
    string? Email = null,
    string? JobTitle = null,
    DateTime? DateOfBirth = null,
    string? Gender = null,
    string SalaryType = "monthly",
    decimal BaseRate = 0.00m,
    List<EmployeeAllowanceDto>? Allowances = null,
    string? BankAccountNumber = null,
    string? BankName = null,
    string? BankAccountHolder = null,
    string? AvatarUrl = null
);

public record UpdateEmployeeStatusRequest(
    string Status
);

public record CreateIdentityAccountRequest(
    string Username,
    string Password,
    string Role = "STAFF"
);

public record EmployeeQueryFilter(
    string? Keyword = null,
    Guid? BranchId = null,
    string? Status = null,
    int Page = 1,
    int PageSize = 20
);

public record EmployeeResponse(
    Guid Id,
    string EmployeeCode,
    string FullName,
    string CitizenId,
    DateTime? DateOfBirth,
    string? Gender,
    string Phone,
    string? Email,
    string? AvatarUrl,
    string? JobTitle,
    Guid BranchId,
    string BranchName,
    string? UserId,
    string? UserName,
    string? Role,
    string SalaryType,
    decimal BaseRate,
    List<EmployeeAllowanceDto> Allowances,
    decimal DebtAmount,
    string Status,
    string? BankAccountNumber,
    string? BankName,
    string? BankAccountHolder,
    DateTime CreatedAt,
    DateTime? UpdatedAt
);

public record EmployeeSummaryDto(
    Guid Id,
    string EmployeeCode,
    string FullName,
    string Phone,
    string? Email,
    Guid BranchId,
    string BranchName,
    string? JobTitle,
    string Status,
    string SalaryType,
    decimal BaseRate,
    string? UserId,
    string? UserName,
    DateTime CreatedAt
);

