using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Application.HR.Schedules.Dtos;

public class CreateScheduleApprovalRequestDto
{
    public ScheduleRequestType RequestType { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? EvidenceImageUrl { get; set; }
    public List<string>? EvidenceImageUrls { get; set; }
    public LeaveType? LeaveType { get; set; }
    public bool IsPaidLeaveRequested { get; set; } = false;
    public ShiftSwapType? SwapType { get; set; }
    public Guid? TargetEmployeeId { get; set; }
    public Guid? TargetScheduleId { get; set; }
    public DateTime? RequestedCheckInTime { get; set; }
    public DateTime? RequestedCheckOutTime { get; set; }
    public int? RequestedOvertimeMinutes { get; set; }
    public Guid? ScheduleId { get; set; }

    public CreateScheduleApprovalRequestDto() { }

    public CreateScheduleApprovalRequestDto(
        ScheduleRequestType RequestType,
        string Reason,
        string? EvidenceImageUrl = null,
        List<string>? EvidenceImageUrls = null,
        LeaveType? LeaveType = null,
        bool IsPaidLeaveRequested = false,
        ShiftSwapType? SwapType = null,
        Guid? TargetEmployeeId = null,
        Guid? TargetScheduleId = null,
        DateTime? RequestedCheckInTime = null,
        DateTime? RequestedCheckOutTime = null,
        int? RequestedOvertimeMinutes = null,
        Guid? ScheduleId = null)
    {
        this.RequestType = RequestType;
        this.Reason = Reason;
        this.EvidenceImageUrl = EvidenceImageUrl;
        this.EvidenceImageUrls = EvidenceImageUrls;
        this.LeaveType = LeaveType;
        this.IsPaidLeaveRequested = IsPaidLeaveRequested;
        this.SwapType = SwapType;
        this.TargetEmployeeId = TargetEmployeeId;
        this.TargetScheduleId = TargetScheduleId;
        this.RequestedCheckInTime = RequestedCheckInTime;
        this.RequestedCheckOutTime = RequestedCheckOutTime;
        this.RequestedOvertimeMinutes = RequestedOvertimeMinutes;
        this.ScheduleId = ScheduleId;
    }
}

public class ReviewScheduleApprovalRequestDto
{
    public bool IsApproved { get; set; }
    public string? ReviewNote { get; set; }
    public bool? IsPaidLeave { get; set; }
    public DateTime? ApprovedCheckInTime { get; set; }
    public DateTime? ApprovedCheckOutTime { get; set; }
    public int? ApprovedOvertimeMinutes { get; set; }

    public ReviewScheduleApprovalRequestDto() { }

    public ReviewScheduleApprovalRequestDto(
        bool IsApproved,
        string? ReviewNote = null,
        bool? IsPaidLeave = null,
        DateTime? ApprovedCheckInTime = null,
        DateTime? ApprovedCheckOutTime = null,
        int? ApprovedOvertimeMinutes = null)
    {
        this.IsApproved = IsApproved;
        this.ReviewNote = ReviewNote;
        this.IsPaidLeave = IsPaidLeave;
        this.ApprovedCheckInTime = ApprovedCheckInTime;
        this.ApprovedCheckOutTime = ApprovedCheckOutTime;
        this.ApprovedOvertimeMinutes = ApprovedOvertimeMinutes;
    }
}

public class PeerConfirmShiftSwapDto
{
    public bool IsAgreed { get; set; }
    public string? Note { get; set; }

    public PeerConfirmShiftSwapDto() { }

    public PeerConfirmShiftSwapDto(bool IsAgreed, string? Note = null)
    {
        this.IsAgreed = IsAgreed;
        this.Note = Note;
    }
}

public record ScheduleApprovalRequestDto(
    Guid Id,
    Guid ScheduleId,
    Guid EmployeeId,
    string EmployeeName,
    string EmployeeCode,
    Guid BranchId,
    string? BranchName,
    ScheduleRequestType RequestType,
    ScheduleApprovalStatus Status,
    string Reason,
    string? EvidenceImageUrl,
    IReadOnlyList<string> EvidenceImageUrls,
    LeaveType? LeaveType,
    bool IsPaidLeave,
    ShiftSwapType? SwapType,
    Guid? TargetEmployeeId,
    string? TargetEmployeeName,
    string? TargetEmployeeCode,
    Guid? TargetScheduleId,
    bool? PeerConfirmed,
    DateTime? PeerConfirmedAt,
    DateTime? RequestedCheckInTime,
    DateTime? RequestedCheckOutTime,
    DateTime? ApprovedCheckInTime,
    DateTime? ApprovedCheckOutTime,
    int? RequestedOvertimeMinutes,
    int? ApprovedOvertimeMinutes,
    string? ReviewNote,
    string? ApprovedBy,
    DateTime? ApprovedAt,
    DateTime CreatedAt,
    // Thông tin ca làm việc
    DateTime WorkDate,
    string ShiftName,
    TimeSpan PlannedStartTime,
    TimeSpan PlannedEndTime
);

public record PendingApprovalRequestsResponse(
    int Total,
    IReadOnlyList<ScheduleApprovalRequestDto> Items
);
