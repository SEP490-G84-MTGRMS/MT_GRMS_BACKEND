using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Application.HR.Schedules.Dtos;

public enum ManualAttendanceType
{
    Working = 1,      // Đi làm
    PaidLeave = 2,    // Nghỉ phép có lương
    UnpaidLeave = 3   // Nghỉ phép không lương
}

public record ManualAttendanceRequest(
    ManualAttendanceType AttendanceType,
    TimeSpan? CheckInTime = null,
    TimeSpan? CheckOutTime = null,
    string? Note = null
);

public record ResetAttendanceRequest(
    string? Reason = null
);

public record ResetAttendanceResponse(
    ScheduleResponse Schedule,
    bool HasUnpaidPayslip,
    string Message
);

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
    IReadOnlyList<CreateScheduleItemRequest> Schedules,
    bool? HasEndDate = null,
    DateTime? EndDate = null
);

public record CreateRecurringScheduleRequest(
    Guid EmployeeId,
    Guid BranchId,
    DateTime StartDate,
    bool HasEndDate = false,
    DateTime? EndDate = null,
    IReadOnlyList<string>? RepeatDays = null,
    IReadOnlyList<Guid>? AdditionalEmployeeIds = null,
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
    DateTime CreatedAt,
    string? UserId = null
);

public record TodayStaffDescriptorDto(
    Guid EmployeeId,
    string EmployeeCode,
    string FullName,
    string? AvatarUrl,
    string? FaceDescriptor,
    Guid? ScheduleId,
    string? ShiftCode,
    string? ShiftName,
    TimeSpan? PlannedStartTime,
    TimeSpan? PlannedEndTime,
    DateTime? CheckInTime,
    DateTime? CheckOutTime,
    string? Status,
    string? FaceImageUrls = null
);

public record FaceIdCheckInRequest(
    Guid ScheduleId,
    Guid EmployeeId,
    decimal FaceMatchScore,
    string? SnapshotBase64
);

public record FaceIdCheckOutRequest(
    Guid ScheduleId,
    Guid EmployeeId,
    decimal FaceMatchScore,
    string? SnapshotBase64
);

public record RegisterFaceDescriptorRequest(
    string FaceDescriptor,
    string? FaceImageUrls = null
);

