using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Application.HR.Schedules.Dtos;

public record CreateScheduleItemRequest(
    Guid EmployeeId,
    Guid BranchId,
    DateTime WorkDate,
    Guid? ShiftConfigId = null,
    string? ShiftCode = null,
    string? ShiftName = null,
    TimeSpan? PlannedStartTime = null,
    TimeSpan? PlannedEndTime = null,
    decimal? DurationHours = null,
    TimeSpan? AllowedCheckInStart = null,
    TimeSpan? AllowedCheckInEnd = null,
    bool? IsCheckInNextDay = null
);

public record UpdateScheduleRequest(
    Guid? EmployeeId = null,
    Guid? BranchId = null,
    DateTime? WorkDate = null,
    Guid? ShiftConfigId = null,
    string? ShiftCode = null,
    string? ShiftName = null,
    TimeSpan? PlannedStartTime = null,
    TimeSpan? PlannedEndTime = null,
    decimal? DurationHours = null,
    TimeSpan? AllowedCheckInStart = null,
    TimeSpan? AllowedCheckInEnd = null,
    bool? IsCheckInNextDay = null,
    ScheduleStatus? Status = null
);

public record BatchCreateScheduleRequest(
    Guid BranchId,
    IReadOnlyList<CreateScheduleItemRequest> Schedules
);

public record PublishSchedulesRequest(
    Guid BranchId,
    DateTime FromDate,
    DateTime ToDate,
    IReadOnlyList<Guid>? EmployeeIds = null
);

public record PublishSchedulesResponse(
    int PublishedCount,
    string Message
);

public record ScheduleQueryFilter(
    Guid? BranchId = null,
    Guid? EmployeeId = null,
    DateTime? FromDate = null,
    DateTime? ToDate = null,
    ScheduleStatus? Status = null,
    string? Keyword = null,
    int Page = 1,
    int PageSize = 100
);

public record ScheduleResponse(
    Guid Id,
    Guid EmployeeId,
    string EmployeeCode,
    string EmployeeName,
    string? EmployeeAvatarUrl,
    Guid BranchId,
    string? BranchName,
    Guid? ShiftConfigId,
    DateTime WorkDate,
    string ShiftCode,
    string ShiftName,
    TimeSpan PlannedStartTime,
    TimeSpan PlannedEndTime,
    decimal DurationHours,
    TimeSpan AllowedCheckInStart,
    TimeSpan AllowedCheckInEnd,
    bool IsCheckInNextDay,
    DateTime? CheckInTime,
    DateTime? CheckOutTime,
    string? CheckInMethod,
    string? CheckInFaceImageUrl,
    decimal? FaceMatchScore,
    string? CheckOutMethod,
    string? CheckOutFaceImageUrl,
    int LateMinutes,
    int EarlyLeaveMinutes,
    int OvertimeMinutes,
    decimal ActualWorkHours,
    ScheduleStatus Status,
    string? ApprovalType,
    ScheduleApprovalStatus ApprovalStatus,
    string? ApprovalReason,
    string? EvidenceImageUrl,
    string? ApprovedBy,
    DateTime? ApprovedAt,
    DateTime CreatedAt
);

