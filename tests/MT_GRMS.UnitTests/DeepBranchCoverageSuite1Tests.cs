using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using MT_GRMS.Application.Cashbook;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Common.Interfaces;
using MT_GRMS.Application.Branches;
using MT_GRMS.Application.HR.AttendancePolicies;
using MT_GRMS.Application.HR.Employees;
using MT_GRMS.Application.HR.Payslips;
using MT_GRMS.Application.HR.Payslips.Dtos;
using MT_GRMS.Application.HR.Schedules;
using MT_GRMS.Application.HR.Schedules.Dtos;
using MT_GRMS.Application.HR.ShiftConfigs;
using MT_GRMS.Application.PosShifts;
using MT_GRMS.Application.PosShifts.Dtos;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Entities.HR;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Domain.Exceptions;
using Xunit;

namespace MT_GRMS.UnitTests;

public class DeepBranchCoverageSuite1Tests
{
    private class FakeTenantContext : ITenantContext
    {
        public string CurrentTenantId { get; set; } = "tenant_suite1";
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
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);
    }

    // =========================================================================
    // SECTION 1: ScheduleService All Days of Week & Edge Branches
    // =========================================================================

    [Fact]
    public async Task ScheduleService_CreateRecurringSchedule_AllWeekdayAliases_CoversAllSwitchBranches()
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
            FullName = "Nguyen Van AllDays",
            EmployeeCode = "NV_ALL",
            TenantId = tenant.CurrentTenantId,
            StartDate = new DateTime(2026, 1, 1),
            BranchId = branchId,
            Status = EmployeeStatus.Active
        };

        empRepo.Setup(r => r.GetByIdAsync(empId, It.IsAny<CancellationToken>())).ReturnsAsync(emp);
        schedRepo.Setup(r => r.GetActiveSchedulesForEmployeesAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EmployeeSchedule>());
        schedRepo.Setup(r => r.AddRangeAsync(It.IsAny<IEnumerable<EmployeeSchedule>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var allAliases = new List<string>
        {
            "T2", "THỨ 2", "THU 2", "MONDAY", "MON", "2",
            "T3", "THỨ 3", "THU 3", "TUESDAY", "TUE", "3",
            "T4", "THỨ 4", "THU 4", "WEDNESDAY", "WED", "4",
            "T5", "THỨ 5", "THU 5", "THURSDAY", "THU", "5",
            "T6", "THỨ 6", "THU 6", "FRIDAY", "FRI", "6",
            "T7", "THỨ 7", "THU 7", "SATURDAY", "SAT", "7",
            "CN", "CHỦ NHẬT", "CHU NHAT", "SUNDAY", "SUN", "8", "0", "CNHAT",
            "Friday", "Saturday", "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday",
            "INVALID_DAY_STRING", "", "   "
        };

        var startDate = new DateTime(2026, 11, 23); // Monday
        var endDate = new DateTime(2026, 11, 29); // Sunday

        var req = new CreateRecurringScheduleRequest(
            empId,
            branchId,
            startDate,
            HasEndDate: true,
            EndDate: endDate,
            RepeatDays: allAliases,
            AdditionalEmployeeIds: new List<Guid> { empId }, // tests filtering duplicate empId
            ShiftConfigId: null,
            ShiftCode: "CA_ALL",
            ShiftName: "Ca Toan Dien",
            PlannedStartTime: new TimeSpan(8, 0, 0),
            PlannedEndTime: new TimeSpan(12, 0, 0),
            DurationHours: 4.0m,
            IsCheckInNextDay: false
        );

        var result = await service.CreateRecurringAsync(req);
        Assert.NotNull(result);
        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task ScheduleService_CreateRecurringSchedule_TargetEndDateBeforeStartDate_ThrowsBusinessException()
    {
        var schedRepo = new Mock<IScheduleRepository>();
        var empRepo = new Mock<IEmployeeRepository>();
        var shiftCfgRepo = new Mock<IShiftConfigRepository>();
        var uow = new FakeUnitOfWork();
        var tenant = new FakeTenantContext();
        var service = new ScheduleService(schedRepo.Object, empRepo.Object, shiftCfgRepo.Object, uow, tenant);

        var req = new CreateRecurringScheduleRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new DateTime(2026, 11, 25),
            HasEndDate: true,
            EndDate: new DateTime(2026, 11, 20), // End date < start date
            RepeatDays: new List<string> { "T2" },
            AdditionalEmployeeIds: null,
            ShiftConfigId: null,
            ShiftCode: "CA_ERR",
            ShiftName: "Ca Loi",
            PlannedStartTime: new TimeSpan(8, 0, 0),
            PlannedEndTime: new TimeSpan(12, 0, 0),
            DurationHours: 4.0m,
            IsCheckInNextDay: false
        );

        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.CreateRecurringAsync(req));
        Assert.Equal("INVALID_DATE_RANGE", ex.ErrorCode);
    }

    [Fact]
    public async Task ScheduleService_CreateRecurringSchedule_EmptyRepeatDays_DefaultsToStartDateDayOfWeek()
    {
        var schedRepo = new Mock<IScheduleRepository>();
        var empRepo = new Mock<IEmployeeRepository>();
        var shiftCfgRepo = new Mock<IShiftConfigRepository>();
        var uow = new FakeUnitOfWork();
        var tenant = new FakeTenantContext();
        var service = new ScheduleService(schedRepo.Object, empRepo.Object, shiftCfgRepo.Object, uow, tenant);

        var empId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var emp = new Employee
        {
            Id = empId,
            FullName = "Nguyen Van DefaultDay",
            EmployeeCode = "NV_DEF",
            TenantId = tenant.CurrentTenantId,
            StartDate = new DateTime(2026, 1, 1),
            BranchId = branchId,
            Status = EmployeeStatus.Active
        };
        empRepo.Setup(r => r.GetByIdAsync(empId, It.IsAny<CancellationToken>())).ReturnsAsync(emp);
        schedRepo.Setup(r => r.GetActiveSchedulesForEmployeesAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EmployeeSchedule>());
        schedRepo.Setup(r => r.AddRangeAsync(It.IsAny<IEnumerable<EmployeeSchedule>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var req = new CreateRecurringScheduleRequest(
            empId,
            branchId,
            new DateTime(2026, 11, 25), // Wednesday
            HasEndDate: true,
            EndDate: new DateTime(2026, 11, 25),
            RepeatDays: null, // null repeat days -> default to Wednesday
            AdditionalEmployeeIds: null,
            ShiftConfigId: null,
            ShiftCode: "CA_DEF",
            ShiftName: "Ca Mac Dinh",
            PlannedStartTime: new TimeSpan(8, 0, 0),
            PlannedEndTime: new TimeSpan(12, 0, 0),
            DurationHours: 4.0m,
            IsCheckInNextDay: false
        );

        var result = await service.CreateRecurringAsync(req);
        Assert.NotNull(result);
        Assert.Single(result);
    }

    [Fact]
    public async Task ScheduleService_CreateRecurringSchedule_ScheduleItemsEmpty_ReturnsEmptyArray()
    {
        var schedRepo = new Mock<IScheduleRepository>();
        var empRepo = new Mock<IEmployeeRepository>();
        var shiftCfgRepo = new Mock<IShiftConfigRepository>();
        var uow = new FakeUnitOfWork();
        var tenant = new FakeTenantContext();
        var service = new ScheduleService(schedRepo.Object, empRepo.Object, shiftCfgRepo.Object, uow, tenant);

        // Start date is Wednesday (2026-11-25), repeat day is only Monday (T2), end date is same day (Wednesday)
        var req = new CreateRecurringScheduleRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new DateTime(2026, 11, 25),
            HasEndDate: true,
            EndDate: new DateTime(2026, 11, 25),
            RepeatDays: new List<string> { "T2" }, // Monday only -> 0 matches in range [Wed..Wed]
            AdditionalEmployeeIds: null,
            ShiftConfigId: null,
            ShiftCode: "CA_EMPTY",
            ShiftName: "Ca Trong",
            PlannedStartTime: new TimeSpan(8, 0, 0),
            PlannedEndTime: new TimeSpan(12, 0, 0),
            DurationHours: 4.0m,
            IsCheckInNextDay: false
        );

        var result = await service.CreateRecurringAsync(req);
        Assert.Empty(result);
    }

    // =========================================================================
    // SECTION 2: ScheduleApprovalService Edge Workflow Branches
    // =========================================================================

    [Fact]
    public async Task ScheduleApprovalService_GetRequestById_UnauthorizedEmployee_ThrowsUnauthorizedAccessException()
    {
        var approvalRepo = new Mock<IScheduleApprovalRequestRepository>();
        var schedRepo = new Mock<IScheduleRepository>();
        var empRepo = new Mock<IEmployeeRepository>();
        var tenant = new FakeTenantContext { CurrentRole = "EMPLOYEE", CurrentUserId = Guid.NewGuid() };
        var uow = new FakeUnitOfWork();

        var service = new ScheduleApprovalService(approvalRepo.Object, schedRepo.Object, empRepo.Object, uow, tenant);

        var schedId = Guid.NewGuid();
        var ownerEmpId = Guid.NewGuid();
        var callerEmpId = Guid.NewGuid();

        var schedule = new EmployeeSchedule
        {
            Id = schedId,
            EmployeeId = ownerEmpId, // Owned by another employee
            WorkDate = DateTime.Today,
            TenantId = tenant.CurrentTenantId
        };
        schedRepo.Setup(r => r.GetByIdAsync(schedId, It.IsAny<CancellationToken>())).ReturnsAsync(schedule);

        var callerEmployee = new Employee { Id = callerEmpId, UserId = tenant.CurrentUserId.ToString() };
        empRepo.Setup(r => r.GetByUserIdAsync(tenant.CurrentUserId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(callerEmployee);

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.GetRequestsByScheduleIdAsync(schedId, tenant.CurrentUserId.ToString(), false));
        Assert.Contains("Bạn không có quyền", ex.Message);
    }

    [Fact]
    public async Task ScheduleApprovalService_RespondToSwapRequest_ThrowsInvalidOperationException()
    {
        var approvalRepo = new Mock<IScheduleApprovalRequestRepository>();
        var schedRepo = new Mock<IScheduleRepository>();
        var empRepo = new Mock<IEmployeeRepository>();
        var tenant = new FakeTenantContext();
        var uow = new FakeUnitOfWork();

        var service = new ScheduleApprovalService(approvalRepo.Object, schedRepo.Object, empRepo.Object, uow, tenant);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.PeerConfirmShiftSwapAsync(Guid.NewGuid(), tenant.CurrentUserId.ToString(), new PeerConfirmShiftSwapDto(true, "Ok")));
        Assert.Contains("trực tiếp", ex.Message);
    }

    [Fact]
    public async Task ScheduleApprovalService_ApproveCheckOutIncident_WithNoCheckInTime_SetsManualCheckIn()
    {
        var approvalRepo = new Mock<IScheduleApprovalRequestRepository>();
        var schedRepo = new Mock<IScheduleRepository>();
        var empRepo = new Mock<IEmployeeRepository>();
        var tenant = new FakeTenantContext { CurrentRole = "STORE_OWNER" };
        var uow = new FakeUnitOfWork();

        var service = new ScheduleApprovalService(approvalRepo.Object, schedRepo.Object, empRepo.Object, uow, tenant);

        var requestId = Guid.NewGuid();
        var schedule = new EmployeeSchedule
        {
            Id = Guid.NewGuid(),
            WorkDate = new DateTime(2026, 11, 20),
            PlannedStartTime = new TimeSpan(8, 0, 0),
            PlannedEndTime = new TimeSpan(17, 0, 0),
            CheckInTime = null, // No check-in time yet
            CheckOutTime = null,
            Status = ScheduleStatus.Published,
            TenantId = tenant.CurrentTenantId
        };

        var req = new ScheduleApprovalRequest
        {
            Id = requestId,
            ScheduleId = schedule.Id,
            Schedule = schedule,
            RequestType = ScheduleRequestType.CheckOutIncident,
            RequestedCheckOutTime = new DateTime(2026, 11, 20, 17, 0, 0),
            Status = ScheduleApprovalStatus.Pending,
            TenantId = tenant.CurrentTenantId
        };

        approvalRepo.Setup(r => r.GetByIdWithDetailsAsync(requestId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(req);

        var dto = new ReviewScheduleApprovalRequestDto(
            IsApproved: true,
            ReviewNote: "Approved",
            ApprovedCheckOutTime: new DateTime(2026, 11, 20, 17, 15, 0)
        );

        var result = await service.ReviewRequestAsync(requestId, tenant.CurrentUserId.ToString(), dto);
        Assert.True(result);
        Assert.Equal("MANUAL", schedule.CheckInMethod);
        Assert.Equal(schedule.WorkDate.Date + schedule.PlannedStartTime, schedule.CheckInTime);
    }

    [Fact]
    public async Task ScheduleApprovalService_DeleteCheckOutIncident_ClearsCheckOutTimeAndMethod()
    {
        var approvalRepo = new Mock<IScheduleApprovalRequestRepository>();
        var schedRepo = new Mock<IScheduleRepository>();
        var empRepo = new Mock<IEmployeeRepository>();
        var tenant = new FakeTenantContext { CurrentRole = "STORE_OWNER" };
        var uow = new FakeUnitOfWork();

        var service = new ScheduleApprovalService(approvalRepo.Object, schedRepo.Object, empRepo.Object, uow, tenant);

        var requestId = Guid.NewGuid();
        var schedule = new EmployeeSchedule
        {
            Id = Guid.NewGuid(),
            WorkDate = new DateTime(2026, 11, 20),
            PlannedStartTime = new TimeSpan(8, 0, 0),
            PlannedEndTime = new TimeSpan(17, 0, 0),
            CheckInTime = new DateTime(2026, 11, 20, 8, 0, 0),
            CheckOutTime = new DateTime(2026, 11, 20, 17, 0, 0),
            CheckOutMethod = "INCIDENT_PENDING",
            Status = ScheduleStatus.Published,
            TenantId = tenant.CurrentTenantId
        };

        var req = new ScheduleApprovalRequest
        {
            Id = requestId,
            ScheduleId = schedule.Id,
            Schedule = schedule,
            RequestType = ScheduleRequestType.CheckOutIncident,
            Status = ScheduleApprovalStatus.Pending,
            TenantId = tenant.CurrentTenantId
        };

        approvalRepo.Setup(r => r.GetByIdWithDetailsAsync(requestId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(req);
        schedRepo.Setup(r => r.GetByIdAsync(schedule.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(schedule);

        var result = await service.DeleteRequestAsync(requestId, tenant.CurrentUserId.ToString(), true);
        Assert.True(result);
        Assert.Null(schedule.CheckOutTime);
        Assert.Null(schedule.CheckOutMethod);
    }

    // =========================================================================
    // SECTION 3: ShiftService Exceptions, Allocation & State Branches
    // =========================================================================

    [Fact]
    public async Task ShiftService_MismatchCashAllocation_ThrowsBusinessException()
    {
        var shiftRepo = new Mock<IShiftRepository>();
        var uow = new FakeUnitOfWork();
        var tenant = new FakeTenantContext();

        var service = new ShiftService(shiftRepo.Object, uow, tenant, null);

        var shiftId = Guid.NewGuid();
        var shift = new ShiftZReport
        {
            Id = shiftId,
            TenantId = tenant.CurrentTenantId,
            Status = ShiftState.Closing,
            CashierUserId = tenant.CurrentUserId
        };
        shiftRepo.Setup(r => r.GetByIdAsync(shiftId, It.IsAny<CancellationToken>())).ReturnsAsync(shift);
        shiftRepo.Setup(r => r.GetParticipantAsync(shiftId, tenant.CurrentUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ShiftParticipant { ShiftId = shiftId, UserId = tenant.CurrentUserId });

        var req = new CloseShiftRequest(
            ActualCashCounted: 100000m,
            RetainedFloatAmount: 50000m,
            DroppedToSafeAmount: 150000m // 50k + 150k = 200k != 100k
        );

        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.CloseShiftAsync(shiftId, req));
        Assert.Equal("INVALID_CASH_ALLOCATION", ex.ErrorCode);
    }

    [Fact]
    public async Task ShiftService_NegativeActualCash_ThrowsBusinessException()
    {
        var shiftRepo = new Mock<IShiftRepository>();
        var uow = new FakeUnitOfWork();
        var tenant = new FakeTenantContext();

        var service = new ShiftService(shiftRepo.Object, uow, tenant, null);

        var shiftId = Guid.NewGuid();
        var shift = new ShiftZReport
        {
            Id = shiftId,
            TenantId = tenant.CurrentTenantId,
            Status = ShiftState.Closing,
            CashierUserId = tenant.CurrentUserId
        };
        shiftRepo.Setup(r => r.GetByIdAsync(shiftId, It.IsAny<CancellationToken>())).ReturnsAsync(shift);
        shiftRepo.Setup(r => r.GetParticipantAsync(shiftId, tenant.CurrentUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ShiftParticipant { ShiftId = shiftId, UserId = tenant.CurrentUserId });

        var req = new CloseShiftRequest(
            ActualCashCounted: -100m, // Negative actual cash
            RetainedFloatAmount: 0m,
            DroppedToSafeAmount: 0m
        );

        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.CloseShiftAsync(shiftId, req));
        Assert.Equal("INVALID_ACTUAL_CASH", ex.ErrorCode);
    }

    [Fact]
    public async Task ShiftService_UpdateCountedCash_ShiftNotInClosingState_ThrowsBusinessException()
    {
        var shiftRepo = new Mock<IShiftRepository>();
        var uow = new FakeUnitOfWork();
        var tenant = new FakeTenantContext();
        var service = new ShiftService(shiftRepo.Object, uow, tenant, null);

        var shiftId = Guid.NewGuid();
        var shift = new ShiftZReport
        {
            Id = shiftId,
            TenantId = tenant.CurrentTenantId,
            Status = ShiftState.Open // Not Closing
        };
        shiftRepo.Setup(r => r.GetByIdAsync(shiftId, It.IsAny<CancellationToken>())).ReturnsAsync(shift);

        var req = new UpdateCountedCashRequest(200000m, "Counted");
        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.UpdateCountedCashAsync(shiftId, tenant.CurrentUserId, req));
        Assert.Equal("INVALID_SHIFT_STATE", ex.ErrorCode);
    }

    [Fact]
    public async Task ShiftService_RejectCashMovement_MovementNotFoundOrNotPending_ThrowsBusinessException()
    {
        var shiftRepo = new Mock<IShiftRepository>();
        var uow = new FakeUnitOfWork();
        var tenant = new FakeTenantContext();
        var service = new ShiftService(shiftRepo.Object, uow, tenant, null);

        // 1. Movement not found
        shiftRepo.Setup(r => r.GetCashMovementByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ShiftCashMovement?)null);

        var ex1 = await Assert.ThrowsAsync<BusinessException>(() =>
            service.RejectCashMovementAsync(Guid.NewGuid(), "Approver", "Rejected"));
        Assert.Equal("MOVEMENT_NOT_FOUND", ex1.ErrorCode);

        // 2. Movement not pending approval (already approved)
        var m = new ShiftCashMovement
        {
            Id = Guid.NewGuid(),
            Status = CashMovementStatus.Recorded
        };
        shiftRepo.Setup(r => r.GetCashMovementByIdAsync(m.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(m);

        var ex2 = await Assert.ThrowsAsync<BusinessException>(() =>
            service.RejectCashMovementAsync(m.Id, "Approver", "Rejected"));
        Assert.Equal("INVALID_STATUS", ex2.ErrorCode);
    }

    // =========================================================================
    // SECTION 4: PayslipService Calculation & Debt Adjustment Branches
    // =========================================================================

    [Fact]
    public async Task PayslipService_DeletePayslip_PaidStatus_ThrowsBusinessException()
    {
        var payslipRepo = new Mock<IPayslipRepository>();
        var empRepo = new Mock<IEmployeeRepository>();
        var schedRepo = new Mock<IScheduleRepository>();
        var policyRepo = new Mock<IBranchRepository>();
        var tenant = new FakeTenantContext();
        var uow = new FakeUnitOfWork();

        var service = new PayslipService(payslipRepo.Object, empRepo.Object, schedRepo.Object, policyRepo.Object, tenant, uow, null);

        var payslipId = Guid.NewGuid();
        var paidPayslip = new Payslip
        {
            Id = payslipId,
            TenantId = tenant.CurrentTenantId,
            Status = PayslipStatus.Paid
        };

        payslipRepo.Setup(r => r.GetByIdAsync(payslipId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(paidPayslip);

        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.DeletePayslipAsync(payslipId));
        Assert.Contains("Không được phép xóa", ex.Message);
    }

    [Fact]
    public async Task PayslipService_PayPayslip_AlreadyPaid_ThrowsBusinessException()
    {
        var payslipRepo = new Mock<IPayslipRepository>();
        var empRepo = new Mock<IEmployeeRepository>();
        var schedRepo = new Mock<IScheduleRepository>();
        var policyRepo = new Mock<IBranchRepository>();
        var tenant = new FakeTenantContext();
        var uow = new FakeUnitOfWork();

        var service = new PayslipService(payslipRepo.Object, empRepo.Object, schedRepo.Object, policyRepo.Object, tenant, uow, null);

        var payslipId = Guid.NewGuid();
        var paidPayslip = new Payslip
        {
            Id = payslipId,
            TenantId = tenant.CurrentTenantId,
            PayslipCode = "PL-2611-0001",
            Status = PayslipStatus.Paid
        };

        payslipRepo.Setup(r => r.GetByIdAsync(payslipId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(paidPayslip);

        var req = new PayPayslipRequest("CASH");
        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.PayPayslipAsync(payslipId, req));
        Assert.Contains("đã được thanh toán", ex.Message);
    }

    [Fact]
    public async Task PayslipService_GenerateMonthlyPayroll_NoSchedules_DeletesExistingPayslipAndSkips()
    {
        var payslipRepo = new Mock<IPayslipRepository>();
        var empRepo = new Mock<IEmployeeRepository>();
        var schedRepo = new Mock<IScheduleRepository>();
        var policyRepo = new Mock<IBranchRepository>();
        var tenant = new FakeTenantContext();
        var uow = new FakeUnitOfWork();

        var service = new PayslipService(payslipRepo.Object, empRepo.Object, schedRepo.Object, policyRepo.Object, tenant, uow, null);

        var empId1 = Guid.NewGuid();
        var empId2 = Guid.NewGuid();

        var emp1 = new Employee
        {
            Id = empId1,
            TenantId = tenant.CurrentTenantId,
            FullName = "Nguyen Thi NoSchedule",
            EmployeeCode = "NV_NOSCHED",
            SalaryType = SalaryType.Monthly,
            BaseRate = 8000000m
        };
        var emp2 = new Employee
        {
            Id = empId2,
            TenantId = tenant.CurrentTenantId,
            FullName = "Tran Van HasSchedule",
            EmployeeCode = "NV_HASSCHED",
            SalaryType = SalaryType.Monthly,
            BaseRate = 9000000m
        };

        empRepo.Setup(r => r.GetActiveEmployeesAsync(It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Employee> { emp1, emp2 });

        // Emp2 has active schedule in month, so employeeIdsWithSchedules is not empty!
        schedRepo.Setup(r => r.GetActiveSchedulesForEmployeesAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EmployeeSchedule>
            {
                new EmployeeSchedule { EmployeeId = empId2, TenantId = tenant.CurrentTenantId, WorkDate = new DateTime(2026, 11, 5) }
            });

        // Emp1 has no completed schedule
        schedRepo.Setup(r => r.GetCompletedSchedulesInPeriodAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EmployeeSchedule>());

        // Existing draft payslip exists for emp1
        var existingDraft = new Payslip
        {
            Id = Guid.NewGuid(),
            EmployeeId = empId1,
            TenantId = tenant.CurrentTenantId,
            Status = PayslipStatus.Unpaid,
            PeriodMonth = "2026-11"
        };
        payslipRepo.Setup(r => r.GetByPeriodAndBranchAsync("2026-11", It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Payslip> { existingDraft });

        var req = new GeneratePayrollBatchRequest("2026-11", null);
        var result = await service.GenerateMonthlyPayrollAsync(req);
        Assert.NotNull(result);
        payslipRepo.Verify(r => r.Delete(existingDraft), Times.Once);
    }
}
