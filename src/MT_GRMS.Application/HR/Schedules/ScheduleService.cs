using MT_GRMS.Application.Common;
using MT_GRMS.Application.HR.Employees;
using MT_GRMS.Application.HR.Schedules.Dtos;
using MT_GRMS.Application.HR.ShiftConfigs;
using MT_GRMS.Domain.Entities.HR;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Domain.Exceptions;

namespace MT_GRMS.Application.HR.Schedules;

public class ScheduleService : IScheduleService
{
    private readonly IScheduleRepository _scheduleRepository;
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IShiftConfigRepository _shiftConfigRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITenantContext _tenantContext;

    public ScheduleService(
        IScheduleRepository scheduleRepository,
        IEmployeeRepository employeeRepository,
        IShiftConfigRepository shiftConfigRepository,
        IUnitOfWork unitOfWork,
        ITenantContext tenantContext)
    {
        _scheduleRepository = scheduleRepository;
        _employeeRepository = employeeRepository;
        _shiftConfigRepository = shiftConfigRepository;
        _unitOfWork = unitOfWork;
        _tenantContext = tenantContext;
    }

    public async Task<ScheduleResponse> CreateAsync(
        CreateScheduleItemRequest request,
        CancellationToken cancellationToken = default)
    {
        var employee = await _employeeRepository.GetByIdAsync(request.EmployeeId, cancellationToken)
            ?? throw new NotFoundException($"Nhân viên với ID {request.EmployeeId} không tồn tại.");

        ShiftConfig? shiftConfig = null;
        if (request.ShiftConfigId.HasValue)
        {
            shiftConfig = await _shiftConfigRepository.GetByIdAsync(request.ShiftConfigId.Value, cancellationToken)
                ?? throw new NotFoundException($"Cấu hình ca với ID {request.ShiftConfigId.Value} không tồn tại.");
        }

        var resolved = ResolveShiftInfo(request, shiftConfig);
        var workDate = request.WorkDate.Date;
        var startDateTime = GetStartDateTime(workDate, resolved.StartTime);
        var endDateTime = GetEndDateTime(workDate, resolved.StartTime, resolved.EndTime, resolved.IsNextDay);

        // Kiểm tra xung đột với các ca hiện có trong DB
        var existingSchedules = await _scheduleRepository.GetActiveSchedulesForEmployeesAsync(
            new[] { request.EmployeeId },
            workDate.AddDays(-1),
            workDate.AddDays(1),
            cancellationToken);

        CheckConflict(employee, resolved.ShiftName, startDateTime, endDateTime, workDate, existingSchedules);

        var schedule = new EmployeeSchedule
        {
            TenantId = _tenantContext.CurrentTenantId,
            EmployeeId = request.EmployeeId,
            BranchId = request.BranchId,
            ShiftConfigId = request.ShiftConfigId,
            WorkDate = workDate,
            ShiftCode = resolved.ShiftCode,
            ShiftName = resolved.ShiftName,
            PlannedStartTime = resolved.StartTime,
            PlannedEndTime = resolved.EndTime,
            DurationHours = resolved.DurationHours,
            AllowedCheckInStart = resolved.AllowedCheckInStart,
            AllowedCheckInEnd = resolved.AllowedCheckInEnd,
            IsCheckInNextDay = resolved.IsNextDay,
            Status = ScheduleStatus.Draft
        };

        await _scheduleRepository.AddAsync(schedule, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        schedule.Employee = employee;
        return MapToResponse(schedule);
    }

    public async Task<IReadOnlyList<ScheduleResponse>> BatchCreateAsync(
        BatchCreateScheduleRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Schedules == null || request.Schedules.Count == 0)
        {
            return Array.Empty<ScheduleResponse>();
        }

        var employeeIds = request.Schedules.Select(s => s.EmployeeId).Distinct().ToList();
        var employees = new Dictionary<Guid, Employee>();
        foreach (var empId in employeeIds)
        {
            var emp = await _employeeRepository.GetByIdAsync(empId, cancellationToken)
                ?? throw new NotFoundException($"Nhân viên với ID {empId} không tồn tại.");
            employees[empId] = emp;
        }

        var shiftConfigIds = request.Schedules
            .Where(s => s.ShiftConfigId.HasValue)
            .Select(s => s.ShiftConfigId!.Value)
            .Distinct()
            .ToList();

        var shiftConfigs = new Dictionary<Guid, ShiftConfig>();
        foreach (var scId in shiftConfigIds)
        {
            var sc = await _shiftConfigRepository.GetByIdAsync(scId, cancellationToken)
                ?? throw new NotFoundException($"Cấu hình ca với ID {scId} không tồn tại.");
            shiftConfigs[scId] = sc;
        }

        var minDate = request.Schedules.Min(s => s.WorkDate).Date;
        var maxDate = request.Schedules.Max(s => s.WorkDate).Date;

        var existingSchedules = (await _scheduleRepository.GetActiveSchedulesForEmployeesAsync(
            employeeIds,
            minDate.AddDays(-1),
            maxDate.AddDays(1),
            cancellationToken)).ToList();

        var newSchedules = new List<EmployeeSchedule>();
        var batchIntervals = new List<(Guid EmployeeId, string ShiftName, DateTime Start, DateTime End, DateTime WorkDate)>();

        foreach (var item in request.Schedules)
        {
            var employee = employees[item.EmployeeId];
            ShiftConfig? shiftConfig = item.ShiftConfigId.HasValue ? shiftConfigs[item.ShiftConfigId.Value] : null;

            var resolved = ResolveShiftInfo(item, shiftConfig);
            var workDate = item.WorkDate.Date;
            var startDateTime = GetStartDateTime(workDate, resolved.StartTime);
            var endDateTime = GetEndDateTime(workDate, resolved.StartTime, resolved.EndTime, resolved.IsNextDay);

            // 1. Kiểm tra xung đột với nội bộ danh sách batch gửi lên
            foreach (var b in batchIntervals.Where(b => b.EmployeeId == item.EmployeeId && b.WorkDate >= workDate.AddDays(-1) && b.WorkDate <= workDate.AddDays(1)))
            {
                if (IsOverlapping(startDateTime, endDateTime, b.Start, b.End))
                {
                    throw new BusinessException(
                        $"Nhân viên {employee.FullName} ({employee.EmployeeCode}) bị trùng lặp thời gian làm việc ngày {workDate:dd/MM/yyyy} giữa ca [{resolved.ShiftName} {resolved.StartTime:hh\\:mm}-{resolved.EndTime:hh\\:mm}] và ca [{b.ShiftName} {b.Start:HH:mm}-{b.End:HH:mm}].");
                }
            }

            // 2. Kiểm tra xung đột với dữ liệu hiện có trong CSDL
            var empDbSchedules = existingSchedules.Where(s => s.EmployeeId == item.EmployeeId).ToList();
            CheckConflict(employee, resolved.ShiftName, startDateTime, endDateTime, workDate, empDbSchedules);

            batchIntervals.Add((item.EmployeeId, resolved.ShiftName, startDateTime, endDateTime, workDate));

            var schedule = new EmployeeSchedule
            {
                TenantId = _tenantContext.CurrentTenantId,
                EmployeeId = item.EmployeeId,
                BranchId = item.BranchId != Guid.Empty ? item.BranchId : request.BranchId,
                ShiftConfigId = item.ShiftConfigId,
                WorkDate = workDate,
                ShiftCode = resolved.ShiftCode,
                ShiftName = resolved.ShiftName,
                PlannedStartTime = resolved.StartTime,
                PlannedEndTime = resolved.EndTime,
                DurationHours = resolved.DurationHours,
                AllowedCheckInStart = resolved.AllowedCheckInStart,
                AllowedCheckInEnd = resolved.AllowedCheckInEnd,
                IsCheckInNextDay = resolved.IsNextDay,
                Status = ScheduleStatus.Draft,
                Employee = employee
            };

            newSchedules.Add(schedule);
        }

        await _scheduleRepository.AddRangeAsync(newSchedules, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return newSchedules.Select(MapToResponse).ToList();
    }

    public async Task<ScheduleResponse> UpdateAsync(
        Guid id,
        UpdateScheduleRequest request,
        CancellationToken cancellationToken = default)
    {
        var schedule = await _scheduleRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException($"Lịch làm việc với ID {id} không tồn tại.");

        if (schedule.Status == ScheduleStatus.Completed)
        {
            throw new BusinessException("Không thể chỉnh sửa ca làm việc đã hoàn thành.");
        }

        var employeeId = request.EmployeeId ?? schedule.EmployeeId;
        var employee = await _employeeRepository.GetByIdAsync(employeeId, cancellationToken)
            ?? throw new NotFoundException($"Nhân viên với ID {employeeId} không tồn tại.");

        ShiftConfig? shiftConfig = null;
        var shiftConfigId = request.ShiftConfigId ?? schedule.ShiftConfigId;
        if (shiftConfigId.HasValue)
        {
            shiftConfig = await _shiftConfigRepository.GetByIdAsync(shiftConfigId.Value, cancellationToken);
        }

        var workDate = (request.WorkDate ?? schedule.WorkDate).Date;
        var startTime = request.PlannedStartTime ?? (shiftConfig != null ? shiftConfig.StartTime : schedule.PlannedStartTime);
        var endTime = request.PlannedEndTime ?? (shiftConfig != null ? shiftConfig.EndTime : schedule.PlannedEndTime);
        var isNextDay = request.IsCheckInNextDay ?? (shiftConfig != null ? shiftConfig.IsCheckInNextDay : schedule.IsCheckInNextDay);
        var shiftName = request.ShiftName ?? (shiftConfig != null ? shiftConfig.ShiftName : schedule.ShiftName);
        var shiftCode = request.ShiftCode ?? (shiftConfig != null ? shiftConfig.ShiftCode : schedule.ShiftCode);

        var startDateTime = GetStartDateTime(workDate, startTime);
        var endDateTime = GetEndDateTime(workDate, startTime, endTime, isNextDay);

        // Kiểm tra xung đột với các ca khác (ngoại trừ ca hiện tại)
        var existingSchedules = (await _scheduleRepository.GetActiveSchedulesForEmployeesAsync(
            new[] { employeeId },
            workDate.AddDays(-1),
            workDate.AddDays(1),
            cancellationToken)).Where(s => s.Id != id).ToList();

        CheckConflict(employee, shiftName, startDateTime, endDateTime, workDate, existingSchedules);

        schedule.EmployeeId = employeeId;
        if (request.BranchId.HasValue && request.BranchId.Value != Guid.Empty)
        {
            schedule.BranchId = request.BranchId.Value;
        }
        schedule.ShiftConfigId = shiftConfigId;
        schedule.WorkDate = workDate;
        schedule.ShiftCode = shiftCode;
        schedule.ShiftName = shiftName;
        schedule.PlannedStartTime = startTime;
        schedule.PlannedEndTime = endTime;
        schedule.IsCheckInNextDay = isNextDay;

        if (request.DurationHours.HasValue)
        {
            schedule.DurationHours = request.DurationHours.Value;
        }
        else if (shiftConfig != null)
        {
            schedule.DurationHours = shiftConfig.DurationHours;
        }

        if (request.AllowedCheckInStart.HasValue)
        {
            schedule.AllowedCheckInStart = request.AllowedCheckInStart.Value;
        }
        else if (shiftConfig != null)
        {
            schedule.AllowedCheckInStart = shiftConfig.AllowedCheckInStart;
        }

        if (request.AllowedCheckInEnd.HasValue)
        {
            schedule.AllowedCheckInEnd = request.AllowedCheckInEnd.Value;
        }
        else if (shiftConfig != null)
        {
            schedule.AllowedCheckInEnd = shiftConfig.AllowedCheckInEnd;
        }

        if (request.Status.HasValue)
        {
            schedule.Status = request.Status.Value;
        }

        _scheduleRepository.Update(schedule);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        schedule.Employee = employee;
        return MapToResponse(schedule);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var schedule = await _scheduleRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException($"Lịch làm việc với ID {id} không tồn tại.");

        if (schedule.Status == ScheduleStatus.Completed)
        {
            throw new BusinessException("Không thể xóa ca làm việc đã hoàn thành.");
        }

        if (schedule.CheckInTime.HasValue)
        {
            throw new BusinessException("Không thể xóa ca làm việc đã có dữ liệu chấm công.");
        }

        _scheduleRepository.Delete(schedule);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<ScheduleResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var schedule = await _scheduleRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException($"Lịch làm việc với ID {id} không tồn tại.");

        return MapToResponse(schedule);
    }

    public async Task<IReadOnlyList<ScheduleResponse>> GetListAsync(
        ScheduleQueryFilter filter,
        CancellationToken cancellationToken = default)
    {
        var schedules = await _scheduleRepository.GetListAsync(filter, cancellationToken);
        return schedules.Select(MapToResponse).ToList();
    }

    public async Task<IReadOnlyList<ScheduleResponse>> GetMySchedulesAsync(
        string userId,
        DateTime? fromDate,
        DateTime? toDate,
        CancellationToken cancellationToken = default)
    {
        var employee = await _employeeRepository.GetByUserIdAsync(userId, cancellationToken);
        if (employee == null)
        {
            if (Guid.TryParse(userId, out var empId))
            {
                employee = await _employeeRepository.GetByIdAsync(empId, cancellationToken);
            }
        }

        if (employee == null)
        {
            return Array.Empty<ScheduleResponse>();
        }

        var filter = new ScheduleQueryFilter
        {
            EmployeeId = employee.Id,
            FromDate = fromDate,
            ToDate = toDate,
            Page = 1,
            PageSize = 500
        };

        var schedules = await _scheduleRepository.GetListAsync(filter, cancellationToken);
        return schedules.Select(MapToResponse).ToList();
    }

    public async Task<PublishSchedulesResponse> PublishAsync(
        PublishSchedulesRequest request,
        CancellationToken cancellationToken = default)
    {
        var fromDate = request.FromDate.Date;
        var toDate = request.ToDate.Date;

        var filter = new ScheduleQueryFilter
        {
            BranchId = request.BranchId,
            FromDate = fromDate,
            ToDate = toDate,
            Status = ScheduleStatus.Draft,
            Page = 1,
            PageSize = 1000
        };

        var schedules = await _scheduleRepository.GetListAsync(filter, cancellationToken);

        if (request.EmployeeIds != null && request.EmployeeIds.Count > 0)
        {
            var empIdSet = request.EmployeeIds.ToHashSet();
            schedules = schedules.Where(s => empIdSet.Contains(s.EmployeeId)).ToList();
        }

        int count = 0;
        foreach (var s in schedules)
        {
            s.Status = ScheduleStatus.Published;
            _scheduleRepository.Update(s);
            count++;
        }

        if (count > 0)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return new PublishSchedulesResponse(count, $"Đã ban hành thành công {count} ca làm việc.");
    }

    private static (string ShiftCode, string ShiftName, TimeSpan StartTime, TimeSpan EndTime, decimal DurationHours, TimeSpan AllowedCheckInStart, TimeSpan AllowedCheckInEnd, bool IsNextDay)
        ResolveShiftInfo(CreateScheduleItemRequest item, ShiftConfig? config)
    {
        if (config != null)
        {
            var code = !string.IsNullOrWhiteSpace(item.ShiftCode) ? item.ShiftCode : config.ShiftCode;
            var name = !string.IsNullOrWhiteSpace(item.ShiftName) ? item.ShiftName : config.ShiftName;
            var start = item.PlannedStartTime ?? config.StartTime;
            var end = item.PlannedEndTime ?? config.EndTime;
            var isNext = item.IsCheckInNextDay ?? config.IsCheckInNextDay;
            var dur = item.DurationHours ?? config.DurationHours;
            var inStart = item.AllowedCheckInStart ?? config.AllowedCheckInStart;
            var inEnd = item.AllowedCheckInEnd ?? config.AllowedCheckInEnd;
            return (code, name, start, end, dur, inStart, inEnd, isNext);
        }
        else
        {
            var code = !string.IsNullOrWhiteSpace(item.ShiftCode) ? item.ShiftCode : "CUSTOM";
            var name = !string.IsNullOrWhiteSpace(item.ShiftName) ? item.ShiftName : "Ca tùy chỉnh";
            var start = item.PlannedStartTime ?? TimeSpan.FromHours(8);
            var end = item.PlannedEndTime ?? TimeSpan.FromHours(17);
            var isNext = item.IsCheckInNextDay ?? (end <= start);
            var dur = item.DurationHours ?? (decimal)(isNext ? (end.Add(TimeSpan.FromDays(1)) - start).TotalHours : (end - start).TotalHours);
            var inStart = item.AllowedCheckInStart ?? (start >= TimeSpan.FromMinutes(15) ? start.Subtract(TimeSpan.FromMinutes(15)) : TimeSpan.Zero);
            var inEnd = item.AllowedCheckInEnd ?? start.Add(TimeSpan.FromMinutes(30));
            return (code, name, start, end, dur, inStart, inEnd, isNext);
        }
    }

    private static DateTime GetStartDateTime(DateTime workDate, TimeSpan startTime)
    {
        return workDate.Date + startTime;
    }

    private static DateTime GetEndDateTime(DateTime workDate, TimeSpan startTime, TimeSpan endTime, bool isNextDay)
    {
        return isNextDay || endTime <= startTime
            ? workDate.Date.AddDays(1) + endTime
            : workDate.Date + endTime;
    }

    private static bool IsOverlapping(DateTime startA, DateTime endA, DateTime startB, DateTime endB)
    {
        return startA < endB && endA > startB;
    }

    private static void CheckConflict(
        Employee employee,
        string newShiftName,
        DateTime newStart,
        DateTime newEnd,
        DateTime workDate,
        IEnumerable<EmployeeSchedule> existingSchedules)
    {
        foreach (var ex in existingSchedules)
        {
            if (ex.Status == ScheduleStatus.Cancelled) continue;

            var exStart = GetStartDateTime(ex.WorkDate, ex.PlannedStartTime);
            var exEnd = GetEndDateTime(ex.WorkDate, ex.PlannedStartTime, ex.PlannedEndTime, ex.IsCheckInNextDay);

            if (IsOverlapping(newStart, newEnd, exStart, exEnd))
            {
                throw new BusinessException(
                    $"Nhân viên {employee.FullName} ({employee.EmployeeCode}) bị trùng lặp thời gian làm việc ngày {workDate:dd/MM/yyyy} với ca đã xếp [{ex.ShiftName} {ex.PlannedStartTime:hh\\:mm}-{ex.PlannedEndTime:hh\\:mm}].");
            }
        }
    }

    private static ScheduleResponse MapToResponse(EmployeeSchedule s)
    {
        return new ScheduleResponse(
            s.Id,
            s.EmployeeId,
            s.Employee?.EmployeeCode ?? string.Empty,
            s.Employee?.FullName ?? string.Empty,
            s.Employee?.AvatarUrl,
            s.BranchId,
            s.Branch?.Name,
            s.ShiftConfigId,
            s.WorkDate,
            s.ShiftCode,
            s.ShiftName,
            s.PlannedStartTime,
            s.PlannedEndTime,
            s.DurationHours,
            s.AllowedCheckInStart,
            s.AllowedCheckInEnd,
            s.IsCheckInNextDay,
            s.CheckInTime,
            s.CheckOutTime,
            s.CheckInMethod,
            s.CheckInFaceImageUrl,
            s.FaceMatchScore,
            s.CheckOutMethod,
            s.CheckOutFaceImageUrl,
            s.LateMinutes,
            s.EarlyLeaveMinutes,
            s.OvertimeMinutes,
            s.ActualWorkHours,
            s.Status,
            s.ApprovalType,
            s.ApprovalStatus,
            s.ApprovalReason,
            s.EvidenceImageUrl,
            s.ApprovedBy,
            s.ApprovedAt,
            s.CreatedAt
        );
    }
}
