using System.Linq.Expressions;
using Moq;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.HR.Employees;
using MT_GRMS.Application.HR.Schedules;
using MT_GRMS.Application.HR.Schedules.Dtos;
using MT_GRMS.Domain.Entities.HR;
using MT_GRMS.Domain.Enums;
using Xunit;

namespace MT_GRMS.UnitTests;

public class ScheduleApprovalServiceTests
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
            return Task.FromResult((IReadOnlyList<EmployeeSchedule>)query.ToList());
        }

        public Task<IReadOnlyList<EmployeeSchedule>> GetActiveSchedulesForEmployeesAsync(
            IEnumerable<Guid> employeeIds,
            DateTime fromDate,
            DateTime toDate,
            CancellationToken cancellationToken = default)
        {
            var idSet = employeeIds.ToHashSet();
            return Task.FromResult((IReadOnlyList<EmployeeSchedule>)Schedules
                .Where(s => idSet.Contains(s.EmployeeId) && s.WorkDate >= fromDate.Date && s.WorkDate <= toDate.Date && !s.IsDeleted)
                .ToList());
        }

        public Task<IReadOnlyList<EmployeeSchedule>> GetByEmployeeAndDateRangeAsync(
            Guid employeeId,
            DateTime fromDate,
            DateTime toDate,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult((IReadOnlyList<EmployeeSchedule>)Schedules
                .Where(s => s.EmployeeId == employeeId && s.WorkDate >= fromDate.Date && s.WorkDate <= toDate.Date && !s.IsDeleted)
                .ToList());
        }

        public Task<IReadOnlyList<EmployeeSchedule>> GetCompletedSchedulesInPeriodAsync(
            IEnumerable<Guid> employeeIds,
            DateTime fromDate,
            DateTime toDate,
            CancellationToken cancellationToken = default)
        {
            var set = employeeIds.ToHashSet();
            return Task.FromResult((IReadOnlyList<EmployeeSchedule>)Schedules
                .Where(s => !s.IsDeleted && set.Contains(s.EmployeeId) && s.WorkDate >= fromDate.Date && s.WorkDate <= toDate.Date && s.Status == ScheduleStatus.Completed)
                .ToList());
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

    private class FakeScheduleApprovalRequestRepository : IScheduleApprovalRequestRepository
    {
        public List<ScheduleApprovalRequest> Requests { get; } = new();

        public Task<ScheduleApprovalRequest?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Requests.FirstOrDefault(r => r.Id == id && !r.IsDeleted));
        }

        public Task<ScheduleApprovalRequest?> GetByIdWithDetailsAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Requests.FirstOrDefault(r => r.Id == id && !r.IsDeleted));
        }

        public Task<IReadOnlyList<ScheduleApprovalRequest>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult((IReadOnlyList<ScheduleApprovalRequest>)Requests.Where(r => !r.IsDeleted).ToList());
        }

        public Task<IReadOnlyList<ScheduleApprovalRequest>> FindAsync(Expression<Func<ScheduleApprovalRequest, bool>> predicate, CancellationToken cancellationToken = default)
        {
            var func = predicate.Compile();
            return Task.FromResult((IReadOnlyList<ScheduleApprovalRequest>)Requests.Where(func).Where(r => !r.IsDeleted).ToList());
        }

        public Task<IReadOnlyList<ScheduleApprovalRequest>> GetByScheduleIdAsync(Guid scheduleId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult((IReadOnlyList<ScheduleApprovalRequest>)Requests
                .Where(r => r.ScheduleId == scheduleId && !r.IsDeleted)
                .ToList());
        }

        public Task<IReadOnlyList<ScheduleApprovalRequest>> GetPendingAsync(Guid? branchId, ScheduleRequestType? requestType, CancellationToken cancellationToken = default)
        {
            return GetRequestsAsync(branchId, requestType, ScheduleApprovalStatus.Pending, cancellationToken);
        }

        public Task<IReadOnlyList<ScheduleApprovalRequest>> GetRequestsAsync(Guid? branchId, ScheduleRequestType? requestType, ScheduleApprovalStatus? status, CancellationToken cancellationToken = default)
        {
            var query = Requests.Where(r => !r.IsDeleted);
            if (status.HasValue)
                query = query.Where(r => r.Status == status.Value);
            if (branchId.HasValue)
                query = query.Where(r => r.BranchId == branchId.Value);
            if (requestType.HasValue)
                query = query.Where(r => r.RequestType == requestType.Value);
            return Task.FromResult((IReadOnlyList<ScheduleApprovalRequest>)query.ToList());
        }

        public Task<bool> HasPendingRequestOfTypeAsync(Guid scheduleId, ScheduleRequestType requestType, CancellationToken cancellationToken = default)
        {
            var exists = Requests.Any(r => r.ScheduleId == scheduleId && r.RequestType == requestType && r.Status == ScheduleApprovalStatus.Pending && !r.IsDeleted);
            return Task.FromResult(exists);
        }

        public Task AddAsync(ScheduleApprovalRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.CompletedTask;
        }

        public void Update(ScheduleApprovalRequest request)
        {
            var idx = Requests.FindIndex(r => r.Id == request.Id);
            if (idx >= 0) Requests[idx] = request;
        }

        public void Delete(ScheduleApprovalRequest request)
        {
            request.IsDeleted = true;
        }
    }

    private readonly FakeScheduleRepository _scheduleRepo = new();
    private readonly FakeScheduleApprovalRequestRepository _requestRepo = new();
    private readonly Mock<IEmployeeRepository> _employeeRepoMock = new();
    private readonly FakeUnitOfWork _uow = new();
    private readonly FakeTenantContext _tenantContext = new();
    private readonly List<Employee> _employees = new();

    public ScheduleApprovalServiceTests()
    {
        _employeeRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => _employees.FirstOrDefault(e => e.Id == id));

        _employeeRepoMock.Setup(r => r.GetByUserIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string uid, CancellationToken _) => _employees.FirstOrDefault(e => e.UserId == uid));
    }

    private ScheduleApprovalService CreateService()
    {
        return new ScheduleApprovalService(
            _requestRepo,
            _scheduleRepo,
            _employeeRepoMock.Object,
            _uow,
            _tenantContext
        );
    }

    [Fact]
    public async Task CreateRequestAsync_WhenValidLeaveRequest_CreatesRequestSuccessfully()
    {
        // Arrange
        var service = CreateService();
        var emp = new Employee
        {
            Id = Guid.NewGuid(),
            UserId = _tenantContext.CurrentUserId.ToString(),
            FullName = "Nguyen Van A",
            EmployeeCode = "NV001",
            TenantId = _tenantContext.CurrentTenantId
        };
        _employees.Add(emp);

        var schedule = new EmployeeSchedule
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.CurrentTenantId,
            EmployeeId = emp.Id,
            BranchId = _tenantContext.CurrentBranchId ?? Guid.NewGuid(),
            WorkDate = DateTime.UtcNow.Date.AddDays(2),
            PlannedStartTime = new TimeSpan(8, 0, 0),
            PlannedEndTime = new TimeSpan(17, 0, 0),
            Status = ScheduleStatus.Published
        };
        _scheduleRepo.Schedules.Add(schedule);

        var dto = new CreateScheduleApprovalRequestDto(
            RequestType: ScheduleRequestType.LeaveRequest,
            Reason: "Xin nghi phep nam vi viec gia dinh",
            LeaveType: LeaveType.AnnualLeave,
            IsPaidLeaveRequested: true
        );

        // Act
        var result = await service.CreateRequestAsync(schedule.Id, emp.UserId, dto);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(ScheduleRequestType.LeaveRequest, result.RequestType);
        Assert.Equal(ScheduleApprovalStatus.Pending, result.Status);
        Assert.Equal(emp.Id, result.EmployeeId);
        Assert.Single(_requestRepo.Requests);
    }

    [Fact]
    public async Task CreateRequestAsync_WhenScheduleLockedToPayslip_ThrowsInvalidOperationException()
    {
        // Arrange
        var service = CreateService();
        var emp = new Employee
        {
            Id = Guid.NewGuid(),
            UserId = _tenantContext.CurrentUserId.ToString(),
            FullName = "Nguyen Van A",
            EmployeeCode = "NV001",
            TenantId = _tenantContext.CurrentTenantId
        };
        _employees.Add(emp);

        var schedule = new EmployeeSchedule
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.CurrentTenantId,
            EmployeeId = emp.Id,
            BranchId = _tenantContext.CurrentBranchId ?? Guid.NewGuid(),
            WorkDate = DateTime.UtcNow.Date.AddDays(-10),
            PlannedStartTime = new TimeSpan(8, 0, 0),
            PlannedEndTime = new TimeSpan(17, 0, 0),
            Status = ScheduleStatus.Published,
            PayslipId = Guid.NewGuid() // CA ĐÃ CHỐT LƯƠNG
        };
        _scheduleRepo.Schedules.Add(schedule);

        var dto = new CreateScheduleApprovalRequestDto(
            RequestType: ScheduleRequestType.CheckInIncident,
            Reason: "Bi loi face id"
        );

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateRequestAsync(schedule.Id, emp.UserId, dto));
    }

    [Fact]
    public async Task CreateRequestAsync_WhenDuplicatePendingRequestExists_ThrowsInvalidOperationException()
    {
        // Arrange
        var service = CreateService();
        var emp = new Employee
        {
            Id = Guid.NewGuid(),
            UserId = _tenantContext.CurrentUserId.ToString(),
            FullName = "Nguyen Van A",
            EmployeeCode = "NV001",
            TenantId = _tenantContext.CurrentTenantId
        };
        _employees.Add(emp);

        var schedule = new EmployeeSchedule
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.CurrentTenantId,
            EmployeeId = emp.Id,
            BranchId = _tenantContext.CurrentBranchId ?? Guid.NewGuid(),
            WorkDate = DateTime.UtcNow.Date.AddDays(2),
            PlannedStartTime = new TimeSpan(8, 0, 0),
            PlannedEndTime = new TimeSpan(17, 0, 0),
            Status = ScheduleStatus.Published
        };
        _scheduleRepo.Schedules.Add(schedule);

        // Đã có 1 đơn pending cùng loại
        _requestRepo.Requests.Add(new ScheduleApprovalRequest
        {
            Id = Guid.NewGuid(),
            ScheduleId = schedule.Id,
            RequestType = ScheduleRequestType.LeaveRequest,
            Status = ScheduleApprovalStatus.Pending,
            EmployeeId = emp.Id,
            TenantId = _tenantContext.CurrentTenantId
        });

        var dto = new CreateScheduleApprovalRequestDto(
            RequestType: ScheduleRequestType.LeaveRequest,
            Reason: "Xin nghi om",
            LeaveType: LeaveType.SickLeave
        );

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateRequestAsync(schedule.Id, emp.UserId, dto));
    }

    [Fact]
    public async Task ReviewRequestAsync_ApproveLeaveRequest_UpdatesScheduleToOnLeave()
    {
        // Arrange
        var service = CreateService();
        var emp = new Employee
        {
            Id = Guid.NewGuid(),
            FullName = "Nguyen Van A",
            EmployeeCode = "NV001",
            TenantId = _tenantContext.CurrentTenantId
        };
        _employees.Add(emp);

        var schedule = new EmployeeSchedule
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.CurrentTenantId,
            EmployeeId = emp.Id,
            BranchId = _tenantContext.CurrentBranchId ?? Guid.NewGuid(),
            WorkDate = DateTime.UtcNow.Date.AddDays(1),
            PlannedStartTime = new TimeSpan(8, 0, 0),
            PlannedEndTime = new TimeSpan(17, 0, 0),
            Status = ScheduleStatus.Published
        };
        _scheduleRepo.Schedules.Add(schedule);

        var request = new ScheduleApprovalRequest
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.CurrentTenantId,
            ScheduleId = schedule.Id,
            Schedule = schedule,
            EmployeeId = emp.Id,
            RequestType = ScheduleRequestType.LeaveRequest,
            LeaveType = LeaveType.AnnualLeave,
            Status = ScheduleApprovalStatus.Pending,
            Reason = "Nghi phep"
        };
        _requestRepo.Requests.Add(request);

        var reviewDto = new ReviewScheduleApprovalRequestDto(
            IsApproved: true,
            ReviewNote: "Dong y cho phep nghi",
            IsPaidLeave: true
        );

        // Act
        var result = await service.ReviewRequestAsync(request.Id, Guid.NewGuid().ToString(), reviewDto);

        // Assert
        Assert.True(result);
        Assert.Equal(ScheduleApprovalStatus.Approved, request.Status);
        Assert.Equal(ScheduleStatus.OnLeave, schedule.Status);
        Assert.True(schedule.IsLeave);
        Assert.True(schedule.IsPaidLeave); // AnnualLeave is paid leave
    }

    [Fact]
    public async Task ReviewRequestAsync_ApproveCrossSwap_SwapsEmployeeIdsCorrectly()
    {
        // Arrange
        var service = CreateService();
        var emp1 = new Employee { Id = Guid.NewGuid(), FullName = "NV A", TenantId = _tenantContext.CurrentTenantId };
        var emp2 = new Employee { Id = Guid.NewGuid(), FullName = "NV B", TenantId = _tenantContext.CurrentTenantId };
        _employees.AddRange(new[] { emp1, emp2 });

        var schedule1 = new EmployeeSchedule
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.CurrentTenantId,
            EmployeeId = emp1.Id,
            BranchId = _tenantContext.CurrentBranchId ?? Guid.NewGuid(),
            WorkDate = DateTime.UtcNow.Date.AddDays(1),
            PlannedStartTime = new TimeSpan(8, 0, 0),
            PlannedEndTime = new TimeSpan(12, 0, 0),
            Status = ScheduleStatus.Published
        };
        var schedule2 = new EmployeeSchedule
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.CurrentTenantId,
            EmployeeId = emp2.Id,
            BranchId = schedule1.BranchId,
            WorkDate = DateTime.UtcNow.Date.AddDays(1),
            PlannedStartTime = new TimeSpan(13, 0, 0),
            PlannedEndTime = new TimeSpan(17, 0, 0),
            Status = ScheduleStatus.Published
        };
        _scheduleRepo.Schedules.AddRange(new[] { schedule1, schedule2 });

        var request = new ScheduleApprovalRequest
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.CurrentTenantId,
            ScheduleId = schedule1.Id,
            Schedule = schedule1,
            EmployeeId = emp1.Id,
            TargetEmployeeId = emp2.Id,
            TargetScheduleId = schedule2.Id,
            RequestType = ScheduleRequestType.ShiftSwap,
            SwapType = ShiftSwapType.CrossSwap,
            Status = ScheduleApprovalStatus.Pending,
            Reason = "Doi ca chieu"
        };
        _requestRepo.Requests.Add(request);

        var reviewDto = new ReviewScheduleApprovalRequestDto(
            IsApproved: true,
            ReviewNote: "Duyet doi ca"
        );

        // Act
        var result = await service.ReviewRequestAsync(request.Id, Guid.NewGuid().ToString(), reviewDto);

        // Assert
        Assert.True(result);
        Assert.Equal(ScheduleApprovalStatus.Approved, request.Status);
        Assert.Equal(emp2.Id, schedule1.EmployeeId);
        Assert.Equal(emp1.Id, schedule2.EmployeeId);
    }

    [Fact]
    public async Task ReviewRequestAsync_ApproveCheckInIncident_UpdatesActualTimesAndMethod()
    {
        // Arrange
        var service = CreateService();
        var emp = new Employee { Id = Guid.NewGuid(), FullName = "NV A", TenantId = _tenantContext.CurrentTenantId };
        _employees.Add(emp);

        var schedule = new EmployeeSchedule
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.CurrentTenantId,
            EmployeeId = emp.Id,
            BranchId = _tenantContext.CurrentBranchId ?? Guid.NewGuid(),
            WorkDate = DateTime.UtcNow.Date,
            PlannedStartTime = new TimeSpan(8, 0, 0),
            PlannedEndTime = new TimeSpan(17, 0, 0),
            Status = ScheduleStatus.Published
        };
        _scheduleRepo.Schedules.Add(schedule);

        var date = DateTime.UtcNow.Date;
        var checkIn = date.AddHours(8).AddMinutes(5);
        var checkOut = date.AddHours(17);

        var request = new ScheduleApprovalRequest
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.CurrentTenantId,
            ScheduleId = schedule.Id,
            Schedule = schedule,
            EmployeeId = emp.Id,
            RequestType = ScheduleRequestType.CheckInIncident,
            RequestedCheckInTime = checkIn,
            RequestedCheckOutTime = checkOut,
            Status = ScheduleApprovalStatus.Pending,
            Reason = "Camera FaceID loi ket noi"
        };
        _requestRepo.Requests.Add(request);

        var reviewDto = new ReviewScheduleApprovalRequestDto(
            IsApproved: true,
            ReviewNote: "Da kiem tra camera loi that",
            ApprovedCheckInTime: checkIn,
            ApprovedCheckOutTime: checkOut
        );

        // Act
        var result = await service.ReviewRequestAsync(request.Id, Guid.NewGuid().ToString(), reviewDto);

        // Assert
        Assert.True(result);
        Assert.Equal(ScheduleApprovalStatus.Approved, request.Status);
        Assert.Equal(checkIn, schedule.CheckInTime);
        Assert.Equal(checkOut, schedule.CheckOutTime);
        Assert.Equal("INCIDENT_EXPLAINED", schedule.CheckInMethod);
        Assert.True(schedule.ActualWorkHours > 0);
    }

    [Fact]
    public async Task ReviewRequestAsync_RejectRequest_MarksStatusRejectedWithoutChangingSchedule()
    {
        // Arrange
        var service = CreateService();
        var emp = new Employee { Id = Guid.NewGuid(), FullName = "NV A", TenantId = _tenantContext.CurrentTenantId };
        _employees.Add(emp);

        var schedule = new EmployeeSchedule
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.CurrentTenantId,
            EmployeeId = emp.Id,
            BranchId = _tenantContext.CurrentBranchId ?? Guid.NewGuid(),
            WorkDate = DateTime.UtcNow.Date.AddDays(1),
            PlannedStartTime = new TimeSpan(8, 0, 0),
            PlannedEndTime = new TimeSpan(17, 0, 0),
            Status = ScheduleStatus.Published
        };
        _scheduleRepo.Schedules.Add(schedule);

        var request = new ScheduleApprovalRequest
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.CurrentTenantId,
            ScheduleId = schedule.Id,
            Schedule = schedule,
            EmployeeId = emp.Id,
            RequestType = ScheduleRequestType.Overtime,
            RequestedOvertimeMinutes = 120,
            Status = ScheduleApprovalStatus.Pending,
            Reason = "Xin lam them 2 tieng"
        };
        _requestRepo.Requests.Add(request);

        var reviewDto = new ReviewScheduleApprovalRequestDto(
            IsApproved: false,
            ReviewNote: "Khong can thiet lam them"
        );

        // Act
        var result = await service.ReviewRequestAsync(request.Id, Guid.NewGuid().ToString(), reviewDto);

        // Assert
        Assert.True(result);
        Assert.Equal(ScheduleApprovalStatus.Rejected, request.Status);
        Assert.Equal("Khong can thiet lam them", request.ReviewNote);
        Assert.Equal(0, schedule.OvertimeMinutes);
    }
}
