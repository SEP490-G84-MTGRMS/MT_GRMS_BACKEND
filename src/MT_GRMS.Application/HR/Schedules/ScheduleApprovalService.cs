using MT_GRMS.Application.Common;
using MT_GRMS.Application.HR.Employees;
using MT_GRMS.Application.HR.Schedules.Dtos;
using MT_GRMS.Domain.Entities.HR;
using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Application.HR.Schedules;

public class ScheduleApprovalService : IScheduleApprovalService
{
    private static DateTime VnNow => DateTime.UtcNow.AddHours(7);
    private readonly IScheduleApprovalRequestRepository _approvalRepository;
    private readonly IScheduleRepository _scheduleRepository;
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITenantContext _tenantContext;

    public ScheduleApprovalService(
        IScheduleApprovalRequestRepository approvalRepository,
        IScheduleRepository scheduleRepository,
        IEmployeeRepository employeeRepository,
        IUnitOfWork unitOfWork,
        ITenantContext tenantContext)
    {
        _approvalRepository = approvalRepository;
        _scheduleRepository = scheduleRepository;
        _employeeRepository = employeeRepository;
        _unitOfWork = unitOfWork;
        _tenantContext = tenantContext;
    }

    public async Task<ScheduleApprovalRequestDto> CreateRequestAsync(
        Guid scheduleId,
        string currentUserId,
        CreateScheduleApprovalRequestDto dto,
        CancellationToken cancellationToken = default)
    {
        var schedule = await _scheduleRepository.GetByIdAsync(scheduleId, cancellationToken)
            ?? throw new KeyNotFoundException("Không tìm thấy ca làm việc chỉ định.");

        if (schedule.PayslipId.HasValue && (schedule.Payslip == null || schedule.Payslip.Status != PayslipStatus.Unpaid))
        {
            throw new InvalidOperationException("Ca làm việc đã được chốt bảng lương, không thể gửi yêu cầu.");
        }

        var currentEmployee = await ResolveEmployeeAsync(currentUserId, cancellationToken);
        if (currentEmployee != null && currentEmployee.Id != schedule.EmployeeId)
        {
            throw new UnauthorizedAccessException("Bạn chỉ được phép gửi yêu cầu cho ca làm việc của chính mình.");
        }

        var existingRequests = await _approvalRepository.GetByScheduleIdAsync(scheduleId, cancellationToken);
        ValidateRequestCompatibility(schedule, existingRequests, dto.RequestType);

        if (dto.RequestType == ScheduleRequestType.CheckInIncident)
        {
            // Tự động kích hoạt CheckInTime theo giờ UTC+7 để nhân viên đủ điều kiện mở phiên bán hàng POS ngay lập tức
            var checkIn = dto.RequestedCheckInTime ?? VnNow;
            if (!schedule.CheckInTime.HasValue)
            {
                schedule.CheckInTime = checkIn;
                schedule.CheckInMethod = "INCIDENT_PENDING";

                var shiftStart = schedule.WorkDate.Date + schedule.PlannedStartTime;
                schedule.LateMinutes = schedule.CheckInTime > shiftStart
                    ? (int)(schedule.CheckInTime.Value - shiftStart).TotalMinutes
                    : 0;
            }

            if (schedule.Status == ScheduleStatus.Scheduled || schedule.Status == ScheduleStatus.Published)
            {
                schedule.Status = ScheduleStatus.InProgress;
            }

            _scheduleRepository.Update(schedule);
        }
        else if (dto.RequestType == ScheduleRequestType.CheckOutIncident)
        {
            // Sự cố ra ca: Ghi nhận giờ ra đề xuất
            if (dto.RequestedCheckOutTime.HasValue && !schedule.CheckOutTime.HasValue)
            {
                schedule.CheckOutTime = dto.RequestedCheckOutTime.Value;
                schedule.CheckOutMethod = "INCIDENT_PENDING";
                _scheduleRepository.Update(schedule);
            }
        }
        else if (dto.RequestType == ScheduleRequestType.Overtime)
        {
            var requestedOtMinutes = dto.RequestedOvertimeMinutes ?? CalculateExtraMinutesOutsideShift(schedule);
            await ValidateDailyWorkAndOvertimeLimitAsync(schedule, requestedOtMinutes, null, cancellationToken);
        }

        var imageUrls = BuildImageUrls(dto);

        var request = new ScheduleApprovalRequest
        {
            ScheduleId = scheduleId,
            EmployeeId = schedule.EmployeeId,
            BranchId = schedule.BranchId,
            RequestType = dto.RequestType,
            Status = ScheduleApprovalStatus.Pending,
            Reason = dto.Reason.Trim(),
            EvidenceImageUrls = imageUrls,
            LeaveType = dto.LeaveType,
            IsPaidLeave = dto.IsPaidLeaveRequested,
            RequestedCheckInTime = dto.RequestedCheckInTime,
            RequestedCheckOutTime = dto.RequestedCheckOutTime,
            RequestedOvertimeMinutes = dto.RequestedOvertimeMinutes,
            TenantId = schedule.TenantId,
            CreatedAt = VnNow
        };

        await _approvalRepository.AddAsync(request, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var created = await _approvalRepository.GetByIdWithDetailsAsync(request.Id, cancellationToken);
        return MapToDto(created ?? request);
    }

    public async Task<IReadOnlyList<ScheduleApprovalRequestDto>> GetRequestsByScheduleIdAsync(
        Guid scheduleId,
        string currentUserId,
        bool isOwnerOrAdmin,
        CancellationToken cancellationToken = default)
    {
        var schedule = await _scheduleRepository.GetByIdAsync(scheduleId, cancellationToken)
            ?? throw new KeyNotFoundException("Không tìm thấy ca làm việc.");

        if (!isOwnerOrAdmin)
        {
            var currentEmployee = await ResolveEmployeeAsync(currentUserId, cancellationToken);
            if (currentEmployee != null && currentEmployee.Id != schedule.EmployeeId)
            {
                throw new UnauthorizedAccessException("Bạn không có quyền xem yêu cầu của ca làm việc này.");
            }
        }

        var list = await _approvalRepository.GetByScheduleIdAsync(scheduleId, cancellationToken);
        return list.Select(MapToDto).ToList();
    }

    public async Task<PendingApprovalRequestsResponse> GetPendingRequestsAsync(
        Guid? branchId,
        ScheduleRequestType? requestType,
        CancellationToken cancellationToken = default)
    {
        return await GetAllRequestsAsync(branchId, requestType, ScheduleApprovalStatus.Pending, cancellationToken);
    }

    public async Task<PendingApprovalRequestsResponse> GetAllRequestsAsync(
        Guid? branchId,
        ScheduleRequestType? requestType,
        ScheduleApprovalStatus? status,
        CancellationToken cancellationToken = default)
    {
        var items = await _approvalRepository.GetRequestsAsync(branchId, requestType, status, cancellationToken);
        var dtos = items.Select(MapToDto).ToList();
        return new PendingApprovalRequestsResponse(dtos.Count, dtos);
    }

    public Task<bool> PeerConfirmShiftSwapAsync(
        Guid requestId,
        string currentUserId,
        PeerConfirmShiftSwapDto dto,
        CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException("Quy trình đổi ca hiện do Quản lý / Chủ cửa hàng phê duyệt trực tiếp.");
    }

    public async Task<bool> ReviewRequestAsync(
        Guid requestId,
        string reviewerUserId,
        ReviewScheduleApprovalRequestDto dto,
        CancellationToken cancellationToken = default)
    {
        var request = await _approvalRepository.GetByIdWithDetailsAsync(requestId, cancellationToken)
            ?? throw new KeyNotFoundException("Không tìm thấy yêu cầu phê duyệt.");

        if (request.Status != ScheduleApprovalStatus.Pending)
        {
            throw new InvalidOperationException("Yêu cầu này đã được xử lý trước đó.");
        }

        var schedule = request.Schedule
            ?? await _scheduleRepository.GetByIdAsync(request.ScheduleId, cancellationToken)
            ?? throw new KeyNotFoundException("Không tìm thấy ca làm việc liên kết.");

        if (schedule.PayslipId.HasValue && (schedule.Payslip == null || schedule.Payslip.Status != PayslipStatus.Unpaid))
        {
            throw new InvalidOperationException("Ca làm việc đã được chốt bảng lương, không thể phê duyệt.");
        }

        request.ApprovedBy = reviewerUserId;
        request.ApprovedAt = VnNow;
        request.ReviewNote = dto.ReviewNote;

        if (!dto.IsApproved)
        {
            request.Status = ScheduleApprovalStatus.Rejected;
            _approvalRepository.Update(request);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return true;
        }

        request.Status = ScheduleApprovalStatus.Approved;

        switch (request.RequestType)
        {
            case ScheduleRequestType.LeaveRequest:
                schedule.Status = ScheduleStatus.OnLeave;
                schedule.IsLeave = true;
                schedule.IsPaidLeave = dto.IsPaidLeave ?? request.IsPaidLeave;
                request.IsPaidLeave = schedule.IsPaidLeave;
                schedule.ApprovalStatus = ScheduleApprovalStatus.Approved;
                schedule.ApprovalType = schedule.IsPaidLeave ? "PAID_LEAVE" : "UNPAID_LEAVE";
                schedule.ApprovedBy = reviewerUserId;
                schedule.ApprovedAt = VnNow;
                break;

            case ScheduleRequestType.ShiftSwap:
                schedule.ApprovalStatus = ScheduleApprovalStatus.Approved;
                schedule.ApprovalType = "SHIFT_SWAP";
                schedule.ApprovedBy = reviewerUserId;
                schedule.ApprovedAt = VnNow;
                break;

            case ScheduleRequestType.CheckInIncident:
                var checkIn = dto.ApprovedCheckInTime ?? request.RequestedCheckInTime;
                if (checkIn.HasValue)
                {
                    schedule.CheckInTime = checkIn.Value;
                    schedule.CheckInMethod = "INCIDENT_EXPLAINED";
                    request.ApprovedCheckInTime = checkIn.Value;

                    var shiftStart = schedule.WorkDate.Date + schedule.PlannedStartTime;
                    schedule.LateMinutes = schedule.CheckInTime > shiftStart
                        ? (int)(schedule.CheckInTime.Value - shiftStart).TotalMinutes
                        : 0;
                }

                var checkOutFromIncident = dto.ApprovedCheckOutTime ?? request.RequestedCheckOutTime;
                if (checkOutFromIncident.HasValue && !schedule.CheckOutTime.HasValue)
                {
                    schedule.CheckOutTime = checkOutFromIncident.Value;
                    schedule.CheckOutMethod = "INCIDENT_EXPLAINED";
                    request.ApprovedCheckOutTime = checkOutFromIncident.Value;
                }

                schedule.ApprovalStatus = ScheduleApprovalStatus.Approved;
                schedule.ApprovalType = "INCIDENT_RESOLVED";
                schedule.ApprovedBy = reviewerUserId;
                schedule.ApprovedAt = VnNow;

                if (schedule.CheckInTime.HasValue && schedule.CheckOutTime.HasValue)
                {
                    schedule.ActualWorkHours = CalculateClampedInShiftHours(schedule);
                    schedule.Status = ScheduleStatus.Completed;
                }
                break;

            case ScheduleRequestType.CheckOutIncident:
                var checkOut = dto.ApprovedCheckOutTime ?? request.RequestedCheckOutTime ?? VnNow;
                schedule.CheckOutTime = checkOut;
                schedule.CheckOutMethod = "INCIDENT_EXPLAINED";
                request.ApprovedCheckOutTime = checkOut;

                if (!schedule.CheckInTime.HasValue)
                {
                    schedule.CheckInTime = schedule.WorkDate.Date + schedule.PlannedStartTime;
                    schedule.CheckInMethod = "MANUAL";
                }

                if (schedule.CheckInTime.HasValue)
                {
                    schedule.ActualWorkHours = CalculateClampedInShiftHours(schedule);
                }

                schedule.Status = ScheduleStatus.Completed;
                schedule.ApprovalStatus = ScheduleApprovalStatus.Approved;
                schedule.ApprovalType = "INCIDENT_RESOLVED";
                schedule.ApprovedBy = reviewerUserId;
                schedule.ApprovedAt = VnNow;
                break;

            case ScheduleRequestType.Overtime:
                var extraOutsideMinutes = CalculateExtraMinutesOutsideShift(schedule);
                int overtimeMinutes;
                if (dto.ApprovedOvertimeMinutes.HasValue)
                {
                    overtimeMinutes = dto.ApprovedOvertimeMinutes.Value;
                }
                else if (extraOutsideMinutes > 0)
                {
                    overtimeMinutes = request.RequestedOvertimeMinutes.HasValue && request.RequestedOvertimeMinutes.Value > 0
                        ? Math.Min(request.RequestedOvertimeMinutes.Value, extraOutsideMinutes)
                        : extraOutsideMinutes;
                }
                else
                {
                    overtimeMinutes = request.RequestedOvertimeMinutes ?? 0;
                }

                await ValidateDailyWorkAndOvertimeLimitAsync(schedule, overtimeMinutes, request.Id, cancellationToken);
                request.ApprovedOvertimeMinutes = overtimeMinutes;
                schedule.OvertimeMinutes += overtimeMinutes;
                schedule.ApprovalStatus = ScheduleApprovalStatus.Approved;
                schedule.ApprovalType = "OVERTIME";
                schedule.ApprovedBy = reviewerUserId;
                schedule.ApprovedAt = VnNow;
                break;
        }

        _scheduleRepository.Update(schedule);
        _approvalRepository.Update(request);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return true;
    }

    public async Task<ScheduleApprovalRequestDto> UpdateRequestAsync(
        Guid requestId,
        string currentUserId,
        bool isOwnerOrAdmin,
        CreateScheduleApprovalRequestDto dto,
        CancellationToken cancellationToken = default)
    {
        var (request, schedule) = await LoadModifiablePendingRequestAsync(
            requestId,
            currentUserId,
            isOwnerOrAdmin,
            "chỉnh sửa",
            cancellationToken);

        if (dto.RequestType != request.RequestType)
        {
            throw new InvalidOperationException("Không thể thay đổi loại yêu cầu khi chỉnh sửa phiếu.");
        }

        if (schedule != null)
        {
            if (request.RequestType == ScheduleRequestType.CheckInIncident &&
                dto.RequestedCheckInTime.HasValue &&
                schedule.CheckInMethod == "INCIDENT_PENDING")
            {
                schedule.CheckInTime = dto.RequestedCheckInTime.Value;
                var shiftStart = schedule.WorkDate.Date + schedule.PlannedStartTime;
                schedule.LateMinutes = schedule.CheckInTime > shiftStart
                    ? (int)(schedule.CheckInTime.Value - shiftStart).TotalMinutes
                    : 0;
                _scheduleRepository.Update(schedule);
            }
            else if (request.RequestType == ScheduleRequestType.CheckOutIncident &&
                     dto.RequestedCheckOutTime.HasValue &&
                     schedule.CheckOutMethod == "INCIDENT_PENDING")
            {
                schedule.CheckOutTime = dto.RequestedCheckOutTime.Value;
                _scheduleRepository.Update(schedule);
            }
            else if (request.RequestType == ScheduleRequestType.Overtime)
            {
                var requestedOtMinutes = dto.RequestedOvertimeMinutes ?? CalculateExtraMinutesOutsideShift(schedule);
                await ValidateDailyWorkAndOvertimeLimitAsync(schedule, requestedOtMinutes, request.Id, cancellationToken);
            }
        }

        var imageUrls = BuildImageUrls(dto);
        request.Reason = dto.Reason.Trim();
        request.EvidenceImageUrls = imageUrls;
        request.LeaveType = request.RequestType == ScheduleRequestType.LeaveRequest ? dto.LeaveType : null;
        request.IsPaidLeave = request.RequestType == ScheduleRequestType.LeaveRequest && dto.IsPaidLeaveRequested;
        request.RequestedCheckInTime = request.RequestType == ScheduleRequestType.CheckInIncident ? dto.RequestedCheckInTime : null;
        request.RequestedCheckOutTime = request.RequestType == ScheduleRequestType.CheckOutIncident ? dto.RequestedCheckOutTime : null;
        request.RequestedOvertimeMinutes = request.RequestType == ScheduleRequestType.Overtime ? dto.RequestedOvertimeMinutes : null;

        _approvalRepository.Update(request);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var updated = await _approvalRepository.GetByIdWithDetailsAsync(request.Id, cancellationToken);
        return MapToDto(updated ?? request);
    }

    public async Task<bool> DeleteRequestAsync(
        Guid requestId,
        string currentUserId,
        bool isOwnerOrAdmin,
        CancellationToken cancellationToken = default)
    {
        var (request, schedule) = await LoadModifiablePendingRequestAsync(
            requestId,
            currentUserId,
            isOwnerOrAdmin,
            "xóa",
            cancellationToken);

        if (schedule != null)
        {
            if (request.RequestType == ScheduleRequestType.CheckInIncident && schedule.CheckInMethod == "INCIDENT_PENDING")
            {
                schedule.CheckInTime = null;
                schedule.CheckInMethod = null;
                schedule.LateMinutes = 0;
                if (schedule.Status == ScheduleStatus.InProgress)
                {
                    schedule.Status = ScheduleStatus.Published;
                }
                _scheduleRepository.Update(schedule);
            }
            else if (request.RequestType == ScheduleRequestType.CheckOutIncident && schedule.CheckOutMethod == "INCIDENT_PENDING")
            {
                schedule.CheckOutTime = null;
                schedule.CheckOutMethod = null;
                _scheduleRepository.Update(schedule);
            }
        }

        _approvalRepository.Delete(request);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<(ScheduleApprovalRequest Request, EmployeeSchedule? Schedule)> LoadModifiablePendingRequestAsync(
        Guid requestId,
        string currentUserId,
        bool isOwnerOrAdmin,
        string actionLabel,
        CancellationToken cancellationToken)
    {
        var request = await _approvalRepository.GetByIdWithDetailsAsync(requestId, cancellationToken)
            ?? throw new KeyNotFoundException("Không tìm thấy phiếu yêu cầu.");

        if (request.Status != ScheduleApprovalStatus.Pending)
        {
            throw new InvalidOperationException($"Chỉ có thể {actionLabel} phiếu yêu cầu đang ở trạng thái chờ duyệt.");
        }

        var schedule = await _scheduleRepository.GetByIdAsync(request.ScheduleId, cancellationToken);
        if (schedule != null && schedule.PayslipId.HasValue && (schedule.Payslip == null || schedule.Payslip.Status != PayslipStatus.Unpaid))
        {
            throw new InvalidOperationException("Ca làm việc đã được chốt bảng lương, không thể thao tác.");
        }

        if (!isOwnerOrAdmin)
        {
            var currentEmployee = await ResolveEmployeeAsync(currentUserId, cancellationToken);
            if (currentEmployee == null || currentEmployee.Id != request.EmployeeId)
            {
                throw new UnauthorizedAccessException($"Bạn chỉ được phép {actionLabel} phiếu yêu cầu của chính mình.");
            }
        }

        return (request, schedule);
    }

    private static void ValidateRequestCompatibility(
        EmployeeSchedule schedule,
        IReadOnlyList<ScheduleApprovalRequest> existingRequests,
        ScheduleRequestType targetType)
    {
        var pendingRequests = existingRequests
            .Where(r => !r.IsDeleted && r.Status == ScheduleApprovalStatus.Pending)
            .ToList();
        var approvedRequests = existingRequests
            .Where(r => !r.IsDeleted && r.Status == ScheduleApprovalStatus.Approved)
            .ToList();

        if (pendingRequests.Any(r => r.RequestType == targetType))
        {
            throw new InvalidOperationException("Đã có yêu cầu cùng loại đang chờ phê duyệt cho ca làm này.");
        }

        if (schedule.Status == ScheduleStatus.Cancelled)
        {
            throw new InvalidOperationException("Không thể gửi yêu cầu cho ca làm việc đã hủy.");
        }

        var hasActiveLeave = schedule.Status == ScheduleStatus.OnLeave
            || pendingRequests.Any(r => r.RequestType == ScheduleRequestType.LeaveRequest)
            || approvedRequests.Any(r => r.RequestType == ScheduleRequestType.LeaveRequest);

        if (hasActiveLeave)
        {
            throw new InvalidOperationException("Ca làm việc đã có đơn xin nghỉ hoặc đã nghỉ, không thể gửi thêm yêu cầu khác.");
        }

        var hasPendingShiftSwap = pendingRequests.Any(r => r.RequestType == ScheduleRequestType.ShiftSwap);
        if (hasPendingShiftSwap && targetType != ScheduleRequestType.ShiftSwap)
        {
            throw new InvalidOperationException("Ca làm việc đang có đơn đổi ca chờ duyệt, không thể gửi thêm yêu cầu khác.");
        }

        switch (targetType)
        {
            case ScheduleRequestType.LeaveRequest:
                if (schedule.Status == ScheduleStatus.Completed || schedule.Status == ScheduleStatus.InProgress || schedule.CheckInTime.HasValue)
                {
                    throw new InvalidOperationException("Không thể xin nghỉ cho ca đã chấm công, đang làm việc hoặc đã kết thúc.");
                }
                if (pendingRequests.Any(r => r.RequestType != ScheduleRequestType.LeaveRequest))
                {
                    throw new InvalidOperationException("Ca làm việc đang có đơn làm thêm giờ, đổi ca hoặc sự cố chờ duyệt, không thể xin nghỉ.");
                }
                break;

            case ScheduleRequestType.ShiftSwap:
                if (schedule.Status == ScheduleStatus.Completed || schedule.Status == ScheduleStatus.InProgress || schedule.CheckInTime.HasValue)
                {
                    throw new InvalidOperationException("Chỉ có thể xin đổi ca khi ca chưa bắt đầu và chưa chấm công.");
                }
                if (pendingRequests.Any(r => r.RequestType != ScheduleRequestType.ShiftSwap))
                {
                    throw new InvalidOperationException("Ca làm việc đang có đơn khác chờ duyệt, không thể xin đổi ca.");
                }
                break;

            case ScheduleRequestType.CheckInIncident:
                if (approvedRequests.Any(r => r.RequestType == ScheduleRequestType.CheckInIncident))
                {
                    throw new InvalidOperationException("Sự cố Check-in của ca làm việc này đã được duyệt.");
                }
                if (schedule.CheckInTime.HasValue && schedule.CheckInMethod != "INCIDENT_PENDING")
                {
                    throw new InvalidOperationException("Ca làm việc đã được ghi nhận giờ vào, không cần gửi sự cố Check-in.");
                }
                break;

            case ScheduleRequestType.CheckOutIncident:
                if (approvedRequests.Any(r => r.RequestType == ScheduleRequestType.CheckOutIncident))
                {
                    throw new InvalidOperationException("Sự cố Check-out của ca làm việc này đã được duyệt.");
                }
                if (schedule.CheckOutTime.HasValue && schedule.CheckOutMethod != "INCIDENT_PENDING")
                {
                    throw new InvalidOperationException("Ca làm việc đã được ghi nhận giờ ra, không cần gửi sự cố Check-out.");
                }
                break;
        }
    }

    private static List<string> BuildImageUrls(CreateScheduleApprovalRequestDto dto)
    {
        var imageUrls = new List<string>();
        if (dto.EvidenceImageUrls != null && dto.EvidenceImageUrls.Count > 0)
        {
            imageUrls.AddRange(dto.EvidenceImageUrls.Where(u => !string.IsNullOrWhiteSpace(u)).Select(u => u.Trim()));
        }
        return imageUrls;
    }

    private async Task<Employee?> ResolveEmployeeAsync(string userId, CancellationToken cancellationToken)
    {
        var employee = await _employeeRepository.GetByUserIdAsync(userId, cancellationToken);
        if (employee == null && Guid.TryParse(userId, out var empId))
        {
            employee = await _employeeRepository.GetByIdAsync(empId, cancellationToken);
        }
        return employee;
    }

    private static ScheduleApprovalRequestDto MapToDto(ScheduleApprovalRequest r)
    {
        var imageUrls = r.EvidenceImageUrls?.ToList() ?? new List<string>();

        return new ScheduleApprovalRequestDto(
            r.Id,
            r.ScheduleId,
            r.EmployeeId,
            r.Employee?.FullName ?? "Nhân viên",
            r.Employee?.EmployeeCode ?? string.Empty,
            r.BranchId,
            r.Schedule?.Branch?.Name,
            r.RequestType,
            r.Status,
            r.Reason,
            imageUrls,
            r.LeaveType,
            r.IsPaidLeave,
            r.RequestedCheckInTime,
            r.RequestedCheckOutTime,
            r.ApprovedCheckInTime,
            r.ApprovedCheckOutTime,
            r.RequestedOvertimeMinutes,
            r.ApprovedOvertimeMinutes,
            r.ReviewNote,
            r.ApprovedBy,
            r.ApprovedAt.HasValue ? DateTime.SpecifyKind(r.ApprovedAt.Value, DateTimeKind.Utc) : null,
            DateTime.SpecifyKind(r.CreatedAt, DateTimeKind.Utc),
            r.Schedule?.WorkDate ?? DateTime.MinValue,
            r.Schedule?.ShiftName ?? string.Empty,
            r.Schedule?.PlannedStartTime ?? TimeSpan.Zero,
            r.Schedule?.PlannedEndTime ?? TimeSpan.Zero
        );
    }

    private static decimal CalculateClampedInShiftHours(EmployeeSchedule schedule)
    {
        if (!schedule.CheckInTime.HasValue || !schedule.CheckOutTime.HasValue)
        {
            return 0m;
        }

        var workDate = schedule.WorkDate.Date;
        var plannedStart = workDate.Add(schedule.PlannedStartTime);
        var isNextDay = schedule.IsCheckInNextDay || schedule.PlannedEndTime < schedule.PlannedStartTime;
        var plannedEnd = isNextDay
            ? workDate.AddDays(1).Add(schedule.PlannedEndTime)
            : workDate.Add(schedule.PlannedEndTime);

        if (schedule.PlannedStartTime == TimeSpan.Zero && schedule.PlannedEndTime == TimeSpan.Zero)
        {
            var rawHours = (decimal)(schedule.CheckOutTime.Value - schedule.CheckInTime.Value).TotalHours;
            return Math.Max(0m, Math.Round(rawHours, 2));
        }

        var effectiveStart = schedule.CheckInTime.Value < plannedStart ? plannedStart : schedule.CheckInTime.Value;
        var effectiveEnd = schedule.CheckOutTime.Value > plannedEnd ? plannedEnd : schedule.CheckOutTime.Value;
        var inShiftMinutes = Math.Max(0, (int)(effectiveEnd - effectiveStart).TotalMinutes);
        return Math.Round((decimal)inShiftMinutes / 60m, 2);
    }

    private static int CalculateExtraMinutesOutsideShift(EmployeeSchedule schedule)
    {
        if (!schedule.CheckInTime.HasValue || !schedule.CheckOutTime.HasValue)
        {
            return 0;
        }

        if (schedule.PlannedStartTime == TimeSpan.Zero && schedule.PlannedEndTime == TimeSpan.Zero)
        {
            return 0;
        }

        var workDate = schedule.WorkDate.Date;
        var plannedStart = workDate.Add(schedule.PlannedStartTime);
        var isNextDay = schedule.IsCheckInNextDay || schedule.PlannedEndTime < schedule.PlannedStartTime;
        var plannedEnd = isNextDay
            ? workDate.AddDays(1).Add(schedule.PlannedEndTime)
            : workDate.Add(schedule.PlannedEndTime);

        var earlyExtraMinutes = schedule.CheckInTime.Value < plannedStart
            ? Math.Max(0, (int)(plannedStart - schedule.CheckInTime.Value).TotalMinutes)
            : 0;
        var lateExtraMinutes = schedule.CheckOutTime.Value > plannedEnd
            ? Math.Max(0, (int)(schedule.CheckOutTime.Value - plannedEnd).TotalMinutes)
            : 0;

        return earlyExtraMinutes + lateExtraMinutes;
    }

    private static decimal GetSchedulePlannedHours(EmployeeSchedule schedule)
    {
        if (schedule.PlannedStartTime != TimeSpan.Zero || schedule.PlannedEndTime != TimeSpan.Zero)
        {
            var isNextDay = schedule.IsCheckInNextDay || schedule.PlannedEndTime < schedule.PlannedStartTime;
            var duration = isNextDay
                ? (TimeSpan.FromHours(24) - schedule.PlannedStartTime) + schedule.PlannedEndTime
                : schedule.PlannedEndTime - schedule.PlannedStartTime;
            var plannedHours = Math.Round((decimal)duration.TotalHours, 2);
            if (plannedHours > 0m)
            {
                return plannedHours;
            }
        }

        return schedule.DurationHours > 0m ? schedule.DurationHours : 0m;
    }

    private async Task ValidateDailyWorkAndOvertimeLimitAsync(
        EmployeeSchedule targetSchedule,
        int additionalOvertimeMinutes,
        Guid? excludeRequestId,
        CancellationToken cancellationToken)
    {
        if (additionalOvertimeMinutes <= 0)
        {
            return;
        }

        var daySchedules = await _scheduleRepository.GetByEmployeeAndDateRangeAsync(
            targetSchedule.EmployeeId,
            targetSchedule.WorkDate.Date,
            targetSchedule.WorkDate.Date,
            cancellationToken);

        var activeDaySchedules = daySchedules
            .Where(s => !s.IsLeave && s.Status != ScheduleStatus.OnLeave && s.Status != ScheduleStatus.Cancelled)
            .ToList();

        if (!activeDaySchedules.Any(s => s.Id == targetSchedule.Id))
        {
            activeDaySchedules.Add(targetSchedule);
        }

        decimal totalPlannedHours = activeDaySchedules.Sum(GetSchedulePlannedHours);
        int existingApprovedOtMinutes = activeDaySchedules.Sum(s => s.OvertimeMinutes);

        var totalHoursWithOt = totalPlannedHours + ((existingApprovedOtMinutes + additionalOvertimeMinutes) / 60m);
        if (totalHoursWithOt > 12m)
        {
            throw new InvalidOperationException(
                $"Tổng giờ làm việc và làm thêm giờ (OT) trong ngày {targetSchedule.WorkDate:dd/MM/yyyy} là {totalHoursWithOt:0.##}h, vượt quá giới hạn tối đa 12 giờ/ngày.");
        }
    }
}
