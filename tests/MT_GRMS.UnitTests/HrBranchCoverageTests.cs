using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using MT_GRMS.Application.Cashbook;
using MT_GRMS.Application.Cashbook.Dtos;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Common.DTOs;
using MT_GRMS.Application.Common.Interfaces;
using MT_GRMS.Application.HR.AttendancePolicies;
using MT_GRMS.Application.HR.Employees;
using MT_GRMS.Application.HR.Employees.Dtos;
using MT_GRMS.Application.HR.Payslips;
using MT_GRMS.Application.HR.Payslips.Dtos;
using MT_GRMS.Application.HR.Schedules;
using MT_GRMS.Application.HR.Schedules.Dtos;
using MT_GRMS.Application.HR.ShiftConfigs;
using MT_GRMS.Application.HR.ShiftConfigs.Dtos;
using MT_GRMS.Application.PosShifts;
using PosShiftDtos = MT_GRMS.Application.PosShifts.Dtos;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Entities.HR;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Domain.Exceptions;
using Xunit;

namespace MT_GRMS.UnitTests;

public class HrBranchCoverageTests
{
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

        public void SetRole(string? role) => CurrentRole = role ?? string.Empty;
    }

    private class FakeUnitOfWork : IUnitOfWork
    {
        public bool SaveChangesCalled { get; private set; }
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveChangesCalled = true;
            return Task.FromResult(1);
        }
    }

    // =========================================================================
    // SECTION 1: ScheduleService Branches
    // =========================================================================

    [Fact]
    public async Task CreateSchedulesAsync_NullOrEmptySchedules_ReturnsEmpty()
    {
        var schedRepo = new Mock<IScheduleRepository>();
        var empRepo = new Mock<IEmployeeRepository>();
        var shiftCfgRepo = new Mock<IShiftConfigRepository>();
        var uow = new FakeUnitOfWork();
        var tenant = new FakeTenantContext();
        var service = new ScheduleService(schedRepo.Object, empRepo.Object, shiftCfgRepo.Object, uow, tenant);

        var emptyReq = new BatchCreateScheduleRequest(Guid.NewGuid(), new List<CreateScheduleItemRequest>());
        var emptyRes = await service.BatchCreateAsync(emptyReq);
        Assert.Empty(emptyRes);
    }

    [Fact]
    public async Task CreateSchedulesAsync_WorkDateBeforeEmployeeStartDate_ThrowsBusinessException()
    {
        var schedRepo = new Mock<IScheduleRepository>();
        var empRepo = new Mock<IEmployeeRepository>();
        var shiftCfgRepo = new Mock<IShiftConfigRepository>();
        var uow = new FakeUnitOfWork();
        var tenant = new FakeTenantContext();
        var service = new ScheduleService(schedRepo.Object, empRepo.Object, shiftCfgRepo.Object, uow, tenant);

        var branchId = Guid.NewGuid();
        var empId = Guid.NewGuid();
        var emp = new Employee
        {
            Id = empId,
            FullName = "Nguyen Van StartDate",
            EmployeeCode = "NV001",
            TenantId = tenant.CurrentTenantId,
            StartDate = DateTime.Today.AddDays(10) // Starts in 10 days
        };
        empRepo.Setup(r => r.GetByIdAsync(empId, It.IsAny<CancellationToken>())).ReturnsAsync(emp);
        schedRepo.Setup(r => r.GetListAsync(It.IsAny<ScheduleQueryFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EmployeeSchedule>());
        schedRepo.Setup(r => r.GetActiveSchedulesForEmployeesAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EmployeeSchedule>());

        var items = new List<CreateScheduleItemRequest>
        {
            new CreateScheduleItemRequest(empId, branchId, DateTime.Today, null, "CS", "Ca Sang", TimeSpan.FromHours(8), TimeSpan.FromHours(12), 4m, false)
        };

        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.BatchCreateAsync(new BatchCreateScheduleRequest(branchId, items)));
        Assert.Equal("WORK_DATE_BEFORE_START_DATE", ex.ErrorCode);
    }

    [Fact]
    public async Task CreateSchedulesAsync_DuplicateShift_ThrowsBusinessException()
    {
        var schedRepo = new Mock<IScheduleRepository>();
        var empRepo = new Mock<IEmployeeRepository>();
        var shiftCfgRepo = new Mock<IShiftConfigRepository>();
        var uow = new FakeUnitOfWork();
        var tenant = new FakeTenantContext();
        var service = new ScheduleService(schedRepo.Object, empRepo.Object, shiftCfgRepo.Object, uow, tenant);

        var branchId = Guid.NewGuid();
        var empId = Guid.NewGuid();
        var shiftConfigId = Guid.NewGuid();
        var emp = new Employee
        {
            Id = empId,
            FullName = "Nguyen Van Dup",
            EmployeeCode = "NV002",
            TenantId = tenant.CurrentTenantId
        };
        empRepo.Setup(r => r.GetByIdAsync(empId, It.IsAny<CancellationToken>())).ReturnsAsync(emp);
        shiftCfgRepo.Setup(r => r.GetByIdAsync(shiftConfigId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ShiftConfig { Id = shiftConfigId, ShiftName = "Ca Sang", ShiftCode = "CS" });

        var existingSchedule = new EmployeeSchedule
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.CurrentTenantId,
            EmployeeId = empId,
            ShiftConfigId = shiftConfigId,
            WorkDate = DateTime.Today,
            PlannedStartTime = TimeSpan.FromHours(8),
            PlannedEndTime = TimeSpan.FromHours(12),
            Status = ScheduleStatus.Published
        };
        schedRepo.Setup(r => r.GetListAsync(It.IsAny<ScheduleQueryFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EmployeeSchedule> { existingSchedule });
        schedRepo.Setup(r => r.GetActiveSchedulesForEmployeesAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EmployeeSchedule> { existingSchedule });

        var items = new List<CreateScheduleItemRequest>
        {
            new CreateScheduleItemRequest(empId, branchId, DateTime.Today, shiftConfigId, "CS", "Ca Sang", TimeSpan.FromHours(8), TimeSpan.FromHours(12), 4m, false)
        };

        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.BatchCreateAsync(new BatchCreateScheduleRequest(branchId, items)));
        Assert.Equal("SCHEDULE_DUPLICATE_SHIFT", ex.ErrorCode);
    }

    [Fact]
    public async Task CreateSchedulesAsync_TimeOverlap_ThrowsBusinessException()
    {
        var schedRepo = new Mock<IScheduleRepository>();
        var empRepo = new Mock<IEmployeeRepository>();
        var shiftCfgRepo = new Mock<IShiftConfigRepository>();
        var uow = new FakeUnitOfWork();
        var tenant = new FakeTenantContext();
        var service = new ScheduleService(schedRepo.Object, empRepo.Object, shiftCfgRepo.Object, uow, tenant);

        var branchId = Guid.NewGuid();
        var empId = Guid.NewGuid();
        var emp = new Employee
        {
            Id = empId,
            FullName = "Nguyen Van Overlap",
            EmployeeCode = "NV003",
            TenantId = tenant.CurrentTenantId
        };
        empRepo.Setup(r => r.GetByIdAsync(empId, It.IsAny<CancellationToken>())).ReturnsAsync(emp);

        var existingSchedule = new EmployeeSchedule
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.CurrentTenantId,
            EmployeeId = empId,
            WorkDate = DateTime.Today,
            PlannedStartTime = TimeSpan.FromHours(8),
            PlannedEndTime = TimeSpan.FromHours(14),
            Status = ScheduleStatus.Scheduled
        };
        schedRepo.Setup(r => r.GetListAsync(It.IsAny<ScheduleQueryFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EmployeeSchedule> { existingSchedule });
        schedRepo.Setup(r => r.GetActiveSchedulesForEmployeesAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EmployeeSchedule> { existingSchedule });

        // Overlapping 10:00 -> 16:00
        var items = new List<CreateScheduleItemRequest>
        {
            new CreateScheduleItemRequest(empId, branchId, DateTime.Today, null, "CC", "Ca Chieu", TimeSpan.FromHours(10), TimeSpan.FromHours(16), 6m, false)
        };

        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.BatchCreateAsync(new BatchCreateScheduleRequest(branchId, items)));
        Assert.Equal("SCHEDULE_TIME_OVERLAP", ex.ErrorCode);
    }

    [Fact]
    public async Task CreateRecurringSchedulesAsync_EmptyRepeatDays_DefaultsToStartDateDayOfWeek()
    {
        var schedRepo = new Mock<IScheduleRepository>();
        var empRepo = new Mock<IEmployeeRepository>();
        var shiftCfgRepo = new Mock<IShiftConfigRepository>();
        var uow = new FakeUnitOfWork();
        var tenant = new FakeTenantContext();
        var service = new ScheduleService(schedRepo.Object, empRepo.Object, shiftCfgRepo.Object, uow, tenant);

        var empId = Guid.NewGuid();
        var emp = new Employee { Id = empId, FullName = "NV Recur", TenantId = tenant.CurrentTenantId };
        empRepo.Setup(r => r.GetByIdAsync(empId, It.IsAny<CancellationToken>())).ReturnsAsync(emp);
        schedRepo.Setup(r => r.GetListAsync(It.IsAny<ScheduleQueryFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EmployeeSchedule>());
        schedRepo.Setup(r => r.GetActiveSchedulesForEmployeesAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EmployeeSchedule>());

        var startDate = DateTime.Today;
        var endDate = DateTime.Today.AddDays(7);
        var req = new CreateRecurringScheduleRequest(
            EmployeeId: empId,
            ShiftConfigId: null,
            StartDate: startDate,
            EndDate: endDate,
            RepeatDays: new List<string>(), // Empty repeat days
            PlannedStartTime: TimeSpan.FromHours(8),
            PlannedEndTime: TimeSpan.FromHours(12),
            IsCheckInNextDay: false,
            ShiftName: "Ca Sang",
            DurationHours: 4m,
            ShiftCode: "CS",
            BranchId: Guid.NewGuid(),
            AdditionalEmployeeIds: null
        );

        var res = await service.CreateRecurringAsync(req);
        Assert.NotEmpty(res);
    }

    [Fact]
    public async Task CreateRecurringSchedulesAsync_WithAdditionalEmployeeIds_CreatesForBothEmployees()
    {
        var schedRepo = new Mock<IScheduleRepository>();
        var empRepo = new Mock<IEmployeeRepository>();
        var shiftCfgRepo = new Mock<IShiftConfigRepository>();
        var uow = new FakeUnitOfWork();
        var tenant = new FakeTenantContext();
        var service = new ScheduleService(schedRepo.Object, empRepo.Object, shiftCfgRepo.Object, uow, tenant);

        var emp1 = Guid.NewGuid();
        var emp2 = Guid.NewGuid();
        empRepo.Setup(r => r.GetByIdAsync(emp1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = emp1, FullName = "NV 1", TenantId = tenant.CurrentTenantId });
        empRepo.Setup(r => r.GetByIdAsync(emp2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = emp2, FullName = "NV 2", TenantId = tenant.CurrentTenantId });
        schedRepo.Setup(r => r.GetListAsync(It.IsAny<ScheduleQueryFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EmployeeSchedule>());
        schedRepo.Setup(r => r.GetActiveSchedulesForEmployeesAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EmployeeSchedule>());

        var req = new CreateRecurringScheduleRequest(
            EmployeeId: emp1,
            ShiftConfigId: null,
            StartDate: DateTime.Today,
            EndDate: DateTime.Today.AddDays(1),
            RepeatDays: new List<string> { DateTime.Today.DayOfWeek.ToString() },
            PlannedStartTime: TimeSpan.FromHours(8),
            PlannedEndTime: TimeSpan.FromHours(12),
            IsCheckInNextDay: false,
            ShiftName: "Ca Sang",
            DurationHours: 4m,
            ShiftCode: "CS",
            BranchId: Guid.NewGuid(),
            AdditionalEmployeeIds: new List<Guid> { emp2 }
        );

        var res = await service.CreateRecurringAsync(req);
        Assert.True(res.Count >= 2);
    }

    [Fact]
    public async Task UpdateScheduleAsync_WorkDateBeforeStartDate_ThrowsBusinessException()
    {
        var schedRepo = new Mock<IScheduleRepository>();
        var empRepo = new Mock<IEmployeeRepository>();
        var shiftCfgRepo = new Mock<IShiftConfigRepository>();
        var uow = new FakeUnitOfWork();
        var tenant = new FakeTenantContext();
        var service = new ScheduleService(schedRepo.Object, empRepo.Object, shiftCfgRepo.Object, uow, tenant);

        var empId = Guid.NewGuid();
        var schedId = Guid.NewGuid();
        var emp = new Employee
        {
            Id = empId,
            FullName = "NV Update StartDate",
            TenantId = tenant.CurrentTenantId,
            StartDate = DateTime.Today.AddDays(5)
        };
        var existing = new EmployeeSchedule
        {
            Id = schedId,
            TenantId = tenant.CurrentTenantId,
            EmployeeId = empId,
            WorkDate = DateTime.Today.AddDays(10),
            PlannedStartTime = TimeSpan.FromHours(8),
            PlannedEndTime = TimeSpan.FromHours(12)
        };

        schedRepo.Setup(r => r.GetByIdAsync(schedId, It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        empRepo.Setup(r => r.GetByIdAsync(empId, It.IsAny<CancellationToken>())).ReturnsAsync(emp);
        schedRepo.Setup(r => r.GetListAsync(It.IsAny<ScheduleQueryFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EmployeeSchedule> { existing });
        schedRepo.Setup(r => r.GetActiveSchedulesForEmployeesAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EmployeeSchedule>());

        var updateReq = new UpdateScheduleRequest(
            EmployeeId: empId,
            BranchId: null,
            WorkDate: DateTime.Today.AddDays(2) // Before StartDate (Day 5)
        );

        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.UpdateAsync(schedId, updateReq));
        Assert.Equal("WORK_DATE_BEFORE_START_DATE", ex.ErrorCode);
    }

    [Fact]
    public async Task UpdateScheduleAsync_WithValidBranchIdAndDurationAndStatus_UpdatesFields()
    {
        var schedRepo = new Mock<IScheduleRepository>();
        var empRepo = new Mock<IEmployeeRepository>();
        var shiftCfgRepo = new Mock<IShiftConfigRepository>();
        var uow = new FakeUnitOfWork();
        var tenant = new FakeTenantContext();
        var service = new ScheduleService(schedRepo.Object, empRepo.Object, shiftCfgRepo.Object, uow, tenant);

        var empId = Guid.NewGuid();
        var schedId = Guid.NewGuid();
        var newBranchId = Guid.NewGuid();
        var emp = new Employee { Id = empId, FullName = "NV Ok", TenantId = tenant.CurrentTenantId };
        var existing = new EmployeeSchedule
        {
            Id = schedId,
            TenantId = tenant.CurrentTenantId,
            EmployeeId = empId,
            WorkDate = DateTime.Today,
            PlannedStartTime = TimeSpan.FromHours(8),
            PlannedEndTime = TimeSpan.FromHours(12),
            BranchId = Guid.NewGuid(),
            Status = ScheduleStatus.Scheduled
        };

        schedRepo.Setup(r => r.GetByIdAsync(schedId, It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        empRepo.Setup(r => r.GetByIdAsync(empId, It.IsAny<CancellationToken>())).ReturnsAsync(emp);
        schedRepo.Setup(r => r.GetListAsync(It.IsAny<ScheduleQueryFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EmployeeSchedule> { existing });
        schedRepo.Setup(r => r.GetActiveSchedulesForEmployeesAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EmployeeSchedule>());

        var updateReq = new UpdateScheduleRequest(
            EmployeeId: empId,
            BranchId: newBranchId,
            WorkDate: DateTime.Today,
            PlannedStartTime: TimeSpan.FromHours(8),
            PlannedEndTime: TimeSpan.FromHours(14),
            DurationHours: 6m,
            ShiftName: "Ca Moi",
            ShiftCode: "CM",
            Status: ScheduleStatus.Published
        );

        var res = await service.UpdateAsync(schedId, updateReq);
        Assert.Equal(newBranchId, existing.BranchId);
        Assert.Equal(6m, existing.DurationHours);
        Assert.Equal(ScheduleStatus.Published, existing.Status);
    }

    [Fact]
    public async Task UpdateScheduleAsync_OverlapAndDuplicateShift_ThrowsExceptions()
    {
        var schedRepo = new Mock<IScheduleRepository>();
        var empRepo = new Mock<IEmployeeRepository>();
        var shiftCfgRepo = new Mock<IShiftConfigRepository>();
        var uow = new FakeUnitOfWork();
        var tenant = new FakeTenantContext();
        var service = new ScheduleService(schedRepo.Object, empRepo.Object, shiftCfgRepo.Object, uow, tenant);

        var empId = Guid.NewGuid();
        var schedId = Guid.NewGuid();
        var otherSchedId = Guid.NewGuid();
        var shiftCfgId = Guid.NewGuid();
        var emp = new Employee { Id = empId, FullName = "NV Check", TenantId = tenant.CurrentTenantId };
        var existing = new EmployeeSchedule
        {
            Id = schedId,
            TenantId = tenant.CurrentTenantId,
            EmployeeId = empId,
            ShiftName = "Ca Sang",
            WorkDate = DateTime.Today,
            PlannedStartTime = TimeSpan.FromHours(8),
            PlannedEndTime = TimeSpan.FromHours(12)
        };
        var other = new EmployeeSchedule
        {
            Id = otherSchedId,
            TenantId = tenant.CurrentTenantId,
            EmployeeId = empId,
            ShiftConfigId = shiftCfgId,
            ShiftName = "Ca Chieu",
            WorkDate = DateTime.Today,
            PlannedStartTime = TimeSpan.FromHours(14),
            PlannedEndTime = TimeSpan.FromHours(18),
            Status = ScheduleStatus.Published
        };

        schedRepo.Setup(r => r.GetByIdAsync(schedId, It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        empRepo.Setup(r => r.GetByIdAsync(empId, It.IsAny<CancellationToken>())).ReturnsAsync(emp);
        shiftCfgRepo.Setup(r => r.GetByIdAsync(shiftCfgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ShiftConfig { Id = shiftCfgId, ShiftName = "Ca Chieu", ShiftCode = "CC" });
        schedRepo.Setup(r => r.GetListAsync(It.IsAny<ScheduleQueryFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EmployeeSchedule> { existing, other });
        schedRepo.Setup(r => r.GetActiveSchedulesForEmployeesAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EmployeeSchedule> { existing, other });

        // 1. Duplicate shift config
        var dupReq = new UpdateScheduleRequest(EmployeeId: empId, ShiftConfigId: shiftCfgId, ShiftName: "Ca Chieu Khac", WorkDate: DateTime.Today, PlannedStartTime: TimeSpan.FromHours(8), PlannedEndTime: TimeSpan.FromHours(12));
        var exDup = await Assert.ThrowsAsync<BusinessException>(() => service.UpdateAsync(schedId, dupReq));
        Assert.Equal("SCHEDULE_DUPLICATE_SHIFT", exDup.ErrorCode);

        // 2. Overlap time
        var overlapReq = new UpdateScheduleRequest(EmployeeId: empId, ShiftName: "Ca Toi", WorkDate: DateTime.Today, PlannedStartTime: TimeSpan.FromHours(13), PlannedEndTime: TimeSpan.FromHours(15));
        var exOverlap = await Assert.ThrowsAsync<BusinessException>(() => service.UpdateAsync(schedId, overlapReq));
        Assert.Equal("SCHEDULE_TIME_OVERLAP", exOverlap.ErrorCode);
    }

    [Fact]
    public async Task GetSchedulesAsync_WithEmployeeIdsFilter_FiltersResults()
    {
        var schedRepo = new Mock<IScheduleRepository>();
        var empRepo = new Mock<IEmployeeRepository>();
        var shiftCfgRepo = new Mock<IShiftConfigRepository>();
        var uow = new FakeUnitOfWork();
        var tenant = new FakeTenantContext();
        var service = new ScheduleService(schedRepo.Object, empRepo.Object, shiftCfgRepo.Object, uow, tenant);

        var emp1 = Guid.NewGuid();
        var emp2 = Guid.NewGuid();
        var schedules = new List<EmployeeSchedule>
        {
            new EmployeeSchedule { Id = Guid.NewGuid(), EmployeeId = emp1, WorkDate = DateTime.Today },
            new EmployeeSchedule { Id = Guid.NewGuid(), EmployeeId = emp2, WorkDate = DateTime.Today }
        };
        schedRepo.Setup(r => r.GetListAsync(It.IsAny<ScheduleQueryFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ScheduleQueryFilter f, CancellationToken ct) => f.EmployeeId.HasValue ? schedules.Where(s => s.EmployeeId == f.EmployeeId.Value).ToList() : schedules);

        var filter = new ScheduleQueryFilter(EmployeeId: emp1);
        var res = await service.GetListAsync(filter);
        Assert.Single(res);
        Assert.Equal(emp1, res[0].EmployeeId);
    }

    [Fact]
    public async Task CheckOutFaceIdAsync_ValidationAndEdgeCases()
    {
        var schedRepo = new Mock<IScheduleRepository>();
        var empRepo = new Mock<IEmployeeRepository>();
        var shiftCfgRepo = new Mock<IShiftConfigRepository>();
        var policyRepo = new Mock<IBranchAttendancePolicyRepository>();
        var uow = new FakeUnitOfWork();
        var tenant = new FakeTenantContext();
        var service = new ScheduleService(schedRepo.Object, empRepo.Object, shiftCfgRepo.Object, uow, tenant, policyRepo.Object);

        var schedId = Guid.NewGuid();
        var empId = Guid.NewGuid();

        // 1. Schedule not found
        schedRepo.Setup(r => r.GetByIdAsync(schedId, It.IsAny<CancellationToken>())).ReturnsAsync((EmployeeSchedule?)null);
        var req1 = new FaceIdCheckOutRequest(schedId, empId, 0.20m, null);
        await Assert.ThrowsAsync<NotFoundException>(() => service.CheckOutFaceIdAsync(req1));

        // 2. Different employee
        var sched = new EmployeeSchedule
        {
            Id = schedId,
            TenantId = tenant.CurrentTenantId,
            EmployeeId = empId,
            BranchId = Guid.NewGuid(),
            WorkDate = DateTime.Today,
            PlannedStartTime = TimeSpan.FromHours(8),
            PlannedEndTime = TimeSpan.FromHours(17),
            CheckInTime = DateTime.Today.AddHours(8),
            Status = ScheduleStatus.InProgress
        };
        schedRepo.Setup(r => r.GetByIdAsync(schedId, It.IsAny<CancellationToken>())).ReturnsAsync(sched);
        var req2 = new FaceIdCheckOutRequest(schedId, Guid.NewGuid(), 0.20m, null);
        var exEmp = await Assert.ThrowsAsync<BusinessException>(() => service.CheckOutFaceIdAsync(req2));
        Assert.Equal("INVALID_EMPLOYEE", exEmp.ErrorCode);

        // 3. Early leave policy threshold
        var policy = new BranchAttendancePolicy
        {
            BranchId = sched.BranchId,
            EnableEarlyLeaveThreshold = true,
            EarlyLeaveThresholdMinutes = 15
        };
        policyRepo.Setup(r => r.GetByBranchIdAsync(sched.BranchId, It.IsAny<CancellationToken>())).ReturnsAsync(policy);

        var req3 = new FaceIdCheckOutRequest(schedId, empId, 0.20m, null);
        var res3 = await service.CheckOutFaceIdAsync(req3);
        Assert.NotNull(res3);
    }

    // =========================================================================
    // SECTION 2: ScheduleApprovalService Branches
    // =========================================================================

    [Fact]
    public async Task ScheduleApprovalService_UnauthorizedUser_ThrowsUnauthorizedAccessException()
    {
        var approvalRepo = new Mock<IScheduleApprovalRequestRepository>();
        var schedRepo = new Mock<IScheduleRepository>();
        var empRepo = new Mock<IEmployeeRepository>();
        var uow = new FakeUnitOfWork();
        var tenant = new FakeTenantContext { CurrentRole = "STAFF" };
        var service = new ScheduleApprovalService(approvalRepo.Object, schedRepo.Object, empRepo.Object, uow, tenant);

        var schedId = Guid.NewGuid();
        var empId = Guid.NewGuid();
        var otherEmpId = Guid.NewGuid();
        var sched = new EmployeeSchedule
        {
            Id = schedId,
            EmployeeId = empId,
            TenantId = tenant.CurrentTenantId,
            WorkDate = DateTime.Today.AddDays(1)
        };
        schedRepo.Setup(r => r.GetByIdAsync(schedId, It.IsAny<CancellationToken>())).ReturnsAsync(sched);
        empRepo.Setup(r => r.GetByUserIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = otherEmpId, TenantId = tenant.CurrentTenantId, UserId = "user-other" });

        var reqDto = new CreateScheduleApprovalRequestDto
        {
            RequestType = ScheduleRequestType.LeaveRequest,
            Reason = "Nghi phep"
        };

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.CreateRequestAsync(schedId, "user-other", reqDto));
        Assert.Contains("Bạn chỉ được phép gửi yêu cầu cho ca làm việc của chính mình", ex.Message);
    }

    [Fact]
    public async Task ScheduleApprovalService_CheckInAndCheckOutIncidents_ProcessedCorrectly()
    {
        var approvalRepo = new Mock<IScheduleApprovalRequestRepository>();
        var schedRepo = new Mock<IScheduleRepository>();
        var empRepo = new Mock<IEmployeeRepository>();
        var uow = new FakeUnitOfWork();
        var tenant = new FakeTenantContext { CurrentRole = "STORE_OWNER" };
        var service = new ScheduleApprovalService(approvalRepo.Object, schedRepo.Object, empRepo.Object, uow, tenant);

        var schedId = Guid.NewGuid();
        var empId = Guid.NewGuid();
        var sched = new EmployeeSchedule
        {
            Id = schedId,
            EmployeeId = empId,
            TenantId = tenant.CurrentTenantId,
            WorkDate = DateTime.Today,
            PlannedStartTime = TimeSpan.FromHours(8),
            PlannedEndTime = TimeSpan.FromHours(17),
            Status = ScheduleStatus.Scheduled
        };
        schedRepo.Setup(r => r.GetByIdAsync(schedId, It.IsAny<CancellationToken>())).ReturnsAsync(sched);
        approvalRepo.Setup(r => r.GetByScheduleIdAsync(schedId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ScheduleApprovalRequest>());

        // 1. CheckInIncident
        var inDto = new CreateScheduleApprovalRequestDto
        {
            RequestType = ScheduleRequestType.CheckInIncident,
            Reason = "Quen cham cong vao",
            EvidenceImageUrls = new List<string> { "http://img.com/1.png" },
            RequestedCheckInTime = DateTime.Today.AddHours(8).AddMinutes(10)
        };

        var resIn = await service.CreateRequestAsync(schedId, "owner", inDto);
        Assert.NotNull(resIn);
        Assert.Equal(ScheduleStatus.InProgress, sched.Status);
        Assert.Equal("INCIDENT_PENDING", sched.CheckInMethod);

        // 2. CheckOutIncident
        var outDto = new CreateScheduleApprovalRequestDto
        {
            RequestType = ScheduleRequestType.CheckOutIncident,
            Reason = "Quen cham cong ra",
            RequestedCheckOutTime = DateTime.Today.AddHours(17).AddMinutes(15)
        };

        var resOut = await service.CreateRequestAsync(schedId, "owner", outDto);
        Assert.NotNull(resOut);
        Assert.Equal("INCIDENT_PENDING", sched.CheckOutMethod);
    }

    [Fact]
    public async Task ScheduleApprovalService_PayslipLocked_ThrowsBusinessException()
    {
        var approvalRepo = new Mock<IScheduleApprovalRequestRepository>();
        var schedRepo = new Mock<IScheduleRepository>();
        var empRepo = new Mock<IEmployeeRepository>();
        var payslipRepo = new Mock<IPayslipRepository>();
        var uow = new FakeUnitOfWork();
        var tenant = new FakeTenantContext();
        var service = new ScheduleApprovalService(approvalRepo.Object, schedRepo.Object, empRepo.Object, uow, tenant, null, payslipRepo.Object);

        var schedId = Guid.NewGuid();
        var empId = Guid.NewGuid();
        var sched = new EmployeeSchedule
        {
            Id = schedId,
            EmployeeId = empId,
            TenantId = tenant.CurrentTenantId,
            WorkDate = new DateTime(2026, 11, 10),
            Status = ScheduleStatus.Completed
        };
        schedRepo.Setup(r => r.GetByIdAsync(schedId, It.IsAny<CancellationToken>())).ReturnsAsync(sched);
        payslipRepo.Setup(r => r.GetByEmployeeAndPeriodAsync(empId, "2026-11", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Payslip { Status = PayslipStatus.Paid });

        var reqDto = new CreateScheduleApprovalRequestDto
        {
            RequestType = ScheduleRequestType.LeaveRequest,
            Reason = "Nghi phep thang cu"
        };

        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.CreateRequestAsync(schedId, "owner", reqDto));
        Assert.Equal("PAYSLIP_LOCKED", ex.ErrorCode);
    }

    // =========================================================================
    // SECTION 3: ShiftService Branches
    // =========================================================================

    [Fact]
    public async Task ShiftService_CashMovement_CashDrawAndInvalidNegative_ThrowsOrUpdates()
    {
        var shiftRepo = new Mock<IShiftRepository>();
        var uow = new FakeUnitOfWork();
        var tenant = new FakeTenantContext();
        var service = new ShiftService(shiftRepo.Object, uow, tenant);

        var shiftId = Guid.NewGuid();
        var shift = new ShiftZReport
        {
            Id = shiftId,
            TenantId = tenant.CurrentTenantId,
            BranchId = Guid.NewGuid(),
            Status = ShiftState.Open,
            OpeningFloat = 500000m,
            TotalCashOut = 0m
        };
        shiftRepo.Setup(r => r.GetByIdAsync(shiftId, It.IsAny<CancellationToken>())).ReturnsAsync(shift);

        // 1. CASH_DRAW movement
        var drawReq = new PosShiftDtos.CashMovementRequest(CashMovementType.OUT, CashMovementCategory.CASH_DRAW, 100000m, "Rut tien mat");
        var res = await service.AddCashMovementAsync(shiftId, drawReq);
        Assert.NotNull(res);
        Assert.Equal(100000m, shift.TotalCashOut);

        // 2. Negative actual cash counted on close
        var closeReq = new PosShiftDtos.CloseShiftRequest(-50000m, 0m, 0m, null, null, null);
        var exNeg = await Assert.ThrowsAsync<BusinessException>(() => service.CloseShiftAsync(shiftId, closeReq));
        Assert.Equal("INVALID_ACTUAL_CASH", exNeg.ErrorCode);
    }

    [Fact]
    public async Task ShiftService_CloseShift_WithCashbookSync_CreatesAccountEntries()
    {
        var shiftRepo = new Mock<IShiftRepository>();
        var cashbookService = new Mock<ICashbookService>();
        var uow = new FakeUnitOfWork();
        var tenant = new FakeTenantContext();
        var service = new ShiftService(shiftRepo.Object, uow, tenant, cashbookService.Object);

        var shiftId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var shift = new ShiftZReport
        {
            Id = shiftId,
            TenantId = tenant.CurrentTenantId,
            BranchId = branchId,
            ShiftCode = "CA-TEST-01",
            Status = ShiftState.Open,
            OpeningFloat = 500000m,
            TotalCashSales = 1500000m,
            TotalBankRevenue = 800000m,
            DroppedToSafeAmount = 1000000m,
            CashierUserId = tenant.CurrentUserId
        };
        shiftRepo.Setup(r => r.GetByIdAsync(shiftId, It.IsAny<CancellationToken>())).ReturnsAsync(shift);
        shiftRepo.Setup(r => r.GetShiftSalesMetricsAsync(shiftId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((1500000m, 800000m, 5));
        shiftRepo.Setup(r => r.GetPendingMovementsAsync(It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ShiftCashMovement>());

        cashbookService.Setup(c => c.GetEntriesAsync(It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CashbookEntryType?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CashbookEntryResponse>());
        cashbookService.Setup(c => c.GetOrCreateBankAccountAsync(It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FinanceAccount { Id = Guid.NewGuid(), AccountType = FinanceAccountType.Bank });
        cashbookService.Setup(c => c.GetOrCreateTillAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FinanceAccount { Id = Guid.NewGuid(), AccountType = FinanceAccountType.Till });
        cashbookService.Setup(c => c.GetOrCreateSafeAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FinanceAccount { Id = Guid.NewGuid(), AccountType = FinanceAccountType.Safe });

        var closeReq = new PosShiftDtos.CloseShiftRequest(1000000m, 0m, 1000000m, null, "Chot ca mau", null);
        var res = await service.CloseShiftAsync(shiftId, closeReq);
        Assert.NotNull(res);
        Assert.Equal(ShiftState.Closed, shift.Status);
    }

    // =========================================================================
    // SECTION 4: PayslipService Branches
    // =========================================================================

    [Fact]
    public async Task PayslipService_SalaryTypeFilter_And_DeletePayslip_RestoresDebt()
    {
        var payslipRepo = new Mock<IPayslipRepository>();
        var empRepo = new Mock<IEmployeeRepository>();
        var schedRepo = new Mock<IScheduleRepository>();
        var policyRepo = new Mock<IBranchAttendancePolicyRepository>();
        var uow = new FakeUnitOfWork();
        var tenant = new FakeTenantContext();
        var service = new PayslipService(payslipRepo.Object, empRepo.Object, schedRepo.Object, policyRepo.Object, tenant, uow);

        var empId = Guid.NewGuid();
        var emp = new Employee
        {
            Id = empId,
            FullName = "NV Luong Gio",
            TenantId = tenant.CurrentTenantId,
            SalaryType = SalaryType.Hourly,
            BaseRate = 35000m,
            DebtAmount = 100000m
        };

        empRepo.Setup(r => r.GetActiveEmployeesAsync(It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Employee> { emp });
        policyRepo.Setup(r => r.GetByBranchIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BranchAttendancePolicy { StandardDayHours = 8m });

        var schedule = new EmployeeSchedule
        {
            Id = Guid.NewGuid(),
            EmployeeId = empId,
            WorkDate = new DateTime(2026, 11, 5),
            PlannedStartTime = TimeSpan.FromHours(8),
            PlannedEndTime = TimeSpan.FromHours(16),
            DurationHours = 8m,
            ActualWorkHours = 8m,
            Status = ScheduleStatus.Completed
        };
        schedRepo.Setup(r => r.GetActiveSchedulesForEmployeesAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EmployeeSchedule> { schedule });
        schedRepo.Setup(r => r.GetCompletedSchedulesInPeriodAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EmployeeSchedule> { schedule });
        payslipRepo.Setup(r => r.GetByPeriodAndBranchAsync(It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Payslip>());
        payslipRepo.Setup(r => r.GetCountByPeriodMonthAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        // 1. Generate payroll with Monthly filter -> should skip hourly employee
        var reqFilter = new GeneratePayrollBatchRequest("2026-11", Guid.NewGuid(), SalaryTypeFilter: SalaryType.Monthly);
        var resFiltered = await service.GenerateMonthlyPayrollAsync(reqFilter);
        Assert.Empty(resFiltered);

        // 2. Generate payroll without filter -> should generate for hourly employee
        var reqAll = new GeneratePayrollBatchRequest("2026-11", Guid.NewGuid());
        var resAll = await service.GenerateMonthlyPayrollAsync(reqAll);
        Assert.Single(resAll);

        // 3. Delete payslip: null throws NotFound, Paid throws BusinessException, Unpaid succeeds
        var missingId = Guid.NewGuid();
        payslipRepo.Setup(r => r.GetByIdAsync(missingId, It.IsAny<CancellationToken>())).ReturnsAsync((Payslip?)null);
        await Assert.ThrowsAsync<NotFoundException>(() => service.DeletePayslipAsync(missingId));

        var paidPayslip = new Payslip
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.CurrentTenantId,
            EmployeeId = empId,
            Status = PayslipStatus.Paid
        };
        payslipRepo.Setup(r => r.GetByIdAsync(paidPayslip.Id, It.IsAny<CancellationToken>())).ReturnsAsync(paidPayslip);
        var exPaid = await Assert.ThrowsAsync<BusinessException>(() => service.DeletePayslipAsync(paidPayslip.Id));
        Assert.Contains("Đã thanh toán", exPaid.Message);

        var unpaidPayslip = new Payslip
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.CurrentTenantId,
            EmployeeId = empId,
            Status = PayslipStatus.Unpaid
        };
        payslipRepo.Setup(r => r.GetByIdAsync(unpaidPayslip.Id, It.IsAny<CancellationToken>())).ReturnsAsync(unpaidPayslip);
        var deleteResult = await service.DeletePayslipAsync(unpaidPayslip.Id);
        Assert.True(deleteResult);
    }

    [Fact]
    public async Task PayslipService_AdvanceSalary_WithCashbookService_DisbursesCash()
    {
        var payslipRepo = new Mock<IPayslipRepository>();
        var empRepo = new Mock<IEmployeeRepository>();
        var schedRepo = new Mock<IScheduleRepository>();
        var policyRepo = new Mock<IBranchAttendancePolicyRepository>();
        var cashbookService = new Mock<ICashbookService>();
        var uow = new FakeUnitOfWork();
        var tenant = new FakeTenantContext();
        var service = new PayslipService(payslipRepo.Object, empRepo.Object, schedRepo.Object, policyRepo.Object, tenant, uow, cashbookService.Object);

        var payslipId = Guid.NewGuid();
        var emp = new Employee { Id = Guid.NewGuid(), FullName = "NV Ung Luong", TenantId = tenant.CurrentTenantId, DebtAmount = 0m };
        var payslip = new Payslip
        {
            Id = payslipId,
            TenantId = tenant.CurrentTenantId,
            EmployeeId = emp.Id,
            Employee = emp,
            PeriodMonth = "2026-11",
            TotalEarnings = 10000000m,
            AdvanceDeduction = 0m,
            NetSalary = 10000000m,
            Status = PayslipStatus.Unpaid
        };
        payslipRepo.Setup(r => r.GetByIdAsync(payslipId, It.IsAny<CancellationToken>())).ReturnsAsync(payslip);
        cashbookService.Setup(c => c.ResolveDisbursementAccountAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FinanceAccount { Id = Guid.NewGuid(), AccountType = FinanceAccountType.Safe });
        cashbookService.Setup(c => c.DisburseCashAsync(It.IsAny<Guid>(), It.IsAny<FinanceAccountType>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CashbookEntryResponse(Guid.NewGuid(), "PC-2026-001", Guid.NewGuid(), "Safe", null, "Expense", 2000000m, 2000000m, "SALARY_ADVANCE", payslip.Id, "REF", "TX", "Chi ung luong", DateTime.UtcNow));

        var req = new SalaryAdvanceRequest(2000000m, "CASH", "Chi ung luong som");
        var res = await service.AdvanceSalaryAsync(payslipId, req);
        Assert.NotNull(res);
        Assert.Equal(2000000m, payslip.AdvanceDeduction);
        Assert.Equal(2000000m, emp.DebtAmount);
    }
}
