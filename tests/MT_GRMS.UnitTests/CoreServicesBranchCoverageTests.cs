using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Xunit;
using MT_GRMS.Application.Cashbook;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Common.Interfaces;
using MT_GRMS.Application.Common.DTOs;
using MT_GRMS.Application.Branches;
using MT_GRMS.Application.HR.AttendancePolicies;
using MT_GRMS.Application.HR.Employees;
using MT_GRMS.Application.HR.Payslips;
using MT_GRMS.Application.HR.Schedules;
using MT_GRMS.Application.HR.Schedules.Dtos;
using MT_GRMS.Application.HR.ShiftConfigs;
using MT_GRMS.Application.PosOrders;
using MT_GRMS.Application.PosOrders.Dtos;
using MT_GRMS.Application.PosShifts;
using MT_GRMS.Application.PosShifts.Dtos;
using MT_GRMS.Application.Promotions;
using MT_GRMS.Application.PurchaseOrders;
using MT_GRMS.Application.PurchaseOrders.Dispatch;
using MT_GRMS.Application.PurchaseOrders.Dtos;
using MT_GRMS.Application.GoodsReceipts;
using MT_GRMS.Application.GoodsReceipts.Dtos;
using MT_GRMS.Application.SupplierReturns;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Entities.HR;
using MT_GRMS.Domain.Entities.Inventory;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Domain.Entities.Sales;
using MT_GRMS.Domain.Entities.Supplier;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Domain.Exceptions;
using MT_GRMS.Infrastructure.Services.Printer;

namespace MT_GRMS.UnitTests;

public class CoreServicesBranchCoverageTests
{
    private class FakeTenantContext : ITenantContext
    {
        public string CurrentTenantId { get; set; } = "tenant-core-cov";
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

    [Fact]
    public async Task ScheduleService_BatchCreate_Validation_Overlap_Duplicate_Branches()
    {
        var schedRepo = new Mock<IScheduleRepository>();
        var empRepo = new Mock<IEmployeeRepository>();
        var shiftCfgRepo = new Mock<IShiftConfigRepository>();
        var uow = new Mock<IUnitOfWork>();
        var tenant = new FakeTenantContext();

        var service = new ScheduleService(schedRepo.Object, empRepo.Object, shiftCfgRepo.Object, uow.Object, tenant);

        var branchId = Guid.NewGuid();
        var empId = Guid.NewGuid();
        var emp = new Employee
        {
            Id = empId,
            FullName = "Nguyen Van A",
            EmployeeCode = "EMP01",
            StartDate = new DateTime(2026, 1, 1),
            Status = EmployeeStatus.Active
        };
        empRepo.Setup(r => r.GetByIdAsync(empId, It.IsAny<CancellationToken>())).ReturnsAsync(emp);
        schedRepo.Setup(r => r.GetActiveSchedulesForEmployeesAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EmployeeSchedule>());

        // 1. request.HasEndDate = true and EndDate < minDate -> INVALID_DATE_RANGE
        var reqInvalidEndDate = new BatchCreateScheduleRequest(
            branchId,
            new List<CreateScheduleItemRequest>
            {
                new(empId, branchId, new DateTime(2026, 11, 20), null, null, "Ca Sáng", new TimeSpan(8, 0, 0), new TimeSpan(12, 0, 0), 4m, false)
            },
            HasEndDate: true,
            EndDate: new DateTime(2026, 11, 19)
        );

        var ex1 = await Assert.ThrowsAsync<BusinessException>(() => service.BatchCreateAsync(reqInvalidEndDate));
        Assert.Equal("INVALID_DATE_RANGE", ex1.ErrorCode);

        // 2. Duplicate shift in same batch -> SCHEDULE_DUPLICATE_SHIFT
        var reqDuplicate = new BatchCreateScheduleRequest(
            branchId,
            new List<CreateScheduleItemRequest>
            {
                new(empId, branchId, new DateTime(2026, 11, 20), null, null, "Ca Sáng", new TimeSpan(8, 0, 0), new TimeSpan(12, 0, 0), 4m, false),
                new(empId, branchId, new DateTime(2026, 11, 20), null, null, "Ca Sáng", new TimeSpan(8, 0, 0), new TimeSpan(12, 0, 0), 4m, false)
            }
        );

        var exDup = await Assert.ThrowsAsync<BusinessException>(() => service.BatchCreateAsync(reqDuplicate));
        Assert.Equal("SCHEDULE_DUPLICATE_SHIFT", exDup.ErrorCode);

        // 3. Overlapping hours in same batch -> SCHEDULE_TIME_OVERLAP
        var reqOverlap = new BatchCreateScheduleRequest(
            branchId,
            new List<CreateScheduleItemRequest>
            {
                new(empId, branchId, new DateTime(2026, 11, 20), null, null, "Ca Sáng", new TimeSpan(8, 0, 0), new TimeSpan(12, 0, 0), 4m, false),
                new(empId, branchId, new DateTime(2026, 11, 20), null, null, "Ca Chiều", new TimeSpan(10, 0, 0), new TimeSpan(14, 0, 0), 4m, false)
            }
        );

        var exOverlap = await Assert.ThrowsAsync<BusinessException>(() => service.BatchCreateAsync(reqOverlap));
        Assert.Equal("SCHEDULE_TIME_OVERLAP", exOverlap.ErrorCode);

        // 4. CreateRecurringAsync with empty matching days -> returns empty
        var reqRecurringEmpty = new CreateRecurringScheduleRequest(
            empId,
            branchId,
            new DateTime(2026, 11, 21), // Saturday
            true,
            new DateTime(2026, 11, 22), // Sunday
            new List<string> { "Monday" }
        );

        var recurringResult = await service.CreateRecurringAsync(reqRecurringEmpty);
        Assert.Empty(recurringResult);

        // 5. PublishAsync with EmployeeIds filter
        var s1 = new EmployeeSchedule { Id = Guid.NewGuid(), BranchId = branchId, EmployeeId = empId, WorkDate = new DateTime(2026, 11, 20), Status = ScheduleStatus.Draft };
        var s2 = new EmployeeSchedule { Id = Guid.NewGuid(), BranchId = branchId, EmployeeId = Guid.NewGuid(), WorkDate = new DateTime(2026, 11, 20), Status = ScheduleStatus.Draft };
        schedRepo.Setup(r => r.GetListAsync(It.IsAny<ScheduleQueryFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EmployeeSchedule> { s1, s2 });

        var pubReq = new PublishSchedulesRequest(
            branchId,
            new DateTime(2026, 11, 20),
            new DateTime(2026, 11, 21),
            new List<Guid> { empId }
        );

        var pubRes = await service.PublishAsync(pubReq);
        Assert.Equal(1, pubRes.PublishedCount);
        Assert.Equal(ScheduleStatus.Published, s1.Status);
        Assert.Equal(ScheduleStatus.Draft, s2.Status);
    }

    [Fact]
    public async Task ScheduleService_ManualAttendance_And_SelfCheckInPolicy_Branches()
    {
        var schedRepo = new Mock<IScheduleRepository>();
        var empRepo = new Mock<IEmployeeRepository>();
        var shiftCfgRepo = new Mock<IShiftConfigRepository>();
        var uow = new Mock<IUnitOfWork>();
        var tenant = new FakeTenantContext();
        var policyRepo = new Mock<IBranchRepository>();

        var service = new ScheduleService(schedRepo.Object, empRepo.Object, shiftCfgRepo.Object, uow.Object, tenant, policyRepo.Object);

        var scheduleId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var empId = Guid.NewGuid();
        var workDate = new DateTime(2026, 11, 20);

        var schedule = new EmployeeSchedule
        {
            Id = scheduleId,
            BranchId = branchId,
            EmployeeId = empId,
            WorkDate = workDate,
            PlannedStartTime = new TimeSpan(8, 0, 0),
            PlannedEndTime = new TimeSpan(17, 0, 0),
            IsCheckInNextDay = false,
            Status = ScheduleStatus.Published,
            ApprovalType = "OVERTIME",
            OvertimeMinutes = 0
        };

        schedRepo.Setup(r => r.GetByIdAsync(scheduleId, It.IsAny<CancellationToken>())).ReturnsAsync(schedule);

        // 1. RecordManualAttendance: outTime < inTime with multi-day -> INVALID_TIME_RANGE
        var manualReqInvalid = new ManualAttendanceRequest(
            ManualAttendanceType.Working,
            new TimeSpan(2, 0, 0, 0),
            new TimeSpan(1, 0, 0),
            "Ghi chu"
        );

        var exTime = await Assert.ThrowsAsync<BusinessException>(() => service.RecordManualAttendanceAsync(scheduleId, manualReqInvalid));
        Assert.Equal("INVALID_TIME_RANGE", exTime.ErrorCode);

        // 2. RecordManualAttendance: late checkin (08:30) and early checkout (16:30), with overtime outside hours
        var manualReqValid = new ManualAttendanceRequest(
            ManualAttendanceType.Working,
            new TimeSpan(8, 30, 0),
            new TimeSpan(16, 30, 0),
            "Ghi chu"
        );

        var resManual = await service.RecordManualAttendanceAsync(scheduleId, manualReqValid);
        Assert.NotNull(resManual);
        Assert.Equal(30, schedule.LateMinutes);
        Assert.Equal(30, schedule.EarlyLeaveMinutes);
        Assert.Equal(ScheduleStatus.Completed, schedule.Status);

        // 3. Overnight shift in RecordManualAttendance: PlannedEndTime < PlannedStartTime
        var overnightSchedId = Guid.NewGuid();
        var overnightSched = new EmployeeSchedule
        {
            Id = overnightSchedId,
            BranchId = branchId,
            EmployeeId = empId,
            WorkDate = workDate,
            PlannedStartTime = new TimeSpan(22, 0, 0),
            PlannedEndTime = new TimeSpan(6, 0, 0),
            IsCheckInNextDay = true,
            Status = ScheduleStatus.Published
        };
        schedRepo.Setup(r => r.GetByIdAsync(overnightSchedId, It.IsAny<CancellationToken>())).ReturnsAsync(overnightSched);

        var manualReqOvernight = new ManualAttendanceRequest(
            ManualAttendanceType.Working,
            new TimeSpan(22, 0, 0),
            new TimeSpan(6, 0, 0),
            "Overnight note"
        );

        var resOvernight = await service.RecordManualAttendanceAsync(overnightSchedId, manualReqOvernight);
        Assert.NotNull(resOvernight);

        // 4. SelfCheckInAsync with policyRepository late threshold
        var checkInSchedId = Guid.NewGuid();
        var checkInEmp = new Employee
        {
            Id = empId,
            FullName = "Nguyen Van B",
            EmployeeCode = "EMP02",
            Status = EmployeeStatus.Active,
            BranchId = branchId,
            UserId = tenant.CurrentUserId.ToString()
        };
        empRepo.Setup(r => r.GetByIdAsync(empId, It.IsAny<CancellationToken>())).ReturnsAsync(checkInEmp);

        var vnNow = DateTime.UtcNow.AddHours(7);
        var startTime = vnNow.TimeOfDay > TimeSpan.FromMinutes(10)
            ? vnNow.TimeOfDay.Subtract(TimeSpan.FromMinutes(10))
            : TimeSpan.FromMinutes(1);
        var endTime = vnNow.TimeOfDay < TimeSpan.FromHours(22)
            ? vnNow.TimeOfDay.Add(TimeSpan.FromHours(1))
            : TimeSpan.FromHours(23).Add(TimeSpan.FromMinutes(50));

        var checkInSched = new EmployeeSchedule
        {
            Id = checkInSchedId,
            BranchId = branchId,
            EmployeeId = empId,
            WorkDate = vnNow.Date,
            PlannedStartTime = startTime,
            PlannedEndTime = endTime,
            Status = ScheduleStatus.Published,
            Employee = checkInEmp
        };
        schedRepo.Setup(r => r.GetByIdAsync(checkInSchedId, It.IsAny<CancellationToken>())).ReturnsAsync(checkInSched);

        var policy = new Branch
        {
            Id = branchId,
            LateThresholdMinutes = 0,
            EnableLateThreshold = true
        };
        policyRepo.Setup(p => p.GetByIdAsync(branchId, It.IsAny<CancellationToken>())).ReturnsAsync(policy);

        var checkInRes = await service.SelfCheckInAsync(checkInSchedId, new SelfCheckInRequest("Self check-in"));
        Assert.NotNull(checkInRes);
        Assert.True(checkInSched.LateMinutes > 0);
    }

    [Fact]
    public async Task ShiftService_Handover_CashMovement_CountedCash_Branches()
    {
        var shiftRepo = new Mock<IShiftRepository>();
        var uow = new Mock<IUnitOfWork>();
        var tenant = new FakeTenantContext();
        tenant.CurrentRole = "STORE_OWNER";

        var service = new ShiftService(shiftRepo.Object, uow.Object, tenant);

        var shiftId = Guid.NewGuid();

        // 1. ConfirmHandoverAsync: SHIFT_NOT_FOUND
        shiftRepo.Setup(r => r.GetByIdAsync(shiftId, It.IsAny<CancellationToken>())).ReturnsAsync((ShiftZReport?)null);
        var exNotFound = await Assert.ThrowsAsync<BusinessException>(() => service.ConfirmHandoverAsync(shiftId, new ConfirmHandoverRequest(HandoverStatus.CONFIRMED, null), Guid.NewGuid().ToString()));
        Assert.Equal("SHIFT_NOT_FOUND", exNotFound.ErrorCode);

        // 2. ConfirmHandoverAsync: SHIFT_NOT_CLOSED
        var openShift = new ShiftZReport { Id = shiftId, Status = ShiftState.Open };
        shiftRepo.Setup(r => r.GetByIdAsync(shiftId, It.IsAny<CancellationToken>())).ReturnsAsync(openShift);
        var exNotClosed = await Assert.ThrowsAsync<BusinessException>(() => service.ConfirmHandoverAsync(shiftId, new ConfirmHandoverRequest(HandoverStatus.CONFIRMED, null), Guid.NewGuid().ToString()));
        Assert.Equal("SHIFT_NOT_CLOSED", exNotClosed.ErrorCode);

        // 3. ConfirmHandoverAsync: HANDOVER_ALREADY_CONFIRMED
        var confirmedShift = new ShiftZReport { Id = shiftId, Status = ShiftState.Closed, HandoverStatus = HandoverStatus.CONFIRMED };
        shiftRepo.Setup(r => r.GetByIdAsync(shiftId, It.IsAny<CancellationToken>())).ReturnsAsync(confirmedShift);
        var exAlready = await Assert.ThrowsAsync<BusinessException>(() => service.ConfirmHandoverAsync(shiftId, new ConfirmHandoverRequest(HandoverStatus.CONFIRMED, null), Guid.NewGuid().ToString()));
        Assert.Equal("HANDOVER_ALREADY_CONFIRMED", exAlready.ErrorCode);

        // 4. ConfirmHandoverAsync: INVALID_HANDOVER_STATUS
        var closedShift = new ShiftZReport { Id = shiftId, Status = ShiftState.Closed, HandoverStatus = HandoverStatus.PENDING };
        shiftRepo.Setup(r => r.GetByIdAsync(shiftId, It.IsAny<CancellationToken>())).ReturnsAsync(closedShift);
        var exInvalidStatus = await Assert.ThrowsAsync<BusinessException>(() => service.ConfirmHandoverAsync(shiftId, new ConfirmHandoverRequest((HandoverStatus)99, null), Guid.NewGuid().ToString()));
        Assert.Equal("INVALID_HANDOVER_STATUS", exInvalidStatus.ErrorCode);

        // 5. ConfirmHandoverAsync: DISPUTE_REASON_TOO_LONG
        var exTooLong = await Assert.ThrowsAsync<BusinessException>(() => service.ConfirmHandoverAsync(shiftId, new ConfirmHandoverRequest(HandoverStatus.DISPUTED, new string('A', 501)), Guid.NewGuid().ToString()));
        Assert.Equal("DISPUTE_REASON_TOO_LONG", exTooLong.ErrorCode);

        // 6. Cash movement approval & rejection
        var movId = Guid.NewGuid();
        shiftRepo.Setup(r => r.GetCashMovementByIdAsync(movId, It.IsAny<CancellationToken>())).ReturnsAsync((ShiftCashMovement?)null);
        var exMov404 = await Assert.ThrowsAsync<BusinessException>(() => service.ApproveCashMovementAsync(movId, "Admin"));
        Assert.Equal("MOVEMENT_NOT_FOUND", exMov404.ErrorCode);

        var movNotPending = new ShiftCashMovement { Id = movId, Status = CashMovementStatus.Recorded };
        shiftRepo.Setup(r => r.GetCashMovementByIdAsync(movId, It.IsAny<CancellationToken>())).ReturnsAsync(movNotPending);
        var exMovNotPending = await Assert.ThrowsAsync<BusinessException>(() => service.ApproveCashMovementAsync(movId, "Admin"));
        Assert.Equal("INVALID_STATUS", exMovNotPending.ErrorCode);

        var exRejectNoReason = await Assert.ThrowsAsync<BusinessException>(() => service.RejectCashMovementAsync(movId, "Admin", ""));
        Assert.Equal("REQUIRED_REASON", exRejectNoReason.ErrorCode);

        // 7. UpdateCountedCashAsync: INVALID_SHIFT_STATE and INVALID_ACTUAL_CASH
        var closingShift = new ShiftZReport { Id = shiftId, Status = ShiftState.Closing, CashierUserId = tenant.CurrentUserId };
        shiftRepo.Setup(r => r.GetByIdAsync(shiftId, It.IsAny<CancellationToken>())).ReturnsAsync(closingShift);
        shiftRepo.Setup(r => r.GetShiftSalesMetricsAsync(shiftId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((1000m, 500m, 2));

        var exNegCash = await Assert.ThrowsAsync<BusinessException>(() => service.UpdateCountedCashAsync(shiftId, tenant.CurrentUserId, new UpdateCountedCashRequest(-100m, "Note")));
        Assert.Equal("INVALID_ACTUAL_CASH", exNegCash.ErrorCode);

        // 8. CloseShiftAsync: INVALID_CASH_ALLOCATION, SHIFT_ALREADY_CLOSED, PENDING_CASH_MOVEMENTS, and Success
        var reqBadAlloc = new CloseShiftRequest(1000m, 600m, 500m, "Receiver", "Note", "Late");
        var exAlloc = await Assert.ThrowsAsync<BusinessException>(() => service.CloseShiftAsync(shiftId, reqBadAlloc));
        Assert.Equal("INVALID_CASH_ALLOCATION", exAlloc.ErrorCode);

        var alreadyClosedShift = new ShiftZReport { Id = shiftId, Status = ShiftState.Closed };
        shiftRepo.Setup(r => r.GetByIdAsync(shiftId, It.IsAny<CancellationToken>())).ReturnsAsync(alreadyClosedShift);
        var exAlreadyClosed = await Assert.ThrowsAsync<BusinessException>(() => service.CloseShiftAsync(shiftId, new CloseShiftRequest(1000m, 500m, 500m)));
        Assert.Equal("SHIFT_ALREADY_CLOSED", exAlreadyClosed.ErrorCode);

        var closingWithPending = new ShiftZReport
        {
            Id = shiftId,
            Status = ShiftState.Closing,
            CashMovements = new List<ShiftCashMovement>
            {
                new() { Status = CashMovementStatus.PendingApproval }
            }
        };
        shiftRepo.Setup(r => r.GetByIdAsync(shiftId, It.IsAny<CancellationToken>())).ReturnsAsync(closingWithPending);
        var exPending = await Assert.ThrowsAsync<BusinessException>(() => service.CloseShiftAsync(shiftId, new CloseShiftRequest(1000m, 500m, 500m)));
        Assert.Equal("PENDING_CASH_MOVEMENTS", exPending.ErrorCode);
    }

    [Fact]
    public async Task OrderService_PreOrder_Loyalty_ExchangeDebt_Branches()
    {
        var orderRepo = new Mock<IOrderRepository>();
        var uow = new Mock<IUnitOfWork>();
        var thermal = new Mock<IThermalPrintService>();
        var promoRepo = new Mock<IPromotionRepository>();
        var shiftRepo = new Mock<IShiftRepository>();
        var shiftService = new Mock<IShiftService>();
        var tenant = new FakeTenantContext();

        // 1. Constructor test
        var simpleOrderService = new OrderService(orderRepo.Object, uow.Object, thermal.Object, null);
        Assert.NotNull(simpleOrderService);

        var service = new OrderService(
            orderRepo.Object,
            uow.Object,
            thermal.Object,
            shiftRepository: shiftRepo.Object,
            promotionRepository: promoRepo.Object,
            tenantContext: tenant,
            shiftService: shiftService.Object
        );

        var branchId = Guid.NewGuid();
        var shiftId = Guid.NewGuid();

        tenant.SetRole("STAFF");
        tenant.CurrentBranchId = branchId;
        var branch = new Branch { Id = branchId, TenantId = tenant.CurrentTenantId, Name = "Chi nhanh 1" };
        orderRepo.Setup(r => r.GetBranchByIdAsync(branchId, It.IsAny<CancellationToken>())).ReturnsAsync(branch);

        // 2. CreateOrderAsync: SHIFT_CLOSING_LOCKDOWN and SHIFT_CLOSED_OR_INVALID
        var closingShift = new ShiftZReport { Id = shiftId, Status = ShiftState.Closing };
        shiftRepo.Setup(r => r.GetActiveShiftAsync(branchId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(closingShift);

        var reqClosing = new OrderCreateRequest(
            branchId,
            null,
            "CASH",
            new List<OrderItemCreateDto> { new(Guid.NewGuid(), 1, 10000) },
            ShiftId: shiftId
        );

        var exClosing = await Assert.ThrowsAsync<BusinessException>(() => service.CreateOrderAsync(reqClosing));
        Assert.Equal("SHIFT_CLOSING_LOCKDOWN", exClosing.ErrorCode);

        // If shiftId is requested but activeShift is null -> SHIFT_CLOSED_OR_INVALID
        var branchIdNoShift = Guid.NewGuid();
        tenant.CurrentBranchId = branchIdNoShift;
        orderRepo.Setup(r => r.GetBranchByIdAsync(branchIdNoShift, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Branch { Id = branchIdNoShift, TenantId = tenant.CurrentTenantId, Name = "Chi nhanh khong ca" });
        shiftRepo.Setup(r => r.GetActiveShiftAsync(branchIdNoShift, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ShiftZReport?)null);

        var reqNoShift = new OrderCreateRequest(
            branchIdNoShift,
            null,
            "CASH",
            new List<OrderItemCreateDto> { new(Guid.NewGuid(), 1, 10000) },
            ShiftId: Guid.NewGuid()
        );

        var exNoShift = await Assert.ThrowsAsync<BusinessException>(() => service.CreateOrderAsync(reqNoShift));
        Assert.Equal("SHIFT_CLOSED_OR_INVALID", exNoShift.ErrorCode);

        // 3. CreatePreOrderAsync: negative deposit
        tenant.SetRole("STORE_OWNER");
        var custId = Guid.NewGuid();
        var customer = new Customer
        {
            Id = custId,
            CustomerCode = "CUST01",
            Name = "Khach Vip",
            TotalDebt = 200000m,
            RewardPoints = 50,
            TotalSpending = 1000000m
        };
        orderRepo.Setup(r => r.GetCustomerByIdAsync(custId, It.IsAny<CancellationToken>())).ReturnsAsync(customer);

        var prodId = Guid.NewGuid();
        var prod = new Product
        {
            Id = prodId,
            Name = "Sản phẩm A",
            SellingPrice = 100000m,
            CostPrice = 60000m,
            BaseUnit = "Cái"
        };
        orderRepo.Setup(r => r.GetProductsByIdsAsync(It.IsAny<List<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, Product> { [prodId] = prod });
        orderRepo.Setup(r => r.GetBranchProductsByProductIdsAsync(branchId, It.IsAny<List<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, BranchProduct>
            {
                [prodId] = new BranchProduct { BranchId = branchId, ProductId = prodId, Status = "Active" }
            });

        var preOrderReqNegDeposit = new PreOrderCreateRequest(
            branchId,
            custId,
            new List<OrderItemCreateDto> { new(prodId, 1, 100000) },
            -10000,
            DateTime.UtcNow.AddDays(2),
            "CASH"
        );

        var exNegDeposit = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreatePreOrderAsync(preOrderReqNegDeposit));
        Assert.Contains("Tiền đặt cọc không được là số âm", exNegDeposit.Message);

        // 4. CancelOrderAsync with DEBT payment method and RewardPoints
        var orderId = Guid.NewGuid();
        var debtOrder = new Order
        {
            Id = orderId,
            OrderCode = "ORD-DEBT-01",
            CustomerId = custId,
            FinalAmount = 50000m,
            PaymentMethod = "DEBT",
            Status = "Completed",
            PointsRedeemed = 20,
            TenantId = "tenant-core-cov",
            Details = new List<OrderDetail>()
        };
        orderRepo.Setup(r => r.GetOrderWithDetailsAsync(orderId, It.IsAny<CancellationToken>())).ReturnsAsync(debtOrder);

        var cancelReq = new CancelOrderRequest("KHACH_HUY", "Huy don");
        var cancelRes = await service.CancelOrderAsync(orderId, cancelReq);
        Assert.NotNull(cancelRes);
        Assert.Equal("Cancelled", debtOrder.Status);
        Assert.Equal("REFUNDED", debtOrder.PaymentStatus);
        Assert.Equal(150000m, customer.TotalDebt); // 200,000 - 50,000
        Assert.Equal(70, customer.RewardPoints); // 50 + 20

        // 5. ConfirmOrderPaymentAsync with Loyalty Settings
        var completedOrder = new Order
        {
            Id = Guid.NewGuid(),
            OrderCode = "ORD-LOYAL-01",
            CustomerId = custId,
            FinalAmount = 100000m,
            Status = "PendingPayment",
            PaymentStatus = "UNPAID",
            TenantId = "tenant-core-cov",
            Details = new List<OrderDetail>()
        };
        orderRepo.Setup(r => r.GetOrderWithDetailsAsync(completedOrder.Id, It.IsAny<CancellationToken>())).ReturnsAsync(completedOrder);

        var loyaltySetting = new LoyaltySetting
        {
            TenantId = "tenant-core-cov",
            IsEnabled = true,
            EarningRateAmount = 50000m,
            EarningPoints = 2
        };
        promoRepo.Setup(p => p.GetLoyaltySettingAsync("tenant-core-cov", It.IsAny<CancellationToken>()))
            .ReturnsAsync(loyaltySetting);

        var completeRes = await service.ConfirmOrderPaymentAsync(completedOrder.Id);
        Assert.NotNull(completeRes);
        // (100,000 / 50,000) * 2 = 4 points earned
        Assert.Equal(74, customer.RewardPoints); // 70 + 4
    }

    [Fact]
    public async Task PurchaseOrderService_MatrixSplit_Preview_Branches()
    {
        var poRepo = new Mock<IPurchaseOrderRepository>();
        var uow = new Mock<IUnitOfWork>();
        var tenant = new FakeTenantContext();
        var dispatchPkg = new Mock<ISupplierDispatchPackageService>();

        var service = new PurchaseOrderService(poRepo.Object, uow.Object, tenant, dispatchPkg.Object);

        var branchId = Guid.NewGuid();
        var supId = Guid.NewGuid();
        var targetDate = DateTime.UtcNow.Date.AddDays(2);

        var branch = new Branch { Id = branchId, Name = "Kho Tổng", IsActive = true };
        poRepo.Setup(r => r.GetBranchByIdAsync(branchId, It.IsAny<CancellationToken>())).ReturnsAsync(branch);

        var draftPo = new PurchaseOrder
        {
            Id = Guid.NewGuid(),
            BranchId = branchId,
            SupplierId = supId,
            Status = "DRAFT",
            Details = new List<PurchaseOrderDetail>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    ProductId = Guid.NewGuid(),
                    DeliveryDate = targetDate,
                    EmployeeQty = 5m
                }
            }
        };

        poRepo.Setup(r => r.GetDraftMatrixPoAsync(branchId, It.IsAny<Guid?>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(draftPo);
        poRepo.Setup(r => r.GetDraftMatrixPosAsync(branchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PurchaseOrder> { draftPo });

        var supCfg = new OrderMatrixSupplierDeliveryConfigDto
        {
            SupplierId = supId,
            SupplierCode = "SUP01",
            SupplierName = "NCC A",
            IsAssignedToBranch = true,
            IsActive = true,
            CutoffTime = "23:59",
            CutoffDayOffset = 0,
            MinOrderQuantity = 0,
            ActiveDeliveryDays = new List<int> { 0, 1, 2, 3, 4, 5, 6 }
        };

        poRepo.Setup(r => r.GetMatrixSupplierConfigsAsync(branchId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OrderMatrixSupplierDeliveryConfigDto> { supCfg });

        var prodId = draftPo.Details.First().ProductId;
        var matrixProd = new OrderMatrixProductDto
        {
            Id = prodId,
            SupplierId = supId,
            Sku = "SKU-01",
            Name = "Sản phẩm 1",
            Price = 10000m,
            IsProductActive = true,
            IsSupplierProductActive = true,
            IsSupplierBranchActive = true,
            IsBranchProductActive = true
        };

        poRepo.Setup(r => r.GetMatrixProductsAsync(branchId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OrderMatrixProductDto> { matrixProd });

        // 1. Preview (dry run) with new PO (existingPo == null) -> ResultSuccess
        var cmdDryRunNew = new SplitMatrixPoCommand
        {
            BranchId = branchId,
            SupplierId = supId,
            Scope = OrderMatrixRules.ScopeNearest,
            WeekStartDate = null
        };

        var dryRes1 = await service.PreviewSplitMatrixPoAsync(cmdDryRunNew);
        Assert.NotNull(dryRes1);
        Assert.Single(dryRes1.Results);
        Assert.Equal(OrderMatrixRules.ResultSuccess, dryRes1.Results[0].Status);

        // 2. Preview with existing committed PO (existingPo != null) -> ResultAmended
        var existingCommittedPo = new PurchaseOrder
        {
            Id = Guid.NewGuid(),
            Code = "PO-20261120-001",
            BranchId = branchId,
            SupplierId = supId,
            RevisionNumber = 1,
            Status = "SUBMITTED"
        };
        poRepo.Setup(r => r.FindCommittedPoAsync(branchId, supId, targetDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingCommittedPo);

        var dryRes2 = await service.PreviewSplitMatrixPoAsync(cmdDryRunNew);
        Assert.NotNull(dryRes2);
        Assert.Single(dryRes2.Results);
        Assert.Equal(OrderMatrixRules.ResultAmended, dryRes2.Results[0].Status);
        Assert.Equal(2, dryRes2.Results[0].RevisionNumber);
    }

    [Fact]
    public async Task GoodsReceiptService_ExchangePo_DistinctBatches_And_StockDeduct_Branches()
    {
        var grRepo = new Mock<IGoodsReceiptRepository>();
        var uow = new Mock<IUnitOfWork>();
        var tenant = new FakeTenantContext();
        var retRepo = new Mock<ISupplierReturnRepository>();

        var service = new GoodsReceiptService(grRepo.Object, uow.Object, tenant, retRepo.Object);

        var branchId = Guid.NewGuid();
        var supId = Guid.NewGuid();
        var poId = Guid.NewGuid();
        var prodId = Guid.NewGuid();
        var returnId = Guid.NewGuid();
        var returnLineId = Guid.NewGuid();
        var podId = Guid.NewGuid();

        var po = new PurchaseOrder
        {
            Id = poId,
            BranchId = branchId,
            SupplierId = supId,
            SourceType = "SUPPLIER_RETURN",
            SourceSupplierReturnId = returnId,
            Details = new List<PurchaseOrderDetail>
            {
                new()
                {
                    Id = podId,
                    ProductId = prodId,
                    OrderedQty = 10m,
                    FinalQty = 10m,
                    SourceSupplierReturnLineId = returnLineId
                }
            }
        };

        grRepo.Setup(r => r.GetPurchaseOrderWithDetailsAsync(tenant.CurrentTenantId, poId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(po);

        var supReturn = new Domain.Entities.Supplier.SupplierReturn
        {
            Id = returnId,
            BranchId = branchId,
            SupplierId = supId,
            Lines = new List<SupplierReturnLine>
            {
                new()
                {
                    Id = returnLineId,
                    ProductId = prodId,
                    BatchCode = "BAT-EX-SKU01-20261120",
                    RequestedQty = 5m,
                    ApprovedQty = 5m
                }
            }
        };
        retRepo.Setup(r => r.GetByIdAsync(tenant.CurrentTenantId, returnId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(supReturn);

        var product = new Product
        {
            Id = prodId,
            ProductCode = "SKU01",
            Name = "Sản phẩm đổi",
            HasBatches = true,
            BaseUnit = "Gói"
        };
        grRepo.Setup(r => r.GetProductByIdAsync(tenant.CurrentTenantId, prodId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);
        grRepo.Setup(r => r.GetProductsByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, Product> { [prodId] = product });

        var stock = new BranchStock
        {
            Id = Guid.NewGuid(),
            BranchId = branchId,
            ProductId = prodId,
            BatchCode = "BAT-EX-SKU01-20261120",
            QuantityOnHand = 20m,
            QuantityReserved = 5m
        };
        grRepo.Setup(r => r.GetBranchStockAsync(tenant.CurrentTenantId, branchId, prodId, "BAT-EX-SKU01-20261120", It.IsAny<CancellationToken>()))
            .ReturnsAsync(stock);

        grRepo.Setup(r => r.GenerateCodeAsync(tenant.CurrentTenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync("GRN-001");
        grRepo.Setup(r => r.UserExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Create GoodsReceipt with empty BatchCode on exchange PO line
        var createCmd = new CreateGoodsReceiptCommand
        {
            BranchId = branchId,
            SupplierId = supId,
            PurchaseOrderId = poId,
            AutoComplete = true,
            Lines = new List<CreateGoodsReceiptLineCommand>
            {
                new()
                {
                    ProductId = prodId,
                    PurchaseOrderDetailId = podId,
                    OrderedQty = 10m,
                    DeliveredQty = 10m,
                    AcceptedQty = 8m,
                    RejectedQty = 2m,
                    RejectionReason = "DAMAGED",
                    UnitPrice = 50000m,
                    BatchCode = "BAT-EX-SKU01-20261120", // Conflicting batch with return line -> triggers auto-rename!
                    ExpiryDate = DateTime.UtcNow.AddMonths(6)
                }
            }
        };

        GoodsReceipt? savedReceipt = null;
        grRepo.Setup(r => r.AddAsync(It.IsAny<GoodsReceipt>(), It.IsAny<CancellationToken>()))
            .Callback<GoodsReceipt, CancellationToken>((gr, _) =>
            {
                savedReceipt = gr;
                gr.Id = Guid.NewGuid();
            })
            .ReturnsAsync((GoodsReceipt gr, CancellationToken _) => gr);

        grRepo.Setup(r => r.GetByIdAsync(tenant.CurrentTenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => savedReceipt != null ? new GoodsReceipt
            {
                Id = savedReceipt.Id,
                Code = "GRN-001",
                BranchId = branchId,
                SupplierId = supId,
                PurchaseOrderId = poId,
                Status = savedReceipt.Status,
                Lines = savedReceipt.Lines
            } : null);

        var result = await service.CreateGoodsReceiptAsync(createCmd);
        Assert.NotNull(result);
        Assert.NotNull(savedReceipt);
        Assert.Equal("COMPLETED", savedReceipt!.Status);

        // Verify that batch code was auto-generated for the exchange line
        var line = savedReceipt.Lines.First();
        Assert.False(string.IsNullOrWhiteSpace(line.BatchCode));
        Assert.StartsWith($"BAT-EX-{prodId.ToString("N")[..8].ToUpperInvariant()}", line.BatchCode);

        // Verify reserved stock was deducted
        Assert.Equal(0m, stock.QuantityReserved);
        Assert.Equal(15m, stock.QuantityOnHand);
    }
}
