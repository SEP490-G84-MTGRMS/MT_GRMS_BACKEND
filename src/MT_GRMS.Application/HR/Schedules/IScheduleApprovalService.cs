using MT_GRMS.Application.HR.Schedules.Dtos;
using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Application.HR.Schedules;

public interface IScheduleApprovalService
{
    Task<ScheduleApprovalRequestDto> CreateRequestAsync(
        Guid scheduleId,
        string currentUserId,
        CreateScheduleApprovalRequestDto dto,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ScheduleApprovalRequestDto>> GetRequestsByScheduleIdAsync(
        Guid scheduleId,
        string currentUserId,
        bool isOwnerOrAdmin,
        CancellationToken cancellationToken = default);

    Task<PendingApprovalRequestsResponse> GetPendingRequestsAsync(
        Guid? branchId,
        ScheduleRequestType? requestType,
        CancellationToken cancellationToken = default);

    Task<PendingApprovalRequestsResponse> GetAllRequestsAsync(
        Guid? branchId,
        ScheduleRequestType? requestType,
        ScheduleApprovalStatus? status,
        CancellationToken cancellationToken = default);

    Task<bool> PeerConfirmShiftSwapAsync(
        Guid requestId,
        string currentUserId,
        PeerConfirmShiftSwapDto dto,
        CancellationToken cancellationToken = default);

    Task<bool> ReviewRequestAsync(
        Guid requestId,
        string reviewerUserId,
        ReviewScheduleApprovalRequestDto dto,
        CancellationToken cancellationToken = default);
}

