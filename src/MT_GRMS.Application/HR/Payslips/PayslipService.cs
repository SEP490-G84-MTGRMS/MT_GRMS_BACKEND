using System.Text.Json;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.HR.AttendancePolicies;
using MT_GRMS.Application.HR.Employees;
using MT_GRMS.Application.HR.Payslips.Dtos;
using MT_GRMS.Application.HR.Schedules;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Entities.HR;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Domain.Exceptions;

namespace MT_GRMS.Application.HR.Payslips;

public class PayslipService : IPayslipService
{
    private const string DefaultConversionRulesJson =
        "[{\"id\":\"rule-1\",\"type\":\"range\",\"fromHours\":1,\"fromMinutes\":0,\"toHours\":4,\"toMinutes\":30,\"workUnits\":0.5,\"workUnitsLabel\":\"nửa công\"},{\"id\":\"rule-2\",\"type\":\"gte\",\"fromHours\":4,\"fromMinutes\":30,\"workUnits\":1,\"workUnitsLabel\":\"1 công\"}]";

    private static DateTime VnNow => DateTime.UtcNow.AddHours(7);
    private readonly IPayslipRepository _payslipRepository;
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IScheduleRepository _scheduleRepository;
    private readonly IBranchAttendancePolicyRepository _attendancePolicyRepository;
    private readonly ITenantContext _tenantContext;
    private readonly IUnitOfWork _unitOfWork;

    public PayslipService(
        IPayslipRepository payslipRepository,
        IEmployeeRepository employeeRepository,
        IScheduleRepository scheduleRepository,
        IBranchAttendancePolicyRepository attendancePolicyRepository,
        ITenantContext tenantContext,
        IUnitOfWork unitOfWork)
    {
        _payslipRepository = payslipRepository;
        _employeeRepository = employeeRepository;
        _scheduleRepository = scheduleRepository;
        _attendancePolicyRepository = attendancePolicyRepository;
        _tenantContext = tenantContext;
        _unitOfWork = unitOfWork;
    }

    public async Task<IReadOnlyList<PayslipResponse>> GenerateMonthlyPayrollAsync(
        GeneratePayrollBatchRequest request,
        CancellationToken cancellationToken = default)
    {
        var parts = request.PeriodMonth.Split('-');
        if (parts.Length != 2 || !int.TryParse(parts[0], out var year) || !int.TryParse(parts[1], out var month))
        {
            throw new BusinessException("Định dạng kỳ tháng PeriodMonth không hợp lệ. Vui lòng nhập yyyy-MM (VD: 2026-11).");
        }

        var startDate = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
        var endDate = startDate.AddMonths(1).AddTicks(-1);

        var employees = await _employeeRepository.GetActiveEmployeesAsync(request.BranchId, cancellationToken);
        if (request.SalaryTypeFilter.HasValue)
        {
            employees = employees.Where(e => e.SalaryType == request.SalaryTypeFilter.Value).ToList();
        }

        if (employees.Count == 0)
        {
            return Array.Empty<PayslipResponse>();
        }

        var employeeIds = employees.Select(e => e.Id).ToList();

        var activeSchedulesInMonth = await _scheduleRepository.GetActiveSchedulesForEmployeesAsync(
            employeeIds, startDate, endDate, cancellationToken);
        var employeeIdsWithSchedules = activeSchedulesInMonth
            .Select(s => s.EmployeeId)
            .ToHashSet();

        var schedules = await _scheduleRepository.GetCompletedSchedulesInPeriodAsync(
            employeeIds, startDate, endDate, cancellationToken);
        foreach (var s in schedules)
        {
            employeeIdsWithSchedules.Add(s.EmployeeId);
        }

        var existingPayslips = await _payslipRepository.GetByPeriodAndBranchAsync(request.PeriodMonth, request.BranchId, cancellationToken);
        var existingDict = existingPayslips.ToDictionary(p => p.EmployeeId);

        if (employeeIdsWithSchedules.Count == 0 && !existingPayslips.Any(p => p.Status == PayslipStatus.Paid))
        {
            throw new BusinessException(
                "NO_SCHEDULES_IN_PERIOD",
                $"Không có nhân viên nào có ca làm việc trong kỳ tháng {month:D2}/{year} để khởi tạo hoặc tính lại bảng lương.");
        }

        var payslipCountInMonth = await _payslipRepository.GetCountByPeriodMonthAsync(request.PeriodMonth, cancellationToken);
        var resultList = new List<Payslip>();
        var periodMonthShort = request.PeriodMonth.Replace("-", "").Substring(2);

        foreach (var emp in employees)
        {
            var hasScheduleInPeriod = employeeIdsWithSchedules.Contains(emp.Id);

            if (existingDict.TryGetValue(emp.Id, out var existingPayslip))
            {
                if (existingPayslip.Status == PayslipStatus.Paid)
                {
                    resultList.Add(existingPayslip);
                    continue;
                }

                // Nếu nhân viên không có bất kỳ ca làm việc nào trong kỳ tháng này thì xóa bỏ phiếu lương chưa thanh toán bị dư (nếu có) và bỏ qua
                if (!hasScheduleInPeriod)
                {
                    _payslipRepository.Delete(existingPayslip);
                    continue;
                }
            }
            else if (!hasScheduleInPeriod)
            {
                // Chỉ khởi tạo / tính lại bảng lương cho những nhân viên có ca làm việc trong kỳ tháng
                continue;
            }

            var empSchedules = schedules.Where(s => s.EmployeeId == emp.Id).ToList();

            var policy = await _attendancePolicyRepository.GetByBranchIdAsync(emp.BranchId, cancellationToken);
            var standardDayHours = policy?.StandardDayHours ?? 8.00m;
            if (standardDayHours <= 0) standardDayHours = 8.00m;

            var standardDays = 26.00m;

            var workingSchedules = empSchedules
                .Where(s => s.Status == ScheduleStatus.Completed && !IsPaidLeaveSchedule(s) && !IsUnpaidLeaveSchedule(s))
                .ToList();
            var paidLeaveSchedules = empSchedules
                .Where(s => IsPaidLeaveSchedule(s) && s.ApprovalStatus == ScheduleApprovalStatus.Approved)
                .ToList();
            var unpaidLeaveSchedules = empSchedules
                .Where(s => IsUnpaidLeaveSchedule(s) && s.ApprovalStatus == ScheduleApprovalStatus.Approved)
                .ToList();

            var totalWorkHours = workingSchedules.Sum(GetInShiftWorkHours);
            var totalShifts = workingSchedules.Count;
            var workedDays = CalculateWorkedDays(workingSchedules, totalWorkHours, standardDayHours, policy);
            var paidLeaveDays = (decimal)paidLeaveSchedules.Count;
            var unpaidLeaveDays = (decimal)unpaidLeaveSchedules.Count;
            var totalLateMinutes = workingSchedules.Sum(s => s.LateMinutes);
            var totalEarlyLeaveMinutes = workingSchedules.Sum(s => s.EarlyLeaveMinutes);

            decimal otHours = 0m;
            decimal overtimePay = 0m;

            var hourlyRate = emp.SalaryType switch
            {
                SalaryType.Monthly => (standardDays * standardDayHours > 0) ? Math.Round(emp.BaseRate / (standardDays * standardDayHours), 2) : 0m,
                SalaryType.Shift => (standardDayHours > 0) ? Math.Round(emp.BaseRate / standardDayHours, 2) : 0m,
                SalaryType.Hourly => emp.BaseRate,
                _ => 0m
            };

            var weekdayMultiplier = ParseOtMultiplier(emp.OtWeekday, 1.5m);
            var sundayMultiplier = ParseOtMultiplier(emp.OtSunday, 2.0m);

            foreach (var sch in workingSchedules)
            {
                if (sch.OvertimeMinutes > 0 && sch.ApprovalStatus == ScheduleApprovalStatus.Approved)
                {
                    var inShiftHours = GetInShiftWorkHours(sch);
                    var maxAllowedOtHours = Math.Max(0m, 12.00m - inShiftHours);
                    var shiftOtHours = Math.Min(maxAllowedOtHours, Math.Round((decimal)sch.OvertimeMinutes / 60.00m, 2));
                    otHours += shiftOtHours;

                    var multiplier = (sch.WorkDate.DayOfWeek == DayOfWeek.Sunday) ? sundayMultiplier : weekdayMultiplier;
                    overtimePay += Math.Round(shiftOtHours * hourlyRate * multiplier, 0, MidpointRounding.AwayFromZero);
                }
            }

            decimal baseSalaryEarned = emp.SalaryType switch
            {
                SalaryType.Monthly => standardDays > 0 ? Math.Round(((workedDays + paidLeaveDays) / standardDays) * emp.BaseRate, 0, MidpointRounding.AwayFromZero) : 0m,
                SalaryType.Shift => Math.Round((totalShifts + paidLeaveDays) * emp.BaseRate, 0, MidpointRounding.AwayFromZero),
                SalaryType.Hourly => Math.Round((totalWorkHours + (paidLeaveDays * standardDayHours)) * emp.BaseRate, 0, MidpointRounding.AwayFromZero),
                _ => emp.BaseRate
            };

            var allowances = emp.Allowances.Where(a => a.Amount > 0).ToList();
            var totalAllowance = allowances.Sum(a => a.Amount);
            var allowanceJsonList = allowances.Select(a => new { name = a.Name, amount = a.Amount }).ToList();
            var allowanceJson = allowanceJsonList.Count > 0 ? JsonSerializer.Serialize(allowanceJsonList) : null;

            var totalEarnings = baseSalaryEarned + overtimePay + totalAllowance;

            decimal socialInsurance = 0m;
            if (emp.SalaryType == SalaryType.Monthly && emp.BaseRate > 0)
            {
                socialInsurance = Math.Round(emp.BaseRate * 0.105m, 0, MidpointRounding.AwayFromZero);
            }

            var currentDebt = emp.DebtAmount > 0 ? emp.DebtAmount : 0m;
            var earningsAfterInsurance = Math.Max(0, totalEarnings - socialInsurance);

            decimal advanceDeduction = 0m;
            decimal remainingDebt = 0m;

            if (currentDebt > 0)
            {
                if (currentDebt <= earningsAfterInsurance)
                {
                    advanceDeduction = currentDebt;
                    remainingDebt = 0m;
                }
                else
                {
                    advanceDeduction = earningsAfterInsurance;
                    remainingDebt = currentDebt - earningsAfterInsurance;
                }
            }

            var totalDeductions = socialInsurance + advanceDeduction;
            var netSalary = Math.Max(0, totalEarnings - totalDeductions);

            Payslip payslip;
            if (existingPayslip != null)
            {
                payslip = existingPayslip;
                payslip.BaseRate = emp.BaseRate;
                payslip.SalaryType = emp.SalaryType;
                payslip.StandardDays = standardDays;
                payslip.StandardDayHours = standardDayHours;
                payslip.WorkedDays = workedDays;
                payslip.TotalWorkHours = totalWorkHours;
                payslip.TotalShifts = totalShifts;
                payslip.OtHours = otHours;
                payslip.PaidLeaveDays = paidLeaveDays;
                payslip.UnpaidLeaveDays = unpaidLeaveDays;
                payslip.TotalLateMinutes = totalLateMinutes;
                payslip.TotalEarlyLeaveMinutes = totalEarlyLeaveMinutes;
                payslip.BaseSalaryEarned = baseSalaryEarned;
                payslip.OvertimePay = overtimePay;
                payslip.TotalAllowance = totalAllowance;
                payslip.AllowancesDetailJson = allowanceJson;
                payslip.TotalEarnings = totalEarnings;
                payslip.AdvanceDeduction = advanceDeduction;
                payslip.RemainingDebt = remainingDebt;
                payslip.SocialInsurance = socialInsurance;
                payslip.TotalDeductions = totalDeductions;
                payslip.NetSalary = netSalary;
                payslip.BankAccountNumber = emp.BankAccountNumber;
                payslip.BankName = emp.BankName;
                payslip.BankAccountHolder = emp.BankAccountHolder;
                payslip.Note = request.Note ?? payslip.Note;
                payslip.UpdatedAt = VnNow;
                payslip.Employee = emp;

                _payslipRepository.Update(payslip);
            }
            else
            {
                payslipCountInMonth++;
                var payslipCode = $"PL-{periodMonthShort}-{payslipCountInMonth:D4}";

                payslip = new Payslip
                {
                    Id = Guid.NewGuid(),
                    TenantId = _tenantContext.CurrentTenantId,
                    BranchId = emp.BranchId,
                    EmployeeId = emp.Id,
                    Employee = emp,
                    PayslipCode = payslipCode,
                    PeriodMonth = request.PeriodMonth,
                    SalaryType = emp.SalaryType,
                    BaseRate = emp.BaseRate,
                    StandardDays = standardDays,
                    StandardDayHours = standardDayHours,
                    WorkedDays = workedDays,
                    TotalWorkHours = totalWorkHours,
                    TotalShifts = totalShifts,
                    OtHours = otHours,
                    PaidLeaveDays = paidLeaveDays,
                    UnpaidLeaveDays = unpaidLeaveDays,
                    TotalLateMinutes = totalLateMinutes,
                    TotalEarlyLeaveMinutes = totalEarlyLeaveMinutes,
                    BaseSalaryEarned = baseSalaryEarned,
                    OvertimePay = overtimePay,
                    TotalAllowance = totalAllowance,
                    AllowancesDetailJson = allowanceJson,
                    TotalEarnings = totalEarnings,
                    AdvanceDeduction = advanceDeduction,
                    RemainingDebt = remainingDebt,
                    SocialInsurance = socialInsurance,
                    TotalDeductions = totalDeductions,
                    NetSalary = netSalary,
                    Status = PayslipStatus.Unpaid,
                    BankAccountNumber = emp.BankAccountNumber,
                    BankName = emp.BankName,
                    BankAccountHolder = emp.BankAccountHolder,
                    Note = request.Note,
                    CreatedAt = VnNow
                };

                await _payslipRepository.AddAsync(payslip, cancellationToken);
            }

            resultList.Add(payslip);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return resultList.Select(MapToResponse).ToList();
    }

    public async Task<PagedResult<PayslipSummaryDto>> GetPayslipsPagedAsync(
        PayslipQueryFilter filter,
        CancellationToken cancellationToken = default)
    {
        var (items, totalCount) = await _payslipRepository.GetPagedListAsync(filter, cancellationToken);

        var dtos = items.Select(p => new PayslipSummaryDto(
            Id: p.Id,
            PayslipCode: p.PayslipCode,
            PeriodMonth: p.PeriodMonth,
            BranchId: p.BranchId,
            BranchName: p.Branch?.Name ?? string.Empty,
            EmployeeId: p.EmployeeId,
            EmployeeCode: p.Employee?.EmployeeCode ?? string.Empty,
            EmployeeName: p.Employee?.FullName ?? string.Empty,
            SalaryType: p.SalaryType,
            WorkedDays: p.WorkedDays,
            TotalWorkHours: p.TotalWorkHours,
            TotalEarnings: p.TotalEarnings,
            TotalDeductions: p.TotalDeductions,
            NetSalary: p.NetSalary,
            Status: p.Status,
            PaidAt: p.PaidAt,
            PaymentMethod: p.PaymentMethod,
            CashbookVoucherCode: p.CashbookVoucherCode
        )).ToList();

        var page = filter.Page > 0 ? filter.Page : 1;
        var pageSize = filter.PageSize > 0 ? filter.PageSize : 20;

        return new PagedResult<PayslipSummaryDto>
        {
            Items = dtos,
            Page = page,
            PageSize = pageSize,
            TotalItems = totalCount
        };
    }

    public async Task<PayslipResponse> GetPayslipByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var payslip = await _payslipRepository.GetByIdAsync(id, cancellationToken);
        if (payslip == null)
        {
            throw new NotFoundException("Payslip", id);
        }

        return MapToResponse(payslip);
    }

    public async Task<PayslipResponse> PayPayslipAsync(
        Guid id,
        PayPayslipRequest request,
        CancellationToken cancellationToken = default)
    {
        var payslip = await _payslipRepository.GetByIdAsync(id, cancellationToken);
        if (payslip == null)
        {
            throw new NotFoundException("Payslip", id);
        }

        if (payslip.Status == PayslipStatus.Paid)
        {
            throw new BusinessException($"Phiếu lương {payslip.PayslipCode} đã được thanh toán trước đó.");
        }

        var method = request.PaymentMethod.Trim().ToUpper();
        var now = VnNow;
        var voucherCode = $"PC-{now:yyyyMM}-{Random.Shared.Next(1000, 9999)}";

        payslip.Status = PayslipStatus.Paid;
        payslip.PaidAt = now;
        payslip.PaymentMethod = method;
        payslip.PaidBy = _tenantContext.CurrentUserId.ToString();
        payslip.Note = request.Note ?? payslip.Note;

        var cashbookTransaction = new CashbookTransaction
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.CurrentTenantId,
            BranchId = payslip.BranchId,
            Type = CashbookType.Payment,
            Category = "Chi phí lương nhân viên",
            Amount = payslip.NetSalary,
            ReferenceCode = payslip.PayslipCode,
            TransactionCode = voucherCode,
            Description = $"Chi trả lương kỳ {payslip.PeriodMonth} cho nhân viên {payslip.Employee?.FullName ?? payslip.EmployeeId.ToString()}",
            CreatedByUserId = _tenantContext.CurrentUserId,
            CreatedAt = now
        };

        await _payslipRepository.AddCashbookTransactionAsync(cashbookTransaction, cancellationToken);

        payslip.CashbookTransactionId = cashbookTransaction.Id;
        payslip.CashbookVoucherCode = voucherCode;

        if (payslip.Employee != null && payslip.AdvanceDeduction > 0)
        {
            payslip.Employee.DebtAmount = Math.Max(0, payslip.Employee.DebtAmount - payslip.AdvanceDeduction);
            _employeeRepository.Update(payslip.Employee);
        }

        await LockEmployeeSchedulesForPaidPayslipAsync(payslip, cancellationToken);

        _payslipRepository.Update(payslip);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return MapToResponse(payslip);
    }

    public async Task<BatchPayResultDto> BatchPayPayslipsAsync(
        BatchPayPayslipsRequest request,
        CancellationToken cancellationToken = default)
    {
        var method = request.PaymentMethod.Trim().ToUpper();
        var successList = new List<string>();
        var errors = new List<string>();

        foreach (var id in request.PayslipIds)
        {
            var payslip = await _payslipRepository.GetByIdAsync(id, cancellationToken);
            if (payslip == null)
            {
                errors.Add($"Phiếu lương {id} không tồn tại.");
                continue;
            }

            if (payslip.Status == PayslipStatus.Paid)
            {
                errors.Add($"Phiếu lương {payslip.PayslipCode} đã được thanh toán trước đó.");
                continue;
            }

            var now = VnNow;
            var voucherCode = $"PC-{now:yyyyMM}-{Random.Shared.Next(1000, 9999)}";

            payslip.Status = PayslipStatus.Paid;
            payslip.PaidAt = now;
            payslip.PaymentMethod = method;
            payslip.PaidBy = _tenantContext.CurrentUserId.ToString();
            payslip.Note = request.Note ?? payslip.Note;

            var cashbookTransaction = new CashbookTransaction
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantContext.CurrentTenantId,
                BranchId = payslip.BranchId,
                Type = CashbookType.Payment,
                Category = "Chi phí lương nhân viên",
                Amount = payslip.NetSalary,
                ReferenceCode = payslip.PayslipCode,
                TransactionCode = voucherCode,
                Description = $"Chi trả lương kỳ {payslip.PeriodMonth} cho nhân viên {payslip.Employee?.FullName ?? payslip.EmployeeId.ToString()}",
                CreatedByUserId = _tenantContext.CurrentUserId,
                CreatedAt = now
            };

            await _payslipRepository.AddCashbookTransactionAsync(cashbookTransaction, cancellationToken);

            payslip.CashbookTransactionId = cashbookTransaction.Id;
            payslip.CashbookVoucherCode = voucherCode;

            if (payslip.Employee != null && payslip.AdvanceDeduction > 0)
            {
                payslip.Employee.DebtAmount = Math.Max(0, payslip.Employee.DebtAmount - payslip.AdvanceDeduction);
                _employeeRepository.Update(payslip.Employee);
            }

            await LockEmployeeSchedulesForPaidPayslipAsync(payslip, cancellationToken);

            _payslipRepository.Update(payslip);
            successList.Add(payslip.PayslipCode);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new BatchPayResultDto(
            TotalRequested: request.PayslipIds.Count,
            SuccessCount: successList.Count,
            FailedCount: errors.Count,
            PaidPayslipCodes: successList,
            Errors: errors
        );
    }

    public async Task<bool> DeletePayslipAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var payslip = await _payslipRepository.GetByIdAsync(id, cancellationToken);
        if (payslip == null)
        {
            throw new NotFoundException("Payslip", id);
        }

        if (payslip.Status == PayslipStatus.Paid)
        {
            throw new BusinessException("Không được phép xóa phiếu lương đã ở trạng thái Đã thanh toán (Paid).");
        }

        _payslipRepository.Delete(payslip);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return true;
    }

    private static decimal ParseOtMultiplier(string? otRateStr, decimal fallbackMultiplier)
    {
        if (string.IsNullOrWhiteSpace(otRateStr))
        {
            return fallbackMultiplier;
        }

        var cleaned = otRateStr.Trim().Replace("%", "").Replace("x", "", StringComparison.OrdinalIgnoreCase).Trim();
        if (decimal.TryParse(cleaned, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var val))
        {
            if (val >= 10m)
            {
                return Math.Round(val / 100m, 2);
            }
            if (val > 0m)
            {
                return Math.Round(val, 2);
            }
        }

        return fallbackMultiplier;
    }

    private static bool IsPaidLeaveSchedule(EmployeeSchedule s)
    {
        return string.Equals(s.ApprovalType, "PAID_LEAVE", StringComparison.OrdinalIgnoreCase)
               || string.Equals(s.CheckInMethod, "MANUAL_PAID_LEAVE", StringComparison.OrdinalIgnoreCase)
               || (s.IsLeave && s.IsPaidLeave);
    }

    private static bool IsUnpaidLeaveSchedule(EmployeeSchedule s)
    {
        return string.Equals(s.ApprovalType, "UNPAID_LEAVE", StringComparison.OrdinalIgnoreCase)
               || string.Equals(s.CheckInMethod, "MANUAL_UNPAID_LEAVE", StringComparison.OrdinalIgnoreCase)
               || (s.IsLeave && !s.IsPaidLeave);
    }

    private static decimal GetInShiftWorkHours(EmployeeSchedule s)
    {
        if (s.OvertimeMinutes > 0 && s.DurationHours > 0 && s.ApprovalStatus == ScheduleApprovalStatus.Approved)
        {
            var otHours = Math.Round((decimal)s.OvertimeMinutes / 60.00m, 2);
            if (s.ActualWorkHours > s.DurationHours && Math.Abs(s.ActualWorkHours - (s.DurationHours + otHours)) < 0.05m)
            {
                return s.DurationHours;
            }
        }

        return s.ActualWorkHours;
    }

    private static decimal CalculateWorkedDays(
        IReadOnlyList<EmployeeSchedule> workingSchedules,
        decimal totalWorkHours,
        decimal standardDayHours,
        BranchAttendancePolicy? policy)
    {
        var shouldConvert = policy == null || policy.IsConvertHoursToDays;
        var rulesJson = (policy != null && !string.IsNullOrWhiteSpace(policy.ConversionRulesJson))
            ? policy.ConversionRulesJson
            : DefaultConversionRulesJson;

        if (shouldConvert && !string.IsNullOrWhiteSpace(rulesJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(rulesJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
                {
                    var rules = new List<(string Type, int FromMinutes, int ToMinutes, decimal WorkUnits)>();
                    foreach (var item in doc.RootElement.EnumerateArray())
                    {
                        if (item.ValueKind != JsonValueKind.Object) continue;
                        var type = item.TryGetProperty("type", out var tProp) ? (tProp.GetString() ?? "range") : "range";
                        var fh = item.TryGetProperty("fromHours", out var fhProp) && fhProp.TryGetInt32(out var fhv) ? fhv : 0;
                        var fm = item.TryGetProperty("fromMinutes", out var fmProp) && fmProp.TryGetInt32(out var fmv) ? fmv : 0;
                        var th = item.TryGetProperty("toHours", out var thProp) && thProp.TryGetInt32(out var thv) ? thv : 0;
                        var tm = item.TryGetProperty("toMinutes", out var tmProp) && tmProp.TryGetInt32(out var tmv) ? tmv : 0;
                        var wu = item.TryGetProperty("workUnits", out var wuProp) && wuProp.TryGetDecimal(out var wuv) ? wuv : 0m;

                        if (wu > 0m)
                        {
                            rules.Add((type, fh * 60 + fm, th * 60 + tm, wu));
                        }
                    }

                    if (rules.Count > 0)
                    {
                        decimal convertedDays = 0m;
                        var schedulesByDate = workingSchedules.GroupBy(s => s.WorkDate.Date);
                        foreach (var dayGroup in schedulesByDate)
                        {
                            var dayHours = dayGroup.Sum(GetInShiftWorkHours);
                            var dayMinutes = (int)Math.Round(dayHours * 60m, MidpointRounding.AwayFromZero);

                            decimal matchedUnits = 0m;
                            var matched = false;

                            foreach (var rule in rules)
                            {
                                if (string.Equals(rule.Type, "range", StringComparison.OrdinalIgnoreCase))
                                {
                                    if (dayMinutes >= rule.FromMinutes && dayMinutes < rule.ToMinutes)
                                    {
                                        matchedUnits = Math.Max(matchedUnits, rule.WorkUnits);
                                        matched = true;
                                    }
                                }
                                else if (string.Equals(rule.Type, "gte", StringComparison.OrdinalIgnoreCase))
                                {
                                    if (dayMinutes >= rule.FromMinutes)
                                    {
                                        matchedUnits = Math.Max(matchedUnits, rule.WorkUnits);
                                        matched = true;
                                    }
                                }
                            }

                            if (matched)
                            {
                                convertedDays += matchedUnits;
                            }
                            else if (standardDayHours > 0)
                            {
                                convertedDays += Math.Round(dayHours / standardDayHours, 2);
                            }
                        }

                        return Math.Round(convertedDays, 2);
                    }
                }
            }
            catch
            {
                // Fallback to standard linear conversion if JSON is malformed
            }
        }

        return standardDayHours > 0 ? Math.Round(totalWorkHours / standardDayHours, 2) : 0m;
    }

    private async Task LockEmployeeSchedulesForPaidPayslipAsync(Payslip payslip, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(payslip.PeriodMonth)) return;
        var parts = payslip.PeriodMonth.Split('-');
        if (parts.Length != 2 || !int.TryParse(parts[0], out var year) || !int.TryParse(parts[1], out var month))
        {
            return;
        }

        var startDate = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
        var endDate = startDate.AddMonths(1).AddTicks(-1);

        var empSchedules = await _scheduleRepository.GetByEmployeeAndDateRangeAsync(
            payslip.EmployeeId, startDate, endDate, cancellationToken);

        foreach (var sch in empSchedules)
        {
            if (sch.Status != ScheduleStatus.Cancelled || IsUnpaidLeaveSchedule(sch))
            {
                sch.PayslipId = payslip.Id;
                _scheduleRepository.Update(sch);
            }
        }
    }

    private static PayslipResponse MapToResponse(Payslip p)
    {
        var bhxhAmount = 0m;
        var bhytAmount = 0m;
        var bhtnAmount = 0m;

        if (p.SocialInsurance > 0 && p.SalaryType == SalaryType.Monthly)
        {
            bhxhAmount = Math.Round(p.BaseRate * 0.08m, 0, MidpointRounding.AwayFromZero);
            bhytAmount = Math.Round(p.BaseRate * 0.015m, 0, MidpointRounding.AwayFromZero);
            bhtnAmount = Math.Round(p.BaseRate * 0.01m, 0, MidpointRounding.AwayFromZero);
        }

        return new PayslipResponse(
            Id: p.Id,
            PayslipCode: p.PayslipCode,
            PeriodMonth: p.PeriodMonth,
            BranchId: p.BranchId,
            BranchName: p.Branch?.Name ?? string.Empty,
            EmployeeId: p.EmployeeId,
            EmployeeCode: p.Employee?.EmployeeCode ?? string.Empty,
            EmployeeName: p.Employee?.FullName ?? string.Empty,
            SalaryType: p.SalaryType,
            BaseRate: p.BaseRate,
            StandardDays: p.StandardDays,
            StandardDayHours: p.StandardDayHours,
            WorkedDays: p.WorkedDays,
            TotalWorkHours: p.TotalWorkHours,
            TotalShifts: p.TotalShifts,
            OtHours: p.OtHours,
            PaidLeaveDays: p.PaidLeaveDays,
            UnpaidLeaveDays: p.UnpaidLeaveDays,
            TotalLateMinutes: p.TotalLateMinutes,
            TotalEarlyLeaveMinutes: p.TotalEarlyLeaveMinutes,
            BaseSalaryEarned: p.BaseSalaryEarned,
            OvertimePay: p.OvertimePay,
            TotalAllowance: p.TotalAllowance,
            AllowancesDetailJson: p.AllowancesDetailJson,
            TotalEarnings: p.TotalEarnings,
            AdvanceDeduction: p.AdvanceDeduction,
            RemainingDebt: p.RemainingDebt,
            SocialInsurance: p.SocialInsurance,
            BhxhAmount: bhxhAmount,
            BhytAmount: bhytAmount,
            BhtnAmount: bhtnAmount,
            TotalDeductions: p.TotalDeductions,
            NetSalary: p.NetSalary,
            Status: p.Status,
            PaidAt: p.PaidAt,
            PaymentMethod: p.PaymentMethod,
            PaidBy: p.PaidBy,
            BankAccountNumber: p.BankAccountNumber,
            BankName: p.BankName,
            BankAccountHolder: p.BankAccountHolder,
            CashbookTransactionId: p.CashbookTransactionId,
            CashbookVoucherCode: p.CashbookVoucherCode,
            Note: p.Note,
            ApprovedBy: p.ApprovedBy,
            ApprovedAt: p.ApprovedAt,
            CreatedAt: p.CreatedAt,
            UserId: p.Employee?.UserId
        );
    }
}
