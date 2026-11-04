namespace MT_GRMS.Application.HR.ShiftConfigs.Dtos;

public record CreateShiftConfigRequest(
    Guid BranchId,
    string ShiftCode,
    string ShiftName,
    TimeSpan StartTime,
    TimeSpan EndTime,
    decimal? DurationHours = null,
    TimeSpan? AllowedCheckInStart = null,
    TimeSpan? AllowedCheckInEnd = null,
    string? Description = null,
    bool IsActive = true
);

public record UpdateShiftConfigRequest(
    string ShiftName,
    TimeSpan StartTime,
    TimeSpan EndTime,
    decimal? DurationHours = null,
    TimeSpan? AllowedCheckInStart = null,
    TimeSpan? AllowedCheckInEnd = null,
    string? Description = null,
    bool IsActive = true
);

public record UpdateShiftConfigStatusRequest(
    bool IsActive
);

public record ShiftConfigResponse(
    Guid Id,
    Guid BranchId,
    string ShiftCode,
    string ShiftName,
    TimeSpan StartTime,
    TimeSpan EndTime,
    decimal DurationHours,
    TimeSpan AllowedCheckInStart,
    TimeSpan AllowedCheckInEnd,
    string? Description,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? UpdatedAt
);

