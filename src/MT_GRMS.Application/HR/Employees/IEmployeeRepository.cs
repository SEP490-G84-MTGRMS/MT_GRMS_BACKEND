using System.Linq.Expressions;
using MT_GRMS.Application.HR.Employees.Dtos;
using MT_GRMS.Domain.Entities.HR;

namespace MT_GRMS.Application.HR.Employees;

public interface IEmployeeRepository
{
    Task<Employee?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Employee?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);
    Task<Employee?> GetByCitizenIdAsync(string citizenId, CancellationToken cancellationToken = default);
    Task<Employee?> GetByPhoneAsync(string phone, CancellationToken cancellationToken = default);
    Task<Employee?> GetByUserIdAsync(string userId, CancellationToken cancellationToken = default);
    Task<(IReadOnlyList<Employee> Items, int TotalCount)> GetPagedListAsync(EmployeeQueryFilter filter, CancellationToken cancellationToken = default);
    Task<string?> GetMaxSequentialCodeAsync(string prefix, int totalLength, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(Expression<Func<Employee, bool>> predicate, CancellationToken cancellationToken = default);
    Task AddAsync(Employee employee, CancellationToken cancellationToken = default);
    void Update(Employee employee);
    void Delete(Employee employee);
    void RemoveAllowances(IEnumerable<EmployeeAllowance> allowances);
    void SyncAllowances(Employee employee, IEnumerable<EmployeeAllowanceDto> newAllowances, string tenantId);
    Task<bool> HasWorkOrPayrollHistoryAsync(Guid employeeId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Employee>> GetActiveEmployeesAsync(Guid? branchId, CancellationToken cancellationToken = default);
}
