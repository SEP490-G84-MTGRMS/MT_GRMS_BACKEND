using MT_GRMS.Application.Common;
using MT_GRMS.Application.HR.AttendancePolicies;
using MT_GRMS.Application.HR.Employees;
using MT_GRMS.Application.HR.Employees.Dtos;
using MT_GRMS.Application.HR.Payslips;
using MT_GRMS.Application.HR.Payslips.Dtos;
using MT_GRMS.Application.HR.Schedules;
using MT_GRMS.Application.HR.Schedules.Dtos;
using MT_GRMS.Application.HR.ShiftConfigs;
using MT_GRMS.Application.HR.ShiftConfigs.Dtos;
using MT_GRMS.Domain.Entities.HR;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Domain.Exceptions;
using Xunit;

namespace MT_GRMS.UnitTests;

public class ScheduleServiceTests
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

    private class FakeScheduleRepository : IScheduleRepository
    {
        public List<EmployeeSchedule> Schedules { get; } = new();

        public Task<EmployeeSchedule?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Schedules.FirstOrDefault(s => s.Id == id && !s.IsDeleted));
        }

        public Task<IReadOnlyList<EmployeeSchedule>> GetListAsync(ScheduleQueryFilter filter, CancellationToken cancellationToken = default)
        {
            var query = Schedules.Where(s => !s.IsDeleted).AsQueryable();

            if (filter.BranchId.HasValue && filter.BranchId.Value != Guid.Empty)
                query = query.Where(s => s.BranchId == filter.BranchId.Value);

            if (filter.EmployeeId.HasValue && filter.EmployeeId.Value != Guid.Empty)
                query = query.Where(s => s.EmployeeId == filter.EmployeeId.Value);

            if (filter.FromDate.HasValue)
                query = query.Where(s => s.WorkDate >= filter.FromDate.Value.Date);

            if (filter.ToDate.HasValue)
                query = query.Where(s => s.WorkDate <= filter.ToDate.Value.Date);

            if (filter.Status.HasValue)
                query = query.Where(s => s.Status == filter.Status.Value);

            return Task.FromResult((IReadOnlyList<EmployeeSchedule>)query.ToList());
        }

        public Task<IReadOnlyList<EmployeeSchedule>> GetActiveSchedulesForEmployeesAsync(
            IEnumerable<Guid> employeeIds,
            DateTime fromDate,
            DateTime toDate,
            CancellationToken cancellationToken = default)
        {
            var idList = employeeIds.ToList();
            var from = fromDate.Date;
            var to = toDate.Date;

            var list = Schedules
                .Where(s => idList.Contains(s.EmployeeId)
                            && s.WorkDate >= from
                            && s.WorkDate <= to
                            && s.Status != ScheduleStatus.Cancelled
                            && !s.IsDeleted)
                .ToList();

            return Task.FromResult((IReadOnlyList<EmployeeSchedule>)list);
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

            return Task.FromResult((IReadOnlyList<EmployeeSchedule>)list);
        }

        public Task<IReadOnlyList<EmployeeSchedule>> GetCompletedSchedulesInPeriodAsync(IEnumerable<Guid> employeeIds, DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default)
        {
            var set = employeeIds.ToHashSet();
            var list = Schedules
                .Where(s => !s.IsDeleted && set.Contains(s.EmployeeId) && s.WorkDate >= fromDate.Date && s.WorkDate <= toDate.Date && s.Status == Domain.Enums.ScheduleStatus.Completed)
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
            var index = Schedules.FindIndex(s => s.Id == schedule.Id);
            if (index >= 0)
            {
                Schedules[index] = schedule;
            }
        }

        public void Delete(EmployeeSchedule schedule)
        {
            schedule.IsDeleted = true;
        }
    }

    private class FakeEmployeeRepository : IEmployeeRepository
    {
        public List<Employee> Employees { get; } = new();

        public Task<IReadOnlyList<Employee>> GetActiveEmployeesAsync(Guid? branchId, CancellationToken cancellationToken = default)
        {
            var query = Employees.Where(e => !e.IsDeleted && e.Status == Domain.Enums.EmployeeStatus.Active);
            if (branchId.HasValue && branchId.Value != Guid.Empty)
            {
                query = query.Where(e => e.BranchId == branchId.Value);
            }
            return Task.FromResult<IReadOnlyList<Employee>>(query.ToList());
        }

        public Task<Employee?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Employees.FirstOrDefault(e => e.Id == id && !e.IsDeleted));
        }

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

        public void Update(Employee employee) { }
        public void Delete(Employee employee) => employee.IsDeleted = true;
        public void RemoveAllowances(IEnumerable<EmployeeAllowance> allowances) { }
        public void SyncAllowances(Employee employee, IEnumerable<EmployeeAllowanceDto> newAllowances, string tenantId) { }
        public Task<bool> HasWorkOrPayrollHistoryAsync(Guid employeeId, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private class FakeShiftConfigRepository : IShiftConfigRepository
    {
        public List<ShiftConfig> ShiftConfigs { get; } = new();

        public Task<ShiftConfig?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(ShiftConfigs.FirstOrDefault(s => s.Id == id && !s.IsDeleted));

        public Task<IReadOnlyList<ShiftConfig>> GetByBranchIdAsync(Guid? branchId, bool? isActive = null, CancellationToken cancellationToken = default)
            => Task.FromResult((IReadOnlyList<ShiftConfig>)ShiftConfigs);

        public Task<(IReadOnlyList<ShiftConfig> Items, int TotalCount)> GetPagedListAsync(ShiftConfigQueryFilter filter, CancellationToken cancellationToken = default)
            => Task.FromResult(((IReadOnlyList<ShiftConfig>)ShiftConfigs, ShiftConfigs.Count));

        public Task<ShiftConfig?> GetByCodeAsync(Guid branchId, string shiftCode, CancellationToken cancellationToken = default)
            => Task.FromResult(ShiftConfigs.FirstOrDefault(s => s.ShiftCode == shiftCode));

        public Task<bool> ExistsAsync(System.Linq.Expressions.Expression<Func<ShiftConfig, bool>> predicate, CancellationToken cancellationToken = default)
            => Task.FromResult(ShiftConfigs.AsQueryable().Any(predicate));

        public Task AddAsync(ShiftConfig shiftConfig, CancellationToken cancellationToken = default)
        {
            ShiftConfigs.Add(shiftConfig);
            return Task.CompletedTask;
        }

        public void Update(ShiftConfig shiftConfig) { }
        public void Delete(ShiftConfig shiftConfig) => shiftConfig.IsDeleted = true;
    }

    private class FakeBranchAttendancePolicyRepository : IBranchAttendancePolicyRepository
    {
        public List<BranchAttendancePolicy> Policies { get; } = new();

        public Task<BranchAttendancePolicy?> GetByBranchIdAsync(Guid branchId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Policies.FirstOrDefault(p => p.BranchId == branchId && !p.IsDeleted));
        }

        public Task<IReadOnlyList<BranchAttendancePolicy>> GetByBranchIdsAsync(IEnumerable<Guid> branchIds, CancellationToken cancellationToken = default)
        {
            var idSet = branchIds.ToHashSet();
            var list = Policies.Where(p => idSet.Contains(p.BranchId) && !p.IsDeleted).ToList();
            return Task.FromResult((IReadOnlyList<BranchAttendancePolicy>)list);
        }

        public Task AddAsync(BranchAttendancePolicy policy, CancellationToken cancellationToken = default)
        {
            Policies.Add(policy);
            return Task.CompletedTask;
        }

        public void Update(BranchAttendancePolicy policy)
        {
            var index = Policies.FindIndex(p => p.Id == policy.Id);
            if (index >= 0) Policies[index] = policy;
        }
    }

    private class FakePayslipRepository : IPayslipRepository
    {
        public List<Payslip> Payslips { get; } = new();

        public Task<Payslip?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(Payslips.FirstOrDefault(p => p.Id == id && !p.IsDeleted));

        public Task<Payslip?> GetByEmployeeAndPeriodAsync(Guid employeeId, string periodMonth, CancellationToken cancellationToken = default)
            => Task.FromResult(Payslips.FirstOrDefault(p => p.EmployeeId == employeeId && p.PeriodMonth == periodMonth && !p.IsDeleted));

        public Task<(IReadOnlyList<Payslip> Items, int TotalCount)> GetPagedListAsync(PayslipQueryFilter filter, CancellationToken cancellationToken = default)
            => Task.FromResult(((IReadOnlyList<Payslip>)Payslips, Payslips.Count));

        public Task<IReadOnlyList<Payslip>> GetByPeriodAndBranchAsync(string periodMonth, Guid? branchId, CancellationToken cancellationToken = default)
            => Task.FromResult((IReadOnlyList<Payslip>)Payslips.Where(p => p.PeriodMonth == periodMonth && !p.IsDeleted).ToList());

        public Task AddAsync(Payslip payslip, CancellationToken cancellationToken = default)
        {
            Payslips.Add(payslip);
            return Task.CompletedTask;
        }

        public Task AddRangeAsync(IEnumerable<Payslip> payslips, CancellationToken cancellationToken = default)
        {
            Payslips.AddRange(payslips);
            return Task.CompletedTask;
        }

        public void Update(Payslip payslip) { }
        public void Delete(Payslip payslip) => payslip.IsDeleted = true;
        public Task<int> GetCountByPeriodMonthAsync(string periodMonth, CancellationToken cancellationToken = default)
            => Task.FromResult(Payslips.Count(p => p.PeriodMonth == periodMonth));
        public Task AddCashbookTransactionAsync(MT_GRMS.Domain.Entities.Finance.CashbookTransaction transaction, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    [Fact]
    public async Task BatchCreateAsync_ShouldSucceed_WhenNoConflicts()
    {
        // Arrange
        var scheduleRepo = new FakeScheduleRepository();
        var employeeRepo = new FakeEmployeeRepository();
        var shiftConfigRepo = new FakeShiftConfigRepository();
        var uow = new FakeUnitOfWork();
        var tenantContext = new FakeTenantContext();

        var emp1 = new Employee { Id = Guid.NewGuid(), FullName = "Nguyen Van A", EmployeeCode = "NV0001" };
        var emp2 = new Employee { Id = Guid.NewGuid(), FullName = "Tran Thi B", EmployeeCode = "NV0002" };
        employeeRepo.Employees.Add(emp1);
        employeeRepo.Employees.Add(emp2);

        var branchId = Guid.NewGuid();
        var workDate = new DateTime(2026, 11, 10);

        var service = new ScheduleService(scheduleRepo, employeeRepo, shiftConfigRepo, uow, tenantContext);

        var request = new BatchCreateScheduleRequest(
            branchId,
            new List<CreateScheduleItemRequest>
            {
                new(emp1.Id, branchId, workDate, null, "CA_SANG", "Ca Sáng", TimeSpan.FromHours(8), TimeSpan.FromHours(14)),
                new(emp2.Id, branchId, workDate, null, "CA_TOI", "Ca Tối", TimeSpan.FromHours(15), TimeSpan.FromHours(22))
            }
        );

        // Act
        var result = await service.BatchCreateAsync(request);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal(2, scheduleRepo.Schedules.Count);
        Assert.All(result, r => Assert.Equal(ScheduleStatus.Draft, r.Status));
        Assert.True(uow.SaveChangesCalled);
    }

    [Fact]
    public async Task BatchCreateAsync_ShouldAllow_MultipleShiftsPerDay_WhenOverlapUnder12Hours()
    {
        // Arrange
        var scheduleRepo = new FakeScheduleRepository();
        var employeeRepo = new FakeEmployeeRepository();
        var shiftConfigRepo = new FakeShiftConfigRepository();
        var uow = new FakeUnitOfWork();
        var tenantContext = new FakeTenantContext();

        var emp = new Employee { Id = Guid.NewGuid(), FullName = "Nguyen Van A", EmployeeCode = "NV0001" };
        employeeRepo.Employees.Add(emp);

        var branchId = Guid.NewGuid();
        var workDate = new DateTime(2026, 11, 10);

        var service = new ScheduleService(scheduleRepo, employeeRepo, shiftConfigRepo, uow, tenantContext);

        // Ca 1: 06:00 - 12:00 (6h), Ca 2: 14:00 - 18:00 (4h) (Không trùng giờ, tổng 10h <= 12 giờ -> HỢP LỆ)
        var request = new BatchCreateScheduleRequest(
            branchId,
            new List<CreateScheduleItemRequest>
            {
                new(emp.Id, branchId, workDate, null, "CA_SANG", "Ca Sáng", TimeSpan.FromHours(6), TimeSpan.FromHours(12)),
                new(emp.Id, branchId, workDate, null, "CA_CHIEU", "Ca Chiều", TimeSpan.FromHours(14), TimeSpan.FromHours(18))
            }
        );

        // Act
        var result = await service.BatchCreateAsync(request);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal(2, scheduleRepo.Schedules.Count);
    }

    [Fact]
    public async Task BatchCreateAsync_ShouldThrowConflictException_WhenOverlapExceeds12Hours()
    {
        // Arrange
        var scheduleRepo = new FakeScheduleRepository();
        var employeeRepo = new FakeEmployeeRepository();
        var shiftConfigRepo = new FakeShiftConfigRepository();
        var uow = new FakeUnitOfWork();
        var tenantContext = new FakeTenantContext();

        var emp = new Employee { Id = Guid.NewGuid(), FullName = "Nguyen Van A", EmployeeCode = "NV0001" };
        employeeRepo.Employees.Add(emp);

        var branchId = Guid.NewGuid();
        var workDate = new DateTime(2026, 11, 10);

        var service = new ScheduleService(scheduleRepo, employeeRepo, shiftConfigRepo, uow, tenantContext);

        // Ca 1: 06:00 - 14:00 (8h), Ca 2: 15:00 - 20:00 (5h) (Tổng 13h > 12 giờ -> BÁO LỖI SCHEDULE_DAILY_HOURS_EXCEEDED)
        var request = new BatchCreateScheduleRequest(
            branchId,
            new List<CreateScheduleItemRequest>
            {
                new(emp.Id, branchId, workDate, null, "CA_DAI_1", "Ca Dài 1", TimeSpan.FromHours(6), TimeSpan.FromHours(14)),
                new(emp.Id, branchId, workDate, null, "CA_DAI_2", "Ca Dài 2", TimeSpan.FromHours(15), TimeSpan.FromHours(20))
            }
        );

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.BatchCreateAsync(request));
        Assert.Equal("SCHEDULE_DAILY_HOURS_EXCEEDED", ex.ErrorCode);
        Assert.Contains("vượt quá giới hạn tối đa 12h", ex.Message);
    }

    [Fact]
    public async Task BatchCreateAsync_ShouldAllow_OverlappingDatabaseSchedule_WhenOverlapUnder12Hours()
    {
        // Arrange
        var scheduleRepo = new FakeScheduleRepository();
        var employeeRepo = new FakeEmployeeRepository();
        var shiftConfigRepo = new FakeShiftConfigRepository();
        var uow = new FakeUnitOfWork();
        var tenantContext = new FakeTenantContext();

        var emp = new Employee { Id = Guid.NewGuid(), FullName = "Nguyen Van A", EmployeeCode = "NV0001" };
        employeeRepo.Employees.Add(emp);

        var branchId = Guid.NewGuid();
        var workDate = new DateTime(2026, 11, 5);

        // Đã có ca sẵn trong DB: 06:00 - 12:00
        scheduleRepo.Schedules.Add(new EmployeeSchedule
        {
            Id = Guid.NewGuid(),
            EmployeeId = emp.Id,
            BranchId = branchId,
            WorkDate = workDate,
            ShiftName = "Ca Sáng",
            PlannedStartTime = TimeSpan.FromHours(6),
            PlannedEndTime = TimeSpan.FromHours(12),
            Status = ScheduleStatus.Draft
        });

        var service = new ScheduleService(scheduleRepo, employeeRepo, shiftConfigRepo, uow, tenantContext);

        // Gửi xếp thêm ca: 14:00 - 18:00 (Không trùng giờ, tổng 10h <= 12 giờ -> Cho phép)
        var request = new BatchCreateScheduleRequest(
            branchId,
            new List<CreateScheduleItemRequest>
            {
                new(emp.Id, branchId, workDate, null, "CA_CHIEU", "Ca Chiều", TimeSpan.FromHours(14), TimeSpan.FromHours(18))
            }
        );

        // Act
        var result = await service.BatchCreateAsync(request);

        // Assert
        Assert.Single(result);
        Assert.Equal(2, scheduleRepo.Schedules.Count);
    }

    [Fact]
    public async Task BatchCreateAsync_ShouldThrowConflictException_WhenOverlappingWithDatabaseScheduleExceeds12Hours()
    {
        // Arrange
        var scheduleRepo = new FakeScheduleRepository();
        var employeeRepo = new FakeEmployeeRepository();
        var shiftConfigRepo = new FakeShiftConfigRepository();
        var uow = new FakeUnitOfWork();
        var tenantContext = new FakeTenantContext();

        var emp = new Employee { Id = Guid.NewGuid(), FullName = "Nguyen Van A", EmployeeCode = "NV0001" };
        employeeRepo.Employees.Add(emp);

        var branchId = Guid.NewGuid();
        var workDate = new DateTime(2026, 11, 5);

        // Đã có ca sẵn trong DB: 06:00 - 14:00 (8h)
        scheduleRepo.Schedules.Add(new EmployeeSchedule
        {
            Id = Guid.NewGuid(),
            EmployeeId = emp.Id,
            BranchId = branchId,
            WorkDate = workDate,
            ShiftName = "Ca Dài 1",
            PlannedStartTime = TimeSpan.FromHours(6),
            PlannedEndTime = TimeSpan.FromHours(14),
            DurationHours = 8.00m,
            Status = ScheduleStatus.Draft
        });

        var service = new ScheduleService(scheduleRepo, employeeRepo, shiftConfigRepo, uow, tenantContext);

        // Gửi xếp thêm ca: 15:00 - 20:00 (5h) (Tổng 13h > 12 giờ -> Ném ngoại lệ)
        var request = new BatchCreateScheduleRequest(
            branchId,
            new List<CreateScheduleItemRequest>
            {
                new(emp.Id, branchId, workDate, null, "CA_DAI_2", "Ca Dài 2", TimeSpan.FromHours(15), TimeSpan.FromHours(20))
            }
        );

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.BatchCreateAsync(request));
        Assert.Equal("SCHEDULE_DAILY_HOURS_EXCEEDED", ex.ErrorCode);
        Assert.Contains("vượt quá giới hạn tối đa 12h", ex.Message);
    }

    [Fact]
    public async Task CreateRecurringAsync_ShouldThrow_WhenEndDateBeforeStartDate()
    {
        // Arrange
        var scheduleRepo = new FakeScheduleRepository();
        var employeeRepo = new FakeEmployeeRepository();
        var shiftConfigRepo = new FakeShiftConfigRepository();
        var uow = new FakeUnitOfWork();
        var tenantContext = new FakeTenantContext();

        var emp = new Employee { Id = Guid.NewGuid(), FullName = "Nguyen Van A", EmployeeCode = "NV0001" };
        employeeRepo.Employees.Add(emp);
        var branchId = Guid.NewGuid();

        var service = new ScheduleService(scheduleRepo, employeeRepo, shiftConfigRepo, uow, tenantContext);

        var startDate = new DateTime(2026, 11, 10);
        var endDate = new DateTime(2026, 11, 5); // Trước ngày bắt đầu

        var request = new CreateRecurringScheduleRequest(
            emp.Id,
            branchId,
            startDate,
            HasEndDate: true,
            EndDate: endDate,
            RepeatDays: new[] { "T2", "T4", "T6" }
        );

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.CreateRecurringAsync(request));
        Assert.Equal("INVALID_DATE_RANGE", ex.ErrorCode);
    }

    [Fact]
    public async Task CreateRecurringAsync_ShouldGenerateUntilEndDate_WhenEndDateSpecified()
    {
        // Arrange
        var scheduleRepo = new FakeScheduleRepository();
        var employeeRepo = new FakeEmployeeRepository();
        var shiftConfigRepo = new FakeShiftConfigRepository();
        var uow = new FakeUnitOfWork();
        var tenantContext = new FakeTenantContext();

        var emp = new Employee { Id = Guid.NewGuid(), FullName = "Nguyen Van A", EmployeeCode = "NV0001" };
        employeeRepo.Employees.Add(emp);
        var branchId = Guid.NewGuid();

        var service = new ScheduleService(scheduleRepo, employeeRepo, shiftConfigRepo, uow, tenantContext);

        var startDate = new DateTime(2026, 11, 1);
        var endDate = new DateTime(2026, 11, 20); // Ngày kết thúc chỉ định

        var request = new CreateRecurringScheduleRequest(
            emp.Id,
            branchId,
            startDate,
            HasEndDate: true,
            EndDate: endDate,
            RepeatDays: new[] { "CN" }
        );

        // Act
        var result = await service.CreateRecurringAsync(request);

        // Assert
        Assert.NotEmpty(result);
        Assert.All(result, s => Assert.True(s.WorkDate <= endDate));
    }

    [Fact]
    public async Task CreateRecurringAsync_ShouldGenerateUntilEndOfNextMonth_WhenNoEndDateSpecified()
    {
        // Arrange
        var scheduleRepo = new FakeScheduleRepository();
        var employeeRepo = new FakeEmployeeRepository();
        var shiftConfigRepo = new FakeShiftConfigRepository();
        var uow = new FakeUnitOfWork();
        var tenantContext = new FakeTenantContext();

        var emp = new Employee { Id = Guid.NewGuid(), FullName = "Nguyen Van A", EmployeeCode = "NV0001" };
        employeeRepo.Employees.Add(emp);
        var branchId = Guid.NewGuid();

        var service = new ScheduleService(scheduleRepo, employeeRepo, shiftConfigRepo, uow, tenantContext);

        var startDate = new DateTime(2026, 11, 6); // Tháng 11/2026 -> hết tháng sau là 31/12/2026

        var request = new CreateRecurringScheduleRequest(
            emp.Id,
            branchId,
            startDate,
            HasEndDate: false,
            EndDate: null,
            RepeatDays: new[] { "CN" } // Lặp Chủ nhật
        );

        // Act
        var result = await service.CreateRecurringAsync(request);

        // Assert: Các ca phải được tạo đến hết tháng 12/2026 (31/12/2026)
        Assert.NotEmpty(result);
        var maxWorkDate = result.Max(s => s.WorkDate);
        Assert.True(maxWorkDate <= new DateTime(2026, 12, 31));
        Assert.True(maxWorkDate >= new DateTime(2026, 12, 25)); // Ca Chủ nhật cuối cùng trong tháng 12/2026
        Assert.All(result, s => Assert.True(s.WorkDate <= new DateTime(2026, 12, 31)));
    }

    [Fact]
    public async Task BatchCreateAsync_ShouldAllow_AdjacentBackToBackShifts()
    {
        // Arrange
        var scheduleRepo = new FakeScheduleRepository();
        var employeeRepo = new FakeEmployeeRepository();
        var shiftConfigRepo = new FakeShiftConfigRepository();
        var uow = new FakeUnitOfWork();
        var tenantContext = new FakeTenantContext();

        var emp = new Employee { Id = Guid.NewGuid(), FullName = "Nguyen Van A", EmployeeCode = "NV0001" };
        employeeRepo.Employees.Add(emp);

        var branchId = Guid.NewGuid();
        var workDate = new DateTime(2026, 11, 10);

        var service = new ScheduleService(scheduleRepo, employeeRepo, shiftConfigRepo, uow, tenantContext);

        // Ca 1: 08:00 - 14:00, Ca 2: 14:00 - 20:00 (Liền kề nhau, không giao nhau)
        var request = new BatchCreateScheduleRequest(
            branchId,
            new List<CreateScheduleItemRequest>
            {
                new(emp.Id, branchId, workDate, null, "CA_1", "Ca 1", TimeSpan.FromHours(8), TimeSpan.FromHours(14)),
                new(emp.Id, branchId, workDate, null, "CA_2", "Ca 2", TimeSpan.FromHours(14), TimeSpan.FromHours(20))
            }
        );

        // Act
        var result = await service.BatchCreateAsync(request);

        // Assert
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task PublishAsync_ShouldUpdateStatusToPublished_ForDraftSchedules()
    {
        // Arrange
        var scheduleRepo = new FakeScheduleRepository();
        var employeeRepo = new FakeEmployeeRepository();
        var shiftConfigRepo = new FakeShiftConfigRepository();
        var uow = new FakeUnitOfWork();
        var tenantContext = new FakeTenantContext();

        var branchId = Guid.NewGuid();
        var fromDate = new DateTime(2026, 11, 1);
        var toDate = new DateTime(2026, 11, 7);

        scheduleRepo.Schedules.Add(new EmployeeSchedule { Id = Guid.NewGuid(), BranchId = branchId, WorkDate = new DateTime(2026, 11, 2), Status = ScheduleStatus.Draft });
        scheduleRepo.Schedules.Add(new EmployeeSchedule { Id = Guid.NewGuid(), BranchId = branchId, WorkDate = new DateTime(2026, 11, 4), Status = ScheduleStatus.Draft });
        scheduleRepo.Schedules.Add(new EmployeeSchedule { Id = Guid.NewGuid(), BranchId = branchId, WorkDate = new DateTime(2026, 11, 6), Status = ScheduleStatus.Draft });

        var service = new ScheduleService(scheduleRepo, employeeRepo, shiftConfigRepo, uow, tenantContext);

        // Act
        var response = await service.PublishAsync(new PublishSchedulesRequest(branchId, fromDate, toDate));

        // Assert
        Assert.Equal(3, response.PublishedCount);
        Assert.All(scheduleRepo.Schedules, s => Assert.Equal(ScheduleStatus.Published, s.Status));
        Assert.True(uow.SaveChangesCalled);
    }

    [Fact]
    public async Task GetMySchedulesAsync_ShouldReturnSchedulesForCurrentUser()
    {
        // Arrange
        var scheduleRepo = new FakeScheduleRepository();
        var employeeRepo = new FakeEmployeeRepository();
        var shiftConfigRepo = new FakeShiftConfigRepository();
        var uow = new FakeUnitOfWork();
        var tenantContext = new FakeTenantContext();

        var userId = Guid.NewGuid().ToString();
        var emp = new Employee { Id = Guid.NewGuid(), UserId = userId, FullName = "Nguyen Van A" };
        employeeRepo.Employees.Add(emp);

        scheduleRepo.Schedules.Add(new EmployeeSchedule { Id = Guid.NewGuid(), EmployeeId = emp.Id, WorkDate = new DateTime(2026, 11, 5), Employee = emp });
        scheduleRepo.Schedules.Add(new EmployeeSchedule { Id = Guid.NewGuid(), EmployeeId = Guid.NewGuid(), WorkDate = new DateTime(2026, 11, 5) });

        var service = new ScheduleService(scheduleRepo, employeeRepo, shiftConfigRepo, uow, tenantContext);

        // Act
        var result = await service.GetMySchedulesAsync(userId, null, null);

        // Assert
        Assert.Single(result);
        Assert.Equal(emp.Id, result[0].EmployeeId);
    }

    [Fact]
    public async Task RecordManualAttendanceAsync_Working_ShouldCalculateLateAndHoursCorrectly()
    {
        // Arrange
        var scheduleRepo = new FakeScheduleRepository();
        var employeeRepo = new FakeEmployeeRepository();
        var shiftConfigRepo = new FakeShiftConfigRepository();
        var policyRepo = new FakeBranchAttendancePolicyRepository();
        var uow = new FakeUnitOfWork();
        var tenantContext = new FakeTenantContext();

        var branchId = Guid.NewGuid();
        var emp = new Employee { Id = Guid.NewGuid(), FullName = "Nguyen Van B", EmployeeCode = "NV001" };
        employeeRepo.Employees.Add(emp);

        var schedule = new EmployeeSchedule
        {
            Id = Guid.NewGuid(),
            EmployeeId = emp.Id,
            BranchId = branchId,
            WorkDate = new DateTime(2026, 11, 6),
            PlannedStartTime = new TimeSpan(8, 0, 0),
            PlannedEndTime = new TimeSpan(12, 0, 0),
            DurationHours = 4.00m,
            Employee = emp
        };
        scheduleRepo.Schedules.Add(schedule);

        // Branch policy: Bật tính trễ sau 15 phút
        policyRepo.Policies.Add(new BranchAttendancePolicy
        {
            Id = Guid.NewGuid(),
            BranchId = branchId,
            EnableLateThreshold = true,
            LateThresholdMinutes = 15,
            EnableEarlyLeaveThreshold = true,
            EarlyLeaveThresholdMinutes = 10
        });

        var service = new ScheduleService(scheduleRepo, employeeRepo, shiftConfigRepo, uow, tenantContext, policyRepo);

        // Chấm công vào lúc 08:20 (trễ 20 phút > 15 phút ngưỡng => late 20 phút), ra đúng 12:00
        var request = new ManualAttendanceRequest(
            AttendanceType: ManualAttendanceType.Working,
            CheckInTime: new TimeSpan(8, 20, 0),
            CheckOutTime: new TimeSpan(12, 0, 0),
            Note: "Đi muộn do kẹt xe"
        );

        // Act
        var result = await service.RecordManualAttendanceAsync(schedule.Id, request);

        // Assert
        Assert.Equal(ScheduleStatus.Completed, result.Status);
        Assert.Equal("WORKING", result.ApprovalType);
        Assert.Equal(ScheduleApprovalStatus.Approved, result.ApprovalStatus);
        Assert.Equal(20, result.LateMinutes);
        Assert.Equal(0, result.EarlyLeaveMinutes);
        Assert.Equal(3.67m, result.ActualWorkHours);
        Assert.Equal("Đi muộn do kẹt xe", result.ApprovalReason);
        Assert.True(uow.SaveChangesCalled);
    }

    [Fact]
    public async Task RecordManualAttendanceAsync_Working_ShouldDefaultToPlannedTimesWhenNull()
    {
        // Arrange
        var scheduleRepo = new FakeScheduleRepository();
        var employeeRepo = new FakeEmployeeRepository();
        var shiftConfigRepo = new FakeShiftConfigRepository();
        var policyRepo = new FakeBranchAttendancePolicyRepository();
        var uow = new FakeUnitOfWork();
        var tenantContext = new FakeTenantContext();

        var branchId = Guid.NewGuid();
        var emp = new Employee { Id = Guid.NewGuid(), FullName = "Tran Thi C", EmployeeCode = "NV002" };
        employeeRepo.Employees.Add(emp);

        var schedule = new EmployeeSchedule
        {
            Id = Guid.NewGuid(),
            EmployeeId = emp.Id,
            BranchId = branchId,
            WorkDate = new DateTime(2026, 11, 6),
            PlannedStartTime = new TimeSpan(8, 0, 0),
            PlannedEndTime = new TimeSpan(16, 0, 0),
            DurationHours = 8.00m,
            Employee = emp
        };
        scheduleRepo.Schedules.Add(schedule);

        var service = new ScheduleService(scheduleRepo, employeeRepo, shiftConfigRepo, uow, tenantContext, policyRepo);

        // Không ghi giờ vào / ra => mặc định khớp ca làm ban đầu
        var request = new ManualAttendanceRequest(
            AttendanceType: ManualAttendanceType.Working,
            CheckInTime: null,
            CheckOutTime: null,
            Note: "Đủ công cả ngày"
        );

        // Act
        var result = await service.RecordManualAttendanceAsync(schedule.Id, request);

        // Assert
        Assert.Equal(ScheduleStatus.Completed, result.Status);
        Assert.Equal(new DateTime(2026, 11, 6, 8, 0, 0), result.CheckInTime);
        Assert.Equal(new DateTime(2026, 11, 6, 16, 0, 0), result.CheckOutTime);
        Assert.Equal(0, result.LateMinutes);
        Assert.Equal(0, result.EarlyLeaveMinutes);
        Assert.Equal(8.00m, result.ActualWorkHours);
    }

    [Fact]
    public async Task RecordManualAttendanceAsync_PaidLeave_ShouldGrantFullHoursAndCompletedStatus()
    {
        // Arrange
        var scheduleRepo = new FakeScheduleRepository();
        var employeeRepo = new FakeEmployeeRepository();
        var shiftConfigRepo = new FakeShiftConfigRepository();
        var uow = new FakeUnitOfWork();
        var tenantContext = new FakeTenantContext();

        var emp = new Employee { Id = Guid.NewGuid(), FullName = "Le Van D", EmployeeCode = "NV003" };
        employeeRepo.Employees.Add(emp);

        var schedule = new EmployeeSchedule
        {
            Id = Guid.NewGuid(),
            EmployeeId = emp.Id,
            BranchId = Guid.NewGuid(),
            WorkDate = new DateTime(2026, 11, 6),
            PlannedStartTime = new TimeSpan(8, 0, 0),
            PlannedEndTime = new TimeSpan(12, 0, 0),
            DurationHours = 4.00m,
            Employee = emp
        };
        scheduleRepo.Schedules.Add(schedule);

        var service = new ScheduleService(scheduleRepo, employeeRepo, shiftConfigRepo, uow, tenantContext);

        var request = new ManualAttendanceRequest(
            AttendanceType: ManualAttendanceType.PaidLeave,
            Note: "Nghỉ phép năm có lương"
        );

        // Act
        var result = await service.RecordManualAttendanceAsync(schedule.Id, request);

        // Assert
        Assert.Equal(ScheduleStatus.Completed, result.Status);
        Assert.Equal("PAID_LEAVE", result.ApprovalType);
        Assert.Null(result.CheckInTime);
        Assert.Null(result.CheckOutTime);
        Assert.Equal(4.00m, result.ActualWorkHours);
        Assert.Equal("Nghỉ phép năm có lương", result.ApprovalReason);
    }

    [Fact]
    public async Task RecordManualAttendanceAsync_UnpaidLeave_ShouldSetZeroHoursAndCancelledStatus()
    {
        // Arrange
        var scheduleRepo = new FakeScheduleRepository();
        var employeeRepo = new FakeEmployeeRepository();
        var shiftConfigRepo = new FakeShiftConfigRepository();
        var uow = new FakeUnitOfWork();
        var tenantContext = new FakeTenantContext();

        var emp = new Employee { Id = Guid.NewGuid(), FullName = "Pham Van E", EmployeeCode = "NV004" };
        employeeRepo.Employees.Add(emp);

        var schedule = new EmployeeSchedule
        {
            Id = Guid.NewGuid(),
            EmployeeId = emp.Id,
            BranchId = Guid.NewGuid(),
            WorkDate = new DateTime(2026, 11, 6),
            PlannedStartTime = new TimeSpan(13, 0, 0),
            PlannedEndTime = new TimeSpan(17, 0, 0),
            DurationHours = 4.00m,
            Employee = emp
        };
        scheduleRepo.Schedules.Add(schedule);

        var service = new ScheduleService(scheduleRepo, employeeRepo, shiftConfigRepo, uow, tenantContext);

        var request = new ManualAttendanceRequest(
            AttendanceType: ManualAttendanceType.UnpaidLeave,
            Note: "Nghỉ việc riêng không lương"
        );

        // Act
        var result = await service.RecordManualAttendanceAsync(schedule.Id, request);

        // Assert
        Assert.Equal(ScheduleStatus.Cancelled, result.Status);
        Assert.Equal("UNPAID_LEAVE", result.ApprovalType);
        Assert.Null(result.CheckInTime);
        Assert.Null(result.CheckOutTime);
        Assert.Equal(0.00m, result.ActualWorkHours);
        Assert.Equal("Nghỉ việc riêng không lương", result.ApprovalReason);
    }

    [Fact]
    public async Task GetTodayStaffDescriptorsAsync_ReturnsEmployeesWithFaceDescriptor()
    {
        var scheduleRepo = new FakeScheduleRepository();
        var employeeRepo = new FakeEmployeeRepository();
        var shiftConfigRepo = new FakeShiftConfigRepository();
        var uow = new FakeUnitOfWork();
        var tenantContext = new FakeTenantContext();

        var branchId = Guid.NewGuid();
        var emp1 = new Employee { Id = Guid.NewGuid(), FullName = "NV Co FaceID", EmployeeCode = "NV01", BranchId = branchId, FaceDescriptor = "[0.1, 0.2]" };
        var emp2 = new Employee { Id = Guid.NewGuid(), FullName = "NV Khong FaceID", EmployeeCode = "NV02", BranchId = branchId, FaceDescriptor = null };
        employeeRepo.Employees.Add(emp1);
        employeeRepo.Employees.Add(emp2);

        var service = new ScheduleService(scheduleRepo, employeeRepo, shiftConfigRepo, uow, tenantContext);

        var result = await service.GetTodayStaffDescriptorsAsync(branchId);

        Assert.Single(result);
        Assert.Equal("NV01", result[0].EmployeeCode);
        Assert.Equal("[0.1, 0.2]", result[0].FaceDescriptor);
    }

    [Fact]
    public async Task CheckInFaceIdAsync_ValidMatch_UpdatesCheckIn()
    {
        var scheduleRepo = new FakeScheduleRepository();
        var employeeRepo = new FakeEmployeeRepository();
        var shiftConfigRepo = new FakeShiftConfigRepository();
        var uow = new FakeUnitOfWork();
        var tenantContext = new FakeTenantContext();

        var empId = Guid.NewGuid();
        var schedule = new EmployeeSchedule
        {
            Id = Guid.NewGuid(),
            EmployeeId = empId,
            BranchId = Guid.NewGuid(),
            WorkDate = DateTime.UtcNow.Date,
            PlannedStartTime = new TimeSpan(8, 0, 0),
            PlannedEndTime = new TimeSpan(17, 0, 0),
            DurationHours = 8.00m
        };
        scheduleRepo.Schedules.Add(schedule);

        var service = new ScheduleService(scheduleRepo, employeeRepo, shiftConfigRepo, uow, tenantContext);

        var request = new FaceIdCheckInRequest(schedule.Id, empId, 0.35m, null);
        var result = await service.CheckInFaceIdAsync(request);

        Assert.NotNull(result.CheckInTime);
        Assert.Equal("FACE_ID", result.CheckInMethod);
        Assert.Equal(0.35m, result.FaceMatchScore);
        Assert.Equal(ScheduleStatus.InProgress, result.Status);
        Assert.True(uow.SaveChangesCalled);
    }

    [Fact]
    public async Task CheckInFaceIdAsync_ScoreBelowThreshold_ThrowsException()
    {
        var scheduleRepo = new FakeScheduleRepository();
        var employeeRepo = new FakeEmployeeRepository();
        var shiftConfigRepo = new FakeShiftConfigRepository();
        var uow = new FakeUnitOfWork();
        var tenantContext = new FakeTenantContext();

        var empId = Guid.NewGuid();
        var schedule = new EmployeeSchedule
        {
            Id = Guid.NewGuid(),
            EmployeeId = empId,
            BranchId = Guid.NewGuid(),
            WorkDate = DateTime.UtcNow.Date,
            PlannedStartTime = new TimeSpan(8, 0, 0),
            PlannedEndTime = new TimeSpan(17, 0, 0),
            DurationHours = 8.00m
        };
        scheduleRepo.Schedules.Add(schedule);

        var service = new ScheduleService(scheduleRepo, employeeRepo, shiftConfigRepo, uow, tenantContext);

        var request = new FaceIdCheckInRequest(schedule.Id, empId, 0.75m, null);
        await Assert.ThrowsAsync<BusinessException>(() => service.CheckInFaceIdAsync(request));
    }

    [Fact]
    public async Task CheckOutFaceIdAsync_ValidMatch_UpdatesCheckOutAndCalculatesHours()
    {
        var scheduleRepo = new FakeScheduleRepository();
        var employeeRepo = new FakeEmployeeRepository();
        var shiftConfigRepo = new FakeShiftConfigRepository();
        var uow = new FakeUnitOfWork();
        var tenantContext = new FakeTenantContext();

        var empId = Guid.NewGuid();
        var nowVn = DateTime.UtcNow.AddHours(7);
        var checkInTime = nowVn.AddHours(-4);
        var schedule = new EmployeeSchedule
        {
            Id = Guid.NewGuid(),
            EmployeeId = empId,
            BranchId = Guid.NewGuid(),
            WorkDate = nowVn.Date,
            PlannedStartTime = nowVn.TimeOfDay.Subtract(TimeSpan.FromHours(5)),
            PlannedEndTime = nowVn.TimeOfDay.Add(TimeSpan.FromHours(5)),
            CheckInTime = checkInTime,
            CheckInMethod = "FACE_ID",
            Status = ScheduleStatus.InProgress
        };
        scheduleRepo.Schedules.Add(schedule);

        var service = new ScheduleService(scheduleRepo, employeeRepo, shiftConfigRepo, uow, tenantContext);

        var request = new FaceIdCheckOutRequest(schedule.Id, empId, 0.38m, null);
        var result = await service.CheckOutFaceIdAsync(request);

        Assert.NotNull(result.CheckOutTime);
        Assert.Equal("FACE_ID", result.CheckOutMethod);
        Assert.Equal(ScheduleStatus.Completed, result.Status);
        Assert.True(result.ActualWorkHours >= 3.9m);
        Assert.Equal(0, result.OvertimeMinutes); // Không tự tiện tính OT nếu chưa có đơn duyệt OT
        Assert.True(uow.SaveChangesCalled);
    }

    [Fact]
    public async Task CheckOutFaceIdAsync_DistanceExceedsThreshold_ThrowsException()
    {
        var scheduleRepo = new FakeScheduleRepository();
        var employeeRepo = new FakeEmployeeRepository();
        var shiftConfigRepo = new FakeShiftConfigRepository();
        var uow = new FakeUnitOfWork();
        var tenantContext = new FakeTenantContext();

        var empId = Guid.NewGuid();
        var schedule = new EmployeeSchedule
        {
            Id = Guid.NewGuid(),
            EmployeeId = empId,
            BranchId = Guid.NewGuid(),
            WorkDate = DateTime.UtcNow.Date,
            PlannedStartTime = new TimeSpan(8, 0, 0),
            PlannedEndTime = new TimeSpan(17, 0, 0),
            CheckInTime = DateTime.UtcNow.AddHours(-4),
            CheckInMethod = "FACE_ID",
            Status = ScheduleStatus.InProgress
        };
        scheduleRepo.Schedules.Add(schedule);

        var service = new ScheduleService(scheduleRepo, employeeRepo, shiftConfigRepo, uow, tenantContext);

        var request = new FaceIdCheckOutRequest(schedule.Id, empId, 0.72m, null);
        await Assert.ThrowsAsync<BusinessException>(() => service.CheckOutFaceIdAsync(request));
    }

    [Fact]
    public async Task RecordManualAttendanceAsync_EarlyCheckInLateCheckOut_WithoutApprovedOt_DoesNotGrantOvertime()
    {
        // Arrange: ca 08:00 - 12:00 (4h). Nhân viên vào sớm 07:30 (-30p), ra muộn 12:30 (+30p) nhưng KHÔNG có đơn OT
        var scheduleRepo = new FakeScheduleRepository();
        var employeeRepo = new FakeEmployeeRepository();
        var shiftConfigRepo = new FakeShiftConfigRepository();
        var uow = new FakeUnitOfWork();
        var tenantContext = new FakeTenantContext();

        var empId = Guid.NewGuid();
        var schedule = new EmployeeSchedule
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant1",
            EmployeeId = empId,
            WorkDate = new DateTime(2026, 11, 6),
            PlannedStartTime = new TimeSpan(8, 0, 0),
            PlannedEndTime = new TimeSpan(12, 0, 0),
            DurationHours = 4.0m,
            Status = ScheduleStatus.Scheduled,
            ApprovalType = null,
            OvertimeMinutes = 0
        };
        scheduleRepo.Schedules.Add(schedule);

        var service = new ScheduleService(scheduleRepo, employeeRepo, shiftConfigRepo, uow, tenantContext);

        var request = new ManualAttendanceRequest(
            AttendanceType: ManualAttendanceType.Working,
            CheckInTime: new TimeSpan(7, 30, 0),
            CheckOutTime: new TimeSpan(12, 30, 0),
            Note: "Vào sớm ra muộn tự phát, không có đơn OT"
        );

        // Act
        var result = await service.RecordManualAttendanceAsync(schedule.Id, request);

        // Assert: Không tự tiện tính OT nếu chưa có đơn OT được duyệt. Giờ làm thực tế chỉ tính tối đa trong khung ca (4.00h)
        Assert.Equal(0, result.OvertimeMinutes);
        Assert.Equal(4.00m, result.ActualWorkHours);
        Assert.Equal(0, result.LateMinutes);
        Assert.Equal(0, result.EarlyLeaveMinutes);
        Assert.Equal("WORKING", result.ApprovalType);
    }

    [Fact]
    public async Task CreateAsync_SCH_VAL_06_WorkDateBeforeEmployeeStartDate_ThrowsBusinessException()
    {
        var scheduleRepo = new FakeScheduleRepository();
        var employeeRepo = new FakeEmployeeRepository();
        var shiftConfigRepo = new FakeShiftConfigRepository();
        var uow = new FakeUnitOfWork();
        var tenantContext = new FakeTenantContext();

        var empId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        employeeRepo.Employees.Add(new Employee
        {
            Id = empId,
            EmployeeCode = "NV0099",
            FullName = "Nguyễn Văn Mới",
            BranchId = branchId,
            Status = EmployeeStatus.Active,
            StartDate = new DateTime(2026, 11, 15)
        });

        var service = new ScheduleService(scheduleRepo, employeeRepo, shiftConfigRepo, uow, tenantContext);

        var request = new CreateScheduleItemRequest(
            EmployeeId: empId,
            BranchId: branchId,
            WorkDate: new DateTime(2026, 11, 10), // Trước StartDate (15/11/2026)
            ShiftName: "Ca Sáng",
            PlannedStartTime: new TimeSpan(8, 0, 0),
            PlannedEndTime: new TimeSpan(12, 0, 0),
            DurationHours: 4m
        );

        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.CreateAsync(request));
        Assert.Equal("WORK_DATE_BEFORE_START_DATE", ex.ErrorCode);
    }

    [Fact]
    public async Task ResetManualAttendanceAsync_PaidPayslip_ThrowsPayslipLocked()
    {
        var scheduleRepo = new FakeScheduleRepository();
        var employeeRepo = new FakeEmployeeRepository();
        var shiftConfigRepo = new FakeShiftConfigRepository();
        var payslipRepo = new FakePayslipRepository();
        var uow = new FakeUnitOfWork();
        var tenantContext = new FakeTenantContext { CurrentRole = "STORE_OWNER" };

        var empId = Guid.NewGuid();
        var schedule = new EmployeeSchedule
        {
            Id = Guid.NewGuid(),
            EmployeeId = empId,
            BranchId = Guid.NewGuid(),
            WorkDate = new DateTime(2026, 11, 10),
            PlannedStartTime = new TimeSpan(8, 0, 0),
            PlannedEndTime = new TimeSpan(12, 0, 0),
            DurationHours = 4m,
            CheckInTime = new DateTime(2026, 11, 10, 8, 0, 0),
            CheckOutTime = new DateTime(2026, 11, 10, 12, 0, 0),
            CheckInMethod = "MANUAL",
            ActualWorkHours = 4m,
            Status = ScheduleStatus.Completed
        };
        scheduleRepo.Schedules.Add(schedule);
        payslipRepo.Payslips.Add(new Payslip
        {
            Id = Guid.NewGuid(),
            EmployeeId = empId,
            PeriodMonth = "2026-11",
            Status = PayslipStatus.Paid
        });

        var service = new ScheduleService(scheduleRepo, employeeRepo, shiftConfigRepo, uow, tenantContext, payslipRepository: payslipRepo);

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            service.ResetManualAttendanceAsync(schedule.Id, new ResetAttendanceRequest("Sai giờ")));
        Assert.Equal("PAYSLIP_LOCKED", ex.ErrorCode);
    }

    [Fact]
    public async Task ResetManualAttendanceAsync_PendingApprovalRequest_ThrowsPendingApprovalRequests()
    {
        var scheduleRepo = new FakeScheduleRepository();
        var employeeRepo = new FakeEmployeeRepository();
        var shiftConfigRepo = new FakeShiftConfigRepository();
        var payslipRepo = new FakePayslipRepository();
        var uow = new FakeUnitOfWork();
        var tenantContext = new FakeTenantContext { CurrentRole = "STORE_OWNER" };

        var schedule = new EmployeeSchedule
        {
            Id = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(),
            BranchId = Guid.NewGuid(),
            WorkDate = new DateTime(2026, 11, 10),
            PlannedStartTime = new TimeSpan(8, 0, 0),
            PlannedEndTime = new TimeSpan(12, 0, 0),
            Status = ScheduleStatus.InProgress,
            CheckInTime = new DateTime(2026, 11, 10, 8, 5, 0),
            ApprovalRequests = new List<ScheduleApprovalRequest>
            {
                new() { Id = Guid.NewGuid(), Status = ScheduleApprovalStatus.Pending, IsDeleted = false }
            }
        };
        scheduleRepo.Schedules.Add(schedule);

        var service = new ScheduleService(scheduleRepo, employeeRepo, shiftConfigRepo, uow, tenantContext, payslipRepository: payslipRepo);

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            service.ResetManualAttendanceAsync(schedule.Id, new ResetAttendanceRequest("Hủy ca")));
        Assert.Equal("PENDING_APPROVAL_REQUESTS", ex.ErrorCode);
    }

    [Fact]
    public async Task ResetManualAttendanceAsync_NoAttendanceData_ThrowsNoAttendanceData()
    {
        var scheduleRepo = new FakeScheduleRepository();
        var employeeRepo = new FakeEmployeeRepository();
        var shiftConfigRepo = new FakeShiftConfigRepository();
        var payslipRepo = new FakePayslipRepository();
        var uow = new FakeUnitOfWork();
        var tenantContext = new FakeTenantContext { CurrentRole = "STORE_OWNER" };

        var schedule = new EmployeeSchedule
        {
            Id = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(),
            BranchId = Guid.NewGuid(),
            WorkDate = new DateTime(2026, 11, 10),
            PlannedStartTime = new TimeSpan(8, 0, 0),
            PlannedEndTime = new TimeSpan(12, 0, 0),
            Status = ScheduleStatus.Published
        };
        scheduleRepo.Schedules.Add(schedule);

        var service = new ScheduleService(scheduleRepo, employeeRepo, shiftConfigRepo, uow, tenantContext, payslipRepository: payslipRepo);

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            service.ResetManualAttendanceAsync(schedule.Id, null));
        Assert.Equal("NO_ATTENDANCE_DATA", ex.ErrorCode);
    }

    [Fact]
    public async Task ResetManualAttendanceAsync_FaceIdWithoutReason_ThrowsResetReasonRequired()
    {
        var scheduleRepo = new FakeScheduleRepository();
        var employeeRepo = new FakeEmployeeRepository();
        var shiftConfigRepo = new FakeShiftConfigRepository();
        var payslipRepo = new FakePayslipRepository();
        var uow = new FakeUnitOfWork();
        var tenantContext = new FakeTenantContext { CurrentRole = "STORE_OWNER" };

        var schedule = new EmployeeSchedule
        {
            Id = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(),
            BranchId = Guid.NewGuid(),
            WorkDate = new DateTime(2026, 11, 10),
            PlannedStartTime = new TimeSpan(8, 0, 0),
            PlannedEndTime = new TimeSpan(12, 0, 0),
            CheckInTime = new DateTime(2026, 11, 10, 8, 0, 0),
            CheckInMethod = "FACE_ID",
            Status = ScheduleStatus.InProgress
        };
        scheduleRepo.Schedules.Add(schedule);

        var service = new ScheduleService(scheduleRepo, employeeRepo, shiftConfigRepo, uow, tenantContext, payslipRepository: payslipRepo);

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            service.ResetManualAttendanceAsync(schedule.Id, new ResetAttendanceRequest("   ")));
        Assert.Equal("RESET_REASON_REQUIRED", ex.ErrorCode);
    }

    [Fact]
    public async Task ResetManualAttendanceAsync_PaidLeaveWithUnpaidPayslip_ResetsAllFieldsAndWarns()
    {
        var scheduleRepo = new FakeScheduleRepository();
        var employeeRepo = new FakeEmployeeRepository();
        var shiftConfigRepo = new FakeShiftConfigRepository();
        var payslipRepo = new FakePayslipRepository();
        var uow = new FakeUnitOfWork();
        var tenantContext = new FakeTenantContext { CurrentRole = "STORE_OWNER" };

        var empId = Guid.NewGuid();
        employeeRepo.Employees.Add(new Employee
        {
            Id = empId,
            EmployeeCode = "NV0010",
            FullName = "Trần Minh Tâm",
            Status = EmployeeStatus.Active
        });

        var schedule = new EmployeeSchedule
        {
            Id = Guid.NewGuid(),
            EmployeeId = empId,
            BranchId = Guid.NewGuid(),
            WorkDate = new DateTime(2026, 11, 10),
            ShiftCode = "CA_SANG",
            ShiftName = "Ca Sáng",
            PlannedStartTime = new TimeSpan(8, 0, 0),
            PlannedEndTime = new TimeSpan(12, 0, 0),
            DurationHours = 4m,
            CheckInMethod = "MANUAL_PAID_LEAVE",
            CheckOutMethod = "MANUAL_PAID_LEAVE",
            LateMinutes = 10,
            EarlyLeaveMinutes = 5,
            OvertimeMinutes = 60,
            ActualWorkHours = 5m,
            IsLeave = true,
            IsPaidLeave = true,
            ApprovalType = "PAID_LEAVE",
            ApprovalStatus = ScheduleApprovalStatus.Approved,
            ApprovalReason = "Nghỉ phép cũ",
            Status = ScheduleStatus.Completed
        };
        scheduleRepo.Schedules.Add(schedule);
        payslipRepo.Payslips.Add(new Payslip
        {
            Id = Guid.NewGuid(),
            EmployeeId = empId,
            PeriodMonth = "2026-11",
            Status = PayslipStatus.Unpaid
        });

        var service = new ScheduleService(scheduleRepo, employeeRepo, shiftConfigRepo, uow, tenantContext, payslipRepository: payslipRepo);

        var result = await service.ResetManualAttendanceAsync(schedule.Id, new ResetAttendanceRequest("Nhân viên vẫn đi làm bình thường"));

        Assert.True(uow.SaveChangesCalled);
        Assert.True(result.HasUnpaidPayslip);
        Assert.Equal(ScheduleStatus.Published, result.Schedule.Status);
        Assert.Null(result.Schedule.CheckInTime);
        Assert.Null(result.Schedule.CheckOutTime);
        Assert.Null(result.Schedule.CheckInMethod);
        Assert.Null(result.Schedule.CheckOutMethod);
        Assert.Equal(0, result.Schedule.LateMinutes);
        Assert.Equal(0, result.Schedule.EarlyLeaveMinutes);
        Assert.Equal(0, result.Schedule.OvertimeMinutes);
        Assert.Equal(0.00m, result.Schedule.ActualWorkHours);
        Assert.Null(result.Schedule.ApprovalType);
        Assert.Equal(ScheduleApprovalStatus.None, result.Schedule.ApprovalStatus);
        Assert.Equal("Nhân viên vẫn đi làm bình thường", result.Schedule.ApprovalReason);
        Assert.False(schedule.IsLeave);
        Assert.False(schedule.IsPaidLeave);
        Assert.Null(schedule.PayslipId);
    }

    [Fact]
    public async Task ResetManualAttendanceAsync_StaffRole_ThrowsForbidden()
    {
        var scheduleRepo = new FakeScheduleRepository();
        var employeeRepo = new FakeEmployeeRepository();
        var shiftConfigRepo = new FakeShiftConfigRepository();
        var uow = new FakeUnitOfWork();
        var tenantContext = new FakeTenantContext { CurrentRole = "CASHIER" };

        var service = new ScheduleService(scheduleRepo, employeeRepo, shiftConfigRepo, uow, tenantContext);

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            service.ResetManualAttendanceAsync(Guid.NewGuid(), new ResetAttendanceRequest("Test")));
        Assert.Equal("FORBIDDEN", ex.ErrorCode);
    }
}

