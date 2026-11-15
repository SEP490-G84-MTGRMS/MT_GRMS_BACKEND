using MT_GRMS.Domain.Constants;

namespace MT_GRMS.Application.HR.Employees.Dtos;

public record EmployeeAllowanceDto(string Name, decimal Amount, Guid? Id = null);

public record CreateEmployeeRequest(
    string FullName,
    string CitizenId,
    string Phone,
    Guid BranchId,
    string? Email = null,
    string? Address = null,
    DateTime? StartDate = null,
    string? Note = null,
    DateTime? DateOfBirth = null,
    string? Gender = null,
    string SalaryType = "monthly",
    decimal BaseRate = 0.00m,
    string? OtWeekday = null,
    string? OtSunday = null,
    List<EmployeeAllowanceDto>? Allowances = null,
    string? BankAccountNumber = null,
    string? BankName = null,
    string? BankAccountHolder = null,
    string? AvatarUrl = null,
    string? FaceDescriptor = null,
    string? FaceImageUrls = null,
    string? UserId = null,
    string? UserName = null,
    string? Password = null,
    string? Role = SystemRoles.Staff,
    string? EmployeeCode = null,
    bool HasLaborContract = false,
    decimal? InsuranceBaseSalary = null
);

public record UpdateEmployeeRequest(
    string FullName,
    string CitizenId,
    string Phone,
    Guid BranchId,
    string? Email = null,
    string? Address = null,
    DateTime? StartDate = null,
    string? Note = null,
    DateTime? DateOfBirth = null,
    string? Gender = null,
    string SalaryType = "monthly",
    decimal BaseRate = 0.00m,
    string? OtWeekday = null,
    string? OtSunday = null,
    List<EmployeeAllowanceDto>? Allowances = null,
    string? BankAccountNumber = null,
    string? BankName = null,
    string? BankAccountHolder = null,
    string? AvatarUrl = null,
    string? FaceDescriptor = null,
    string? FaceImageUrls = null,
    string? UserId = null,
    string? UserName = null,
    string? Password = null,
    string? Role = SystemRoles.Staff,
    string? EmployeeCode = null,
    bool HasLaborContract = false,
    decimal? InsuranceBaseSalary = null
);

public record UpdateEmployeeStatusRequest(
    string Status
);

public record CreateIdentityAccountRequest(
    string Username,
    string Password,
    string Role = SystemRoles.Staff,
    string? DisplayName = null
);

public record EmployeeQueryFilter(
    string? Keyword = null,
    Guid? BranchId = null,
    List<Guid>? BranchIds = null,
    string? Status = null,
    string? SalaryType = null,
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
    string? Address,
    DateTime? StartDate,
    string? Note,
    string? AvatarUrl,
    Guid BranchId,
    string BranchName,
    string? UserId,
    string? UserName,
    string? Role,
    string SalaryType,
    decimal BaseRate,
    string? OtWeekday,
    string? OtSunday,
    List<EmployeeAllowanceDto> Allowances,
    decimal DebtAmount,
    string Status,
    string? BankAccountNumber,
    string? BankName,
    string? BankAccountHolder,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    string? FaceDescriptor = null,
    string? FaceImageUrls = null,
    DateTime? FaceRegisteredAt = null,
    bool HasLaborContract = false,
    decimal? InsuranceBaseSalary = null
);

public record EmployeeSummaryDto(
    Guid Id,
    string EmployeeCode,
    string FullName,
    string Phone,
    string? Email,
    string? AvatarUrl,
    string? Address,
    DateTime? StartDate,
    string? Note,
    Guid BranchId,
    string BranchName,
    string Status,
    string SalaryType,
    decimal BaseRate,
    string? OtWeekday,
    string? OtSunday,
    decimal DebtAmount,
    string? UserId,
    string? UserName,
    DateTime CreatedAt,
    bool HasFaceId = false
);
