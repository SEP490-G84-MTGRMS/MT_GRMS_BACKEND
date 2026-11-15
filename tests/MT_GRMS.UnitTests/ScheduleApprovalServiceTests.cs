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
            RequestType = ScheduleRequestType.ShiftSwap,
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

        // Assert: Chỉ đánh dấu duyệt đơn đổi ca, không tự động đổi EmployeeId của các ca
        Assert.True(result);
        Assert.Equal(ScheduleApprovalStatus.Approved, request.Status);
        Assert.Equal("SHIFT_SWAP", schedule1.ApprovalType);
        Assert.Equal(emp1.Id, schedule1.EmployeeId);
        Assert.Equal(emp2.Id, schedule2.EmployeeId);
    }

    [Fact]
    public async Task CreateRequestAsync_ShiftSwap_WithOnlyScheduleAndReason_CreatesPendingRequest()
    {
        // Arrange
        var service = CreateService();
        var userId = Guid.NewGuid().ToString();
        var emp = new Employee { Id = Guid.NewGuid(), UserId = userId, FullName = "NV Xin Đổi Ca", TenantId = _tenantContext.CurrentTenantId };
        _employees.Add(emp);

        var schedule = new EmployeeSchedule
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.CurrentTenantId,
            EmployeeId = emp.Id,
            BranchId = _tenantContext.CurrentBranchId ?? Guid.NewGuid(),
            WorkDate = DateTime.UtcNow.Date.AddDays(1),
            ShiftName = "Ca Sáng",
            PlannedStartTime = new TimeSpan(8, 0, 0),
            PlannedEndTime = new TimeSpan(12, 0, 0),
            Status = ScheduleStatus.Published
        };
        _scheduleRepo.Schedules.Add(schedule);

        var dto = new CreateScheduleApprovalRequestDto(
            RequestType: ScheduleRequestType.ShiftSwap,
            Reason: "Xin đổi từ Ca Sáng sang Ca Chiều ngày mai do bận việc gia đình"
        );

        // Act
        var created = await service.CreateRequestAsync(schedule.Id, userId, dto);

        // Assert
        Assert.Equal(ScheduleRequestType.ShiftSwap, created.RequestType);
        Assert.Equal(ScheduleApprovalStatus.Pending, created.Status);
        Assert.Equal("Xin đổi từ Ca Sáng sang Ca Chiều ngày mai do bận việc gia đình", created.Reason);
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
        Assert.Equal("INCIDENT_EXPLAINED", schedule.CheckInMethod);
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

    [Fact]
    public async Task UpdateRequestAsync_PendingRequest_UpdatesFields()
    {
        // Arrange
        var service = CreateService();
        var userId = Guid.NewGuid().ToString();
        var emp = new Employee { Id = Guid.NewGuid(), UserId = userId, FullName = "NV A", TenantId = _tenantContext.CurrentTenantId };
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
            Employee = emp,
            RequestType = ScheduleRequestType.LeaveRequest,
            LeaveType = LeaveType.SickLeave,
            IsPaidLeave = false,
            Status = ScheduleApprovalStatus.Pending,
            Reason = "Lý do cũ"
        };
        _requestRepo.Requests.Add(request);

        var dto = new CreateScheduleApprovalRequestDto(
            RequestType: ScheduleRequestType.LeaveRequest,
            Reason: "Cập nhật lý do xin nghỉ phép năm",
            LeaveType: LeaveType.AnnualLeave,
            IsPaidLeaveRequested: true
        );

        // Act
        var updated = await service.UpdateRequestAsync(request.Id, userId, isOwnerOrAdmin: false, dto);

        // Assert
        Assert.Equal("Cập nhật lý do xin nghỉ phép năm", updated.Reason);
        Assert.Equal(LeaveType.AnnualLeave, updated.LeaveType);
        Assert.True(updated.IsPaidLeave);
    }

    [Fact]
    public async Task UpdateRequestAsync_NonPendingRequest_ThrowsInvalidOperationException()
    {
        // Arrange
        var service = CreateService();
        var userId = Guid.NewGuid().ToString();
        var emp = new Employee { Id = Guid.NewGuid(), UserId = userId, FullName = "NV A", TenantId = _tenantContext.CurrentTenantId };
        _employees.Add(emp);

        var request = new ScheduleApprovalRequest
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.CurrentTenantId,
            ScheduleId = Guid.NewGuid(),
            EmployeeId = emp.Id,
            RequestType = ScheduleRequestType.LeaveRequest,
            Status = ScheduleApprovalStatus.Approved,
            Reason = "Đã duyệt"
        };
        _requestRepo.Requests.Add(request);

        var dto = new CreateScheduleApprovalRequestDto(
            RequestType: ScheduleRequestType.LeaveRequest,
            Reason: "Thử sửa lại sau khi đã duyệt",
            LeaveType: LeaveType.SickLeave
        );

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdateRequestAsync(request.Id, userId, isOwnerOrAdmin: false, dto));
    }

    [Fact]
    public async Task DeleteRequestAsync_PendingCheckInIncident_DeletesAndRevertsScheduleCheckIn()
    {
        // Arrange
        var service = CreateService();
        var userId = Guid.NewGuid().ToString();
        var emp = new Employee { Id = Guid.NewGuid(), UserId = userId, FullName = "NV A", TenantId = _tenantContext.CurrentTenantId };
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
            CheckInTime = DateTime.UtcNow.Date.AddHours(8).AddMinutes(10),
            CheckInMethod = "INCIDENT_PENDING",
            LateMinutes = 10,
            Status = ScheduleStatus.InProgress
        };
        _scheduleRepo.Schedules.Add(schedule);

        var request = new ScheduleApprovalRequest
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.CurrentTenantId,
            ScheduleId = schedule.Id,
            Schedule = schedule,
            EmployeeId = emp.Id,
            RequestType = ScheduleRequestType.CheckInIncident,
            Status = ScheduleApprovalStatus.Pending,
            Reason = "Lỗi FaceID"
        };
        _requestRepo.Requests.Add(request);

        // Act
        var deleted = await service.DeleteRequestAsync(request.Id, userId, isOwnerOrAdmin: false);

        // Assert
        Assert.True(deleted);
        Assert.True(request.IsDeleted);
        Assert.Null(schedule.CheckInTime);
        Assert.Null(schedule.CheckInMethod);
        Assert.Equal(0, schedule.LateMinutes);
        Assert.Equal(ScheduleStatus.Published, schedule.Status);
    }

    [Fact]
    public async Task DeleteRequestAsync_NonPendingRequest_ThrowsInvalidOperationException()
    {
        // Arrange
        var service = CreateService();
        var userId = Guid.NewGuid().ToString();
        var emp = new Employee { Id = Guid.NewGuid(), UserId = userId, FullName = "NV A", TenantId = _tenantContext.CurrentTenantId };
        _employees.Add(emp);

        var request = new ScheduleApprovalRequest
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.CurrentTenantId,
            ScheduleId = Guid.NewGuid(),
            EmployeeId = emp.Id,
            RequestType = ScheduleRequestType.ShiftSwap,
            Status = ScheduleApprovalStatus.Rejected,
            Reason = "Đã bị từ chối"
        };
        _requestRepo.Requests.Add(request);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DeleteRequestAsync(request.Id, userId, isOwnerOrAdmin: false));
    }

    [Fact]
    public async Task CreateRequestAsync_OvertimeWhenLeaveRequestPending_ThrowsInvalidOperationException()
    {
        // Arrange
        var service = CreateService();
        var userId = Guid.NewGuid().ToString();
        var emp = new Employee { Id = Guid.NewGuid(), UserId = userId, FullName = "NV A", TenantId = _tenantContext.CurrentTenantId };
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

        _requestRepo.Requests.Add(new ScheduleApprovalRequest
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.CurrentTenantId,
            ScheduleId = schedule.Id,
            EmployeeId = emp.Id,
            RequestType = ScheduleRequestType.LeaveRequest,
            LeaveType = LeaveType.AnnualLeave,
            Status = ScheduleApprovalStatus.Pending,
            Reason = "Xin nghỉ phép"
        });

        var overtimeDto = new CreateScheduleApprovalRequestDto(
            RequestType: ScheduleRequestType.Overtime,
            Reason: "Đăng ký làm thêm 60 phút",
            RequestedOvertimeMinutes: 60
        );

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateRequestAsync(schedule.Id, userId, overtimeDto));
    }

    [Fact]
    public async Task CreateRequestAsync_LeaveRequestWhenOvertimeExists_ThrowsInvalidOperationException()
    {
        // Arrange
        var service = CreateService();
        var userId = Guid.NewGuid().ToString();
        var emp = new Employee { Id = Guid.NewGuid(), UserId = userId, FullName = "NV A", TenantId = _tenantContext.CurrentTenantId };
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

        _requestRepo.Requests.Add(new ScheduleApprovalRequest
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.CurrentTenantId,
            ScheduleId = schedule.Id,
            EmployeeId = emp.Id,
            RequestType = ScheduleRequestType.Overtime,
            RequestedOvertimeMinutes = 60,
            Status = ScheduleApprovalStatus.Pending,
            Reason = "Đăng ký OT"
        });

        var leaveDto = new CreateScheduleApprovalRequestDto(
            RequestType: ScheduleRequestType.LeaveRequest,
            Reason: "Xin nghỉ phép năm",
            LeaveType: LeaveType.AnnualLeave
        );

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateRequestAsync(schedule.Id, userId, leaveDto));
    }

    [Fact]
    public async Task CreateRequestAsync_LeaveRequestWhenShiftSwapApproved_CreatesPendingRequest()
    {
        // Arrange
        var service = CreateService();
        var userId = Guid.NewGuid().ToString();
        var emp = new Employee { Id = Guid.NewGuid(), UserId = userId, FullName = "NV A", TenantId = _tenantContext.CurrentTenantId };
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

        _requestRepo.Requests.Add(new ScheduleApprovalRequest
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.CurrentTenantId,
            ScheduleId = schedule.Id,
            EmployeeId = emp.Id,
            RequestType = ScheduleRequestType.ShiftSwap,
            Status = ScheduleApprovalStatus.Approved,
            Reason = "Đổi ca đã được duyệt"
        });

        var leaveDto = new CreateScheduleApprovalRequestDto(
            RequestType: ScheduleRequestType.LeaveRequest,
            Reason: "Có việc đột xuất nên xin nghỉ ca sau khi đã đổi ca",
            LeaveType: LeaveType.AnnualLeave
        );

        // Act
        var created = await service.CreateRequestAsync(schedule.Id, userId, leaveDto);

        // Assert
        Assert.Equal(ScheduleRequestType.LeaveRequest, created.RequestType);
        Assert.Equal(ScheduleApprovalStatus.Pending, created.Status);
    }

    [Fact]
    public async Task CreateRequestAsync_OvertimeExceeding12HoursDailyLimit_ThrowsInvalidOperationException()
    {
        // Arrange
        var service = CreateService();
        var userId = Guid.NewGuid().ToString();
        var emp = new Employee { Id = Guid.NewGuid(), UserId = userId, FullName = "NV A", TenantId = _tenantContext.CurrentTenantId };
        _employees.Add(emp);

        var schedule = new EmployeeSchedule
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.CurrentTenantId,
            EmployeeId = emp.Id,
            BranchId = _tenantContext.CurrentBranchId ?? Guid.NewGuid(),
            WorkDate = DateTime.UtcNow.Date,
            PlannedStartTime = new TimeSpan(8, 0, 0),
            PlannedEndTime = new TimeSpan(16, 0, 0),
            DurationHours = 8.0m,
            Status = ScheduleStatus.Completed
        };
        _scheduleRepo.Schedules.Add(schedule);

        // 8h ca chính + 300 phút (5h) OT = 13h > 12h
        var overtimeDto = new CreateScheduleApprovalRequestDto(
            RequestType: ScheduleRequestType.Overtime,
            Reason: "Đăng ký làm thêm 5 tiếng",
            RequestedOvertimeMinutes: 300
        );

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateRequestAsync(schedule.Id, userId, overtimeDto));
        Assert.Contains("12 giờ/ngày", ex.Message);
    }

    [Fact]
    public async Task ReviewRequestAsync_OvertimeApproved_CalculatesFromEarlyCheckInAndLateCheckOut()
    {
        // Arrange
        var service = CreateService();
        var emp = new Employee { Id = Guid.NewGuid(), FullName = "NV A", TenantId = _tenantContext.CurrentTenantId };
        _employees.Add(emp);

        var workDate = new DateTime(2026, 11, 10);
        var schedule = new EmployeeSchedule
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.CurrentTenantId,
            EmployeeId = emp.Id,
            BranchId = _tenantContext.CurrentBranchId ?? Guid.NewGuid(),
            WorkDate = workDate,
            PlannedStartTime = new TimeSpan(8, 0, 0),
            PlannedEndTime = new TimeSpan(16, 0, 0),
            DurationHours = 8.0m,
            // Đi sớm 30p (07:30) + về muộn 90p (17:30) => 120 phút OT thực tế ngoài khung ca
            CheckInTime = workDate.AddHours(7).AddMinutes(30),
            CheckOutTime = workDate.AddHours(17).AddMinutes(30),
            Status = ScheduleStatus.Completed
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
            RequestedOvertimeMinutes = 180,
            Status = ScheduleApprovalStatus.Pending,
            Reason = "Xin duyệt OT theo giờ chấm công thực tế"
        };
        _requestRepo.Requests.Add(request);

        var reviewDto = new ReviewScheduleApprovalRequestDto(
            IsApproved: true,
            ReviewNote: "Duyệt OT theo giờ đi sớm về muộn"
        );

        // Act
        var result = await service.ReviewRequestAsync(request.Id, Guid.NewGuid().ToString(), reviewDto);

        // Assert
        Assert.True(result);
        Assert.Equal(120, request.ApprovedOvertimeMinutes);
        Assert.Equal(120, schedule.OvertimeMinutes);
    }
}
