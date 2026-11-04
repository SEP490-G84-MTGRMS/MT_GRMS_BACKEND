using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.HR.Employees;
using MT_GRMS.Application.HR.Employees.Dtos;
using MT_GRMS.Domain.Entities.HR;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Infrastructure.Persistence;

namespace MT_GRMS.Infrastructure.Repositories;

public class EmployeeRepository : IEmployeeRepository
{
    private readonly AppDbContext _context;
    private readonly DbSet<Employee> _dbSet;

    public EmployeeRepository(AppDbContext context)
    {
        _context = context;
        _dbSet = context.Set<Employee>();
    }

    public async Task<Employee?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Include(e => e.Branch)
            .Include(e => e.Allowances)
            .Include(e => e.User)
                .ThenInclude(u => u!.TenantUsers)
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
    }

    public async Task<Employee?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        var normalizedCode = code.Trim().ToUpper();
        return await _dbSet
            .FirstOrDefaultAsync(e => e.EmployeeCode.ToUpper() == normalizedCode, cancellationToken);
    }

    public async Task<Employee?> GetByPhoneAsync(string phone, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .FirstOrDefaultAsync(e => e.Phone == phone, cancellationToken);
    }

    public async Task<Employee?> GetByCitizenIdAsync(string citizenId, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .FirstOrDefaultAsync(e => e.CitizenId == citizenId, cancellationToken);
    }

    public async Task<Employee?> GetByUserIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .FirstOrDefaultAsync(e => e.UserId == userId, cancellationToken);
    }

    public async Task<(IReadOnlyList<Employee> Items, int TotalCount)> GetPagedListAsync(
        EmployeeQueryFilter filter,
        CancellationToken cancellationToken = default)
    {
        var query = _dbSet
            .AsNoTracking()
            .Include(e => e.Branch)
            .Include(e => e.Allowances)
            .Include(e => e.User)
            .Where(e => !e.IsDeleted);

        if (!string.IsNullOrWhiteSpace(filter.Keyword))
        {
            var kw = filter.Keyword.Trim().ToLower();
            query = query.Where(e =>
                e.FullName.ToLower().Contains(kw) ||
                e.EmployeeCode.ToLower().Contains(kw) ||
                e.Phone.Contains(kw) ||
                e.CitizenId.Contains(kw) ||
                (e.Email != null && e.Email.ToLower().Contains(kw)));
        }

        if (filter.BranchId.HasValue && filter.BranchId.Value != Guid.Empty)
        {
            query = query.Where(e => e.BranchId == filter.BranchId.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            var st = filter.Status.Trim().ToLower();
            if (Enum.TryParse<EmployeeStatus>(st, true, out var parsedStatus))
            {
                query = query.Where(e => e.Status == parsedStatus);
            }
        }

        if (!string.IsNullOrWhiteSpace(filter.SalaryType))
        {
            var st = filter.SalaryType.Trim().ToLower();
            if (Enum.TryParse<SalaryType>(st, true, out var parsedSalaryType))
            {
                query = query.Where(e => e.SalaryType == parsedSalaryType);
            }
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var page = filter.Page > 0 ? filter.Page : 1;
        var pageSize = filter.PageSize > 0 ? filter.PageSize : 20;

        var items = await query
            .OrderByDescending(e => e.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<string?> GetMaxSequentialCodeAsync(string prefix, int totalLength, CancellationToken cancellationToken = default)
    {
        var normalizedPrefix = prefix.Trim().ToUpper();
        return await _dbSet
            .IgnoreQueryFilters()
            .Where(e => e.EmployeeCode.StartsWith(normalizedPrefix) && e.EmployeeCode.Length == totalLength)
            .OrderByDescending(e => e.EmployeeCode)
            .Select(e => e.EmployeeCode)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> ExistsAsync(Expression<Func<Employee, bool>> predicate, CancellationToken cancellationToken = default)
    {
        return await _dbSet.AnyAsync(predicate, cancellationToken);
    }

    public async Task AddAsync(Employee employee, CancellationToken cancellationToken = default)
    {
        await _dbSet.AddAsync(employee, cancellationToken);
    }

    public void Update(Employee employee)
    {
        _dbSet.Update(employee);
    }

    public void Delete(Employee employee)
    {
        _dbSet.Remove(employee);
    }
}
