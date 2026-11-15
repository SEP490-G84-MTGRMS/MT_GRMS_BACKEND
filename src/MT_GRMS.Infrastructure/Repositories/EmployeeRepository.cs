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

        if (filter.BranchIds != null && filter.BranchIds.Count > 0)
        {
            var validBranchIds = filter.BranchIds.Where(id => id != Guid.Empty).ToList();
            if (validBranchIds.Count > 0)
            {
                query = query.Where(e => validBranchIds.Contains(e.BranchId));
            }
        }
        else if (filter.BranchId.HasValue && filter.BranchId.Value != Guid.Empty)
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
            .OrderBy(e => e.Status == EmployeeStatus.Active ? 0 : 1)
            .ThenByDescending(e => e.CreatedAt)
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

    public async Task<bool> ExistsIgnoringFiltersAsync(Expression<Func<Employee, bool>> predicate, CancellationToken cancellationToken = default)
    {
        return await _dbSet.IgnoreQueryFilters().AnyAsync(predicate, cancellationToken);
    }

    public async Task<Employee?> GetDeletedDuplicateAsync(string tenantId, string citizenId, string phone, string? employeeCode = null, Guid? excludeId = null, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .IgnoreQueryFilters()
            .Where(e => e.TenantId == tenantId && e.IsDeleted && (!excludeId.HasValue || e.Id != excludeId.Value))
            .FirstOrDefaultAsync(e =>
                e.CitizenId == citizenId
                || e.Phone == phone
                || (!string.IsNullOrEmpty(employeeCode) && e.EmployeeCode == employeeCode),
                cancellationToken);
    }

    public async Task AddAsync(Employee employee, CancellationToken cancellationToken = default)
    {
        await _dbSet.AddAsync(employee, cancellationToken);
    }

    public void Update(Employee employee)
    {
        var entry = _context.Entry(employee);
        if (entry.State == EntityState.Detached)
        {
            _dbSet.Update(employee);
        }
    }

    public void RemoveAllowances(IEnumerable<EmployeeAllowance> allowances)
    {
        _context.EmployeeAllowances.RemoveRange(allowances);
    }

    /// <summary>
    /// Cách 1: So sánh và Đồng bộ hóa (Sync & Diffing) cho EmployeeAllowance
    /// - Thêm mới (Insert): Các phụ cấp chưa có Id hoặc Id = Guid.Empty -> _context.EmployeeAllowances.Add + State = Added
    /// - Cập nhật (Update): Các phụ cấp có Id tồn tại trong cả NewList và OldList -> Update properties + State = Modified
    /// - Xóa (Delete): Các phụ cấp có trong OldList nhưng không xuất hiện trong NewList -> _context.EmployeeAllowances.RemoveRange (không gọi employee.Allowances.Remove để tránh severed relationship)
    /// </summary>
    public void SyncAllowances(Employee employee, IEnumerable<EmployeeAllowanceDto> newAllowances, string tenantId)
    {
        var oldAllowances = employee.Allowances.ToList();
        var incoming = (newAllowances ?? Enumerable.Empty<EmployeeAllowanceDto>())
            .Where(a => !string.IsNullOrWhiteSpace(a.Name))
            .ToList();

        // 1. Xóa (Delete): Các phụ cấp có trong OldList nhưng không xuất hiện trong NewList
        var incomingIds = incoming
            .Where(a => a.Id.HasValue && a.Id.Value != Guid.Empty)
            .Select(a => a.Id!.Value)
            .ToHashSet();

        var toDelete = oldAllowances
            .Where(old => !incomingIds.Contains(old.Id))
            .ToList();

        if (toDelete.Count > 0)
        {
            _context.EmployeeAllowances.RemoveRange(toDelete);
            foreach (var del in toDelete)
            {
                employee.Allowances.Remove(del);
            }
        }

        // 2. Cập nhật (Update) & 3. Thêm mới (Insert)
        foreach (var dto in incoming)
        {
            if (dto.Id.HasValue && dto.Id.Value != Guid.Empty)
            {
                var existing = oldAllowances.FirstOrDefault(a => a.Id == dto.Id.Value);
                if (existing != null)
                {
                    // Cập nhật phụ cấp cũ
                    existing.Name = dto.Name.Trim();
                    existing.Amount = Math.Max(0, dto.Amount);
                    _context.Entry(existing).State = EntityState.Modified;
                }
                else
                {
                    // Nếu Id gửi lên không tìm thấy trong OldList, INSERT mới
                    var newAllowance = new EmployeeAllowance
                    {
                        EmployeeId = employee.Id,
                        TenantId = tenantId,
                        Name = dto.Name.Trim(),
                        Amount = Math.Max(0, dto.Amount)
                    };
                    _context.EmployeeAllowances.Add(newAllowance);
                    _context.Entry(newAllowance).State = EntityState.Added;
                    if (!employee.Allowances.Contains(newAllowance))
                    {
                        employee.Allowances.Add(newAllowance);
                    }
                }
            }
            else
            {
                // Thêm mới (Insert): Các phụ cấp chưa có Id hoặc Id = 0 / Guid.Empty
                var newAllowance = new EmployeeAllowance
                {
                    EmployeeId = employee.Id,
                    TenantId = tenantId,
                    Name = dto.Name.Trim(),
                    Amount = Math.Max(0, dto.Amount)
                };
                _context.EmployeeAllowances.Add(newAllowance);
                _context.Entry(newAllowance).State = EntityState.Added;
            }
        }
    }

    public void Delete(Employee employee)
    {
        _dbSet.Remove(employee);
    }

    /// <summary>
    /// Kiểm tra nhân viên đã phát sinh lịch làm việc (EmployeeSchedule) hoặc phiếu lương (Payslip) chưa.
    /// Theo Business Rule: Chỉ được phép xóa nhân viên khi chưa lưu trữ bất kỳ ca làm việc hoặc phiếu lương nào.
    /// </summary>
    public async Task<bool> HasWorkOrPayrollHistoryAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        var hasSchedules = await _context.EmployeeSchedules
            .AnyAsync(s => s.EmployeeId == employeeId, cancellationToken);
        if (hasSchedules) return true;

        var hasPayslips = await _context.Payslips
            .AnyAsync(p => p.EmployeeId == employeeId, cancellationToken);
        return hasPayslips;
    }

    public async Task<IReadOnlyList<Employee>> GetActiveEmployeesAsync(Guid? branchId, CancellationToken cancellationToken = default)
    {
        var query = _dbSet
            .Include(e => e.Allowances)
            .Include(e => e.Branch)
            .Where(e => e.Status == EmployeeStatus.Active);

        if (branchId.HasValue && branchId.Value != Guid.Empty)
        {
            query = query.Where(e => e.BranchId == branchId.Value);
        }

        return await query.ToListAsync(cancellationToken);
    }
}
