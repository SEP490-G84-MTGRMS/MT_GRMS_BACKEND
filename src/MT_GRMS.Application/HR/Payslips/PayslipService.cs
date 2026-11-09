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

        var schedules = await _scheduleRepository.GetCompletedSchedulesInPeriodAsync(
            employeeIds, startDate, endDate, cancellationToken);

        var existingPayslips = await _payslipRepository.GetByPeriodAndBranchAsync(request.PeriodMonth, request.BranchId, cancellationToken);
        var existingDict = existingPayslips.ToDictionary(p => p.EmployeeId);

        var payslipCountInMonth = await _payslipRepository.GetCountByPeriodMonthAsync(request.PeriodMonth, cancellationToken);
        var resultList = new List<Payslip>();
        var periodMonthShort = request.PeriodMonth.Replace("-", "").Substring(2);

        foreach (var emp in employees)
        {
            if (existingDict.TryGetValue(emp.Id, out var existingPayslip))
            {
                if (existingPayslip.Status == PayslipStatus.Paid)
                {
                    resultList.Add(existingPayslip);
                    continue;
                }
            }

            var empSchedules = schedules.Where(s => s.EmployeeId == emp.Id).ToList();

            var policy = await _attendancePolicyRepository.GetByBranchIdAsync(emp.BranchId, cancellationToken);
            var standardDayHours = policy?.StandardDayHours ?? 8.00m;
            if (standardDayHours <= 0) standardDayHours = 8.00m;

            var standardDays = 26.00m;

            var totalWorkHours = empSchedules.Sum(s => s.ActualWorkHours);
            var totalShifts = empSchedules.Count(s => s.Status == ScheduleStatus.Completed);
            var workedDays = standardDayHours > 0 ? Math.Round(totalWorkHours / standardDayHours, 2) : 0m;
            var paidLeaveDays = empSchedules.Count(s => s.ApprovalType == "PAID_LEAVE");
            var unpaidLeaveDays = empSchedules.Count(s => s.ApprovalType == "UNPAID_LEAVE");
            var totalLateMinutes = empSchedules.Sum(s => s.LateMinutes);
            var totalEarlyLeaveMinutes = empSchedules.Sum(s => s.EarlyLeaveMinutes);

            decimal otHours = 0m;
            decimal overtimePay = 0m;

            var hourlyRate = emp.SalaryType switch
            {
                SalaryType.Monthly => (standardDays * standardDayHours > 0) ? Math.Round(emp.BaseRate / (standardDays * standardDayHours), 2) : 0m,
                SalaryType.Shift => (standardDayHours > 0) ? Math.Round(emp.BaseRate / standardDayHours, 2) : 0m,
                SalaryType.Hourly => emp.BaseRate,
                _ => 0m
            };

            foreach (var sch in empSchedules)
            {
                if (sch.OvertimeMinutes > 0 && sch.ApprovalStatus == ScheduleApprovalStatus.Approved)
                {
                    var shiftOtHours = Math.Round((decimal)sch.OvertimeMinutes / 60.00m, 2);
                    otHours += shiftOtHours;

                    var multiplier = (sch.WorkDate.DayOfWeek == DayOfWeek.Sunday) ? 2.0m : 1.5m;
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
                payslip.UpdatedAt = DateTime.UtcNow;
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
                    CreatedAt = DateTime.UtcNow
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
        var now = DateTime.UtcNow;
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

            var now = DateTime.UtcNow;
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
