using MT_GRMS.Application.Common;
using MT_GRMS.Application.HR.Employees;
using MT_GRMS.Application.HR.Employees.Dtos;
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

        // Ca 1: 06:00 - 14:00, Ca 2: 12:00 - 18:00 (Trùng nhau 2 giờ <= 12 giờ -> HỢP LỆ)
        var request = new BatchCreateScheduleRequest(
            branchId,
            new List<CreateScheduleItemRequest>
            {
                new(emp.Id, branchId, workDate, null, "CA_SANG", "Ca Sáng", TimeSpan.FromHours(6), TimeSpan.FromHours(14)),
                new(emp.Id, branchId, workDate, null, "CA_GIAOTHOA", "Ca Giao thoa", TimeSpan.FromHours(12), TimeSpan.FromHours(18))
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

        // Ca 1: 00:00 - 16:00, Ca 2: 02:00 - 18:00 (Trùng nhau 14 giờ > 12 giờ -> BÁO LỖI)
        var request = new BatchCreateScheduleRequest(
            branchId,
            new List<CreateScheduleItemRequest>
            {
                new(emp.Id, branchId, workDate, null, "CA_DAI_1", "Ca Dài 1", TimeSpan.FromHours(0), TimeSpan.FromHours(16)),
                new(emp.Id, branchId, workDate, null, "CA_DAI_2", "Ca Dài 2", TimeSpan.FromHours(2), TimeSpan.FromHours(18))
            }
        );

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.BatchCreateAsync(request));
        Assert.Equal("SCHEDULE_OVERLAP_EXCEEDED", ex.ErrorCode);
        Assert.Contains("vượt quá 12 giờ", ex.Message);
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

        // Đã có ca sẵn trong DB: 06:00 - 14:00
        scheduleRepo.Schedules.Add(new EmployeeSchedule
        {
            Id = Guid.NewGuid(),
            EmployeeId = emp.Id,
            BranchId = branchId,
            WorkDate = workDate,
            ShiftName = "Ca Sáng",
            PlannedStartTime = TimeSpan.FromHours(6),
            PlannedEndTime = TimeSpan.FromHours(14),
            Status = ScheduleStatus.Draft
        });

        var service = new ScheduleService(scheduleRepo, employeeRepo, shiftConfigRepo, uow, tenantContext);

        // Gửi xếp thêm ca: 12:00 - 18:00 (Giao thoa 2 giờ <= 12 giờ -> Cho phép)
        var request = new BatchCreateScheduleRequest(
            branchId,
            new List<CreateScheduleItemRequest>
            {
                new(emp.Id, branchId, workDate, null, "CA_GIAOTHOA", "Ca Giao thoa", TimeSpan.FromHours(12), TimeSpan.FromHours(18))
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

        // Đã có ca sẵn trong DB: 00:00 - 16:00
        scheduleRepo.Schedules.Add(new EmployeeSchedule
        {
            Id = Guid.NewGuid(),
            EmployeeId = emp.Id,
            BranchId = branchId,
            WorkDate = workDate,
            ShiftName = "Ca Dài 1",
            PlannedStartTime = TimeSpan.FromHours(0),
            PlannedEndTime = TimeSpan.FromHours(16),
            Status = ScheduleStatus.Draft
        });

        var service = new ScheduleService(scheduleRepo, employeeRepo, shiftConfigRepo, uow, tenantContext);

        // Gửi xếp thêm ca: 02:00 - 18:00 (Giao thoa 14 giờ > 12 giờ -> Ném ngoại lệ)
        var request = new BatchCreateScheduleRequest(
            branchId,
            new List<CreateScheduleItemRequest>
            {
                new(emp.Id, branchId, workDate, null, "CA_DAI_2", "Ca Dài 2", TimeSpan.FromHours(2), TimeSpan.FromHours(18))
            }
        );

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.BatchCreateAsync(request));
        Assert.Equal("SCHEDULE_OVERLAP_EXCEEDED", ex.ErrorCode);
        Assert.Contains("vượt quá 12 giờ", ex.Message);
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
}

