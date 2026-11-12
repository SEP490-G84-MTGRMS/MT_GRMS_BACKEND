using MT_GRMS.Application.Common;
using MT_GRMS.Application.HR.AttendancePolicies;
using MT_GRMS.Application.HR.Employees;
using MT_GRMS.Application.HR.Employees.Dtos;
using MT_GRMS.Application.HR.Payslips;
using MT_GRMS.Application.HR.Payslips.Dtos;
using MT_GRMS.Application.HR.Schedules;
using MT_GRMS.Application.HR.Schedules.Dtos;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Entities.HR;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Domain.Exceptions;
using Xunit;

namespace MT_GRMS.UnitTests;

public class PayslipServiceTests
{
    private class FakeUnitOfWork : IUnitOfWork
    {
        public bool SaveChangesCalled { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveChangesCalled = true;
            return Task.FromResult(1);
        }
    }

    private class FakeTenantContext : ITenantContext
    {
        public string CurrentTenantId { get; set; } = "tenant_test_01";
        public Guid CurrentUserId { get; set; } = Guid.NewGuid();
        public string CurrentRole { get; set; } = "STORE_OWNER";
        public Guid? CurrentBranchId { get; set; } = Guid.NewGuid();

        public void SetContext(string tenantId, Guid userId, Guid? branchId)
        {
            CurrentTenantId = tenantId;
            CurrentUserId = userId;
            CurrentBranchId = branchId;
        }

        public void SetRole(string? role)
        {
            CurrentRole = role ?? string.Empty;
        }
    }

    private class FakePayslipRepository : IPayslipRepository
    {
        public List<Payslip> Payslips { get; } = new();
        public List<CashbookTransaction> CashbookTransactions { get; } = new();

        public Task<Payslip?> GetByIdAsync(Guid id, CancellationToken ct = default)
        {
            return Task.FromResult(Payslips.FirstOrDefault(p => p.Id == id && !p.IsDeleted));
        }

        public Task<Payslip?> GetByEmployeeAndPeriodAsync(Guid employeeId, string periodMonth, CancellationToken ct = default)
        {
            return Task.FromResult(Payslips.FirstOrDefault(p => p.EmployeeId == employeeId && p.PeriodMonth == periodMonth && !p.IsDeleted));
        }

        public Task<IReadOnlyList<Payslip>> GetByPeriodAndBranchAsync(string periodMonth, Guid? branchId, CancellationToken ct = default)
        {
            var query = Payslips.Where(p => p.PeriodMonth == periodMonth && !p.IsDeleted);
            if (branchId.HasValue && branchId.Value != Guid.Empty)
            {
                query = query.Where(p => p.BranchId == branchId.Value);
            }
            return Task.FromResult<IReadOnlyList<Payslip>>(query.ToList());
        }

        public Task<(IReadOnlyList<Payslip> Items, int TotalCount)> GetPagedListAsync(PayslipQueryFilter filter, CancellationToken ct = default)
        {
            var query = Payslips.Where(p => !p.IsDeleted).AsQueryable();

            if (!string.IsNullOrWhiteSpace(filter.PeriodMonth))
                query = query.Where(p => p.PeriodMonth == filter.PeriodMonth);

            if (filter.BranchId.HasValue && filter.BranchId.Value != Guid.Empty)
                query = query.Where(p => p.BranchId == filter.BranchId.Value);

            if (filter.Status.HasValue)
                query = query.Where(p => p.Status == filter.Status.Value);

            var total = query.Count();
            var items = query.Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize).ToList();
            return Task.FromResult(((IReadOnlyList<Payslip>)items, total));
        }

        public Task<int> GetCountByPeriodMonthAsync(string periodMonth, CancellationToken ct = default)
        {
            return Task.FromResult(Payslips.Count(p => p.PeriodMonth == periodMonth && !p.IsDeleted));
        }

        public Task AddAsync(Payslip payslip, CancellationToken ct = default)
        {
            Payslips.Add(payslip);
            return Task.CompletedTask;
        }

        public Task AddRangeAsync(IEnumerable<Payslip> payslips, CancellationToken ct = default)
        {
            Payslips.AddRange(payslips);
            return Task.CompletedTask;
        }

        public Task AddCashbookTransactionAsync(CashbookTransaction transaction, CancellationToken ct = default)
        {
            CashbookTransactions.Add(transaction);
            return Task.CompletedTask;
        }

        public void Update(Payslip payslip)
        {
            var idx = Payslips.FindIndex(p => p.Id == payslip.Id);
            if (idx >= 0) Payslips[idx] = payslip;
        }

        public void Delete(Payslip payslip)
        {
            payslip.IsDeleted = true;
        }
    }

    private class FakeEmployeeRepository : IEmployeeRepository
    {
        public List<Employee> Employees { get; } = new();

        public Task<IReadOnlyList<Employee>> GetActiveEmployeesAsync(Guid? branchId, CancellationToken cancellationToken = default)
        {
            var query = Employees.Where(e => !e.IsDeleted && e.Status == EmployeeStatus.Active);
            if (branchId.HasValue && branchId.Value != Guid.Empty)
                query = query.Where(e => e.BranchId == branchId.Value);

            return Task.FromResult<IReadOnlyList<Employee>>(query.ToList());
        }

        public Task<Employee?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(Employees.FirstOrDefault(e => e.Id == id && !e.IsDeleted));

        public Task<Employee?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
            => Task.FromResult(Employees.FirstOrDefault(e => e.EmployeeCode == code && !e.IsDeleted));

        public Task<Employee?> GetByCitizenIdAsync(string citizenId, CancellationToken cancellationToken = default)
            => Task.FromResult(Employees.FirstOrDefault(e => e.CitizenId == citizenId && !e.IsDeleted));

        public Task<Employee?> GetByPhoneAsync(string phone, CancellationToken cancellationToken = default)
            => Task.FromResult(Employees.FirstOrDefault(e => e.Phone == phone && !e.IsDeleted));

        public Task<Employee?> GetByUserIdAsync(string userId, CancellationToken cancellationToken = default)
            => Task.FromResult(Employees.FirstOrDefault(e => e.UserId == userId && !e.IsDeleted));

        public Task<(IReadOnlyList<Employee> Items, int TotalCount)> GetPagedListAsync(EmployeeQueryFilter filter, CancellationToken cancellationToken = default)
            => Task.FromResult(((IReadOnlyList<Employee>)Employees, Employees.Count));

        public Task<string?> GetMaxSequentialCodeAsync(string prefix, int totalLength, CancellationToken cancellationToken = default)
            => Task.FromResult<string?>("NV0005");

        public Task<bool> ExistsAsync(System.Linq.Expressions.Expression<Func<Employee, bool>> predicate, CancellationToken cancellationToken = default)
            => Task.FromResult(Employees.AsQueryable().Any(predicate));

        public Task AddAsync(Employee employee, CancellationToken cancellationToken = default)
        {
            Employees.Add(employee);
            return Task.CompletedTask;
        }

        public void Update(Employee employee)
        {
            var idx = Employees.FindIndex(e => e.Id == employee.Id);
            if (idx >= 0) Employees[idx] = employee;
        }

        public void Delete(Employee employee)
        {
            employee.IsDeleted = true;
        }

        public void RemoveAllowances(IEnumerable<EmployeeAllowance> allowances) { }
        public void SyncAllowances(Employee employee, IEnumerable<EmployeeAllowanceDto> newAllowances, string tenantId) { }
        public Task<bool> HasWorkOrPayrollHistoryAsync(Guid employeeId, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private class FakeScheduleRepository : IScheduleRepository
    {
        public List<EmployeeSchedule> Schedules { get; } = new();

        public Task<IReadOnlyList<EmployeeSchedule>> GetCompletedSchedulesInPeriodAsync(
            IEnumerable<Guid> employeeIds,
            DateTime fromDate,
            DateTime toDate,
            CancellationToken cancellationToken = default)
        {
            var set = employeeIds.ToHashSet();
            var list = Schedules
                .Where(s => !s.IsDeleted
                            && set.Contains(s.EmployeeId)
                            && s.WorkDate >= fromDate.Date
                            && s.WorkDate <= toDate.Date
                            && (s.Status == ScheduleStatus.Completed
                                || (s.Status == ScheduleStatus.OnLeave && s.ApprovalStatus == ScheduleApprovalStatus.Approved)
                                || ((s.ApprovalType == "PAID_LEAVE" || s.ApprovalType == "UNPAID_LEAVE") && s.ApprovalStatus == ScheduleApprovalStatus.Approved)))
                .ToList();
            return Task.FromResult<IReadOnlyList<EmployeeSchedule>>(list);
        }

        public Task<EmployeeSchedule?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(Schedules.FirstOrDefault(s => s.Id == id && !s.IsDeleted));

        public Task<IReadOnlyList<EmployeeSchedule>> GetListAsync(ScheduleQueryFilter filter, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<EmployeeSchedule>>(Schedules);

        public Task<IReadOnlyList<EmployeeSchedule>> GetActiveSchedulesForEmployeesAsync(
            IEnumerable<Guid> employeeIds,
            DateTime fromDate,
            DateTime toDate,
            CancellationToken cancellationToken = default)
        {
            var idList = employeeIds.ToList();
            var list = Schedules
                .Where(s => idList.Contains(s.EmployeeId)
                            && s.WorkDate >= fromDate.Date
                            && s.WorkDate <= toDate.Date
                            && s.Status != ScheduleStatus.Cancelled
                            && !s.IsDeleted)
                .ToList();
            return Task.FromResult<IReadOnlyList<EmployeeSchedule>>(list);
        }

        public Task<IReadOnlyList<EmployeeSchedule>> GetByEmployeeAndDateRangeAsync(
            Guid employeeId,
            DateTime fromDate,
            DateTime toDate,
            CancellationToken cancellationToken = default)
        {
            var list = Schedules
                .Where(s => s.EmployeeId == employeeId
                            && s.WorkDate >= fromDate.Date
                            && s.WorkDate <= toDate.Date
                            && !s.IsDeleted)
                .ToList();
            return Task.FromResult<IReadOnlyList<EmployeeSchedule>>(list);
        }

        public Task AddRangeAsync(IEnumerable<EmployeeSchedule> schedules, CancellationToken cancellationToken = default)
        {
            Schedules.AddRange(schedules);
            return Task.CompletedTask;
        }

        public Task AddAsync(EmployeeSchedule schedule, CancellationToken cancellationToken = default)
        {
            Schedules.Add(schedule);
            return Task.CompletedTask;
        }

        public void Update(EmployeeSchedule schedule)
        {
            var idx = Schedules.FindIndex(s => s.Id == schedule.Id);
            if (idx >= 0) Schedules[idx] = schedule;
        }

        public void Delete(EmployeeSchedule schedule)
        {
            schedule.IsDeleted = true;
        }
    }

    private class FakeAttendancePolicyRepository : IBranchAttendancePolicyRepository
    {
        public List<BranchAttendancePolicy> Policies { get; } = new();

        public Task<BranchAttendancePolicy?> GetByBranchIdAsync(Guid branchId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Policies.FirstOrDefault(p => p.BranchId == branchId && !p.IsDeleted));
        }

        public Task<IReadOnlyList<BranchAttendancePolicy>> GetByBranchIdsAsync(IEnumerable<Guid> branchIds, CancellationToken cancellationToken = default)
        {
            var set = branchIds.ToHashSet();
            var list = Policies.Where(p => set.Contains(p.BranchId) && !p.IsDeleted).ToList();
            return Task.FromResult<IReadOnlyList<BranchAttendancePolicy>>(list);
        }

        public Task AddAsync(BranchAttendancePolicy policy, CancellationToken cancellationToken = default)
        {
            Policies.Add(policy);
            return Task.CompletedTask;
        }

        public void Update(BranchAttendancePolicy policy)
        {
            var idx = Policies.FindIndex(p => p.Id == policy.Id);
            if (idx >= 0) Policies[idx] = policy;
        }
    }

    private (PayslipService service, FakePayslipRepository payslipRepo, FakeEmployeeRepository empRepo, FakeScheduleRepository schedRepo, FakeAttendancePolicyRepository policyRepo, FakeTenantContext tenantContext, FakeUnitOfWork uow) CreateService()
    {
        var payslipRepo = new FakePayslipRepository();
        var empRepo = new FakeEmployeeRepository();
        var schedRepo = new FakeScheduleRepository();
        var policyRepo = new FakeAttendancePolicyRepository();
        var tenantContext = new FakeTenantContext();
        var uow = new FakeUnitOfWork();

        var service = new PayslipService(payslipRepo, empRepo, schedRepo, policyRepo, tenantContext, uow);
        return (service, payslipRepo, empRepo, schedRepo, policyRepo, tenantContext, uow);
    }

    [Fact]
    public async Task GenerateMonthlyPayroll_MonthlySalary_CalculatesStandardWorkDaysAndSocialInsurance()
    {
        // Arrange
        var (service, payslipRepo, empRepo, schedRepo, policyRepo, tenantContext, uow) = CreateService();
        var branchId = Guid.NewGuid();

        var employee = new Employee
        {
            Id = Guid.NewGuid(),
            BranchId = branchId,
            EmployeeCode = "NV0001",
            FullName = "Nguyen Van A",
            SalaryType = SalaryType.Monthly,
            BaseRate = 10_400_000m,
            Status = EmployeeStatus.Active
        };
        empRepo.Employees.Add(employee);

        var shift = new ShiftConfig
        {
            Id = Guid.NewGuid(),
            BranchId = branchId,
            ShiftName = "Ca Hanh Chinh",
            ShiftCode = "HC",
            StartTime = new TimeSpan(8, 0, 0),
            EndTime = new TimeSpan(17, 0, 0),
            DurationHours = 8.0m
        };

        // Add 26 completed shifts in 2026-11 (weekdays), each 8.0 hours
        for (int day = 1; day <= 26; day++)
        {
            var date = new DateTime(2026, 11, day);
            var sched = new EmployeeSchedule
            {
                Id = Guid.NewGuid(),
                EmployeeId = employee.Id,
                BranchId = branchId,
                ShiftConfigId = shift.Id,
                WorkDate = date,
                Status = ScheduleStatus.Completed,
                ActualWorkHours = 8.0m
            };
            schedRepo.Schedules.Add(sched);
        }

        // Act
        var result = await service.GenerateMonthlyPayrollAsync(new GeneratePayrollBatchRequest("2026-11", branchId));

        // Assert
        Assert.Single(result);
        var payslip = result[0];
        Assert.Equal("NV0001", payslip.EmployeeCode);
        Assert.Equal(26m, payslip.WorkedDays);
        Assert.Equal(10_400_000m, payslip.BaseSalaryEarned);
        // BHXH 10.5% for Monthly: 10,400,000 * 10.5% = 1,092,000
        Assert.Equal(1_092_000m, payslip.SocialInsurance);
        // Net salary = 10,400,000 - 1,092,000 = 9,308,000
        Assert.Equal(9_308_000m, payslip.NetSalary);
        Assert.Equal(PayslipStatus.Unpaid, payslip.Status);
        Assert.True(uow.SaveChangesCalled);
    }

    [Fact]
    public async Task GenerateMonthlyPayroll_HourlySalary_NoInsurance_CalculatesHourlyWages()
    {
        // Arrange
        var (service, payslipRepo, empRepo, schedRepo, policyRepo, tenantContext, uow) = CreateService();
        var branchId = Guid.NewGuid();

        var employee = new Employee
        {
            Id = Guid.NewGuid(),
            BranchId = branchId,
            EmployeeCode = "NV0002",
            FullName = "Tran Thi B",
            SalaryType = SalaryType.Hourly,
            BaseRate = 30_000m,
            Status = EmployeeStatus.Active
        };
        empRepo.Employees.Add(employee);

        var shift = new ShiftConfig
        {
            Id = Guid.NewGuid(),
            BranchId = branchId,
            ShiftName = "Ca Part-time",
            ShiftCode = "PT",
            StartTime = new TimeSpan(18, 0, 0),
            EndTime = new TimeSpan(22, 0, 0),
            DurationHours = 4.0m
        };

        // 10 shifts of 4 hours = 40 hours
        for (int day = 1; day <= 10; day++)
        {
            schedRepo.Schedules.Add(new EmployeeSchedule
            {
                Id = Guid.NewGuid(),
                EmployeeId = employee.Id,
                BranchId = branchId,
                ShiftConfigId = shift.Id,
                WorkDate = new DateTime(2026, 11, day),
                Status = ScheduleStatus.Completed,
                ActualWorkHours = 4.0m
            });
        }

        // Act
        var result = await service.GenerateMonthlyPayrollAsync(new GeneratePayrollBatchRequest("2026-11", branchId));

        // Assert
        Assert.Single(result);
        var payslip = result[0];
        Assert.Equal(40.0m, payslip.TotalWorkHours);
        Assert.Equal(1_200_000m, payslip.BaseSalaryEarned); // 40h * 30,000
        Assert.Equal(0m, payslip.SocialInsurance); // Part-time: 0 BHXH
        Assert.Equal(1_200_000m, payslip.NetSalary);
    }

    [Fact]
    public async Task GenerateMonthlyPayroll_ApprovedOvertime_CalculatesWeekdayAndWeekendOt()
    {
        // Arrange
        var (service, payslipRepo, empRepo, schedRepo, policyRepo, tenantContext, uow) = CreateService();
        var branchId = Guid.NewGuid();

        var employee = new Employee
        {
            Id = Guid.NewGuid(),
            BranchId = branchId,
            EmployeeCode = "NV0003",
            FullName = "Le Van C",
            SalaryType = SalaryType.Monthly,
            BaseRate = 10_400_000m, // Standard hourly rate = 10,400,000 / (26 * 8) = 50,000 VND
            Status = EmployeeStatus.Active
        };
        empRepo.Employees.Add(employee);

        // Schedule 1: Wednesday 2026-11-04 (Weekday) with 2h OT approved (120 minutes)
        var sched1 = new EmployeeSchedule
        {
            Id = Guid.NewGuid(),
            EmployeeId = employee.Id,
            BranchId = branchId,
            WorkDate = new DateTime(2026, 11, 4), // Wednesday
            Status = ScheduleStatus.Completed,
            ActualWorkHours = 8.0m,
            OvertimeMinutes = 120,
            ApprovalStatus = ScheduleApprovalStatus.Approved
        };
        schedRepo.Schedules.Add(sched1);

        // Schedule 2: Sunday 2026-11-08 (Weekend) with 2h OT approved (120 minutes)
        var sched2 = new EmployeeSchedule
        {
            Id = Guid.NewGuid(),
            EmployeeId = employee.Id,
            BranchId = branchId,
            WorkDate = new DateTime(2026, 11, 8), // Sunday
            Status = ScheduleStatus.Completed,
            ActualWorkHours = 8.0m,
            OvertimeMinutes = 120,
            ApprovalStatus = ScheduleApprovalStatus.Approved
        };
        schedRepo.Schedules.Add(sched2);

        // Act
        var result = await service.GenerateMonthlyPayrollAsync(new GeneratePayrollBatchRequest("2026-11", branchId));

        // Assert
        Assert.Single(result);
        var payslip = result[0];
        Assert.Equal(4.0m, payslip.OtHours);
        // Weekday: 2h * 50,000 * 1.5 = 150,000
        // Sunday: 2h * 50,000 * 2.0 = 200,000
        // Total OT Pay = 350,000
        Assert.Equal(350_000m, payslip.OvertimePay);
    }

    [Fact]
    public async Task GenerateMonthlyPayroll_AdvanceDebtDeduction_EnforcesZeroFloor()
    {
        // Arrange
        var (service, payslipRepo, empRepo, schedRepo, policyRepo, tenantContext, uow) = CreateService();
        var branchId = Guid.NewGuid();

        var employee = new Employee
        {
            Id = Guid.NewGuid(),
            BranchId = branchId,
            EmployeeCode = "NV0004",
            FullName = "Pham Van D",
            SalaryType = SalaryType.Hourly,
            BaseRate = 20_000m,
            DebtAmount = 1_000_000m, // Advance debt of 1,000,000 VND
            Status = EmployeeStatus.Active
        };
        empRepo.Employees.Add(employee);

        // 1 shift of 4 hours -> BaseSalaryEarned = 4h * 20,000 = 80,000 VND
        schedRepo.Schedules.Add(new EmployeeSchedule
        {
            Id = Guid.NewGuid(),
            EmployeeId = employee.Id,
            BranchId = branchId,
            WorkDate = new DateTime(2026, 11, 2),
            Status = ScheduleStatus.Completed,
            ActualWorkHours = 4.0m
        });

        // Act
        var result = await service.GenerateMonthlyPayrollAsync(new GeneratePayrollBatchRequest("2026-11", branchId));

        // Assert
        Assert.Single(result);
        var payslip = result[0];
        Assert.Equal(80_000m, payslip.BaseSalaryEarned);
        // Advance deduction can only deduct up to available earnings (80,000)
        Assert.Equal(80_000m, payslip.AdvanceDeduction);
        Assert.Equal(0m, payslip.NetSalary); // Never negative
        Assert.Equal(920_000m, payslip.RemainingDebt);
    }

    [Fact]
    public async Task PayPayslip_UnpaidPayslip_MarksPaidAndGeneratesCashbookTransaction()
    {
        // Arrange
        var (service, payslipRepo, empRepo, schedRepo, policyRepo, tenantContext, uow) = CreateService();
        var branchId = Guid.NewGuid();

        var payslip = new Payslip
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant_test_01",
            BranchId = branchId,
            EmployeeId = Guid.NewGuid(),
            PayslipCode = "PL-202611-0001",
            PeriodMonth = "2026-11",
            NetSalary = 5_000_000m,
            Status = PayslipStatus.Unpaid
        };
        payslipRepo.Payslips.Add(payslip);

        var payRequest = new PayPayslipRequest(
            PaymentMethod: "BANK_TRANSFER",
            Note: "Tra luong T11 qua VCB"
        );

        // Act
        var result = await service.PayPayslipAsync(payslip.Id, payRequest);

        // Assert
        Assert.Equal(PayslipStatus.Paid, result.Status);
        Assert.Equal("BANK_TRANSFER", result.PaymentMethod);
        Assert.NotNull(result.PaidAt);
        Assert.Equal(tenantContext.CurrentUserId.ToString(), result.PaidBy);

        // Cashbook check
        Assert.Single(payslipRepo.CashbookTransactions);
        var tx = payslipRepo.CashbookTransactions[0];
        Assert.Equal(CashbookType.Payment, tx.Type);
        Assert.Equal(5_000_000m, tx.Amount);
        Assert.Equal(branchId, tx.BranchId);
        Assert.StartsWith("PC-", tx.TransactionCode);
        Assert.True(uow.SaveChangesCalled);
    }

    [Fact]
    public async Task PayPayslip_AlreadyPaid_ThrowsBusinessException()
    {
        // Arrange
        var (service, payslipRepo, empRepo, schedRepo, policyRepo, tenantContext, uow) = CreateService();

        var payslip = new Payslip
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant_test_01",
            EmployeeId = Guid.NewGuid(),
            PayslipCode = "PL-202611-0001",
            PeriodMonth = "2026-11",
            NetSalary = 5_000_000m,
            Status = PayslipStatus.Paid
        };
        payslipRepo.Payslips.Add(payslip);

        var payRequest = new PayPayslipRequest(PaymentMethod: "CASH");

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            service.PayPayslipAsync(payslip.Id, payRequest));
        Assert.Contains("đã được thanh toán", ex.Message);
    }

    [Fact]
    public async Task DeletePayslip_PaidPayslip_ThrowsBusinessException()
    {
        // Arrange
        var (service, payslipRepo, empRepo, schedRepo, policyRepo, tenantContext, uow) = CreateService();

        var payslip = new Payslip
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant_test_01",
            EmployeeId = Guid.NewGuid(),
            PayslipCode = "PL-202611-0001",
            PeriodMonth = "2026-11",
            Status = PayslipStatus.Paid
        };
        payslipRepo.Payslips.Add(payslip);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            service.DeletePayslipAsync(payslip.Id));
        Assert.Contains("Không được phép xóa phiếu lương đã ở trạng thái", ex.Message);
    }

    [Fact]
    public async Task GenerateMonthlyPayroll_ShiftSalary_NoInsurance_CalculatesShiftPay()
    {
        // Arrange (REQ-PAY-01 Scenario 2: Part-time Shift)
        var (service, payslipRepo, empRepo, schedRepo, policyRepo, tenantContext, uow) = CreateService();
        var branchId = Guid.NewGuid();

        var employee = new Employee
        {
            Id = Guid.NewGuid(),
            BranchId = branchId,
            EmployeeCode = "NV_SHIFT",
            FullName = "Nguyen Shift",
            SalaryType = SalaryType.Shift,
            BaseRate = 150_000m,
            Status = EmployeeStatus.Active
        };
        empRepo.Employees.Add(employee);

        var shift = new ShiftConfig
        {
            Id = Guid.NewGuid(),
            BranchId = branchId,
            ShiftName = "Ca Toi",
            ShiftCode = "TOI",
            StartTime = new TimeSpan(18, 0, 0),
            EndTime = new TimeSpan(22, 0, 0),
            DurationHours = 4.0m
        };

        // 20 completed shifts
        for (int day = 1; day <= 20; day++)
        {
            schedRepo.Schedules.Add(new EmployeeSchedule
            {
                Id = Guid.NewGuid(),
                EmployeeId = employee.Id,
                BranchId = branchId,
                ShiftConfigId = shift.Id,
                WorkDate = new DateTime(2026, 11, day),
                Status = ScheduleStatus.Completed,
                ActualWorkHours = 4.0m
            });
        }

        // Act
        var result = await service.GenerateMonthlyPayrollAsync(new GeneratePayrollBatchRequest("2026-11", branchId));

        // Assert
        Assert.Single(result);
        var payslip = result[0];
        Assert.Equal(20, payslip.TotalShifts);
        Assert.Equal(3_000_000m, payslip.BaseSalaryEarned); // 20 ca * 150,000 VND
        Assert.Equal(0m, payslip.SocialInsurance);
        Assert.Equal(3_000_000m, payslip.NetSalary);
    }

    [Fact]
    public async Task GenerateMonthlyPayroll_ExistingPaidPayslip_PreservesAndDoesNotOverwrite()
    {
        // Arrange (REQ-PAY-03 Scenario 1: Chặn ghi đè phiếu Paid)
        var (service, payslipRepo, empRepo, schedRepo, policyRepo, tenantContext, uow) = CreateService();
        var branchId = Guid.NewGuid();

        var employee = new Employee
        {
            Id = Guid.NewGuid(),
            BranchId = branchId,
            EmployeeCode = "NV_PAID",
            FullName = "Nguyen Paid",
            SalaryType = SalaryType.Monthly,
            BaseRate = 10_000_000m,
            Status = EmployeeStatus.Active
        };
        empRepo.Employees.Add(employee);

        var existingPaidPayslip = new Payslip
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant_test_01",
            BranchId = branchId,
            EmployeeId = employee.Id,
            Employee = employee,
            PayslipCode = "PL-202611-0001",
            PeriodMonth = "2026-11",
            NetSalary = 9_000_000m,
            Status = PayslipStatus.Paid
        };
        payslipRepo.Payslips.Add(existingPaidPayslip);

        // Act: Generate batch again for this month
        var result = await service.GenerateMonthlyPayrollAsync(new GeneratePayrollBatchRequest("2026-11", branchId));

        // Assert: Existing paid payslip is preserved without mutation
        Assert.Single(result);
        var payslip = result[0];
        Assert.Equal(PayslipStatus.Paid, payslip.Status);
        Assert.Equal(9_000_000m, payslip.NetSalary);
        Assert.Equal("PL-202611-0001", payslip.PayslipCode);
    }

    [Fact]
    public async Task BatchPayPayslips_MultipleUnpaidPayslips_MarksAllPaidAndGeneratesCashbook()
    {
        // Arrange (REQ-PAY-04: Chi trả theo lô)
        var (service, payslipRepo, empRepo, schedRepo, policyRepo, tenantContext, uow) = CreateService();
        var branchId = Guid.NewGuid();

        var p1 = new Payslip
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant_test_01",
            BranchId = branchId,
            EmployeeId = Guid.NewGuid(),
            PayslipCode = "PL-202611-0001",
            PeriodMonth = "2026-11",
            NetSalary = 5_000_000m,
            Status = PayslipStatus.Unpaid
        };
        var p2 = new Payslip
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant_test_01",
            BranchId = branchId,
            EmployeeId = Guid.NewGuid(),
            PayslipCode = "PL-202611-0002",
            PeriodMonth = "2026-11",
            NetSalary = 6_000_000m,
            Status = PayslipStatus.Unpaid
        };
        payslipRepo.Payslips.Add(p1);
        payslipRepo.Payslips.Add(p2);

        var request = new BatchPayPayslipsRequest(
            PayslipIds: new List<Guid> { p1.Id, p2.Id },
            PaymentMethod: "VIETQR",
            Note: "Chi tra luong thang 11 qua VietQR"
        );

        // Act
        var result = await service.BatchPayPayslipsAsync(request);

        // Assert
        Assert.Equal(2, result.TotalRequested);
        Assert.Equal(2, result.SuccessCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Equal(PayslipStatus.Paid, p1.Status);
        Assert.Equal(PayslipStatus.Paid, p2.Status);
        Assert.Equal(2, payslipRepo.CashbookTransactions.Count);
        Assert.True(uow.SaveChangesCalled);
    }

    [Fact]
    public async Task GenerateMonthlyPayroll_OnlyGeneratesForEmployeesWithSchedulesInPeriod()
    {
        // Arrange
        var (service, payslipRepo, empRepo, schedRepo, policyRepo, tenantContext, uow) = CreateService();
        var branchId = Guid.NewGuid();

        var empWithSchedule = new Employee
        {
            Id = Guid.NewGuid(),
            BranchId = branchId,
            EmployeeCode = "NV_HAS_SCHED",
            FullName = "Co Ca Lam",
            SalaryType = SalaryType.Monthly,
            BaseRate = 8_000_000m,
            Status = EmployeeStatus.Active
        };
        var empWithoutSchedule = new Employee
        {
            Id = Guid.NewGuid(),
            BranchId = branchId,
            EmployeeCode = "NV_NO_SCHED",
            FullName = "Khong Co Ca",
            SalaryType = SalaryType.Monthly,
            BaseRate = 8_000_000m,
            Status = EmployeeStatus.Active
        };
        empRepo.Employees.Add(empWithSchedule);
        empRepo.Employees.Add(empWithoutSchedule);

        schedRepo.Schedules.Add(new EmployeeSchedule
        {
            Id = Guid.NewGuid(),
            EmployeeId = empWithSchedule.Id,
            BranchId = branchId,
            WorkDate = new DateTime(2026, 11, 10),
            Status = ScheduleStatus.Completed,
            ActualWorkHours = 8.0m
        });

        // Act
        var result = await service.GenerateMonthlyPayrollAsync(new GeneratePayrollBatchRequest("2026-11", branchId));

        // Assert: Chỉ tạo phiếu lương cho nhân viên có ca làm trong tháng
        Assert.Single(result);
        Assert.Equal(empWithSchedule.Id, result[0].EmployeeId);
    }

    [Fact]
    public async Task GenerateMonthlyPayroll_ManualPaidLeaveAndUnpaidLeave_DoesNotDoubleCountPaidLeaveAndCountsUnpaidLeave()
    {
        var (service, payslipRepo, empRepo, schedRepo, policyRepo, tenantContext, uow) = CreateService();
        var branchId = Guid.NewGuid();

        var employee = new Employee
        {
            Id = Guid.NewGuid(),
            BranchId = branchId,
            EmployeeCode = "NV_LEAVE",
            FullName = "Tran Nghi Phep",
            SalaryType = SalaryType.Shift,
            BaseRate = 200_000m,
            Status = EmployeeStatus.Active
        };
        empRepo.Employees.Add(employee);

        // 1 ca đi làm bình thường
        schedRepo.Schedules.Add(new EmployeeSchedule
        {
            Id = Guid.NewGuid(),
            EmployeeId = employee.Id,
            BranchId = branchId,
            WorkDate = new DateTime(2026, 11, 1),
            DurationHours = 8.0m,
            ActualWorkHours = 8.0m,
            Status = ScheduleStatus.Completed,
            ApprovalType = "WORKING",
            ApprovalStatus = ScheduleApprovalStatus.Approved
        });

        // 1 ca nghỉ phép có lương chấm công tay (Status = Completed, ActualWorkHours = DurationHours, ApprovalType = PAID_LEAVE)
        schedRepo.Schedules.Add(new EmployeeSchedule
        {
            Id = Guid.NewGuid(),
            EmployeeId = employee.Id,
            BranchId = branchId,
            WorkDate = new DateTime(2026, 11, 2),
            DurationHours = 8.0m,
            ActualWorkHours = 8.0m,
            IsLeave = true,
            IsPaidLeave = true,
            Status = ScheduleStatus.Completed,
            ApprovalType = "PAID_LEAVE",
            CheckInMethod = "MANUAL_PAID_LEAVE",
            ApprovalStatus = ScheduleApprovalStatus.Approved
        });

        // 1 ca nghỉ không lương chấm công tay (Status = Cancelled, ApprovalType = UNPAID_LEAVE)
        schedRepo.Schedules.Add(new EmployeeSchedule
        {
            Id = Guid.NewGuid(),
            EmployeeId = employee.Id,
            BranchId = branchId,
            WorkDate = new DateTime(2026, 11, 3),
            DurationHours = 8.0m,
            ActualWorkHours = 0.0m,
            IsLeave = true,
            IsPaidLeave = false,
            Status = ScheduleStatus.Cancelled,
            ApprovalType = "UNPAID_LEAVE",
            CheckInMethod = "MANUAL_UNPAID_LEAVE",
            ApprovalStatus = ScheduleApprovalStatus.Approved
        });

        var result = await service.GenerateMonthlyPayrollAsync(new GeneratePayrollBatchRequest("2026-11", branchId));

        Assert.Single(result);
        var payslip = result[0];
        Assert.Equal(1, payslip.TotalShifts);
        Assert.Equal(8.0m, payslip.TotalWorkHours);
        Assert.Equal(1m, payslip.PaidLeaveDays);
        Assert.Equal(1m, payslip.UnpaidLeaveDays);
        // 1 ca làm + 1 ca phép có lương = 2 * 200,000 = 400,000 (không bị nhân đôi thành 600,000)
        Assert.Equal(400_000m, payslip.BaseSalaryEarned);
    }

    [Fact]
    public async Task GenerateMonthlyPayroll_WithConversionRules_AppliesPolicyWorkUnitsAndLocksSchedulesOnPay()
    {
        var (service, payslipRepo, empRepo, schedRepo, policyRepo, tenantContext, uow) = CreateService();
        var branchId = Guid.NewGuid();

        policyRepo.Policies.Add(new BranchAttendancePolicy
        {
            Id = Guid.NewGuid(),
            BranchId = branchId,
            StandardDayHours = 8.0m,
            IsConvertHoursToDays = true,
            ConversionRulesJson = "[{\"type\":\"range\",\"fromHours\":1,\"fromMinutes\":0,\"toHours\":4,\"toMinutes\":30,\"workUnits\":0.5},{\"type\":\"gte\",\"fromHours\":4,\"fromMinutes\":30,\"workUnits\":1.0}]"
        });

        var employee = new Employee
        {
            Id = Guid.NewGuid(),
            BranchId = branchId,
            EmployeeCode = "NV_CONV",
            FullName = "Le Quy Doi",
            SalaryType = SalaryType.Monthly,
            BaseRate = 7_800_000m, // 300,000 VND / công (26 công)
            Status = EmployeeStatus.Active
        };
        empRepo.Employees.Add(employee);

        var s1 = new EmployeeSchedule
        {
            Id = Guid.NewGuid(),
            EmployeeId = employee.Id,
            BranchId = branchId,
            WorkDate = new DateTime(2026, 11, 5),
            DurationHours = 4.0m,
            ActualWorkHours = 3.5m, // 3.5h thuộc [1h..4h30) => 0.5 công
            Status = ScheduleStatus.Completed
        };
        var s2 = new EmployeeSchedule
        {
            Id = Guid.NewGuid(),
            EmployeeId = employee.Id,
            BranchId = branchId,
            WorkDate = new DateTime(2026, 11, 6),
            DurationHours = 8.0m,
            ActualWorkHours = 6.0m, // 6.0h >= 4h30 => 1.0 công
            Status = ScheduleStatus.Completed
        };
        schedRepo.Schedules.Add(s1);
        schedRepo.Schedules.Add(s2);

        var generated = await service.GenerateMonthlyPayrollAsync(new GeneratePayrollBatchRequest("2026-11", branchId));
        Assert.Single(generated);
        Assert.Equal(1.5m, generated[0].WorkedDays); // 0.5 + 1.0 = 1.5 công

        await service.PayPayslipAsync(generated[0].Id, new PayPayslipRequest("CASH", "Chi luong"));
        Assert.Equal(generated[0].Id, s1.PayslipId);
        Assert.Equal(generated[0].Id, s2.PayslipId);
    }

    [Fact]
    public async Task GenerateMonthlyPayroll_WhenPolicyNotConfigured_UsesDefaultConversionRulesAndCapsDailyWorkWithOtAt12Hours()
    {
        var (service, payslipRepo, empRepo, schedRepo, policyRepo, tenantContext, uow) = CreateService();
        var branchId = Guid.NewGuid();

        var employee = new Employee
        {
            Id = Guid.NewGuid(),
            BranchId = branchId,
            EmployeeCode = "NV_DEFAULT_POLICY",
            FullName = "Tran Mac Dinh",
            SalaryType = SalaryType.Monthly,
            BaseRate = 10_400_000m, // 26 * 8 = 208h => 50,000 VND/h
            Status = EmployeeStatus.Active
        };
        empRepo.Employees.Add(employee);

        // Ca 1 (Thứ 5 - 05/11/2026): Ca 4h => Mặc định 0.5 công
        schedRepo.Schedules.Add(new EmployeeSchedule
        {
            Id = Guid.NewGuid(),
            EmployeeId = employee.Id,
            BranchId = branchId,
            WorkDate = new DateTime(2026, 11, 5),
            DurationHours = 4.0m,
            ActualWorkHours = 4.0m,
            Status = ScheduleStatus.Completed
        });

        // Ca 2 (Chủ nhật - 08/11/2026): Ca 8h + OT 360 phút (6h), nhưng giới hạn 12h/ngày nên OT chỉ tính tối đa 4h
        schedRepo.Schedules.Add(new EmployeeSchedule
        {
            Id = Guid.NewGuid(),
            EmployeeId = employee.Id,
            BranchId = branchId,
            WorkDate = new DateTime(2026, 11, 8),
            DurationHours = 8.0m,
            ActualWorkHours = 8.0m,
            OvertimeMinutes = 360, // 6h raw, capped to 4h (8h + 4h = 12h)
            ApprovalStatus = ScheduleApprovalStatus.Approved,
            Status = ScheduleStatus.Completed
        });

        var generated = await service.GenerateMonthlyPayrollAsync(new GeneratePayrollBatchRequest("2026-11", branchId));

        Assert.Single(generated);
        var payslip = generated[0];
        Assert.Equal(1.5m, payslip.WorkedDays); // 0.5 (ca 4h) + 1.0 (ca 8h)
        Assert.Equal(4.0m, payslip.OtHours); // Capped at 4h instead of 6h
        // OT Sunday (200% default): 4h * 50,000 * 2.0 = 400,000
        Assert.Equal(400_000m, payslip.OvertimePay);
    }
}
