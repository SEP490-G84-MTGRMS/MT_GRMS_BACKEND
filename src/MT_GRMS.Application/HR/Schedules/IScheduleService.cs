using MT_GRMS.Application.HR.Schedules.Dtos;

namespace MT_GRMS.Application.HR.Schedules;

public interface IScheduleService
{
    Task<ScheduleResponse> CreateAsync(CreateScheduleItemRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ScheduleResponse>> BatchCreateAsync(BatchCreateScheduleRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ScheduleResponse>> CreateRecurringAsync(CreateRecurringScheduleRequest request, CancellationToken cancellationToken = default);
    Task<ScheduleResponse> UpdateAsync(Guid id, UpdateScheduleRequest request, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ScheduleResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ScheduleResponse>> GetListAsync(ScheduleQueryFilter filter, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ScheduleResponse>> GetMySchedulesAsync(string userId, DateTime? fromDate, DateTime? toDate, CancellationToken cancellationToken = default);
    Task<PublishSchedulesResponse> PublishAsync(PublishSchedulesRequest request, CancellationToken cancellationToken = default);
}

