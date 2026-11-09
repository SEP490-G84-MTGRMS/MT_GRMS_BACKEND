using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Entities.HR;
using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Application.HR.Schedules;

public interface IScheduleApprovalRequestRepository : IRepository<ScheduleApprovalRequest>
{
    Task<ScheduleApprovalRequest?> GetByIdWithDetailsAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ScheduleApprovalRequest>> GetByScheduleIdAsync(Guid scheduleId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ScheduleApprovalRequest>> GetPendingAsync(Guid? branchId, ScheduleRequestType? requestType, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ScheduleApprovalRequest>> GetRequestsAsync(Guid? branchId, ScheduleRequestType? requestType, ScheduleApprovalStatus? status, CancellationToken cancellationToken = default);
    Task<bool> HasPendingRequestOfTypeAsync(Guid scheduleId, ScheduleRequestType requestType, CancellationToken cancellationToken = default);
}

