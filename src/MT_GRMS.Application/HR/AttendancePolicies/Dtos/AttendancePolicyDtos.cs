namespace MT_GRMS.Application.HR.AttendancePolicies.Dtos;

public record UpsertAttendancePolicyRequest(
    decimal StandardDayHours = 8.00m,
    bool IsConvertHoursToDays = true,
    string? ConversionRulesJson = null,
    bool RecordLateEarlyIfHalfDay = false,
    bool EnableLateThreshold = true,
    int LateThresholdMinutes = 0,
    bool EnableEarlyLeaveThreshold = true,
    int EarlyLeaveThresholdMinutes = 0,
    bool EnableOtBeforeShift = true,
    int OtBeforeShiftMinutes = 0,
    bool EnableOtAfterShift = true,
    int OtAfterShiftMinutes = 0,
    bool EnableOtAllActualTime = false
);

public record CopyAttendancePolicyRequest(
    Guid SourceBranchId,
    IReadOnlyList<Guid> TargetBranchIds
);

public record CopyAttendancePolicyResultDto(
    Guid SourceBranchId,
    int CopiedCount,
    IReadOnlyList<Guid> TargetBranchIds,
    string Message
);

public record AttendancePolicyResponse(
    Guid Id,
    Guid BranchId,
    string? BranchName,
    decimal StandardDayHours,
    bool IsConvertHoursToDays,
    string? ConversionRulesJson,
    bool RecordLateEarlyIfHalfDay,
    bool EnableLateThreshold,
    int LateThresholdMinutes,
    bool EnableEarlyLeaveThreshold,
    int EarlyLeaveThresholdMinutes,
    bool EnableOtBeforeShift,
    int OtBeforeShiftMinutes,
    bool EnableOtAfterShift,
    int OtAfterShiftMinutes,
    bool EnableOtAllActualTime,
    bool IsCustomized
);

