using System.Text.Json;
using MT_GRMS.Application.Cashbook;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Expenses;
using MT_GRMS.Application.HR.AttendancePolicies;
using MT_GRMS.Application.HR.Employees;
using MT_GRMS.Application.HR.Payslips.Dtos;
using MT_GRMS.Application.HR.Schedules;
using MT_GRMS.Application.Notifications;
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
    private readonly ICashbookService? _cashbookService;
    private readonly IExpenseRepository? _expenseRepository;
    private readonly INotificationService? _notificationService;

    public PayslipService(
        IPayslipRepository payslipRepository,
        IEmployeeRepository employeeRepository,
        IScheduleRepository scheduleRepository,
        IBranchAttendancePolicyRepository attendancePolicyRepository,
        ITenantContext tenantContext,
        IUnitOfWork unitOfWork,
        ICashbookService? cashbookService = null,
        IExpenseRepository? expenseRepository = null,
        INotificationService? notificationService = null)
    {
        _payslipRepository = payslipRepository;
        _employeeRepository = employeeRepository;
        _scheduleRepository = scheduleRepository;
        _attendancePolicyRepository = attendancePolicyRepository;
        _tenantContext = tenantContext;
        _unitOfWork = unitOfWork;
        _cashbookService = cashbookService;
        _expenseRepository = expenseRepository;
        _notificationService = notificationService;
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

            const decimal monthlyFormulaStandardDays = 26.00m;
            var assignedShiftCount = activeSchedulesInMonth
                .Where(s => s.EmployeeId == emp.Id)
                .Select(s => s.Id)
                .Concat(empSchedules.Select(s => s.Id))
                .Distinct()
                .Count();
            var standardDays = assignedShiftCount > 0 ? (decimal)assignedShiftCount : monthlyFormulaStandardDays;

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
            var totalLateMinutes = workingSchedules.Sum(s => CalculateEffectiveLateMinutes(s, policy));
            var totalEarlyLeaveMinutes = workingSchedules.Sum(s => CalculateEffectiveEarlyLeaveMinutes(s, policy));

            decimal otHours = 0m;
            decimal overtimePay = 0m;

            var hourlyRate = emp.SalaryType switch
            {
                SalaryType.Monthly => (monthlyFormulaStandardDays * standardDayHours > 0) ? Math.Round(emp.BaseRate / (monthlyFormulaStandardDays * standardDayHours), 2) : 0m,
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

            // ─── Làm tròn giờ làm việc đối với nhân viên tính lương theo giờ (Hourly) theo block 30 phút ───
            var roundedWorkHours = emp.SalaryType == SalaryType.Hourly
                ? RoundTo30MinuteBlock(totalWorkHours)
                : totalWorkHours;

            decimal baseSalaryEarned = emp.SalaryType switch
            {
                SalaryType.Monthly => standardDays > 0 ? Math.Round(((workedDays + paidLeaveDays) / standardDays) * emp.BaseRate, 0, MidpointRounding.AwayFromZero) : 0m,
                SalaryType.Shift => Math.Round((totalShifts + paidLeaveDays) * emp.BaseRate, 0, MidpointRounding.AwayFromZero),
                SalaryType.Hourly => Math.Round((roundedWorkHours + (paidLeaveDays * standardDayHours)) * emp.BaseRate, 0, MidpointRounding.AwayFromZero),
                _ => emp.BaseRate
            };

            if (emp.SalaryType == SalaryType.Hourly)
            {
                totalWorkHours = roundedWorkHours;
            }

            var allowances = emp.Allowances.Where(a => a.Amount > 0).ToList();
            var totalAllowance = allowances.Sum(a => a.Amount);
            var allowanceJsonList = allowances.Select(a => new { name = a.Name, amount = a.Amount }).ToList();
            var allowanceJson = allowanceJsonList.Count > 0 ? JsonSerializer.Serialize(allowanceJsonList) : null;

            var totalEarnings = baseSalaryEarned + overtimePay + totalAllowance;

            decimal socialInsurance = 0m;
            decimal insuranceBase = 0m;
            if (emp.SalaryType == SalaryType.Monthly && emp.HasLaborContract)
            {
                insuranceBase = emp.InsuranceBaseSalary ?? emp.BaseRate;
                if (insuranceBase > 0)
                {
                    var bhxh = Math.Round(insuranceBase * 0.08m, 0, MidpointRounding.AwayFromZero);
                    var bhyt = Math.Round(insuranceBase * 0.015m, 0, MidpointRounding.AwayFromZero);
                    var bhtn = Math.Round(insuranceBase * 0.01m, 0, MidpointRounding.AwayFromZero);
                    socialInsurance = bhxh + bhyt + bhtn;
                }
                else
                {
                    insuranceBase = 0m;
                }
            }

            var cashbookAdvance = existingPayslip != null
                ? await GetPayslipAdvanceFromCashbookAsync(emp.BranchId, existingPayslip.Id, existingPayslip.PayslipCode, cancellationToken)
                : 0m;
            var currentDebt = Math.Max(
                Math.Max(emp.DebtAmount > 0 ? emp.DebtAmount : 0m, cashbookAdvance),
                existingPayslip?.AdvanceDeduction ?? 0m);
            if (currentDebt > emp.DebtAmount)
            {
                emp.DebtAmount = currentDebt;
                _employeeRepository.Update(emp);
            }
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
                payslip.InsuranceBaseSalary = insuranceBase;
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
                    InsuranceBaseSalary = insuranceBase,
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

        if (_notificationService != null)
        {
            foreach (var p in resultList.Where(x => x.Status == PayslipStatus.Unpaid))
            {
                await NotifyPayslipSafeAsync(
                    p.TenantId,
                    p.EmployeeId,
                    p.Employee?.UserId,
                    "PAYSLIP_GENERATED",
                    "Phiếu lương mới",
                    $"Phiếu lương kỳ {p.PeriodMonth} ({p.PayslipCode}) đã được tạo.",
                    cancellationToken);
            }
        }

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
            CashbookVoucherCode: p.CashbookVoucherCode,
            ApprovedAt: p.ApprovedAt,
            ApprovedBy: p.ApprovedBy
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

    public async Task<PayslipResponse> AdvanceSalaryAsync(
        Guid id,
        SalaryAdvanceRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Amount <= 0 || request.Amount > 1_000_000_000m)
        {
            throw new BusinessException("Số tiền tạm ứng phải lớn hơn 0 và không vượt quá 1,000,000,000 VNĐ.");
        }

        var payslip = await _payslipRepository.GetByIdAsync(id, cancellationToken);
        if (payslip == null)
        {
            throw new NotFoundException("Payslip", id);
        }

        if (payslip.Status == PayslipStatus.Paid)
        {
            throw new BusinessException($"Phiếu lương {payslip.PayslipCode} đã thanh toán, không thể tạm ứng thêm.");
        }

        var maxAdvanceable = Math.Max(0m, payslip.TotalEarnings - payslip.SocialInsurance - payslip.AdvanceDeduction);
        if (maxAdvanceable <= 0m)
        {
            throw new BusinessException(
                $"Nhân viên chưa có lương đã làm được khả dụng để tạm ứng cho phiếu {payslip.PayslipCode}.");
        }

        if (request.Amount > maxAdvanceable)
        {
            throw new BusinessException(
                $"Số tiền tạm ứng ({request.Amount:N0} VNĐ) không được vượt quá tiền lương đã làm được còn lại ({maxAdvanceable:N0} VNĐ).");
        }

        var employee = payslip.Employee ?? await _employeeRepository.GetByIdAsync(payslip.EmployeeId, cancellationToken);
        if (employee == null)
        {
            throw new NotFoundException("Employee", payslip.EmployeeId);
        }

        var (paidAtVn, paidAtUtc) = ResolvePaymentTimes(request.PaidAt);
        var method = string.IsNullOrWhiteSpace(request.PaymentMethod)
            ? "CASH"
            : request.PaymentMethod.Trim().ToUpperInvariant();
        var voucherCode = $"TU-{paidAtVn:yyyyMM}-{Random.Shared.Next(1000, 9999)}";
        var description = string.IsNullOrWhiteSpace(request.Note)
            ? $"Tạm ứng lương kỳ {payslip.PeriodMonth} cho nhân viên {employee.FullName}"
            : request.Note.Trim();

        var cashbookService = _cashbookService ?? throw new InvalidOperationException("Cashbook service is required to disburse a salary advance.");
        var disbursedAccount = await cashbookService.ResolveDisbursementAccountAsync(
            payslip.BranchId, method, request.CashbookAccountId, cancellationToken);
        var entry = await cashbookService.DisburseCashAsync(
            disbursedAccount.Id,
            disbursedAccount.AccountType,
            request.Amount,
            "SALARY_ADVANCE",
            payslip.Id,
            payslip.PayslipCode,
            description,
            cancellationToken);
        voucherCode = entry.EntryCode;

        await RecordSalaryExpenseVoucherAsync(
            payslip,
            employee.FullName,
            request.Amount,
            method,
            disbursedAccount,
            paidAtUtc,
            description,
            cancellationToken);

        employee.DebtAmount += request.Amount;
        _employeeRepository.Update(employee);

        var earningsAfterInsurance = Math.Max(0, payslip.TotalEarnings - payslip.SocialInsurance);
        payslip.AdvanceDeduction = Math.Min(employee.DebtAmount, earningsAfterInsurance);
        payslip.RemainingDebt = Math.Max(0, employee.DebtAmount - payslip.AdvanceDeduction);
        payslip.TotalDeductions = payslip.SocialInsurance + payslip.AdvanceDeduction;
        payslip.NetSalary = Math.Max(0, payslip.TotalEarnings - payslip.TotalDeductions);
        _payslipRepository.Update(payslip);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

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
        var (paidAtVn, paidAtUtc) = ResolvePaymentTimes(request.PaidAt);
        var employeeName = payslip.Employee?.FullName ?? payslip.EmployeeId.ToString();
        var description = string.IsNullOrWhiteSpace(request.Note)
            ? $"Chi trả lương kỳ {payslip.PeriodMonth} cho nhân viên {employeeName}"
            : request.Note.Trim();

        var (voucherCode, disbursedAccount, cashbookEntryId) = await DisbursePayslipCashbookAsync(
            payslip, method, request.CashbookAccountId, paidAtVn, description, cancellationToken);

        await RecordSalaryExpenseVoucherAsync(
            payslip,
            employeeName,
            payslip.NetSalary,
            method,
            disbursedAccount,
            paidAtUtc,
            description,
            cancellationToken);

        payslip.Status = PayslipStatus.Paid;
        payslip.PaidAt = paidAtVn;
        payslip.PaymentMethod = method;
        payslip.PaidBy = _tenantContext.CurrentUserId.ToString();
        payslip.Note = request.Note ?? payslip.Note;

        payslip.CashbookEntryId = cashbookEntryId;
        payslip.CashbookVoucherCode = voucherCode;

        if (payslip.Employee != null && payslip.AdvanceDeduction > 0)
        {
            payslip.Employee.DebtAmount = Math.Max(0, payslip.Employee.DebtAmount - payslip.AdvanceDeduction);
            _employeeRepository.Update(payslip.Employee);
        }

        await LockEmployeeSchedulesForPaidPayslipAsync(payslip, cancellationToken);

        _payslipRepository.Update(payslip);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await NotifyPayslipSafeAsync(
            payslip.TenantId,
            payslip.EmployeeId,
            payslip.Employee?.UserId,
            "PAYSLIP_PAID",
            "Đã thanh toán lương",
            $"Phiếu lương kỳ {payslip.PeriodMonth} số tiền {payslip.NetSalary:N0}đ đã được thanh toán ({payslip.CashbookVoucherCode}).",
            cancellationToken);

        return MapToResponse(payslip);
    }

    public async Task<BatchPayResultDto> BatchPayPayslipsAsync(
        BatchPayPayslipsRequest request,
        CancellationToken cancellationToken = default)
    {
        var method = request.PaymentMethod.Trim().ToUpper();
        var successList = new List<string>();
        var errors = new List<string>();

        var paidPayslips = new List<Payslip>();
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

            var (paidAtVn, paidAtUtc) = ResolvePaymentTimes(request.PaidAt);
            var employeeName = payslip.Employee?.FullName ?? payslip.EmployeeId.ToString();
            var description = string.IsNullOrWhiteSpace(request.Note)
                ? $"Chi trả lương kỳ {payslip.PeriodMonth} cho nhân viên {employeeName}"
                : request.Note.Trim();

            string voucherCode;
            Guid? cashbookEntryId;
            FinanceAccount? disbursedAccount;
            try
            {
                (voucherCode, disbursedAccount, cashbookEntryId) = await DisbursePayslipCashbookAsync(
                    payslip, method, request.CashbookAccountId, paidAtVn, description, cancellationToken);
            }
            catch (Exception ex)
            {
                errors.Add($"Phiếu lương {payslip.PayslipCode}: {ex.Message}");
                continue;
            }

            await RecordSalaryExpenseVoucherAsync(
                payslip,
                employeeName,
                payslip.NetSalary,
                method,
                disbursedAccount,
                paidAtUtc,
                description,
                cancellationToken);

            payslip.Status = PayslipStatus.Paid;
            payslip.PaidAt = paidAtVn;
            payslip.PaymentMethod = method;
            payslip.PaidBy = _tenantContext.CurrentUserId.ToString();
            payslip.Note = request.Note ?? payslip.Note;

            payslip.CashbookEntryId = cashbookEntryId;
            payslip.CashbookVoucherCode = voucherCode;

            if (payslip.Employee != null && payslip.AdvanceDeduction > 0)
            {
                payslip.Employee.DebtAmount = Math.Max(0, payslip.Employee.DebtAmount - payslip.AdvanceDeduction);
                _employeeRepository.Update(payslip.Employee);
            }

            await LockEmployeeSchedulesForPaidPayslipAsync(payslip, cancellationToken);

            _payslipRepository.Update(payslip);
            successList.Add(payslip.PayslipCode);
            paidPayslips.Add(payslip);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        foreach (var paid in paidPayslips)
        {
            await NotifyPayslipSafeAsync(
                paid.TenantId,
                paid.EmployeeId,
                paid.Employee?.UserId,
                "PAYSLIP_PAID",
                "Đã thanh toán lương",
                $"Phiếu lương kỳ {paid.PeriodMonth} số tiền {paid.NetSalary:N0}đ đã được thanh toán ({paid.CashbookVoucherCode}).",
                cancellationToken);
        }

        return new BatchPayResultDto(
            TotalRequested: request.PayslipIds.Count,
            SuccessCount: successList.Count,
            FailedCount: errors.Count,
            PaidPayslipCodes: successList,
            Errors: errors
        );
    }

    private async Task NotifyPayslipSafeAsync(
        string tenantId,
        Guid employeeId,
        string? userId,
        string type,
        string title,
        string message,
        CancellationToken cancellationToken)
    {
        if (_notificationService == null) return;
        try
        {
            var targetUserId = userId;
            if (string.IsNullOrWhiteSpace(targetUserId))
            {
                var emp = await _employeeRepository.GetByIdAsync(employeeId, cancellationToken);
                targetUserId = emp?.UserId;
            }
            if (string.IsNullOrWhiteSpace(targetUserId)) return;

            await _notificationService.CreateAndPushAsync(
                tenantId,
                new[] { targetUserId },
                type,
                title,
                message,
                "/employee/payroll",
                cancellationToken);
        }
        catch
        {
            // Không làm gián đoạn luồng tính/trả lương
        }
    }

    private async Task<decimal> GetPayslipAdvanceFromCashbookAsync(
        Guid branchId,
        Guid payslipId,
        string payslipCode,
        CancellationToken cancellationToken)
    {
        if (_cashbookService == null)
        {
            return 0m;
        }

        try
        {
            var entries = await _cashbookService.GetEntriesAsync(
                accountId: null,
                branchId: null,
                fromDate: null,
                toDate: null,
                page: 1,
                pageSize: 500,
                entryType: CashbookEntryType.Payment,
                sourceType: "SALARY_ADVANCE",
                keyword: null,
                ct: cancellationToken);

            return entries
                .Where(e => e.SourceId == payslipId ||
                            string.Equals(e.ReferenceCode, payslipCode, StringComparison.OrdinalIgnoreCase) ||
                            (!string.IsNullOrWhiteSpace(e.Description) &&
                             e.Description.Contains(payslipCode, StringComparison.OrdinalIgnoreCase)))
                .Sum(e => e.Amount);
        }
        catch
        {
            return 0m;
        }
    }

    private static (DateTime PaidAtVn, DateTime PaidAtUtc) ResolvePaymentTimes(DateTime? requestedPaidAt)
    {
        if (!requestedPaidAt.HasValue)
        {
            var utcNow = DateTime.UtcNow;
            return (utcNow.AddHours(7), utcNow);
        }

        var dt = requestedPaidAt.Value;
        var utc = dt.Kind switch
        {
            DateTimeKind.Utc => dt,
            DateTimeKind.Local => dt.ToUniversalTime(),
            _ => DateTime.SpecifyKind(dt, DateTimeKind.Utc)
        };
        return (utc.AddHours(7), utc);
    }

    private static FinanceAccountType InferPaymentSourceType(string method)
    {
        var normalized = (method ?? string.Empty).Trim().ToUpperInvariant();
        if (normalized.Contains("BANK") || normalized.Contains("TRANSFER") || normalized.Contains("VIETQR") || normalized == "QR")
        {
            return FinanceAccountType.Bank;
        }
        return FinanceAccountType.Safe;
    }

    private async Task RecordSalaryExpenseVoucherAsync(
        Payslip payslip,
        string beneficiaryName,
        decimal amount,
        string method,
        FinanceAccount? account,
        DateTime paymentTimeUtc,
        string description,
        CancellationToken cancellationToken)
    {
        if (_expenseRepository == null || amount <= 0)
        {
            return;
        }

        var voucher = new ExpenseVoucher
        {
            TenantId = _tenantContext.CurrentTenantId,
            BranchId = payslip.BranchId,
            VoucherCode = $"EXP-{DateTime.UtcNow:yyyyMMddHHmmssfff}",
            BeneficiaryName = beneficiaryName,
            Category = "Salary",
            Amount = amount,
            Status = ExpenseVoucherStatus.Paid,
            Nature = FinancialNature.OperatingExpense,
            PaymentSourceType = account?.AccountType ?? InferPaymentSourceType(method),
            PaymentSourceAccountId = account?.Id,
            IsTaxDeductible = false,
            ExpenseDate = paymentTimeUtc,
            PaidAtUtc = paymentTimeUtc,
            ApprovedByUserId = _tenantContext.CurrentUserId,
            ApprovedAtUtc = DateTime.UtcNow,
            ReferenceCode = payslip.PayslipCode,
            Note = description,
            IsFlaggedAsSuspiciousSplit = false
        };

        await _expenseRepository.AddAsync(voucher, cancellationToken);
    }

    private async Task<(string VoucherCode, FinanceAccount? Account, Guid? EntryId)> DisbursePayslipCashbookAsync(
        Payslip payslip,
        string method,
        Guid? cashbookAccountId,
        DateTime paidAtVn,
        string description,
        CancellationToken cancellationToken)
    {
        var fallbackVoucherCode = $"PC-{paidAtVn:yyyyMM}-{Random.Shared.Next(1000, 9999)}";
        if (payslip.NetSalary <= 0)
        {
            return (fallbackVoucherCode, null, null);
        }

        var cashbookService = _cashbookService ?? throw new InvalidOperationException("Cashbook service is required to disburse a payslip.");

        var account = await cashbookService.ResolveDisbursementAccountAsync(
            payslip.BranchId, method, cashbookAccountId, cancellationToken);
        var entry = await cashbookService.DisburseCashAsync(
            account.Id,
            account.AccountType,
            payslip.NetSalary,
            "SALARY",
            payslip.Id,
            payslip.PayslipCode,
            description,
            cancellationToken);
        return (entry.EntryCode, account, entry.Id);
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

    /// <summary>
    /// Làm tròn giờ làm việc theo block 30 phút đối với nhân viên hưởng lương theo giờ (Hourly):
    /// - 0 đến 14 phút -> 0 phút (0.0h)
    /// - 15 đến 44 phút -> 30 phút (0.5h)
    /// - 45 đến 59 phút -> 60 phút (+1.0h)
    /// </summary>
    public static decimal RoundTo30MinuteBlock(decimal rawHours)
    {
        if (rawHours <= 0m) return 0m;

        var totalMinutes = (long)Math.Round(rawHours * 60m, MidpointRounding.AwayFromZero);
        var fullHours = totalMinutes / 60;
        var remainderMinutes = totalMinutes % 60;

        decimal fractionHours = remainderMinutes switch
        {
            >= 45 => 1.0m,
            >= 15 => 0.5m,
            _ => 0.0m
        };

        return (decimal)fullHours + fractionHours;
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

    private static int CalculateEffectiveLateMinutes(EmployeeSchedule s, BranchAttendancePolicy? policy)
    {
        if (policy == null)
        {
            return s.LateMinutes;
        }

        if (!policy.EnableLateThreshold)
        {
            return 0;
        }

        if (s.CheckInTime.HasValue)
        {
            var plannedStart = s.WorkDate.Date.Add(s.PlannedStartTime);
            var rawLate = s.CheckInTime.Value > plannedStart
                ? (int)(s.CheckInTime.Value - plannedStart).TotalMinutes
                : 0;
            return rawLate > policy.LateThresholdMinutes ? rawLate : 0;
        }

        return s.LateMinutes > policy.LateThresholdMinutes ? s.LateMinutes : 0;
    }

    private static int CalculateEffectiveEarlyLeaveMinutes(EmployeeSchedule s, BranchAttendancePolicy? policy)
    {
        if (policy == null)
        {
            return s.EarlyLeaveMinutes;
        }

        if (!policy.EnableEarlyLeaveThreshold)
        {
            return 0;
        }

        if (s.CheckOutTime.HasValue)
        {
            var plannedEnd = s.WorkDate.Date.Add(s.PlannedEndTime);
            if (s.PlannedEndTime <= s.PlannedStartTime)
            {
                plannedEnd = plannedEnd.AddDays(1);
            }

            var rawEarly = s.CheckOutTime.Value < plannedEnd
                ? (int)(plannedEnd - s.CheckOutTime.Value).TotalMinutes
                : 0;
            return rawEarly > policy.EarlyLeaveThresholdMinutes ? rawEarly : 0;
        }

        return s.EarlyLeaveMinutes > policy.EarlyLeaveThresholdMinutes ? s.EarlyLeaveMinutes : 0;
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
            sch.PayslipId = payslip.Id;
            // Tránh DbSet.Update(sch) attach lại navigation từ AsNoTracking() gây xung đột tracking Branch/Employee.
            sch.Employee = null;
            sch.Branch = null;
            sch.ShiftConfig = null;
            sch.Payslip = null;
            _scheduleRepository.Update(sch);
        }
    }

    private static PayslipResponse MapToResponse(Payslip p)
    {
        var bhxhAmount = 0m;
        var bhytAmount = 0m;
        var bhtnAmount = 0m;

        if (p.SocialInsurance > 0 && p.SalaryType == SalaryType.Monthly)
        {
            var insuranceBase = p.InsuranceBaseSalary > 0 ? p.InsuranceBaseSalary : p.BaseRate;
            bhxhAmount = Math.Round(insuranceBase * 0.08m, 0, MidpointRounding.AwayFromZero);
            bhytAmount = Math.Round(insuranceBase * 0.015m, 0, MidpointRounding.AwayFromZero);
            bhtnAmount = Math.Round(insuranceBase * 0.01m, 0, MidpointRounding.AwayFromZero);
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
            CashbookEntryId: p.CashbookEntryId,
            CashbookVoucherCode: p.CashbookVoucherCode,
            Note: p.Note,
            ApprovedBy: p.ApprovedBy,
            ApprovedAt: p.ApprovedAt,
            CreatedAt: p.CreatedAt,
            UserId: p.Employee?.UserId,
            OtWeekday: p.Employee?.OtWeekday ?? "150%",
            OtSunday: p.Employee?.OtSunday ?? "200%",
            InsuranceBaseSalary: p.InsuranceBaseSalary
        );
    }
}
