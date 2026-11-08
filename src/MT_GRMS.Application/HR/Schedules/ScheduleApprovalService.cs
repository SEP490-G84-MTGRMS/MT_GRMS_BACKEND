using MT_GRMS.Application.Common;
using MT_GRMS.Application.HR.Employees;
using MT_GRMS.Application.HR.Schedules.Dtos;
using MT_GRMS.Domain.Entities.HR;
using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Application.HR.Schedules;

public class ScheduleApprovalService : IScheduleApprovalService
{
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

        if (schedule.PayslipId.HasValue)
        {
            throw new InvalidOperationException("Ca làm việc đã được chốt bảng lương, không thể gửi yêu cầu.");
        }

        var currentEmployee = await ResolveEmployeeAsync(currentUserId, cancellationToken);
        if (currentEmployee != null && currentEmployee.Id != schedule.EmployeeId)
        {
            throw new UnauthorizedAccessException("Bạn chỉ được phép gửi yêu cầu cho ca làm việc của chính mình.");
        }

        var hasDuplicate = await _approvalRepository.HasPendingRequestOfTypeAsync(scheduleId, dto.RequestType, cancellationToken);
        if (hasDuplicate)
        {
            throw new InvalidOperationException("Đã có yêu cầu cùng loại đang chờ phê duyệt cho ca làm này.");
        }

        if (dto.RequestType == ScheduleRequestType.ShiftSwap)
        {
            if (schedule.Status == ScheduleStatus.Completed || schedule.Status == ScheduleStatus.Cancelled)
            {
                throw new InvalidOperationException("Chỉ có thể xin đổi ca khi ca chưa kết thúc.");
            }

            if (!dto.TargetEmployeeId.HasValue || dto.TargetEmployeeId.Value == Guid.Empty)
            {
                throw new ArgumentException("Vui lòng chọn đồng nghiệp muốn đổi ca cùng.");
            }

            if (dto.TargetEmployeeId.Value == schedule.EmployeeId)
            {
                throw new InvalidOperationException("Không thể tự đổi ca với chính mình.");
            }

            if (dto.SwapType == ShiftSwapType.CrossSwap)
            {
                if (!dto.TargetScheduleId.HasValue || dto.TargetScheduleId.Value == Guid.Empty)
                {
                    throw new ArgumentException("Vui lòng chọn ca của đồng nghiệp muốn đổi chéo.");
                }

                var targetSchedule = await _scheduleRepository.GetByIdAsync(dto.TargetScheduleId.Value, cancellationToken)
                    ?? throw new KeyNotFoundException("Không tìm thấy ca của đồng nghiệp muốn đổi.");

                if (targetSchedule.PayslipId.HasValue)
                {
                    throw new InvalidOperationException("Ca làm việc của đồng nghiệp đã chốt bảng lương, không thể đổi.");
                }

                if (targetSchedule.BranchId != schedule.BranchId)
                {
                    throw new InvalidOperationException("Chỉ được đổi ca với đồng nghiệp làm cùng chi nhánh.");
                }
            }
        }
        else if (dto.RequestType == ScheduleRequestType.LeaveRequest)
        {
            if (schedule.Status == ScheduleStatus.Completed || schedule.Status == ScheduleStatus.Cancelled)
            {
                throw new InvalidOperationException("Chỉ có thể xin nghỉ cho ca chưa kết thúc.");
            }
        }
        else if (dto.RequestType == ScheduleRequestType.CheckInIncident)
        {
            // Tự động kích hoạt CheckInTime để nhân viên đủ điều kiện mở phiên bán hàng POS ngay lập tức
            var checkIn = dto.RequestedCheckInTime ?? DateTime.UtcNow;
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

        var imageUrls = new List<string>();
        if (dto.EvidenceImageUrls != null && dto.EvidenceImageUrls.Count > 0)
        {
            imageUrls.AddRange(dto.EvidenceImageUrls.Where(u => !string.IsNullOrWhiteSpace(u)).Select(u => u.Trim()));
        }
        if (!string.IsNullOrWhiteSpace(dto.EvidenceImageUrl) && !imageUrls.Contains(dto.EvidenceImageUrl.Trim()))
        {
            imageUrls.Insert(0, dto.EvidenceImageUrl.Trim());
        }

        var request = new ScheduleApprovalRequest
        {
            ScheduleId = scheduleId,
            EmployeeId = schedule.EmployeeId,
            BranchId = schedule.BranchId,
            RequestType = dto.RequestType,
            Status = ScheduleApprovalStatus.Pending,
            Reason = dto.Reason.Trim(),
            EvidenceImageUrl = imageUrls.FirstOrDefault(),
            EvidenceImageUrls = imageUrls,
            LeaveType = dto.LeaveType,
            IsPaidLeave = dto.IsPaidLeaveRequested,
            SwapType = dto.SwapType,
            TargetEmployeeId = dto.TargetEmployeeId,
            TargetScheduleId = dto.TargetScheduleId,
            PeerConfirmed = (dto.RequestType == ScheduleRequestType.ShiftSwap) ? false : null,
            RequestedCheckInTime = dto.RequestedCheckInTime,
            RequestedCheckOutTime = dto.RequestedCheckOutTime,
            RequestedOvertimeMinutes = dto.RequestedOvertimeMinutes,
            TenantId = schedule.TenantId,
            CreatedAt = DateTime.UtcNow
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

    public async Task<bool> PeerConfirmShiftSwapAsync(
        Guid requestId,
        string currentUserId,
        PeerConfirmShiftSwapDto dto,
        CancellationToken cancellationToken = default)
    {
        var request = await _approvalRepository.GetByIdWithDetailsAsync(requestId, cancellationToken)
            ?? throw new KeyNotFoundException("Không tìm thấy yêu cầu.");

        if (request.RequestType != ScheduleRequestType.ShiftSwap || request.Status != ScheduleApprovalStatus.Pending)
        {
            throw new InvalidOperationException("Yêu cầu này không phải là đơn đổi ca đang chờ xử lý.");
        }

        var currentEmployee = await ResolveEmployeeAsync(currentUserId, cancellationToken);
        if (currentEmployee == null || currentEmployee.Id != request.TargetEmployeeId)
        {
            throw new UnauthorizedAccessException("Chỉ đồng nghiệp được đề nghị đổi ca mới có quyền xác nhận yêu cầu này.");
        }

        request.PeerConfirmed = dto.IsAgreed;
        request.PeerConfirmedAt = DateTime.UtcNow;

        if (!dto.IsAgreed)
        {
            request.Status = ScheduleApprovalStatus.Rejected;
            request.ReviewNote = $"Đồng nghiệp từ chối đổi ca: {dto.Note}";
        }

        _approvalRepository.Update(request);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
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

        if (schedule.PayslipId.HasValue)
        {
            throw new InvalidOperationException("Ca làm việc đã được chốt bảng lương, không thể phê duyệt.");
        }

        request.ApprovedBy = reviewerUserId;
        request.ApprovedAt = DateTime.UtcNow;
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
                schedule.ApprovedAt = DateTime.UtcNow;
                break;

            case ScheduleRequestType.ShiftSwap:
                schedule.ApprovalStatus = ScheduleApprovalStatus.Approved;
                schedule.ApprovalType = "SHIFT_SWAP";
                schedule.ApprovedBy = reviewerUserId;
                schedule.ApprovedAt = DateTime.UtcNow;

                if (request.SwapType == ShiftSwapType.CrossSwap && request.TargetScheduleId.HasValue)
                {
                    var targetSchedule = request.TargetSchedule
                        ?? await _scheduleRepository.GetByIdAsync(request.TargetScheduleId.Value, cancellationToken);

                    if (targetSchedule != null)
                    {
                        var tempEmployeeId = schedule.EmployeeId;
                        schedule.EmployeeId = targetSchedule.EmployeeId;
                        targetSchedule.EmployeeId = tempEmployeeId;
                        targetSchedule.ApprovalStatus = ScheduleApprovalStatus.Approved;
                        targetSchedule.ApprovalType = "SHIFT_SWAP";
                        targetSchedule.ApprovedBy = reviewerUserId;
                        targetSchedule.ApprovedAt = DateTime.UtcNow;
                        _scheduleRepository.Update(targetSchedule);
                    }
                }
                else if (request.TargetEmployeeId.HasValue)
                {
                    schedule.EmployeeId = request.TargetEmployeeId.Value;
                }
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

                schedule.ApprovalStatus = ScheduleApprovalStatus.Approved;
                schedule.ApprovalType = "INCIDENT_RESOLVED";
                schedule.ApprovedBy = reviewerUserId;
                schedule.ApprovedAt = DateTime.UtcNow;

                if (schedule.CheckInTime.HasValue && schedule.CheckOutTime.HasValue)
                {
                    var hours = (decimal)(schedule.CheckOutTime.Value - schedule.CheckInTime.Value).TotalHours;
                    schedule.ActualWorkHours = Math.Max(0, Math.Round(hours, 2));
                    schedule.Status = ScheduleStatus.Completed;
                }
                break;

            case ScheduleRequestType.CheckOutIncident:
                var checkOut = dto.ApprovedCheckOutTime ?? request.RequestedCheckOutTime ?? DateTime.UtcNow;
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
                    var hours = (decimal)(schedule.CheckOutTime.Value - schedule.CheckInTime.Value).TotalHours;
                    schedule.ActualWorkHours = Math.Max(0, Math.Round(hours, 2));
                }

                schedule.Status = ScheduleStatus.Completed;
                schedule.ApprovalStatus = ScheduleApprovalStatus.Approved;
                schedule.ApprovalType = "INCIDENT_RESOLVED";
                schedule.ApprovedBy = reviewerUserId;
                schedule.ApprovedAt = DateTime.UtcNow;
                break;

            case ScheduleRequestType.Overtime:
                var overtimeMinutes = dto.ApprovedOvertimeMinutes ?? request.RequestedOvertimeMinutes ?? 0;
                request.ApprovedOvertimeMinutes = overtimeMinutes;
                schedule.OvertimeMinutes += overtimeMinutes;
                schedule.ApprovalStatus = ScheduleApprovalStatus.Approved;
                schedule.ApprovalType = "OVERTIME";
                schedule.ApprovedBy = reviewerUserId;
                schedule.ApprovedAt = DateTime.UtcNow;
                break;
        }

        _scheduleRepository.Update(schedule);
        _approvalRepository.Update(request);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
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
        if (imageUrls.Count == 0 && !string.IsNullOrWhiteSpace(r.EvidenceImageUrl))
        {
            imageUrls.Add(r.EvidenceImageUrl);
        }

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
            r.EvidenceImageUrl ?? imageUrls.FirstOrDefault(),
            imageUrls,
            r.LeaveType,
            r.IsPaidLeave,
            r.SwapType,
            r.TargetEmployeeId,
            r.TargetEmployee?.FullName,
            r.TargetEmployee?.EmployeeCode,
            r.TargetScheduleId,
            r.PeerConfirmed,
            r.PeerConfirmedAt.HasValue ? DateTime.SpecifyKind(r.PeerConfirmedAt.Value, DateTimeKind.Utc) : null,
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
}
