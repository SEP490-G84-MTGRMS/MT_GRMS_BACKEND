using MT_GRMS.Application.HR.Schedules.Dtos;
using MT_GRMS.Domain.Entities.HR;

namespace MT_GRMS.Application.HR.Schedules;

public interface IScheduleRepository
{
    Task<EmployeeSchedule?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<EmployeeSchedule>> GetListAsync(ScheduleQueryFilter filter, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<EmployeeSchedule>> GetActiveSchedulesForEmployeesAsync(IEnumerable<Guid> employeeIds, DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<EmployeeSchedule>> GetByEmployeeAndDateRangeAsync(Guid employeeId, DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default);
    Task AddRangeAsync(IEnumerable<EmployeeSchedule> schedules, CancellationToken cancellationToken = default);
    Task AddAsync(EmployeeSchedule schedule, CancellationToken cancellationToken = default);
    void Update(EmployeeSchedule schedule);
    void Delete(EmployeeSchedule schedule);
}

