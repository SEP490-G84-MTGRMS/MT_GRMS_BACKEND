using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.HR.Schedules;
using MT_GRMS.Application.HR.Schedules.Dtos;
using MT_GRMS.Domain.Entities.HR;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Infrastructure.Persistence;

namespace MT_GRMS.Infrastructure.Repositories;

public class ScheduleRepository : IScheduleRepository
{
    private readonly AppDbContext _context;
    private readonly DbSet<EmployeeSchedule> _dbSet;

    public ScheduleRepository(AppDbContext context)
    {
        _context = context;
        _dbSet = context.Set<EmployeeSchedule>();
    }

    public async Task<EmployeeSchedule?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Include(s => s.Employee)
            .Include(s => s.Branch)
            .Include(s => s.ShiftConfig)
            .FirstOrDefaultAsync(s => s.Id == id && !s.IsDeleted, cancellationToken);
    }

    public async Task<IReadOnlyList<EmployeeSchedule>> GetListAsync(
        ScheduleQueryFilter filter,
        CancellationToken cancellationToken = default)
    {
        var query = _dbSet
            .AsNoTracking()
            .Include(s => s.Employee)
            .Include(s => s.Branch)
            .Include(s => s.ShiftConfig)
            .Where(s => !s.IsDeleted);

        if (filter.BranchId.HasValue && filter.BranchId.Value != Guid.Empty)
        {
            query = query.Where(s => s.BranchId == filter.BranchId.Value);
        }

        if (filter.EmployeeId.HasValue && filter.EmployeeId.Value != Guid.Empty)
        {
            query = query.Where(s => s.EmployeeId == filter.EmployeeId.Value);
        }

        if (filter.FromDate.HasValue)
        {
            var from = filter.FromDate.Value.Date;
            query = query.Where(s => s.WorkDate >= from);
        }

        if (filter.ToDate.HasValue)
        {
            var to = filter.ToDate.Value.Date;
            query = query.Where(s => s.WorkDate <= to);
        }

        if (filter.Status.HasValue)
        {
            query = query.Where(s => s.Status == filter.Status.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.Keyword))
        {
            var kw = filter.Keyword.Trim().ToLower();
            query = query.Where(s =>
                s.ShiftName.ToLower().Contains(kw) ||
                s.ShiftCode.ToLower().Contains(kw) ||
                (s.Employee != null && (s.Employee.FullName.ToLower().Contains(kw) || s.Employee.EmployeeCode.ToLower().Contains(kw))));
        }

        query = query
            .OrderBy(s => s.WorkDate)
            .ThenBy(s => s.PlannedStartTime)
            .ThenBy(s => s.Employee != null ? s.Employee.FullName : string.Empty);

        if (filter.Page > 0 && filter.PageSize > 0)
        {
            query = query
                .Skip((filter.Page - 1) * filter.PageSize)
                .Take(filter.PageSize);
        }

        return await query.ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<EmployeeSchedule>> GetActiveSchedulesForEmployeesAsync(
        IEnumerable<Guid> employeeIds,
        DateTime fromDate,
        DateTime toDate,
        CancellationToken cancellationToken = default)
    {
        var idList = employeeIds.ToList();
        var from = fromDate.Date;
        var to = toDate.Date;

        return await _dbSet
            .AsNoTracking()
            .Include(s => s.Employee)
            .Where(s => idList.Contains(s.EmployeeId)
                        && s.WorkDate >= from
                        && s.WorkDate <= to
                        && s.Status != ScheduleStatus.Cancelled
                        && !s.IsDeleted)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<EmployeeSchedule>> GetCompletedSchedulesInPeriodAsync(
        IEnumerable<Guid> employeeIds,
        DateTime fromDate,
        DateTime toDate,
        CancellationToken cancellationToken = default)
    {
        var idList = employeeIds.ToList();
        var from = fromDate.Date;
        var to = toDate.Date;

        return await _dbSet
            .AsNoTracking()
            .Where(s => idList.Contains(s.EmployeeId)
                        && s.WorkDate >= from
                        && s.WorkDate <= to
                        && (s.Status == ScheduleStatus.Completed || (s.Status == ScheduleStatus.OnLeave && s.ApprovalStatus == ScheduleApprovalStatus.Approved))
                        && !s.IsDeleted)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<EmployeeSchedule>> GetByEmployeeAndDateRangeAsync(
        Guid employeeId,
        DateTime fromDate,
        DateTime toDate,
        CancellationToken cancellationToken = default)
    {
        var from = fromDate.Date;
        var to = toDate.Date;

        return await _dbSet
            .AsNoTracking()
            .Include(s => s.Employee)
            .Include(s => s.Branch)
            .Include(s => s.ShiftConfig)
            .Where(s => s.EmployeeId == employeeId
                        && s.WorkDate >= from
                        && s.WorkDate <= to
                        && !s.IsDeleted)
            .OrderBy(s => s.WorkDate)
            .ThenBy(s => s.PlannedStartTime)
            .ToListAsync(cancellationToken);
    }

    public async Task AddRangeAsync(IEnumerable<EmployeeSchedule> schedules, CancellationToken cancellationToken = default)
    {
        await _dbSet.AddRangeAsync(schedules, cancellationToken);
    }

    public async Task AddAsync(EmployeeSchedule schedule, CancellationToken cancellationToken = default)
    {
        await _dbSet.AddAsync(schedule, cancellationToken);
    }

    public void Update(EmployeeSchedule schedule)
    {
        _dbSet.Update(schedule);
    }

    public void Delete(EmployeeSchedule schedule)
    {
        schedule.IsDeleted = true;
        _dbSet.Update(schedule);
    }
}

