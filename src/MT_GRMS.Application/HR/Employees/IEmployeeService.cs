using MT_GRMS.Application.Common;
using MT_GRMS.Application.HR.Employees.Dtos;

namespace MT_GRMS.Application.HR.Employees;

public interface IEmployeeService
{
    Task<PagedResult<EmployeeSummaryDto>> GetEmployeesPagedAsync(EmployeeQueryFilter filter, CancellationToken cancellationToken = default);
    Task<EmployeeResponse> GetEmployeeByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<EmployeeResponse> CreateEmployeeAsync(CreateEmployeeRequest request, CancellationToken cancellationToken = default);
    Task<EmployeeResponse> UpdateEmployeeAsync(Guid id, UpdateEmployeeRequest request, CancellationToken cancellationToken = default);
    Task<bool> UpdateStatusAsync(Guid id, string status, CancellationToken cancellationToken = default);
    Task<bool> DeleteEmployeeAsync(Guid id, CancellationToken cancellationToken = default);
    Task<string> CreateIdentityAccountAsync(Guid employeeId, CreateIdentityAccountRequest request, CancellationToken cancellationToken = default);
}

