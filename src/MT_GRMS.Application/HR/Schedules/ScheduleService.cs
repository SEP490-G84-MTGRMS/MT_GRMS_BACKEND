using MT_GRMS.Application.Common;
using MT_GRMS.Application.HR.AttendancePolicies;
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
    private readonly IBranchAttendancePolicyRepository? _policyRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITenantContext _tenantContext;

    public ScheduleService(
        IScheduleRepository scheduleRepository,
        IEmployeeRepository employeeRepository,
        IShiftConfigRepository shiftConfigRepository,
        IUnitOfWork unitOfWork,
        ITenantContext tenantContext,
        IBranchAttendancePolicyRepository? policyRepository = null)
    {
        _scheduleRepository = scheduleRepository;
        _employeeRepository = employeeRepository;
        _shiftConfigRepository = shiftConfigRepository;
        _unitOfWork = unitOfWork;
        _tenantContext = tenantContext;
        _policyRepository = policyRepository;
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
        var batchIntervals = new List<(Guid EmployeeId, string ShiftName, DateTime Start, DateTime End, DateTime WorkDate)>();

        foreach (var item in schedulesToProcess)
        {
            var employee = employees[item.EmployeeId];
            ShiftConfig? shiftConfig = item.ShiftConfigId.HasValue ? shiftConfigs[item.ShiftConfigId.Value] : null;

            var resolved = ResolveShiftInfo(item, shiftConfig);
            var workDate = item.WorkDate.Date;
            var startDateTime = GetStartDateTime(workDate, resolved.StartTime);
            var endDateTime = GetEndDateTime(workDate, resolved.StartTime, resolved.EndTime, resolved.IsNextDay);

            // 1. Kiểm tra xung đột với nội bộ danh sách batch gửi lên (cho phép nhiều ca, miễn trùng không quá 12 giờ)
            var intraBatchOverlap = TimeSpan.Zero;
            foreach (var b in batchIntervals.Where(b => b.EmployeeId == item.EmployeeId && b.WorkDate >= workDate.AddDays(-1) && b.WorkDate <= workDate.AddDays(1)))
            {
                var overlap = CalculateOverlap(startDateTime, endDateTime, b.Start, b.End);
                if (overlap > TimeSpan.Zero)
                {
                    if (overlap.TotalHours > 12)
                    {
                        throw new BusinessException(
                            "SCHEDULE_OVERLAP_EXCEEDED",
                            $"Nhân viên {employee.FullName} ({employee.EmployeeCode}) bị trùng lặp thời gian làm việc vượt quá 12 giờ vào ngày {workDate:dd/MM/yyyy} ({overlap.TotalHours:0.#} giờ) giữa ca [{resolved.ShiftName} {resolved.StartTime:hh\\:mm}-{resolved.EndTime:hh\\:mm}] và ca [{b.ShiftName} {b.Start:HH:mm}-{b.End:HH:mm}].");
                    }
                    intraBatchOverlap += overlap;
                }
            }

            if (intraBatchOverlap.TotalHours > 12)
            {
                throw new BusinessException(
                    "SCHEDULE_OVERLAP_EXCEEDED",
                    $"Tổng thời gian trùng lặp các ca trong ngày {workDate:dd/MM/yyyy} của nhân viên {employee.FullName} ({employee.EmployeeCode}) trong danh sách phân ca vượt quá 12 giờ ({intraBatchOverlap.TotalHours:0.#} giờ).");
            }

            // 2. Kiểm tra xung đột với dữ liệu hiện có trong CSDL
            var empDbSchedules = existingSchedules.Where(s => s.EmployeeId == item.EmployeeId).ToList();
            CheckConflict(employee, resolved.ShiftName, startDateTime, endDateTime, workDate, empDbSchedules, intraBatchOverlap);

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
        string newShiftName,
        DateTime newStart,
        DateTime newEnd,
        DateTime workDate,
        IEnumerable<EmployeeSchedule> existingSchedules,
        TimeSpan existingOverlapOffset = default)
    {
        var totalOverlap = existingOverlapOffset;

        foreach (var ex in existingSchedules)
        {
            if (ex.Status == ScheduleStatus.Cancelled) continue;

            var exStart = GetStartDateTime(ex.WorkDate, ex.PlannedStartTime);
            var exEnd = GetEndDateTime(ex.WorkDate, ex.PlannedStartTime, ex.PlannedEndTime, ex.IsCheckInNextDay);

            var overlap = CalculateOverlap(newStart, newEnd, exStart, exEnd);
            if (overlap > TimeSpan.Zero)
            {
                if (overlap.TotalHours > 12)
                {
                    throw new BusinessException(
                        "SCHEDULE_OVERLAP_EXCEEDED",
                        $"Nhân viên {employee.FullName} ({employee.EmployeeCode}) có thời gian trùng giữa các ca ngày {workDate:dd/MM/yyyy} vượt quá 12 giờ ({overlap.TotalHours:0.#} giờ) giữa ca [{newShiftName} {newStart:HH:mm}-{newEnd:HH:mm}] và ca [{ex.ShiftName} {ex.PlannedStartTime:hh\\:mm}-{ex.PlannedEndTime:hh\\:mm}].");
                }

                totalOverlap += overlap;
            }
        }

        if (totalOverlap.TotalHours > 12)
        {
            throw new BusinessException(
                "SCHEDULE_OVERLAP_EXCEEDED",
                $"Tổng thời gian trùng lặp các ca trong ngày {workDate:dd/MM/yyyy} của nhân viên {employee.FullName} ({employee.EmployeeCode}) vượt quá 12 giờ ({totalOverlap.TotalHours:0.#} giờ).");
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
            s.CreatedAt
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
        schedule.ApprovedAt = DateTime.UtcNow;

        _scheduleRepository.Update(schedule);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (schedule.Employee == null)
        {
            schedule.Employee = await _employeeRepository.GetByIdAsync(schedule.EmployeeId, cancellationToken);
        }

        return MapToResponse(schedule);
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

        // Tính số phút làm thêm trước ca (OT Before)
        var otBefore = 0;
        if (checkInDateTime < plannedStartDateTime)
        {
            var rawBefore = (int)(plannedStartDateTime - checkInDateTime).TotalMinutes;
            if (policy != null)
            {
                if (policy.EnableOtBeforeShift && rawBefore >= policy.OtBeforeShiftMinutes)
                {
                    otBefore = rawBefore;
                }
            }
            else
            {
                otBefore = rawBefore;
            }
        }

        // Tính số phút làm thêm sau ca (OT After)
        var otAfter = 0;
        if (checkOutDateTime > plannedEndDateTime)
        {
            var rawAfter = (int)(checkOutDateTime - plannedEndDateTime).TotalMinutes;
            if (policy != null)
            {
                if (policy.EnableOtAfterShift && rawAfter >= policy.OtAfterShiftMinutes)
                {
                    otAfter = rawAfter;
                }
            }
            else
            {
                otAfter = rawAfter;
            }
        }

        var actualHours = Math.Max(0, Math.Round((decimal)(checkOutDateTime - checkInDateTime).TotalHours, 2));

        schedule.CheckInTime = checkInDateTime;
        schedule.CheckOutTime = checkOutDateTime;
        schedule.CheckInMethod = "MANUAL";
        schedule.CheckOutMethod = "MANUAL";
        schedule.LateMinutes = lateMinutes;
        schedule.EarlyLeaveMinutes = earlyLeaveMinutes;
        schedule.OvertimeMinutes = otBefore + otAfter;
        schedule.ActualWorkHours = actualHours;
        schedule.Status = ScheduleStatus.Completed;
        schedule.ApprovalType = "WORKING";
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
        schedule.Status = ScheduleStatus.Cancelled;
        schedule.ApprovalType = "UNPAID_LEAVE";
        schedule.ApprovalStatus = ScheduleApprovalStatus.Approved;
        schedule.ApprovalReason = request.Note;
    }
}
