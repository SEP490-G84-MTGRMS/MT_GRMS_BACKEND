using System.IO;
using System.Text;
using Moq;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Common.DTOs;
using MT_GRMS.Application.Common.Interfaces;
using MT_GRMS.Application.Branches;
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
        public string? CustomRawUserId { get; set; } = null;
        public string CurrentRawUserId => CustomRawUserId ?? (CurrentUserId != Guid.Empty ? CurrentUserId.ToString() : string.Empty);

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

        public void SetRawUserId(string? rawUserId)
        {
            CustomRawUserId = rawUserId;
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

    private class FakeBranchAttendancePolicyRepository : IBranchRepository
    {
        public List<Branch> Policies { get; } = new();

        public Task<Branch?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(Policies.FirstOrDefault(p => p.Id == id && !p.IsDeleted));

        public Task<IReadOnlyList<Branch>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Branch>>(Policies.Where(p => !p.IsDeleted).ToList());

        public Task<IReadOnlyList<Branch>> FindAsync(System.Linq.Expressions.Expression<Func<Branch, bool>> predicate, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Branch>>(Policies.AsQueryable().Where(predicate).ToList());

        public Task<Branch?> FirstOrDefaultAsync(System.Linq.Expressions.Expression<Func<Branch, bool>> predicate, CancellationToken cancellationToken = default)
            => Task.FromResult(Policies.AsQueryable().FirstOrDefault(predicate));

        public Task<bool> ExistsAsync(System.Linq.Expressions.Expression<Func<Branch, bool>> predicate, CancellationToken cancellationToken = default)
            => Task.FromResult(Policies.AsQueryable().Any(predicate));

        public Task<int> CountAsync(System.Linq.Expressions.Expression<Func<Branch, bool>>? predicate = null, CancellationToken cancellationToken = default)
            => Task.FromResult(predicate == null ? Policies.Count : Policies.AsQueryable().Count(predicate));

        public Task AddAsync(Branch entity, CancellationToken cancellationToken = default)
        {
            Policies.Add(entity);
            return Task.CompletedTask;
        }

        public Task AddRangeAsync(IEnumerable<Branch> entities, CancellationToken cancellationToken = default)
        {
            Policies.AddRange(entities);
            return Task.CompletedTask;
        }

        public void Update(Branch entity)
        {
            var index = Policies.FindIndex(p => p.Id == entity.Id);
            if (index >= 0) Policies[index] = entity;
        }

        public void Delete(Branch entity) => entity.IsDeleted = true;

        public Task<(IReadOnlyList<Branch> Items, int TotalCount)> GetPagedListAsync(string? keyword, string? branchCode, string? status, IReadOnlyList<string>? branchCodes, IReadOnlyList<string>? branchNames, IReadOnlyList<string>? addresses, IReadOnlyList<string>? phones, int pageIndex, int pageSize, CancellationToken cancellationToken = default)
            => Task.FromResult(((IReadOnlyList<Branch>)Policies, Policies.Count));

        public Task<bool> ExistsByCodeAsync(string code, CancellationToken cancellationToken = default)
            => Task.FromResult(Policies.Any(b => b.BranchCode == code && !b.IsDeleted));

        public Task<int> CountActiveAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Policies.Count(b => !b.IsDeleted));

        public Task<int> CountTotalAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Policies.Count(b => !b.IsDeleted));

        public Task<MT_GRMS.Application.Branches.Dtos.BranchDetailDto?> GetBranchDetailAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult<MT_GRMS.Application.Branches.Dtos.BranchDetailDto?>(null);
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
        policyRepo.Policies.Add(new Branch
        {
            Id = branchId,
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

    [Fact]
    public async Task ScheduleService_Crud_And_Publish_Lifecycle()
    {
        var scheduleRepo = new FakeScheduleRepository();
        var employeeRepo = new FakeEmployeeRepository();
        var shiftConfigRepo = new FakeShiftConfigRepository();
        var uow = new FakeUnitOfWork();
        var tenantContext = new FakeTenantContext();

        var empId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var shiftId = Guid.NewGuid();

        employeeRepo.Employees.Add(new Employee
        {
            Id = empId,
            BranchId = branchId,
            FullName = "Staff One",
            UserId = tenantContext.CurrentUserId.ToString(),
            Status = EmployeeStatus.Active
        });

        shiftConfigRepo.ShiftConfigs.Add(new ShiftConfig
        {
            Id = shiftId,
            BranchId = branchId,
            ShiftName = "Ca Sang",
            ShiftCode = "SANG",
            StartTime = new TimeSpan(7, 0, 0),
            EndTime = new TimeSpan(11, 0, 0),
            DurationHours = 4.0m
        });

        var service = new ScheduleService(scheduleRepo, employeeRepo, shiftConfigRepo, uow, tenantContext);

        // 1. CreateAsync employee not found
        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.CreateAsync(new CreateScheduleItemRequest(Guid.NewGuid(), branchId, DateTime.UtcNow.Date, shiftId)));

        // 2. CreateAsync valid
        var createReq = new CreateScheduleItemRequest(empId, branchId, DateTime.UtcNow.Date.AddDays(1), shiftId);
        var created = await service.CreateAsync(createReq);
        Assert.NotNull(created);
        Assert.Equal(empId, created.EmployeeId);
        Assert.Equal(ScheduleStatus.Draft, created.Status);

        // 3. GetByIdAsync
        var fetched = await service.GetByIdAsync(created.Id);
        Assert.NotNull(fetched);
        Assert.Equal(created.Id, fetched.Id);
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetByIdAsync(Guid.NewGuid()));

        // 4. UpdateAsync
        var updated = await service.UpdateAsync(created.Id, new UpdateScheduleRequest(ShiftConfigId: shiftId));
        Assert.NotNull(updated);
        await Assert.ThrowsAsync<NotFoundException>(() => service.UpdateAsync(Guid.NewGuid(), new UpdateScheduleRequest()));

        // 5. GetListAsync
        var list = await service.GetListAsync(new ScheduleQueryFilter { BranchId = branchId });
        Assert.Single(list);

        // 6. GetMySchedulesAsync
        var myScheds = await service.GetMySchedulesAsync(tenantContext.CurrentUserId.ToString(), DateTime.UtcNow.Date, DateTime.UtcNow.Date.AddDays(7));
        Assert.Single(myScheds);

        // 7. PublishAsync
        var pubResp = await service.PublishAsync(new PublishSchedulesRequest(branchId, DateTime.UtcNow.Date, DateTime.UtcNow.Date.AddDays(7)));
        Assert.NotNull(pubResp);

        // 8. DeleteAsync
        await service.DeleteAsync(created.Id);
        Assert.True(scheduleRepo.Schedules.First(s => s.Id == created.Id).IsDeleted);
        await Assert.ThrowsAsync<NotFoundException>(() => service.DeleteAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task FaceId_CheckIn_And_CheckOut_Lifecycle_And_Validations()
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
            TenantId = tenantContext.CurrentTenantId,
            FullName = "Le Thi B",
            EmployeeCode = "NV02",
            Status = EmployeeStatus.Active,
            BranchId = branchId
        });

        var service = new ScheduleService(scheduleRepo, employeeRepo, shiftConfigRepo, uow, tenantContext);

        var today = DateTime.UtcNow.Date;
        var schedule = new EmployeeSchedule
        {
            Id = Guid.NewGuid(),
            TenantId = tenantContext.CurrentTenantId,
            EmployeeId = empId,
            BranchId = branchId,
            WorkDate = today,
            PlannedStartTime = new TimeSpan(8, 0, 0),
            PlannedEndTime = new TimeSpan(17, 0, 0),
            Status = ScheduleStatus.Published
        };
        scheduleRepo.Schedules.Add(schedule);

        // 1. CheckIn: Schedule not found
        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.CheckInFaceIdAsync(new FaceIdCheckInRequest(Guid.NewGuid(), empId, 0.2m, null)));

        // 2. CheckIn: EmployeeId mismatch
        var exEmp = await Assert.ThrowsAsync<BusinessException>(() =>
            service.CheckInFaceIdAsync(new FaceIdCheckInRequest(schedule.Id, Guid.NewGuid(), 0.2m, null)));
        Assert.Equal("INVALID_EMPLOYEE", exEmp.ErrorCode);

        // 3. CheckIn: FaceMatchScore > 0.45m
        var exFace = await Assert.ThrowsAsync<BusinessException>(() =>
            service.CheckInFaceIdAsync(new FaceIdCheckInRequest(schedule.Id, empId, 0.65m, null)));
        Assert.Equal("FACE_MATCH_FAILED", exFace.ErrorCode);

        // 4. CheckIn: Locked by paid payslip
        schedule.PayslipId = Guid.NewGuid();
        schedule.Payslip = new Payslip { Status = PayslipStatus.Paid };
        var exLocked = await Assert.ThrowsAsync<BusinessException>(() =>
            service.CheckInFaceIdAsync(new FaceIdCheckInRequest(schedule.Id, empId, 0.2m, null)));
        Assert.Equal("PAYSLIP_LOCKED", exLocked.ErrorCode);
        schedule.PayslipId = null;
        schedule.Payslip = null;

        // 5. CheckOut before CheckIn
        var exNotCheckedIn = await Assert.ThrowsAsync<BusinessException>(() =>
            service.CheckOutFaceIdAsync(new FaceIdCheckOutRequest(schedule.Id, empId, 0.2m, null)));
        Assert.Equal("NOT_CHECKED_IN", exNotCheckedIn.ErrorCode);

        // 6. CheckIn success
        var checkInRes = await service.CheckInFaceIdAsync(new FaceIdCheckInRequest(schedule.Id, empId, 0.25m, null));
        Assert.NotNull(checkInRes);
        Assert.Equal(ScheduleStatus.InProgress, checkInRes.Status);
        Assert.NotNull(checkInRes.CheckInTime);

        // 7. Double CheckIn -> ALREADY_CHECKED_IN
        var exAlready = await Assert.ThrowsAsync<BusinessException>(() =>
            service.CheckInFaceIdAsync(new FaceIdCheckInRequest(schedule.Id, empId, 0.25m, null)));
        Assert.Equal("ALREADY_CHECKED_IN", exAlready.ErrorCode);

        // 8. CheckOut success
        var checkOutRes = await service.CheckOutFaceIdAsync(new FaceIdCheckOutRequest(schedule.Id, empId, 0.30m, null));
        Assert.NotNull(checkOutRes);
        Assert.Equal(ScheduleStatus.Completed, checkOutRes.Status);
        Assert.NotNull(checkOutRes.CheckOutTime);
    }

    [Fact]
    public async Task SelfCheckInAsync_Validations_And_Success()
    {
        var scheduleRepo = new FakeScheduleRepository();
        var employeeRepo = new FakeEmployeeRepository();
        var shiftConfigRepo = new FakeShiftConfigRepository();
        var uow = new FakeUnitOfWork();
        var tenantContext = new FakeTenantContext();

        var empId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var userId = tenantContext.CurrentUserId.ToString();

        var emp = new Employee
        {
            Id = empId,
            UserId = userId,
            TenantId = tenantContext.CurrentTenantId,
            FullName = "Tran Van C",
            EmployeeCode = "NV03",
            Status = EmployeeStatus.Active,
            BranchId = branchId
        };
        employeeRepo.Employees.Add(emp);

        var service = new ScheduleService(scheduleRepo, employeeRepo, shiftConfigRepo, uow, tenantContext);

        var vnToday = DateTime.UtcNow.AddHours(7).Date;
        var schedule = new EmployeeSchedule
        {
            Id = Guid.NewGuid(),
            TenantId = tenantContext.CurrentTenantId,
            EmployeeId = empId,
            Employee = emp,
            BranchId = branchId,
            WorkDate = vnToday,
            PlannedStartTime = TimeSpan.Zero,
            PlannedEndTime = new TimeSpan(23, 59, 59),
            Status = ScheduleStatus.Published
        };
        scheduleRepo.Schedules.Add(schedule);

        // 1. FaceDescriptor registered -> FACE_ID_REQUIRED
        emp.FaceDescriptor = "vector-descriptor-data";
        var exFaceReq = await Assert.ThrowsAsync<BusinessException>(() =>
            service.SelfCheckInAsync(schedule.Id, new SelfCheckInRequest()));
        Assert.Equal("FACE_ID_REQUIRED", exFaceReq.ErrorCode);
        emp.FaceDescriptor = null;

        // 2. Schedule not in Published/Scheduled status
        schedule.Status = ScheduleStatus.Draft;
        var exStatus = await Assert.ThrowsAsync<BusinessException>(() =>
            service.SelfCheckInAsync(schedule.Id, new SelfCheckInRequest()));
        Assert.Equal("INVALID_SCHEDULE_STATUS", exStatus.ErrorCode);
        schedule.Status = ScheduleStatus.Published;

        // 3. Success SelfCheckIn
        var selfCheckInRes = await service.SelfCheckInAsync(schedule.Id, new SelfCheckInRequest("Chấm công thủ công"));
        Assert.NotNull(selfCheckInRes);
        Assert.Equal(ScheduleStatus.InProgress, selfCheckInRes.Status);
        Assert.NotNull(selfCheckInRes.CheckInTime);
    }

    [Fact]
    public async Task CreateRecurringAsync_ShouldCoverAllDaysOfWeek()
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
            TenantId = tenantContext.CurrentTenantId,
            FullName = "Pham D",
            EmployeeCode = "NV04",
            Status = EmployeeStatus.Active,
            BranchId = branchId
        });

        var shiftId = Guid.NewGuid();
        shiftConfigRepo.ShiftConfigs.Add(new ShiftConfig
        {
            Id = shiftId,
            BranchId = branchId,
            ShiftName = "Ca Hanh Chinh",
            ShiftCode = "HC",
            StartTime = new TimeSpan(8, 0, 0),
            EndTime = new TimeSpan(17, 0, 0),
            DurationHours = 8.0m
        });

        var service = new ScheduleService(scheduleRepo, employeeRepo, shiftConfigRepo, uow, tenantContext);

        var startDate = DateTime.UtcNow.Date.AddDays(7);
        var endDate = startDate.AddDays(14);

        // Include all day representations to exercise ParseDaysOfWeek
        var req = new CreateRecurringScheduleRequest(
            EmployeeId: empId,
            BranchId: branchId,
            StartDate: startDate,
            HasEndDate: true,
            EndDate: endDate,
            RepeatDays: ["T2", "THỨ 3", "T4", "THURSDAY", "T6", "T7", "CN"],
            ShiftConfigId: shiftId
        );

        var createdList = await service.CreateRecurringAsync(req);
        Assert.NotEmpty(createdList);
    }

    [Fact]
    public async Task EnsureScheduleNotLockedByPaidPayslipAsync_WithPaidPayslip_ThrowsLockedException()
    {
        var scheduleRepo = new FakeScheduleRepository();
        var employeeRepo = new FakeEmployeeRepository();
        var shiftConfigRepo = new FakeShiftConfigRepository();
        var policyRepo = new FakeBranchAttendancePolicyRepository();
        var payslipRepo = new FakePayslipRepository();
        var uow = new FakeUnitOfWork();
        var tenantContext = new FakeTenantContext();

        var empId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var scheduleId1 = Guid.NewGuid();
        var payslipId1 = Guid.NewGuid();

        // 1. Locked by schedule.PayslipId
        var paidPayslip = new Payslip
        {
            Id = payslipId1,
            TenantId = tenantContext.CurrentTenantId,
            EmployeeId = empId,
            Status = PayslipStatus.Paid,
            PeriodMonth = "2026-11"
        };
        payslipRepo.Payslips.Add(paidPayslip);

        var sched1 = new EmployeeSchedule
        {
            Id = scheduleId1,
            TenantId = tenantContext.CurrentTenantId,
            EmployeeId = empId,
            BranchId = branchId,
            WorkDate = new DateTime(2026, 11, 15),
            PlannedStartTime = new TimeSpan(8, 0, 0),
            PlannedEndTime = new TimeSpan(17, 0, 0),
            PayslipId = payslipId1,
            Payslip = paidPayslip,
            Status = ScheduleStatus.InProgress,
            CheckInTime = new DateTime(2026, 11, 15, 8, 5, 0)
        };
        scheduleRepo.Schedules.Add(sched1);

        var service = new ScheduleService(scheduleRepo, employeeRepo, shiftConfigRepo, uow, tenantContext, policyRepo, null, payslipRepo);

        var ex1 = await Assert.ThrowsAsync<BusinessException>(() => service.ResetManualAttendanceAsync(scheduleId1, null));
        Assert.Equal("PAYSLIP_LOCKED", ex1.ErrorCode);

        // 2. Locked by period payslip in repo (when schedule.PayslipId is null)
        var scheduleId2 = Guid.NewGuid();
        var sched2 = new EmployeeSchedule
        {
            Id = scheduleId2,
            TenantId = tenantContext.CurrentTenantId,
            EmployeeId = empId,
            BranchId = branchId,
            WorkDate = new DateTime(2026, 11, 16),
            PlannedStartTime = new TimeSpan(8, 0, 0),
            PlannedEndTime = new TimeSpan(17, 0, 0),
            PayslipId = null,
            Status = ScheduleStatus.Completed,
            CheckInTime = new DateTime(2026, 11, 16, 8, 0, 0)
        };
        scheduleRepo.Schedules.Add(sched2);

        var req = new ManualAttendanceRequest(
            AttendanceType: ManualAttendanceType.Working,
            CheckInTime: new TimeSpan(8, 0, 0),
            CheckOutTime: new TimeSpan(17, 0, 0)
        );

        var ex2 = await Assert.ThrowsAsync<BusinessException>(() => service.RecordManualAttendanceAsync(scheduleId2, req));
        Assert.Equal("PAYSLIP_LOCKED", ex2.ErrorCode);
    }

    [Fact]
    public async Task ApplyWorkingAttendance_WithBranchAttendancePolicy_CoversEarlyLateAndOvertime()
    {
        var scheduleRepo = new FakeScheduleRepository();
        var employeeRepo = new FakeEmployeeRepository();
        var shiftConfigRepo = new FakeShiftConfigRepository();
        var policyRepo = new FakeBranchAttendancePolicyRepository();
        var uow = new FakeUnitOfWork();
        var tenantContext = new FakeTenantContext();

        var empId = Guid.NewGuid();
        var branchId = Guid.NewGuid();

        // Setup policy with late & early thresholds
        policyRepo.Policies.Add(new Branch
        {
            Id = branchId,
            TenantId = tenantContext.CurrentTenantId,
            EnableLateThreshold = true,
            LateThresholdMinutes = 15,
            EnableEarlyLeaveThreshold = true,
            EarlyLeaveThresholdMinutes = 10
        });

        var service = new ScheduleService(scheduleRepo, employeeRepo, shiftConfigRepo, uow, tenantContext, policyRepo);

        // Case A: Within late threshold (10 min late) & within early threshold (5 min early) -> 0 late, 0 early
        var schedA = new EmployeeSchedule
        {
            Id = Guid.NewGuid(),
            TenantId = tenantContext.CurrentTenantId,
            EmployeeId = empId,
            BranchId = branchId,
            WorkDate = new DateTime(2026, 10, 1),
            PlannedStartTime = new TimeSpan(8, 0, 0),
            PlannedEndTime = new TimeSpan(17, 0, 0),
            Status = ScheduleStatus.Published
        };
        scheduleRepo.Schedules.Add(schedA);

        var reqA = new ManualAttendanceRequest(
            AttendanceType: ManualAttendanceType.Working,
            CheckInTime: new TimeSpan(8, 10, 0),
            CheckOutTime: new TimeSpan(16, 55, 0)
        );
        var resA = await service.RecordManualAttendanceAsync(schedA.Id, reqA);
        Assert.Equal(0, resA.LateMinutes);
        Assert.Equal(0, resA.EarlyLeaveMinutes);

        // Case B: Exceeds late threshold (25 min late) & exceeds early threshold (20 min early)
        var schedB = new EmployeeSchedule
        {
            Id = Guid.NewGuid(),
            TenantId = tenantContext.CurrentTenantId,
            EmployeeId = empId,
            BranchId = branchId,
            WorkDate = new DateTime(2026, 10, 2),
            PlannedStartTime = new TimeSpan(8, 0, 0),
            PlannedEndTime = new TimeSpan(17, 0, 0),
            Status = ScheduleStatus.Published
        };
        scheduleRepo.Schedules.Add(schedB);

        var reqB = new ManualAttendanceRequest(
            AttendanceType: ManualAttendanceType.Working,
            CheckInTime: new TimeSpan(8, 25, 0),
            CheckOutTime: new TimeSpan(16, 40, 0)
        );
        var resB = await service.RecordManualAttendanceAsync(schedB.Id, reqB);
        Assert.Equal(25, resB.LateMinutes);
        Assert.Equal(20, resB.EarlyLeaveMinutes);

        // Case C: Overtime calculation with approved OT
        var schedC = new EmployeeSchedule
        {
            Id = Guid.NewGuid(),
            TenantId = tenantContext.CurrentTenantId,
            EmployeeId = empId,
            BranchId = branchId,
            WorkDate = new DateTime(2026, 10, 3),
            PlannedStartTime = new TimeSpan(8, 0, 0),
            PlannedEndTime = new TimeSpan(17, 0, 0),
            ApprovalType = "OVERTIME",
            ApprovalStatus = ScheduleApprovalStatus.Approved,
            OvertimeMinutes = 60,
            Status = ScheduleStatus.Published
        };
        scheduleRepo.Schedules.Add(schedC);

        // Worked until 18:30 (90 min extra outside, capped by OvertimeMinutes = 60)
        var reqC = new ManualAttendanceRequest(
            AttendanceType: ManualAttendanceType.Working,
            CheckInTime: new TimeSpan(8, 0, 0),
            CheckOutTime: new TimeSpan(18, 30, 0)
        );
        var resC = await service.RecordManualAttendanceAsync(schedC.Id, reqC);
        Assert.Equal(60, resC.OvertimeMinutes);

        // Case D: Invalid time range checkOut < checkIn without next day
        var reqD = new ManualAttendanceRequest(
            AttendanceType: ManualAttendanceType.Working,
            CheckInTime: new TimeSpan(17, 0, 0),
            CheckOutTime: new TimeSpan(8, 0, 0)
        );
        var schedD = new EmployeeSchedule
        {
            Id = Guid.NewGuid(),
            TenantId = tenantContext.CurrentTenantId,
            EmployeeId = empId,
            BranchId = branchId,
            WorkDate = new DateTime(2026, 10, 4),
            PlannedStartTime = new TimeSpan(8, 0, 0),
            PlannedEndTime = new TimeSpan(17, 0, 0),
            Status = ScheduleStatus.Published
        };
        scheduleRepo.Schedules.Add(schedD);
        var resD = await service.RecordManualAttendanceAsync(schedD.Id, reqD);
        Assert.NotNull(resD);
    }

    [Fact]
    public async Task CheckInFaceIdAsync_AllBranchConditions_ShouldWorkExpectedly()
    {
        var scheduleRepo = new FakeScheduleRepository();
        var employeeRepo = new FakeEmployeeRepository();
        var shiftConfigRepo = new FakeShiftConfigRepository();
        var policyRepo = new FakeBranchAttendancePolicyRepository();
        var uow = new FakeUnitOfWork();
        var tenantContext = new FakeTenantContext();

        var mockCloudinary = new Mock<ICloudinaryService>();
        mockCloudinary
            .Setup(c => c.UploadImageAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ImageUploadResponseDto { Url = "https://cdn.example.com/face.jpg", PublicId = "face_123" });

        var service = new ScheduleService(scheduleRepo, employeeRepo, shiftConfigRepo, uow, tenantContext, policyRepo, mockCloudinary.Object);

        var empId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var schedId = Guid.NewGuid();

        var schedule = new EmployeeSchedule
        {
            Id = schedId,
            TenantId = tenantContext.CurrentTenantId,
            EmployeeId = empId,
            BranchId = branchId,
            WorkDate = DateTime.UtcNow.AddHours(7).Date,
            PlannedStartTime = new TimeSpan(6, 0, 0),
            PlannedEndTime = new TimeSpan(14, 0, 0),
            Status = ScheduleStatus.Published
        };
        scheduleRepo.Schedules.Add(schedule);

        // 1. Not Found
        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.CheckInFaceIdAsync(new FaceIdCheckInRequest(Guid.NewGuid(), empId, 0.2m, null)));

        // 2. Employee mismatch
        await Assert.ThrowsAsync<BusinessException>(() =>
            service.CheckInFaceIdAsync(new FaceIdCheckInRequest(schedId, Guid.NewGuid(), 0.2m, null)));

        // 3. Face match score > 0.45
        var exFace = await Assert.ThrowsAsync<BusinessException>(() =>
            service.CheckInFaceIdAsync(new FaceIdCheckInRequest(schedId, empId, 0.55m, null)));
        Assert.Equal("FACE_MATCH_FAILED", exFace.ErrorCode);

        // 4. Valid check-in with base64 snapshot (including data URI prefix)
        var sampleBase64 = "data:image/jpeg;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes("fake_image_bytes"));
        var result = await service.CheckInFaceIdAsync(new FaceIdCheckInRequest(schedId, empId, 0.35m, sampleBase64));
        Assert.NotNull(result);
        Assert.Equal(ScheduleStatus.InProgress, result.Status);
        Assert.Equal("FACE_ID", result.CheckInMethod);

        // 5. Already checked in
        var exAlready = await Assert.ThrowsAsync<BusinessException>(() =>
            service.CheckInFaceIdAsync(new FaceIdCheckInRequest(schedId, empId, 0.2m, null)));
        Assert.Equal("ALREADY_CHECKED_IN", exAlready.ErrorCode);
    }

    [Fact]
    public async Task CheckOutFaceIdAsync_AllBranchConditions_ShouldWorkExpectedly()
    {
        var scheduleRepo = new FakeScheduleRepository();
        var employeeRepo = new FakeEmployeeRepository();
        var shiftConfigRepo = new FakeShiftConfigRepository();
        var policyRepo = new FakeBranchAttendancePolicyRepository();
        var uow = new FakeUnitOfWork();
        var tenantContext = new FakeTenantContext();

        var mockCloudinary = new Mock<ICloudinaryService>();
        mockCloudinary
            .Setup(c => c.UploadImageAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ImageUploadResponseDto { Url = "https://cdn.example.com/face_out.jpg", PublicId = "face_out_123" });

        var service = new ScheduleService(scheduleRepo, employeeRepo, shiftConfigRepo, uow, tenantContext, policyRepo, mockCloudinary.Object);

        var empId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var schedId = Guid.NewGuid();

        var schedule = new EmployeeSchedule
        {
            Id = schedId,
            TenantId = tenantContext.CurrentTenantId,
            EmployeeId = empId,
            BranchId = branchId,
            WorkDate = DateTime.UtcNow.AddHours(7).Date,
            PlannedStartTime = new TimeSpan(6, 0, 0),
            PlannedEndTime = new TimeSpan(14, 0, 0),
            Status = ScheduleStatus.InProgress,
            ApprovalType = "OVERTIME",
            OvertimeMinutes = 30
        };
        scheduleRepo.Schedules.Add(schedule);

        // 1. Not checked in yet
        var exNotCheckedIn = await Assert.ThrowsAsync<BusinessException>(() =>
            service.CheckOutFaceIdAsync(new FaceIdCheckOutRequest(schedId, empId, 0.2m, null)));
        Assert.Equal("NOT_CHECKED_IN", exNotCheckedIn.ErrorCode);

        // 2. Set CheckInTime
        schedule.CheckInTime = DateTime.UtcNow.AddHours(7).Date.AddHours(6);

        // 3. Face match failed
        var exFace = await Assert.ThrowsAsync<BusinessException>(() =>
            service.CheckOutFaceIdAsync(new FaceIdCheckOutRequest(schedId, empId, 0.55m, null)));
        Assert.Equal("FACE_MATCH_FAILED", exFace.ErrorCode);

        // 4. Valid check-out with snapshot
        var sampleBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("checkout_image_bytes"));
        var result = await service.CheckOutFaceIdAsync(new FaceIdCheckOutRequest(schedId, empId, 0.25m, sampleBase64));
        Assert.NotNull(result);
        Assert.Equal(ScheduleStatus.Completed, result.Status);
        Assert.Equal("FACE_ID", result.CheckOutMethod);
    }

    [Fact]
    public async Task FaceIdMultiShift_AutoDetectAndExplicitScheduleId_WorksForSecondShiftAndOvernight()
    {
        var scheduleRepo = new FakeScheduleRepository();
        var employeeRepo = new FakeEmployeeRepository();
        var shiftConfigRepo = new FakeShiftConfigRepository();
        var policyRepo = new FakeBranchAttendancePolicyRepository();
        var uow = new FakeUnitOfWork();
        var tenantContext = new FakeTenantContext();
        var service = new ScheduleService(scheduleRepo, employeeRepo, shiftConfigRepo, uow, tenantContext, policyRepo);

        var empId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var vnNow = DateTime.UtcNow.AddHours(7);
        var today = vnNow.Date;

        // Ca 1 đã hoàn thành (Completed), Ca 2 chưa chấm công (Scheduled)
        var shift1 = new EmployeeSchedule
        {
            Id = Guid.NewGuid(),
            TenantId = tenantContext.CurrentTenantId,
            EmployeeId = empId,
            BranchId = branchId,
            WorkDate = today,
            ShiftName = "Ca Sáng",
            PlannedStartTime = new TimeSpan(7, 0, 0),
            PlannedEndTime = new TimeSpan(11, 0, 0),
            CheckInTime = today.AddHours(7),
            CheckOutTime = today.AddHours(11),
            Status = ScheduleStatus.Completed
        };
        var shift2 = new EmployeeSchedule
        {
            Id = Guid.NewGuid(),
            TenantId = tenantContext.CurrentTenantId,
            EmployeeId = empId,
            BranchId = branchId,
            WorkDate = today,
            ShiftName = "Ca Chiều",
            PlannedStartTime = new TimeSpan(13, 0, 0),
            PlannedEndTime = new TimeSpan(17, 0, 0),
            Status = ScheduleStatus.Scheduled
        };
        scheduleRepo.Schedules.AddRange(new[] { shift1, shift2 });

        // Auto-detect Check-in (ScheduleId == null) -> phải chọn Ca 2 thay vì Ca 1 đã Completed
        var checkInRes = await service.CheckInFaceIdAsync(new FaceIdCheckInRequest(null, empId, 0.25m, null));
        Assert.Equal(shift2.Id, checkInRes.Id);
        Assert.Equal(ScheduleStatus.InProgress, checkInRes.Status);

        // Auto-detect Check-out (ScheduleId == null) -> phải chọn Ca 2 đang InProgress
        var checkOutRes = await service.CheckOutFaceIdAsync(new FaceIdCheckOutRequest(null, empId, 0.25m, null));
        Assert.Equal(shift2.Id, checkOutRes.Id);
        Assert.Equal(ScheduleStatus.Completed, checkOutRes.Status);

        // Explicit ScheduleId cho Ca 3
        var shift3 = new EmployeeSchedule
        {
            Id = Guid.NewGuid(),
            TenantId = tenantContext.CurrentTenantId,
            EmployeeId = empId,
            BranchId = branchId,
            WorkDate = today,
            ShiftName = "Ca Tối",
            PlannedStartTime = new TimeSpan(18, 0, 0),
            PlannedEndTime = new TimeSpan(22, 0, 0),
            Status = ScheduleStatus.Scheduled
        };
        scheduleRepo.Schedules.Add(shift3);

        var explicitCheckIn = await service.CheckInFaceIdAsync(new FaceIdCheckInRequest(shift3.Id, empId, 0.20m, null));
        Assert.Equal(shift3.Id, explicitCheckIn.Id);
        Assert.Equal(ScheduleStatus.InProgress, explicitCheckIn.Status);
    }

    [Fact]
    public async Task SelfCheckInAsync_AllBranchConditions_ShouldWorkExpectedly()
    {
        var scheduleRepo = new FakeScheduleRepository();
        var employeeRepo = new FakeEmployeeRepository();
        var shiftConfigRepo = new FakeShiftConfigRepository();
        var policyRepo = new FakeBranchAttendancePolicyRepository();
        var uow = new FakeUnitOfWork();
        var tenantContext = new FakeTenantContext();

        var service = new ScheduleService(scheduleRepo, employeeRepo, shiftConfigRepo, uow, tenantContext, policyRepo);

        var empId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var schedId = Guid.NewGuid();
        var currentUserIdStr = tenantContext.CurrentUserId.ToString();

        var employee = new Employee
        {
            Id = empId,
            TenantId = tenantContext.CurrentTenantId,
            UserId = currentUserIdStr,
            FullName = "Nguyen Van A",
            EmployeeCode = "NV01",
            Status = EmployeeStatus.Active,
            BranchId = branchId
        };
        employeeRepo.Employees.Add(employee);

        var vnNow = DateTime.UtcNow.AddHours(7);

        var schedule = new EmployeeSchedule
        {
            Id = schedId,
            TenantId = tenantContext.CurrentTenantId,
            EmployeeId = empId,
            Employee = employee,
            BranchId = branchId,
            WorkDate = vnNow.Date,
            PlannedStartTime = TimeSpan.Zero,
            PlannedEndTime = new TimeSpan(23, 59, 0),
            Status = ScheduleStatus.Published
        };
        scheduleRepo.Schedules.Add(schedule);

        // 1. RawUserId empty -> Unauthorized
        tenantContext.SetRawUserId(" ");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.SelfCheckInAsync(schedId, new SelfCheckInRequest()));
        tenantContext.SetRawUserId(currentUserIdStr);

        // 2. Tenant mismatch -> Unauthorized
        schedule.TenantId = "different_tenant";
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.SelfCheckInAsync(schedId, new SelfCheckInRequest()));
        schedule.TenantId = tenantContext.CurrentTenantId;

        // 3. Employee inactive -> Unauthorized
        employee.Status = EmployeeStatus.Inactive;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.SelfCheckInAsync(schedId, new SelfCheckInRequest()));
        employee.Status = EmployeeStatus.Active;

        // 4. Employee has FaceDescriptor -> FACE_ID_REQUIRED
        employee.FaceDescriptor = "registered_descriptor_data";
        var exFace = await Assert.ThrowsAsync<BusinessException>(() =>
            service.SelfCheckInAsync(schedId, new SelfCheckInRequest()));
        Assert.Equal("FACE_ID_REQUIRED", exFace.ErrorCode);
        employee.FaceDescriptor = null;

        // 5. Work date not today -> INVALID_WORK_DATE
        schedule.WorkDate = vnNow.Date.AddDays(-1);
        var exDate = await Assert.ThrowsAsync<BusinessException>(() =>
            service.SelfCheckInAsync(schedId, new SelfCheckInRequest()));
        Assert.Equal("INVALID_WORK_DATE", exDate.ErrorCode);
        schedule.WorkDate = vnNow.Date;

        // 6. Outside window (e.g. shift 01:00 - 02:00 while vnNow is current time)
        var earlyTime = new TimeSpan(1, 0, 0);
        var earlyEndTime = new TimeSpan(2, 0, 0);
        schedule.PlannedStartTime = earlyTime;
        schedule.PlannedEndTime = earlyEndTime;
        if (vnNow.TimeOfDay > new TimeSpan(2, 35, 0))
        {
            var exWin = await Assert.ThrowsAsync<BusinessException>(() =>
                service.SelfCheckInAsync(schedId, new SelfCheckInRequest()));
            Assert.Equal("OUTSIDE_CHECKIN_WINDOW", exWin.ErrorCode);
        }

        // 7. Success with all-time shift (00:00 to 23:59)
        schedule.PlannedStartTime = TimeSpan.Zero;
        schedule.PlannedEndTime = new TimeSpan(23, 59, 0);
        var res = await service.SelfCheckInAsync(schedId, new SelfCheckInRequest());
        Assert.NotNull(res);
        Assert.Equal(ScheduleStatus.InProgress, res.Status);
    }

    [Fact]
    public async Task GetMySchedulesAsync_WithEmployeeGuidFallback_ShouldReturnExpected()
    {
        var scheduleRepo = new FakeScheduleRepository();
        var employeeRepo = new FakeEmployeeRepository();
        var shiftConfigRepo = new FakeShiftConfigRepository();
        var uow = new FakeUnitOfWork();
        var tenantContext = new FakeTenantContext();

        var empId = Guid.NewGuid();
        var branchId = Guid.NewGuid();

        // Employee whose UserId is DIFFERENT from its Id
        var employee = new Employee
        {
            Id = empId,
            TenantId = tenantContext.CurrentTenantId,
            UserId = "auth0|user_12345",
            FullName = "Tran B",
            EmployeeCode = "NV02",
            Status = EmployeeStatus.Active,
            BranchId = branchId
        };
        employeeRepo.Employees.Add(employee);

        var sched = new EmployeeSchedule
        {
            Id = Guid.NewGuid(),
            TenantId = tenantContext.CurrentTenantId,
            EmployeeId = empId,
            Employee = employee,
            BranchId = branchId,
            WorkDate = DateTime.UtcNow.Date,
            PlannedStartTime = new TimeSpan(8, 0, 0),
            PlannedEndTime = new TimeSpan(17, 0, 0),
            Status = ScheduleStatus.Published
        };
        scheduleRepo.Schedules.Add(sched);

        var service = new ScheduleService(scheduleRepo, employeeRepo, shiftConfigRepo, uow, tenantContext);

        // 1. By empId string fallback (when GetByUserIdAsync returns null)
        var result1 = await service.GetMySchedulesAsync(empId.ToString(), null, null);
        Assert.Single(result1);

        // 2. Unknown userId -> returns empty
        var result2 = await service.GetMySchedulesAsync("non_existent_user", null, null);
        Assert.Empty(result2);
    }

    [Fact]
    public async Task CreateAsync_EmployeeInactive_ThrowsBusinessException()
    {
        var scheduleRepo = new FakeScheduleRepository();
        var employeeRepo = new FakeEmployeeRepository();
        var shiftConfigRepo = new FakeShiftConfigRepository();
        var uow = new FakeUnitOfWork();
        var tenantContext = new FakeTenantContext();

        var emp = new Employee
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant_test_01",
            FullName = "Inactive Emp",
            EmployeeCode = "NV9999",
            Status = EmployeeStatus.Inactive
        };
        employeeRepo.Employees.Add(emp);

        var service = new ScheduleService(scheduleRepo, employeeRepo, shiftConfigRepo, uow, tenantContext);

        var request = new CreateScheduleItemRequest(
            EmployeeId: emp.Id,
            BranchId: Guid.NewGuid(),
            WorkDate: DateTime.Today,
            PlannedStartTime: new TimeSpan(8, 0, 0),
            PlannedEndTime: new TimeSpan(17, 0, 0)
        );

        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.CreateAsync(request));
        Assert.Equal("EMPLOYEE_INACTIVE", ex.ErrorCode);
    }

    [Fact]
    public async Task CreateRecurringScheduleAsync_EndDateBeforeStartDate_ThrowsBusinessException()
    {
        var scheduleRepo = new FakeScheduleRepository();
        var employeeRepo = new FakeEmployeeRepository();
        var shiftConfigRepo = new FakeShiftConfigRepository();
        var uow = new FakeUnitOfWork();
        var tenantContext = new FakeTenantContext();

        var emp = new Employee
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant_test_01",
            FullName = "Active Emp",
            EmployeeCode = "NV0001",
            Status = EmployeeStatus.Active
        };
        employeeRepo.Employees.Add(emp);

        var service = new ScheduleService(scheduleRepo, employeeRepo, shiftConfigRepo, uow, tenantContext);

        var request = new CreateRecurringScheduleRequest(
            EmployeeId: emp.Id,
            BranchId: Guid.NewGuid(),
            StartDate: DateTime.Today,
            HasEndDate: true,
            EndDate: DateTime.Today.AddDays(-2),
            RepeatDays: new List<string> { "Monday" },
            PlannedStartTime: new TimeSpan(8, 0, 0),
            PlannedEndTime: new TimeSpan(17, 0, 0)
        );

        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.CreateRecurringAsync(request));
        Assert.Equal("INVALID_DATE_RANGE", ex.ErrorCode);
    }

    [Fact]
    public async Task CreateRecurringScheduleAsync_EmployeeInactive_ThrowsBusinessException()
    {
        var scheduleRepo = new FakeScheduleRepository();
        var employeeRepo = new FakeEmployeeRepository();
        var shiftConfigRepo = new FakeShiftConfigRepository();
        var uow = new FakeUnitOfWork();
        var tenantContext = new FakeTenantContext();

        var emp = new Employee
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant_test_01",
            FullName = "Inactive Emp",
            EmployeeCode = "NV0002",
            Status = EmployeeStatus.Inactive
        };
        employeeRepo.Employees.Add(emp);

        var service = new ScheduleService(scheduleRepo, employeeRepo, shiftConfigRepo, uow, tenantContext);

        var request = new CreateRecurringScheduleRequest(
            EmployeeId: emp.Id,
            BranchId: Guid.NewGuid(),
            StartDate: DateTime.Today,
            HasEndDate: true,
            EndDate: DateTime.Today.AddDays(7),
            RepeatDays: new List<string> { "Monday" },
            PlannedStartTime: new TimeSpan(8, 0, 0),
            PlannedEndTime: new TimeSpan(17, 0, 0)
        );

        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.CreateRecurringAsync(request));
        Assert.Equal("EMPLOYEE_INACTIVE", ex.ErrorCode);
    }

    [Fact]
    public async Task UpdateAsync_CompletedSchedule_ThrowsBusinessException()
    {
        var scheduleRepo = new FakeScheduleRepository();
        var employeeRepo = new FakeEmployeeRepository();
        var shiftConfigRepo = new FakeShiftConfigRepository();
        var uow = new FakeUnitOfWork();
        var tenantContext = new FakeTenantContext();

        var sched = new EmployeeSchedule
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant_test_01",
            EmployeeId = Guid.NewGuid(),
            BranchId = Guid.NewGuid(),
            WorkDate = DateTime.Today,
            Status = ScheduleStatus.Completed
        };
        scheduleRepo.Schedules.Add(sched);

        var service = new ScheduleService(scheduleRepo, employeeRepo, shiftConfigRepo, uow, tenantContext);

        var request = new UpdateScheduleRequest(Status: ScheduleStatus.Published);

        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.UpdateAsync(sched.Id, request));
        Assert.Contains("Không thể chỉnh sửa ca làm việc đã hoàn thành", ex.Message);
    }

    [Fact]
    public async Task DeleteAsync_CompletedSchedule_ThrowsBusinessException()
    {
        var scheduleRepo = new FakeScheduleRepository();
        var employeeRepo = new FakeEmployeeRepository();
        var shiftConfigRepo = new FakeShiftConfigRepository();
        var uow = new FakeUnitOfWork();
        var tenantContext = new FakeTenantContext();

        var sched = new EmployeeSchedule
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant_test_01",
            EmployeeId = Guid.NewGuid(),
            BranchId = Guid.NewGuid(),
            WorkDate = DateTime.Today,
            Status = ScheduleStatus.Completed
        };
        scheduleRepo.Schedules.Add(sched);

        var service = new ScheduleService(scheduleRepo, employeeRepo, shiftConfigRepo, uow, tenantContext);

        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.DeleteAsync(sched.Id));
        Assert.Contains("Không thể xóa ca làm việc đã hoàn thành", ex.Message);
    }

    [Fact]
    public async Task DeleteAsync_LeaveSchedule_ThrowsBusinessException()
    {
        var scheduleRepo = new FakeScheduleRepository();
        var employeeRepo = new FakeEmployeeRepository();
        var shiftConfigRepo = new FakeShiftConfigRepository();
        var uow = new FakeUnitOfWork();
        var tenantContext = new FakeTenantContext();

        var sched = new EmployeeSchedule
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant_test_01",
            EmployeeId = Guid.NewGuid(),
            BranchId = Guid.NewGuid(),
            WorkDate = DateTime.Today,
            Status = ScheduleStatus.OnLeave
        };
        scheduleRepo.Schedules.Add(sched);

        var service = new ScheduleService(scheduleRepo, employeeRepo, shiftConfigRepo, uow, tenantContext);

        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.DeleteAsync(sched.Id));
        Assert.Contains("Không thể xóa ca làm việc đang ở trạng thái nghỉ phép", ex.Message);
    }

    [Fact]
    public async Task DeleteAsync_WithAttendanceData_ThrowsBusinessException()
    {
        var scheduleRepo = new FakeScheduleRepository();
        var employeeRepo = new FakeEmployeeRepository();
        var shiftConfigRepo = new FakeShiftConfigRepository();
        var uow = new FakeUnitOfWork();
        var tenantContext = new FakeTenantContext();

        var sched = new EmployeeSchedule
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant_test_01",
            EmployeeId = Guid.NewGuid(),
            BranchId = Guid.NewGuid(),
            WorkDate = DateTime.Today,
            Status = ScheduleStatus.Published,
            CheckInTime = DateTime.UtcNow
        };
        scheduleRepo.Schedules.Add(sched);

        var service = new ScheduleService(scheduleRepo, employeeRepo, shiftConfigRepo, uow, tenantContext);

        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.DeleteAsync(sched.Id));
        Assert.Contains("Không thể xóa ca làm việc đã có dữ liệu chấm công", ex.Message);
    }

    [Fact]
    public async Task RecordManualAttendanceAsync_InvalidAttendanceType_ThrowsBusinessException()
    {
        var scheduleRepo = new FakeScheduleRepository();
        var employeeRepo = new FakeEmployeeRepository();
        var shiftConfigRepo = new FakeShiftConfigRepository();
        var uow = new FakeUnitOfWork();
        var tenantContext = new FakeTenantContext();

        var sched = new EmployeeSchedule
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant_test_01",
            EmployeeId = Guid.NewGuid(),
            BranchId = Guid.NewGuid(),
            WorkDate = DateTime.Today,
            Status = ScheduleStatus.Published,
            PlannedStartTime = new TimeSpan(8, 0, 0),
            PlannedEndTime = new TimeSpan(17, 0, 0)
        };
        scheduleRepo.Schedules.Add(sched);

        var service = new ScheduleService(scheduleRepo, employeeRepo, shiftConfigRepo, uow, tenantContext);

        var request = new ManualAttendanceRequest(
            AttendanceType: (ManualAttendanceType)99
        );

        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.RecordManualAttendanceAsync(sched.Id, request));
        Assert.Equal("INVALID_ATTENDANCE_TYPE", ex.ErrorCode);
    }

    [Fact]
    public async Task ResetManualAttendanceAsync_ReasonTooLong_ThrowsBusinessException()
    {
        var scheduleRepo = new FakeScheduleRepository();
        var employeeRepo = new FakeEmployeeRepository();
        var shiftConfigRepo = new FakeShiftConfigRepository();
        var uow = new FakeUnitOfWork();
        var tenantContext = new FakeTenantContext();

        var sched = new EmployeeSchedule
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant_test_01",
            EmployeeId = Guid.NewGuid(),
            BranchId = Guid.NewGuid(),
            WorkDate = DateTime.Today,
            Status = ScheduleStatus.Completed,
            CheckInTime = DateTime.UtcNow
        };
        scheduleRepo.Schedules.Add(sched);

        var service = new ScheduleService(scheduleRepo, employeeRepo, shiftConfigRepo, uow, tenantContext);

        var request = new ResetAttendanceRequest(
            Reason: new string('A', 256)
        );

        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.ResetManualAttendanceAsync(sched.Id, request));
        Assert.Equal("INVALID_RESET_REASON", ex.ErrorCode);
    }
}


