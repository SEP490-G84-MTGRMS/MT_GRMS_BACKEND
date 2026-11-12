using MT_GRMS.Application.Common;
using MT_GRMS.Application.Common.Interfaces;
using MT_GRMS.Application.HR.AttendancePolicies;
using MT_GRMS.Application.HR.Employees;
using MT_GRMS.Application.HR.Payslips;
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
    private readonly IBranchAttendancePolicyRepository? _policyRepository;
    private readonly ICloudinaryService? _cloudinaryService;
    private readonly IPayslipRepository? _payslipRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITenantContext _tenantContext;

    public ScheduleService(
        IScheduleRepository scheduleRepository,
        IEmployeeRepository employeeRepository,
        IShiftConfigRepository shiftConfigRepository,
        IUnitOfWork unitOfWork,
        ITenantContext tenantContext,
        IBranchAttendancePolicyRepository? policyRepository = null,
        ICloudinaryService? cloudinaryService = null,
        IPayslipRepository? payslipRepository = null)
    {
        _scheduleRepository = scheduleRepository;
        _employeeRepository = employeeRepository;
        _shiftConfigRepository = shiftConfigRepository;
        _unitOfWork = unitOfWork;
        _tenantContext = tenantContext;
        _policyRepository = policyRepository;
        _cloudinaryService = cloudinaryService;
        _payslipRepository = payslipRepository;
    }

    public async Task<ScheduleResponse> CreateAsync(
        CreateScheduleItemRequest request,
        CancellationToken cancellationToken = default)
    {
        var employee = await _employeeRepository.GetByIdAsync(request.EmployeeId, cancellationToken)
            ?? throw new NotFoundException($"Nhân viên với ID {request.EmployeeId} không tồn tại.");

        if (employee.Status != EmployeeStatus.Active)
        {
            throw new BusinessException("EMPLOYEE_INACTIVE", $"Không thể phân ca cho nhân viên \"{employee.FullName}\" vì đã ngừng làm việc.");
        }

        if (employee.StartDate.HasValue && request.WorkDate.Date < employee.StartDate.Value.Date)
        {
            throw new BusinessException(
                "WORK_DATE_BEFORE_START_DATE",
                $"Không thể phân ca cho nhân viên \"{employee.FullName}\" vào ngày {request.WorkDate:dd/MM/yyyy} trước ngày bắt đầu làm việc ({employee.StartDate.Value:dd/MM/yyyy}).");
        }

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

        CheckConflict(employee, request.ShiftConfigId, resolved.ShiftName, startDateTime, endDateTime, resolved.DurationHours, workDate, existingSchedules);

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

        var minDate = request.Schedules.Min(s => s.WorkDate).Date;
        var maxDate = request.Schedules.Max(s => s.WorkDate).Date;

        var schedulesToProcess = request.Schedules.ToList();

        // Xử lý kiểm tra ngày kết thúc
        if (request.HasEndDate == true && request.EndDate.HasValue)
        {
            var endDate = request.EndDate.Value.Date;
            if (endDate < minDate)
            {
                throw new BusinessException("INVALID_DATE_RANGE", "Ngày kết thúc không được nhỏ hơn ngày bắt đầu.");
            }

            // Ngừng tạo lịch sau thời gian này (ngày = input)
            schedulesToProcess = schedulesToProcess.Where(s => s.WorkDate.Date <= endDate).ToList();
            if (schedulesToProcess.Count == 0)
            {
                return Array.Empty<ScheduleResponse>();
            }
        }

        var employeeIds = schedulesToProcess.Select(s => s.EmployeeId).Distinct().ToList();
        var employees = new Dictionary<Guid, Employee>();
        foreach (var empId in employeeIds)
        {
            var emp = await _employeeRepository.GetByIdAsync(empId, cancellationToken)
                ?? throw new NotFoundException($"Nhân viên với ID {empId} không tồn tại.");
            if (emp.Status != EmployeeStatus.Active)
            {
                throw new BusinessException("EMPLOYEE_INACTIVE", $"Không thể phân ca cho nhân viên \"{emp.FullName}\" vì đã ngừng làm việc.");
            }
            employees[empId] = emp;
        }

        var shiftConfigIds = schedulesToProcess
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

        var effectiveMinDate = schedulesToProcess.Min(s => s.WorkDate).Date;
        var effectiveMaxDate = schedulesToProcess.Max(s => s.WorkDate).Date;

        var existingSchedules = (await _scheduleRepository.GetActiveSchedulesForEmployeesAsync(
            employeeIds,
            effectiveMinDate.AddDays(-1),
            effectiveMaxDate.AddDays(1),
            cancellationToken)).ToList();

        var newSchedules = new List<EmployeeSchedule>();
        var batchIntervals = new List<(Guid EmployeeId, Guid? ShiftConfigId, string ShiftName, DateTime Start, DateTime End, DateTime WorkDate, decimal DurationHours)>();

        foreach (var item in schedulesToProcess)
        {
            var employee = employees[item.EmployeeId];
            if (employee.StartDate.HasValue && item.WorkDate.Date < employee.StartDate.Value.Date)
            {
                throw new BusinessException(
                    "WORK_DATE_BEFORE_START_DATE",
                    $"Không thể phân ca cho nhân viên \"{employee.FullName}\" vào ngày {item.WorkDate:dd/MM/yyyy} trước ngày bắt đầu làm việc ({employee.StartDate.Value:dd/MM/yyyy}).");
            }
            ShiftConfig? shiftConfig = item.ShiftConfigId.HasValue ? shiftConfigs[item.ShiftConfigId.Value] : null;

            var resolved = ResolveShiftInfo(item, shiftConfig);
            var workDate = item.WorkDate.Date;
            var startDateTime = GetStartDateTime(workDate, resolved.StartTime);
            var endDateTime = GetEndDateTime(workDate, resolved.StartTime, resolved.EndTime, resolved.IsNextDay);

            // 1. Kiểm tra xung đột với nội bộ danh sách batch gửi lên (không trùng ca, không chồng lấn giờ, tổng giờ <= 12h/ngày)
            foreach (var b in batchIntervals.Where(b => b.EmployeeId == item.EmployeeId && b.WorkDate >= workDate.AddDays(-1) && b.WorkDate <= workDate.AddDays(1)))
            {
                if (b.WorkDate == workDate && (
                    (item.ShiftConfigId.HasValue && b.ShiftConfigId == item.ShiftConfigId) ||
                    string.Equals(b.ShiftName.Trim(), resolved.ShiftName.Trim(), StringComparison.OrdinalIgnoreCase)))
                {
                    throw new BusinessException(
                        "SCHEDULE_DUPLICATE_SHIFT",
                        $"Nhân viên {employee.FullName} ({employee.EmployeeCode}) đã được phân ca [{resolved.ShiftName}] vào ngày {workDate:dd/MM/yyyy}, không thể thêm trùng cùng một ca.");
                }

                var overlap = CalculateOverlap(startDateTime, endDateTime, b.Start, b.End);
                if (overlap > TimeSpan.Zero)
                {
                    throw new BusinessException(
                        "SCHEDULE_TIME_OVERLAP",
                        $"Nhân viên {employee.FullName} ({employee.EmployeeCode}) bị trùng khung giờ làm việc vào ngày {workDate:dd/MM/yyyy} giữa ca [{resolved.ShiftName} {resolved.StartTime:hh\\:mm}-{resolved.EndTime:hh\\:mm}] và ca [{b.ShiftName} {b.Start:HH:mm}-{b.End:HH:mm}].");
                }
            }

            var intraBatchSameDayHours = batchIntervals
                .Where(b => b.EmployeeId == item.EmployeeId && b.WorkDate == workDate)
                .Sum(b => b.DurationHours);

            // 2. Kiểm tra xung đột với dữ liệu hiện có trong CSDL
            var empDbSchedules = existingSchedules.Where(s => s.EmployeeId == item.EmployeeId).ToList();
            CheckConflict(employee, item.ShiftConfigId, resolved.ShiftName, startDateTime, endDateTime, resolved.DurationHours, workDate, empDbSchedules, intraBatchSameDayHours);

            batchIntervals.Add((item.EmployeeId, item.ShiftConfigId, resolved.ShiftName, startDateTime, endDateTime, workDate, resolved.DurationHours));

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

    public async Task<IReadOnlyList<ScheduleResponse>> CreateRecurringAsync(
        CreateRecurringScheduleRequest request,
        CancellationToken cancellationToken = default)
    {
        var startDate = request.StartDate.Date;
        DateTime targetEndDate;

        if (request.HasEndDate && request.EndDate.HasValue)
        {
            // Nếu input != null thì sẽ tạo lịch đến ngày = input
            targetEndDate = request.EndDate.Value.Date;
            if (targetEndDate < startDate)
            {
                throw new BusinessException("INVALID_DATE_RANGE", "Ngày kết thúc không được nhỏ hơn ngày bắt đầu.");
            }
        }
        else
        {
            // Nếu input bằng null thì sẽ tự động tạo lịch đến hết tháng hiện tại và tháng sau
            var currentMonth = new DateTime(startDate.Year, startDate.Month, 1);
            var nextMonth = currentMonth.AddMonths(1);
            var daysInNextMonth = DateTime.DaysInMonth(nextMonth.Year, nextMonth.Month);
            targetEndDate = new DateTime(nextMonth.Year, nextMonth.Month, daysInNextMonth);
        }

        var repeatDays = ParseDaysOfWeek(request.RepeatDays);
        if (repeatDays.Count == 0)
        {
            repeatDays.Add(startDate.DayOfWeek);
        }

        var allEmpIds = new List<Guid> { request.EmployeeId };
        if (request.AdditionalEmployeeIds != null && request.AdditionalEmployeeIds.Count > 0)
        {
            allEmpIds.AddRange(request.AdditionalEmployeeIds.Where(id => id != request.EmployeeId).Distinct());
        }

        var scheduleItems = new List<CreateScheduleItemRequest>();
        for (var date = startDate; date <= targetEndDate; date = date.AddDays(1))
        {
            if (repeatDays.Contains(date.DayOfWeek))
            {
                foreach (var empId in allEmpIds)
                {
                    scheduleItems.Add(new CreateScheduleItemRequest(
                        empId,
                        request.BranchId,
                        date,
                        request.ShiftConfigId,
                        request.ShiftCode,
                        request.ShiftName,
                        request.PlannedStartTime,
                        request.PlannedEndTime,
                        request.DurationHours,
                        request.AllowedCheckInStart,
                        request.AllowedCheckInEnd,
                        request.IsCheckInNextDay
                    ));
                }
            }
        }

        if (scheduleItems.Count == 0)
        {
            return Array.Empty<ScheduleResponse>();
        }

        var batchRequest = new BatchCreateScheduleRequest(
            request.BranchId,
            scheduleItems,
            request.HasEndDate,
            request.EndDate
        );

        return await BatchCreateAsync(batchRequest, cancellationToken);
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
        if (employee.StartDate.HasValue && workDate < employee.StartDate.Value.Date)
        {
            throw new BusinessException(
                "WORK_DATE_BEFORE_START_DATE",
                $"Không thể phân ca cho nhân viên \"{employee.FullName}\" vào ngày {workDate:dd/MM/yyyy} trước ngày bắt đầu làm việc ({employee.StartDate.Value:dd/MM/yyyy}).");
        }
        var startTime = request.PlannedStartTime ?? (shiftConfig != null ? shiftConfig.StartTime : schedule.PlannedStartTime);
        var endTime = request.PlannedEndTime ?? (shiftConfig != null ? shiftConfig.EndTime : schedule.PlannedEndTime);
        var isNextDay = request.IsCheckInNextDay ?? (shiftConfig != null ? shiftConfig.IsCheckInNextDay : schedule.IsCheckInNextDay);
        var shiftName = request.ShiftName ?? (shiftConfig != null ? shiftConfig.ShiftName : schedule.ShiftName);
        var shiftCode = (shiftConfig != null && !string.IsNullOrWhiteSpace(shiftConfig.ShiftCode))
            ? shiftConfig.ShiftCode
            : (request.ShiftCode ?? schedule.ShiftCode);
        if (!string.IsNullOrEmpty(shiftCode) && shiftCode.Length > 30) shiftCode = shiftCode[..30];

        var startDateTime = GetStartDateTime(workDate, startTime);
        var endDateTime = GetEndDateTime(workDate, startTime, endTime, isNextDay);

        // Kiểm tra xung đột với các ca khác (ngoại trừ ca hiện tại)
        var existingSchedules = (await _scheduleRepository.GetActiveSchedulesForEmployeesAsync(
            new[] { employeeId },
            workDate.AddDays(-1),
            workDate.AddDays(1),
            cancellationToken)).Where(s => s.Id != id).ToList();

        var newDurationHours = request.DurationHours ?? shiftConfig?.DurationHours ?? (decimal)(endDateTime - startDateTime).TotalHours;
        CheckConflict(employee, shiftConfigId, shiftName, startDateTime, endDateTime, newDurationHours, workDate, existingSchedules);

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

        var isLeaveSchedule = schedule.IsLeave
            || schedule.Status == ScheduleStatus.OnLeave
            || string.Equals(schedule.ApprovalType, "PAID_LEAVE", StringComparison.OrdinalIgnoreCase)
            || string.Equals(schedule.ApprovalType, "UNPAID_LEAVE", StringComparison.OrdinalIgnoreCase)
            || string.Equals(schedule.CheckInMethod, "MANUAL_PAID_LEAVE", StringComparison.OrdinalIgnoreCase)
            || string.Equals(schedule.CheckInMethod, "MANUAL_UNPAID_LEAVE", StringComparison.OrdinalIgnoreCase);

        if (isLeaveSchedule)
        {
            throw new BusinessException("Không thể xóa ca làm việc đang ở trạng thái nghỉ phép (nghỉ phép có lương hoặc không lương).");
        }

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
            var code = (config != null && !string.IsNullOrWhiteSpace(config.ShiftCode))
                ? config.ShiftCode
                : (!string.IsNullOrWhiteSpace(item.ShiftCode) ? item.ShiftCode : "SHIFT");
            if (code.Length > 30) code = code[..30];
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
            if (code.Length > 30) code = code[..30];
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

    private static TimeSpan CalculateOverlap(DateTime startA, DateTime endA, DateTime startB, DateTime endB)
    {
        var overlapStart = startA > startB ? startA : startB;
        var overlapEnd = endA < endB ? endA : endB;
        if (overlapEnd > overlapStart)
        {
            return overlapEnd - overlapStart;
        }
        return TimeSpan.Zero;
    }

    private static void CheckConflict(
        Employee employee,
        Guid? newShiftConfigId,
        string newShiftName,
        DateTime newStart,
        DateTime newEnd,
        decimal newDurationHours,
        DateTime workDate,
        IEnumerable<EmployeeSchedule> existingSchedules,
        decimal existingDayHoursOffset = 0m)
    {
        var totalDayHours = existingDayHoursOffset + newDurationHours;

        foreach (var ex in existingSchedules)
        {
            if (ex.Status == ScheduleStatus.Cancelled) continue;

            // 1. Kiểm tra trùng lặp cùng ca trong cùng một ngày
            if (ex.WorkDate.Date == workDate.Date)
            {
                var isSameShift = (newShiftConfigId.HasValue && ex.ShiftConfigId.HasValue && ex.ShiftConfigId.Value == newShiftConfigId.Value) ||
                                  string.Equals((ex.ShiftName ?? string.Empty).Trim(), (newShiftName ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);

                if (isSameShift)
                {
                    throw new BusinessException(
                        "SCHEDULE_DUPLICATE_SHIFT",
                        $"Nhân viên {employee.FullName} ({employee.EmployeeCode}) đã có ca [{ex.ShiftName}] vào ngày {workDate:dd/MM/yyyy}, không thể phân trùng cùng một ca.");
                }

                var exHours = ex.DurationHours > 0
                    ? ex.DurationHours
                    : (decimal)(GetEndDateTime(ex.WorkDate, ex.PlannedStartTime, ex.PlannedEndTime, ex.IsCheckInNextDay) - GetStartDateTime(ex.WorkDate, ex.PlannedStartTime)).TotalHours;

                var exOtHours = (ex.OvertimeMinutes > 0 && ex.ApprovalStatus == ScheduleApprovalStatus.Approved)
                    ? Math.Round((decimal)ex.OvertimeMinutes / 60m, 2)
                    : 0m;

                totalDayHours += exHours + exOtHours;
            }

            // 2. Kiểm tra chồng lấn khung giờ làm việc giữa các ca
            var exStart = GetStartDateTime(ex.WorkDate, ex.PlannedStartTime);
            var exEnd = GetEndDateTime(ex.WorkDate, ex.PlannedStartTime, ex.PlannedEndTime, ex.IsCheckInNextDay);

            var overlap = CalculateOverlap(newStart, newEnd, exStart, exEnd);
            if (overlap > TimeSpan.Zero)
            {
                throw new BusinessException(
                    "SCHEDULE_TIME_OVERLAP",
                    $"Nhân viên {employee.FullName} ({employee.EmployeeCode}) bị trùng thời gian làm việc vào ngày {workDate:dd/MM/yyyy} giữa ca [{newShiftName} {newStart:HH:mm}-{newEnd:HH:mm}] và ca [{ex.ShiftName} {ex.PlannedStartTime:hh\\:mm}-{ex.PlannedEndTime:hh\\:mm}].");
            }
        }

        // 3. Kiểm tra tổng thời gian làm việc các ca trong ngày không vượt quá 12 giờ
        if (totalDayHours > 12m)
        {
            throw new BusinessException(
                "SCHEDULE_DAILY_HOURS_EXCEEDED",
                $"Tổng thời gian các ca trong ngày {workDate:dd/MM/yyyy} của nhân viên {employee.FullName} ({employee.EmployeeCode}) là {totalDayHours:0.#}h, vượt quá giới hạn tối đa 12h/ngày.");
        }
    }

    private static HashSet<DayOfWeek> ParseDaysOfWeek(IEnumerable<string>? days)
    {
        var result = new HashSet<DayOfWeek>();
        if (days == null) return result;

        foreach (var raw in days)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var d = raw.Trim().ToUpperInvariant();
            switch (d)
            {
                case "T2":
                case "THỨ 2":
                case "THU 2":
                case "MONDAY":
                case "MON":
                case "2":
                    result.Add(DayOfWeek.Monday);
                    break;
                case "T3":
                case "THỨ 3":
                case "THU 3":
                case "TUESDAY":
                case "TUE":
                case "3":
                    result.Add(DayOfWeek.Tuesday);
                    break;
                case "T4":
                case "THỨ 4":
                case "THU 4":
                case "WEDNESDAY":
                case "WED":
                case "4":
                    result.Add(DayOfWeek.Wednesday);
                    break;
                case "T5":
                case "THỨ 5":
                case "THU 5":
                case "THURSDAY":
                case "THU":
                case "5":
                    result.Add(DayOfWeek.Thursday);
                    break;
                case "T6":
                case "THỨ 6":
                case "THU 6":
                case "FRIDAY":
                case "FRI":
                case "6":
                    result.Add(DayOfWeek.Friday);
                    break;
                case "T7":
                case "THỨ 7":
                case "THU 7":
                case "SATURDAY":
                case "SAT":
                case "7":
                    result.Add(DayOfWeek.Saturday);
                    break;
                case "CN":
                case "CHỦ NHẬT":
                case "CHU NHAT":
                case "SUNDAY":
                case "SUN":
                case "8":
                case "0":
                case "CNHAT":
                    result.Add(DayOfWeek.Sunday);
                    break;
                default:
                    if (Enum.TryParse<DayOfWeek>(raw, true, out var parsed))
                    {
                        result.Add(parsed);
                    }
                    break;
            }
        }

        return result;
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
            s.CreatedAt,
            s.Employee?.UserId
        );
    }

    public async Task<ScheduleResponse> RecordManualAttendanceAsync(
        Guid id,
        ManualAttendanceRequest request,
        CancellationToken cancellationToken = default)
    {
        var schedule = await _scheduleRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException($"Lịch làm việc với ID {id} không tồn tại.");

        BranchAttendancePolicy? policy = null;
        if (_policyRepository != null)
        {
            policy = await _policyRepository.GetByBranchIdAsync(schedule.BranchId, cancellationToken);
        }

        switch (request.AttendanceType)
        {
            case ManualAttendanceType.Working:
                ApplyWorkingAttendance(schedule, request, policy);
                break;

            case ManualAttendanceType.PaidLeave:
                ApplyPaidLeaveAttendance(schedule, request);
                break;

            case ManualAttendanceType.UnpaidLeave:
                ApplyUnpaidLeaveAttendance(schedule, request);
                break;

            default:
                throw new BusinessException("INVALID_ATTENDANCE_TYPE", "Loại chấm công không hợp lệ.");
        }

        schedule.ApprovedBy = _tenantContext.CurrentUserId != Guid.Empty
            ? _tenantContext.CurrentUserId.ToString()
            : "Manager";
        schedule.ApprovedAt = DateTime.UtcNow.AddHours(7);

        _scheduleRepository.Update(schedule);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (schedule.Employee == null)
        {
            schedule.Employee = await _employeeRepository.GetByIdAsync(schedule.EmployeeId, cancellationToken);
        }

        return MapToResponse(schedule);
    }

    public async Task<ResetAttendanceResponse> ResetManualAttendanceAsync(
        Guid id,
        ResetAttendanceRequest? request,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(_tenantContext.CurrentRole)
            && !string.Equals(_tenantContext.CurrentRole, "STORE_OWNER", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(_tenantContext.CurrentRole, "ADMIN", StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessException("FORBIDDEN", "Chỉ Chủ cửa hàng hoặc Quản trị viên mới có quyền hủy chấm công.");
        }

        var schedule = await _scheduleRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException($"Lịch làm việc với ID {id} không tồn tại.");

        // 1. Kiểm tra khóa bảng lương: chặn hủy nếu ca hoặc kỳ lương tháng của nhân viên đã thanh toán (Paid/Partial)
        if (schedule.Payslip != null && schedule.Payslip.Status is PayslipStatus.Paid or PayslipStatus.Partial)
        {
            throw new BusinessException(
                "PAYSLIP_LOCKED",
                "Không thể hủy chấm công vì ca làm việc này đã được chốt và thanh toán trong phiếu lương.");
        }

        var periodMonth = schedule.WorkDate.ToString("yyyy-MM");
        var periodPayslip = _payslipRepository != null
            ? await _payslipRepository.GetByEmployeeAndPeriodAsync(schedule.EmployeeId, periodMonth, cancellationToken)
            : null;

        if (periodPayslip != null && periodPayslip.Status is PayslipStatus.Paid or PayslipStatus.Partial)
        {
            throw new BusinessException(
                "PAYSLIP_LOCKED",
                $"Không thể hủy chấm công vì phiếu lương tháng {schedule.WorkDate:MM/yyyy} của nhân viên đã được thanh toán.");
        }

        var hasUnpaidPayslip = (periodPayslip != null && periodPayslip.Status == PayslipStatus.Unpaid)
            || (schedule.Payslip != null && schedule.Payslip.Status == PayslipStatus.Unpaid);

        // 2. Chặn hủy nếu đang có phiếu yêu cầu chờ duyệt gắn với ca làm việc này
        if (schedule.ApprovalRequests.Any(r => !r.IsDeleted && r.Status == ScheduleApprovalStatus.Pending))
        {
            throw new BusinessException(
                "PENDING_APPROVAL_REQUESTS",
                "Ca làm việc đang có phiếu yêu cầu chờ duyệt. Vui lòng xử lý phiếu yêu cầu trước khi hủy chấm công.");
        }

        // 3. Chặn hủy nếu ca làm việc chưa có dữ liệu chấm công
        var hasAttendanceData = schedule.CheckInTime.HasValue
            || schedule.CheckOutTime.HasValue
            || schedule.IsLeave
            || string.Equals(schedule.ApprovalType, "PAID_LEAVE", StringComparison.OrdinalIgnoreCase)
            || string.Equals(schedule.ApprovalType, "UNPAID_LEAVE", StringComparison.OrdinalIgnoreCase)
            || schedule.Status is ScheduleStatus.InProgress or ScheduleStatus.Completed or ScheduleStatus.Cancelled or ScheduleStatus.OnLeave;

        if (!hasAttendanceData)
        {
            throw new BusinessException(
                "NO_ATTENDANCE_DATA",
                "Ca làm việc chưa có dữ liệu chấm công để hủy.");
        }

        // 4. Kiểm tra lý do hủy chấm công (bắt buộc nếu đã điểm danh bằng FaceID)
        var trimmedReason = request?.Reason?.Trim();
        if (!string.IsNullOrEmpty(trimmedReason) && trimmedReason.Length > 255)
        {
            throw new BusinessException(
                "INVALID_RESET_REASON",
                "Lý do hủy chấm công tối đa 255 ký tự.");
        }

        var isFaceIdAttendance = string.Equals(schedule.CheckInMethod, "FACE_ID", StringComparison.OrdinalIgnoreCase)
            || string.Equals(schedule.CheckOutMethod, "FACE_ID", StringComparison.OrdinalIgnoreCase);

        if (isFaceIdAttendance && string.IsNullOrWhiteSpace(trimmedReason))
        {
            throw new BusinessException(
                "RESET_REASON_REQUIRED",
                "Ca làm việc đã chấm công bằng FaceID. Vui lòng nhập lý do khi hủy chấm công.");
        }

        // 5. Reset dữ liệu chấm công và giữ nguyên thông tin ca phân bổ
        var vnNow = DateTime.UtcNow.AddHours(7);
        var actor = _tenantContext.CurrentUserId != Guid.Empty
            ? _tenantContext.CurrentUserId.ToString()
            : "Manager";

        schedule.Status = ScheduleStatus.Published;
        schedule.CheckInTime = null;
        schedule.CheckOutTime = null;
        schedule.CheckInMethod = null;
        schedule.CheckOutMethod = null;
        schedule.CheckInFaceImageUrl = null;
        schedule.CheckOutFaceImageUrl = null;
        schedule.FaceMatchScore = null;
        schedule.LateMinutes = 0;
        schedule.EarlyLeaveMinutes = 0;
        schedule.OvertimeMinutes = 0;
        schedule.ActualWorkHours = 0.00m;
        schedule.IsLeave = false;
        schedule.IsPaidLeave = false;
        schedule.ApprovalType = null;
        schedule.ApprovalStatus = ScheduleApprovalStatus.None;
        schedule.EvidenceImageUrl = null;
        schedule.PayslipId = null;
        schedule.ApprovalReason = string.IsNullOrWhiteSpace(trimmedReason) ? null : trimmedReason;
        schedule.ApprovedBy = string.IsNullOrWhiteSpace(trimmedReason) ? null : actor;
        schedule.ApprovedAt = string.IsNullOrWhiteSpace(trimmedReason) ? null : vnNow;
        schedule.UpdatedAt = vnNow;

        _scheduleRepository.Update(schedule);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (schedule.Employee == null)
        {
            schedule.Employee = await _employeeRepository.GetByIdAsync(schedule.EmployeeId, cancellationToken);
        }

        var message = hasUnpaidPayslip
            ? $"Đã hủy chấm công. Lưu ý: nhân viên đang có phiếu lương tháng {schedule.WorkDate:MM/yyyy} ở trạng thái chưa thanh toán, vui lòng tính lại lương để cập nhật công."
            : "Đã hủy chấm công và đưa ca làm việc về trạng thái chờ vào ca.";

        return new ResetAttendanceResponse(MapToResponse(schedule), hasUnpaidPayslip, message);
    }

    private static void ApplyWorkingAttendance(
        EmployeeSchedule schedule,
        ManualAttendanceRequest request,
        BranchAttendancePolicy? policy)
    {
        var inTime = request.CheckInTime ?? schedule.PlannedStartTime;
        var outTime = request.CheckOutTime ?? schedule.PlannedEndTime;

        var workDate = schedule.WorkDate.Date;
        var checkInDateTime = workDate.Add(inTime);
        var checkOutDateTime = workDate.Add(outTime);

        if (schedule.IsCheckInNextDay || outTime < inTime)
        {
            checkOutDateTime = checkOutDateTime.AddDays(1);
        }

        if (checkOutDateTime < checkInDateTime)
        {
            throw new BusinessException("INVALID_TIME_RANGE", "Thời gian ra ca không thể trước thời gian vào ca.");
        }

        var plannedStartDateTime = workDate.Add(schedule.PlannedStartTime);
        var plannedEndDateTime = workDate.Add(schedule.PlannedEndTime);
        if (schedule.IsCheckInNextDay || schedule.PlannedEndTime < schedule.PlannedStartTime)
        {
            plannedEndDateTime = plannedEndDateTime.AddDays(1);
        }

        // Tính số phút đi muộn
        var lateMinutes = 0;
        if (checkInDateTime > plannedStartDateTime)
        {
            var rawLate = (int)(checkInDateTime - plannedStartDateTime).TotalMinutes;
            if (policy != null)
            {
                if (policy.EnableLateThreshold && rawLate > policy.LateThresholdMinutes)
                {
                    lateMinutes = rawLate;
                }
            }
            else
            {
                lateMinutes = rawLate;
            }
        }

        // Tính số phút về sớm
        var earlyLeaveMinutes = 0;
        if (checkOutDateTime < plannedEndDateTime)
        {
            var rawEarly = (int)(plannedEndDateTime - checkOutDateTime).TotalMinutes;
            if (policy != null)
            {
                if (policy.EnableEarlyLeaveThreshold && rawEarly > policy.EarlyLeaveThresholdMinutes)
                {
                    earlyLeaveMinutes = rawEarly;
                }
            }
            else
            {
                earlyLeaveMinutes = rawEarly;
            }
        }

        // Thời gian làm việc thực tế trong ca chỉ tính trong khung ca kế hoạch (không cộng dồn giờ OT vào ActualWorkHours để tránh tính trùng lương chính + lương OT)
        var effectiveStart = checkInDateTime < plannedStartDateTime ? plannedStartDateTime : checkInDateTime;
        var effectiveEnd = checkOutDateTime > plannedEndDateTime ? plannedEndDateTime : checkOutDateTime;
        var inShiftMinutes = Math.Max(0, (int)(effectiveEnd - effectiveStart).TotalMinutes);
        var inShiftHours = Math.Round((decimal)inShiftMinutes / 60m, 2);

        // Không tự tiện tính OT khi check-in sớm hoặc về muộn khi chưa có đơn xin OT được duyệt (ScheduleRequestType.Overtime).
        // Chỉ khi có đơn OT được Chủ cửa hàng phê duyệt thì thời gian đi sớm / về muộn dôi ra ngoài khung ca mới được tính OT, và tổng Giờ làm + OT không vượt quá 12h (720 phút)/ngày.
        var earlyExtraMinutes = checkInDateTime < plannedStartDateTime ? (int)(plannedStartDateTime - checkInDateTime).TotalMinutes : 0;
        var lateExtraMinutes = checkOutDateTime > plannedEndDateTime ? (int)(checkOutDateTime - plannedEndDateTime).TotalMinutes : 0;
        var extraOutsideMinutes = Math.Max(0, earlyExtraMinutes + lateExtraMinutes);

        var approvedOtMinutes = 0;
        if (schedule.ApprovalType == "OVERTIME" || (schedule.ApprovalStatus == ScheduleApprovalStatus.Approved && schedule.OvertimeMinutes > 0))
        {
            if (schedule.OvertimeMinutes > 0)
            {
                approvedOtMinutes = extraOutsideMinutes > 0
                    ? Math.Min(schedule.OvertimeMinutes, extraOutsideMinutes)
                    : schedule.OvertimeMinutes;
            }
            else
            {
                approvedOtMinutes = extraOutsideMinutes;
            }

            var maxAllowedOtMinutes = Math.Max(0, 720 - inShiftMinutes);
            approvedOtMinutes = Math.Min(approvedOtMinutes, maxAllowedOtMinutes);
        }

        schedule.CheckInTime = checkInDateTime;
        schedule.CheckOutTime = checkOutDateTime;
        schedule.CheckInMethod = "MANUAL";
        schedule.CheckOutMethod = "MANUAL";
        schedule.LateMinutes = lateMinutes;
        schedule.EarlyLeaveMinutes = earlyLeaveMinutes;
        schedule.OvertimeMinutes = approvedOtMinutes;
        schedule.ActualWorkHours = inShiftHours;
        schedule.Status = ScheduleStatus.Completed;
        if (schedule.ApprovalType != "OVERTIME")
        {
            schedule.ApprovalType = "WORKING";
        }
        schedule.ApprovalStatus = ScheduleApprovalStatus.Approved;
        schedule.ApprovalReason = request.Note;
    }

    private static void ApplyPaidLeaveAttendance(
        EmployeeSchedule schedule,
        ManualAttendanceRequest request)
    {
        schedule.CheckInTime = null;
        schedule.CheckOutTime = null;
        schedule.CheckInMethod = "MANUAL_PAID_LEAVE";
        schedule.CheckOutMethod = "MANUAL_PAID_LEAVE";
        schedule.LateMinutes = 0;
        schedule.EarlyLeaveMinutes = 0;
        schedule.OvertimeMinutes = 0;
        schedule.ActualWorkHours = schedule.DurationHours;
        schedule.IsLeave = true;
        schedule.IsPaidLeave = true;
        schedule.Status = ScheduleStatus.Completed;
        schedule.ApprovalType = "PAID_LEAVE";
        schedule.ApprovalStatus = ScheduleApprovalStatus.Approved;
        schedule.ApprovalReason = request.Note;
    }

    private static void ApplyUnpaidLeaveAttendance(
        EmployeeSchedule schedule,
        ManualAttendanceRequest request)
    {
        schedule.CheckInTime = null;
        schedule.CheckOutTime = null;
        schedule.CheckInMethod = "MANUAL_UNPAID_LEAVE";
        schedule.CheckOutMethod = "MANUAL_UNPAID_LEAVE";
        schedule.LateMinutes = 0;
        schedule.EarlyLeaveMinutes = 0;
        schedule.OvertimeMinutes = 0;
        schedule.ActualWorkHours = 0.00m;
        schedule.IsLeave = true;
        schedule.IsPaidLeave = false;
        schedule.Status = ScheduleStatus.Cancelled;
        schedule.ApprovalType = "UNPAID_LEAVE";
        schedule.ApprovalStatus = ScheduleApprovalStatus.Approved;
        schedule.ApprovalReason = request.Note;
    }

    public async Task<IReadOnlyList<TodayStaffDescriptorDto>> GetTodayStaffDescriptorsAsync(
        Guid branchId,
        CancellationToken cancellationToken = default)
    {
        var vnNow = DateTime.UtcNow.AddHours(7);
        var today = vnNow.Date;
        var yesterday = today.AddDays(-1);

        var filter = new ScheduleQueryFilter
        {
            BranchId = branchId,
            FromDate = yesterday,
            ToDate = today,
            PageSize = 200
        };

        var schedules = await _scheduleRepository.GetListAsync(filter, cancellationToken);
        var employees = await _employeeRepository.GetActiveEmployeesAsync(branchId, cancellationToken);

        var scheduleDict = schedules
            .Where(s => s.Status != ScheduleStatus.Cancelled)
            .OrderByDescending(s => s.WorkDate)
            .ThenBy(s => s.PlannedStartTime)
            .GroupBy(s => s.EmployeeId)
            .ToDictionary(g => g.Key, g => g.First());

        var result = employees
            .Where(e => !string.IsNullOrWhiteSpace(e.FaceDescriptor))
            .Select(e =>
            {
                scheduleDict.TryGetValue(e.Id, out var sch);
                return new TodayStaffDescriptorDto(
                    e.Id,
                    e.EmployeeCode,
                    e.FullName,
                    e.AvatarUrl,
                    e.FaceDescriptor,
                    sch?.Id,
                    sch?.ShiftCode,
                    sch?.ShiftName,
                    sch?.PlannedStartTime,
                    sch?.PlannedEndTime,
                    sch?.CheckInTime,
                    sch?.CheckOutTime,
                    sch?.Status.ToString(),
                    e.FaceImageUrls
                );
            })
            .ToList();

        return result;
    }

    public async Task<ScheduleResponse> CheckInFaceIdAsync(
        FaceIdCheckInRequest request,
        CancellationToken cancellationToken = default)
    {
        var schedule = await _scheduleRepository.GetByIdAsync(request.ScheduleId, cancellationToken);
        if (schedule == null)
        {
            throw new NotFoundException($"Không tìm thấy ca làm việc với ID '{request.ScheduleId}'.");
        }

        if (schedule.EmployeeId != request.EmployeeId)
        {
            throw new BusinessException("INVALID_EMPLOYEE", "Nhân viên nhận diện không trùng khớp với ca làm việc.");
        }

        if (schedule.CheckInTime.HasValue)
        {
            throw new BusinessException("ALREADY_CHECKED_IN", "Ca làm việc này đã được điểm danh vào trước đó.");
        }

        if (request.FaceMatchScore > 0.45m)
        {
            throw new BusinessException("FACE_MATCH_FAILED", $"Khoảng cách nhận diện khuôn mặt ({request.FaceMatchScore:F2}) vượt quá ngưỡng khoảng cách cho phép (tối đa 0.45).");
        }

        string? snapshotUrl = null;
        if (!string.IsNullOrWhiteSpace(request.SnapshotBase64))
        {
            snapshotUrl = await UploadSnapshotAsync(request.SnapshotBase64, $"checkin_{schedule.EmployeeId}", cancellationToken);
        }

        var vnNow = DateTime.UtcNow.AddHours(7);
        var workDate = schedule.WorkDate;
        var plannedStartDateTime = workDate.Add(schedule.PlannedStartTime);

        var lateMinutes = 0;
        BranchAttendancePolicy? policy = null;
        if (_policyRepository != null)
        {
            policy = await _policyRepository.GetByBranchIdAsync(schedule.BranchId, cancellationToken);
        }

        if (vnNow > plannedStartDateTime)
        {
            var diff = (int)(vnNow - plannedStartDateTime).TotalMinutes;
            if (policy != null)
            {
                if (policy.EnableLateThreshold && diff > policy.LateThresholdMinutes)
                {
                    lateMinutes = diff;
                }
            }
            else
            {
                lateMinutes = diff;
            }
        }

        schedule.CheckInTime = vnNow;
        schedule.CheckInMethod = "FACE_ID";
        schedule.CheckInFaceImageUrl = snapshotUrl;
        schedule.FaceMatchScore = request.FaceMatchScore;
        schedule.LateMinutes = lateMinutes;
        schedule.Status = ScheduleStatus.InProgress;
        schedule.UpdatedAt = vnNow;

        _scheduleRepository.Update(schedule);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return MapToResponse(schedule);
    }

    public async Task<ScheduleResponse> CheckOutFaceIdAsync(
        FaceIdCheckOutRequest request,
        CancellationToken cancellationToken = default)
    {
        var schedule = await _scheduleRepository.GetByIdAsync(request.ScheduleId, cancellationToken);
        if (schedule == null)
        {
            throw new NotFoundException($"Không tìm thấy ca làm việc với ID '{request.ScheduleId}'.");
        }

        if (schedule.EmployeeId != request.EmployeeId)
        {
            throw new BusinessException("INVALID_EMPLOYEE", "Nhân viên nhận diện không trùng khớp với ca làm việc.");
        }

        if (!schedule.CheckInTime.HasValue)
        {
            throw new BusinessException("NOT_CHECKED_IN", "Ca làm việc chưa được điểm danh vào. Không thể điểm danh ra.");
        }

        if (schedule.CheckOutTime.HasValue)
        {
            throw new BusinessException("ALREADY_CHECKED_OUT", "Ca làm việc này đã được điểm danh ra trước đó.");
        }

        if (request.FaceMatchScore > 0.45m)
        {
            throw new BusinessException("FACE_MATCH_FAILED", $"Khoảng cách nhận diện khuôn mặt ({request.FaceMatchScore:F2}) vượt quá ngưỡng khoảng cách cho phép (tối đa 0.45).");
        }

        string? snapshotUrl = null;
        if (!string.IsNullOrWhiteSpace(request.SnapshotBase64))
        {
            snapshotUrl = await UploadSnapshotAsync(request.SnapshotBase64, $"checkout_{schedule.EmployeeId}", cancellationToken);
        }

        var vnNow = DateTime.UtcNow.AddHours(7);
        var workDate = schedule.WorkDate;
        var plannedStartDateTime = workDate.Add(schedule.PlannedStartTime);
        var isNextDay = schedule.IsCheckInNextDay || schedule.PlannedEndTime < schedule.PlannedStartTime;
        var plannedEndDateTime = isNextDay ? workDate.AddDays(1).Add(schedule.PlannedEndTime) : workDate.Add(schedule.PlannedEndTime);

        BranchAttendancePolicy? policy = null;
        if (_policyRepository != null)
        {
            policy = await _policyRepository.GetByBranchIdAsync(schedule.BranchId, cancellationToken);
        }

        var earlyLeaveMinutes = 0;
        if (vnNow < plannedEndDateTime)
        {
            var diff = (int)(plannedEndDateTime - vnNow).TotalMinutes;
            if (policy != null)
            {
                if (policy.EnableEarlyLeaveThreshold && diff > policy.EarlyLeaveThresholdMinutes)
                {
                    earlyLeaveMinutes = diff;
                }
            }
            else
            {
                earlyLeaveMinutes = diff;
            }
        }

        // Tính giờ làm việc thực tế chuẩn trong khung ca (không cộng dồn giờ OT vào ActualWorkHours để tránh tính trùng lương chính + lương OT)
        // CheckInTime đã được lưu theo giờ UTC+7
        var checkInVn = schedule.CheckInTime.Value;
        var effectiveStart = checkInVn < plannedStartDateTime ? plannedStartDateTime : checkInVn;
        var effectiveEnd = vnNow > plannedEndDateTime ? plannedEndDateTime : vnNow;
        var inShiftMinutes = Math.Max(0, (int)(effectiveEnd - effectiveStart).TotalMinutes);
        var inShiftHours = Math.Round((decimal)inShiftMinutes / 60m, 2);

        // Tăng ca (OT) bắt buộc phải có đơn xin OT được phê duyệt (ScheduleRequestType.Overtime).
        // Chỉ khi có đơn OT được Chủ cửa hàng phê duyệt thì thời gian đi sớm / về muộn dôi ra ngoài khung ca mới được tính OT, và tổng Giờ làm + OT không vượt quá 12h (720 phút)/ngày.
        var earlyExtraMinutes = checkInVn < plannedStartDateTime ? (int)(plannedStartDateTime - checkInVn).TotalMinutes : 0;
        var lateExtraMinutes = vnNow > plannedEndDateTime ? (int)(vnNow - plannedEndDateTime).TotalMinutes : 0;
        var extraOutsideMinutes = Math.Max(0, earlyExtraMinutes + lateExtraMinutes);

        var approvedOtMinutes = 0;
        if (schedule.ApprovalType == "OVERTIME" || (schedule.ApprovalStatus == ScheduleApprovalStatus.Approved && schedule.OvertimeMinutes > 0))
        {
            if (schedule.OvertimeMinutes > 0)
            {
                approvedOtMinutes = extraOutsideMinutes > 0
                    ? Math.Min(schedule.OvertimeMinutes, extraOutsideMinutes)
                    : schedule.OvertimeMinutes;
            }
            else
            {
                approvedOtMinutes = extraOutsideMinutes;
            }

            var maxAllowedOtMinutes = Math.Max(0, 720 - inShiftMinutes);
            approvedOtMinutes = Math.Min(approvedOtMinutes, maxAllowedOtMinutes);
        }

        schedule.CheckOutTime = vnNow;
        schedule.CheckOutMethod = "FACE_ID";
        schedule.CheckOutFaceImageUrl = snapshotUrl;
        schedule.FaceMatchScore = request.FaceMatchScore;
        schedule.EarlyLeaveMinutes = earlyLeaveMinutes;
        schedule.OvertimeMinutes = approvedOtMinutes;
        schedule.ActualWorkHours = inShiftHours;
        schedule.Status = ScheduleStatus.Completed;
        schedule.UpdatedAt = vnNow;

        _scheduleRepository.Update(schedule);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return MapToResponse(schedule);
    }

    private async Task<string?> UploadSnapshotAsync(string? base64Data, string prefix, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(base64Data) || _cloudinaryService == null)
            return null;

        try
        {
            var cleanBase64 = base64Data;
            var commaIdx = cleanBase64.IndexOf(',');
            if (commaIdx >= 0)
            {
                cleanBase64 = cleanBase64.Substring(commaIdx + 1);
            }

            var bytes = Convert.FromBase64String(cleanBase64);
            using var stream = new MemoryStream(bytes);
            var fileName = $"{prefix}_{Guid.NewGuid():N}.jpg";
            var result = await _cloudinaryService.UploadImageAsync(stream, fileName, "mtgrms/attendance", cancellationToken);
            return result?.Url;
        }
        catch
        {
            return null;
        }
    }
}
