namespace MT_GRMS.Application.HR.ShiftConfigs.Dtos;

public record CreateShiftConfigRequest(
    string ShiftName,
    TimeSpan StartTime,
    TimeSpan EndTime,
    IReadOnlyList<Guid>? BranchIds = null,
    Guid? BranchId = null,
    string? ShiftCode = null,
    decimal? DurationHours = null,
    bool? IsCheckInNextDay = null,
    string? Description = null,
    bool IsActive = true
);

public record UpdateShiftConfigRequest(
    string ShiftName,
    TimeSpan StartTime,
    TimeSpan EndTime,
    IReadOnlyList<Guid>? BranchIds = null,
    Guid? BranchId = null,
    string? ShiftCode = null,
    decimal? DurationHours = null,
    bool? IsCheckInNextDay = null,
    string? Description = null,
    bool IsActive = true
);

public record UpdateShiftConfigStatusRequest(
    bool IsActive
);

public record ShiftConfigQueryFilter(
    string? Keyword = null,
    Guid? BranchId = null,
    List<Guid>? BranchIds = null,
    bool? IsActive = null,
    int Page = 1,
    int PageSize = 20
);

public record ShiftConfigResponse(
    Guid Id,
    Guid BranchId,
    IReadOnlyList<Guid> BranchIds,
    string ShiftCode,
    string ShiftName,
    TimeSpan StartTime,
    TimeSpan EndTime,
    decimal DurationHours,
    bool IsCheckInNextDay,
    string? Description,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    string? BranchName = null,
    IReadOnlyList<string>? BranchNames = null
);
