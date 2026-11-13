using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using MT_GRMS.Application.Auth;
using MT_GRMS.Application.Auth.Dtos;
using MT_GRMS.Application.Branches;
using MT_GRMS.Application.Branches.Dtos;
using MT_GRMS.Application.Cashbook;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.HR.Employees;
using MT_GRMS.Application.HR.Schedules;
using MT_GRMS.Application.HR.Schedules.Dtos;
using MT_GRMS.Application.HR.ShiftConfigs;
using MT_GRMS.Application.PosOrders;
using MT_GRMS.Application.PosOrders.Dtos;
using MT_GRMS.Application.PosOrders.Validators;
using MT_GRMS.Application.PosShifts;
using MT_GRMS.Application.PosShifts.Dtos;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Entities.HR;
using MT_GRMS.Domain.Entities.Identity;
using MT_GRMS.Domain.Entities.Inventory;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Domain.Entities.Sales;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Domain.Exceptions;
using MT_GRMS.Infrastructure.Services.Printer;
using Xunit;

namespace MT_GRMS.UnitTests;

public class PosDbSyncOwnerShiftlessAndBranchCatalogTests
{
    private readonly Guid _branchId = Guid.Parse("11111111-aaaa-bbbb-cccc-111111111111");
    private readonly Guid _productId = Guid.Parse("33333333-aaaa-bbbb-cccc-333333333333");

    [Fact]
    public void ResolveDeterministicGuid_ForStringUserIds_ReturnsDistinctDeterministicNonEmptyGuids()
    {
        var guid1a = TenantContext.ResolveDeterministicGuid("usr_employee_annhien_02");
        var guid1b = TenantContext.ResolveDeterministicGuid("usr_employee_annhien_02");
        var guid2 = TenantContext.ResolveDeterministicGuid("usr_employee_annhien_04");

        Assert.NotEqual(Guid.Empty, guid1a);
        Assert.NotEqual(Guid.Empty, guid2);
        Assert.Equal(guid1a, guid1b);
        Assert.NotEqual(guid1a, guid2);
    }

    [Fact]
    public async Task CheckCashierShiftEligibility_WithStringUserIdAndAllTime235959Schedule_AllowsAndOpenShiftStoresEmployeeName()
    {
        var tenantCtx = new TenantContext();
        var detGuid = TenantContext.ResolveDeterministicGuid("usr_employee_annhien_02");
        tenantCtx.SetContext("tenant_an_nhien_02", detGuid, _branchId);
        tenantCtx.SetRawUserId("usr_employee_annhien_02");
        tenantCtx.SetRole("STAFF");

        var emp = new Employee
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant_an_nhien_02",
            BranchId = _branchId,
            UserId = "usr_employee_annhien_02",
            EmployeeCode = "NV-AN-01",
            FullName = "Phạm Thị Yến",
            Status = EmployeeStatus.Active
        };

        var vnToday = DateTime.UtcNow.AddHours(7).Date;
        var allTimeSchedule = new EmployeeSchedule
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant_an_nhien_02",
            EmployeeId = emp.Id,
            BranchId = _branchId,
            WorkDate = vnToday,
            ShiftCode = "CA-AN-247",
            ShiftName = "Ca Toàn Thời Gian 24/7",
            PlannedStartTime = TimeSpan.Zero,
            PlannedEndTime = new TimeSpan(23, 59, 59),
            AllowedCheckInStart = TimeSpan.Zero,
            AllowedCheckInEnd = new TimeSpan(23, 59, 59),
            Status = ScheduleStatus.Scheduled
        };

        var shiftRepoMock = new Mock<IShiftRepository>();
        shiftRepoMock.Setup(r => r.GetEmployeeByUserIdAsync("usr_employee_annhien_02", It.IsAny<CancellationToken>()))
            .ReturnsAsync(emp);
        shiftRepoMock.Setup(r => r.GetCashierSchedulesAsync(emp.Id, _branchId, vnToday, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EmployeeSchedule> { allTimeSchedule });
        shiftRepoMock.Setup(r => r.GenerateNextShiftCodeAsync(_branchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync("SHF-001");

        ShiftParticipant? capturedParticipant = null;
        shiftRepoMock.Setup(r => r.AddParticipantAsync(It.IsAny<ShiftParticipant>(), It.IsAny<CancellationToken>()))
            .Callback<ShiftParticipant, CancellationToken>((p, _) => capturedParticipant = p)
            .Returns(Task.CompletedTask);

        var uowMock = new Mock<IUnitOfWork>();
        var shiftService = new ShiftService(shiftRepoMock.Object, uowMock.Object, tenantCtx);

        var eligibility = await shiftService.CheckCashierShiftEligibilityAsync(_branchId, detGuid, "STAFF");
        Assert.True(eligibility.IsAllowed);
        Assert.Equal("Phạm Thị Yến", eligibility.EmployeeName);

        await shiftService.OpenShiftAsync(
            new MT_GRMS.Application.PosShifts.Dtos.OpenShiftRequest(_branchId, Guid.NewGuid(), "Ca Sáng", 500000m, null),
            detGuid);

        Assert.NotNull(capturedParticipant);
        Assert.Equal("Phạm Thị Yến", capturedParticipant!.FullName);
    }

    [Fact]
    public async Task SelfCheckInAsync_EnforcesOwnershipIdempotencyAndFaceDescriptorGuard()
    {
        var vnToday = DateTime.UtcNow.AddHours(7).Date;
        var tenantCtx = new TenantContext();
        tenantCtx.SetContext("tenant_an_nhien_02", TenantContext.ResolveDeterministicGuid("usr_employee_annhien_02"), _branchId);
        tenantCtx.SetRawUserId("usr_employee_annhien_02");
        tenantCtx.SetRole("STAFF");

        var emp = new Employee
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant_an_nhien_02",
            BranchId = _branchId,
            UserId = "usr_employee_annhien_02",
            EmployeeCode = "NV-AN-01",
            FullName = "Phạm Thị Yến",
            FaceDescriptor = null,
            Status = EmployeeStatus.Active
        };

        var schedule = new EmployeeSchedule
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant_an_nhien_02",
            EmployeeId = emp.Id,
            Employee = emp,
            BranchId = _branchId,
            WorkDate = vnToday,
            ShiftCode = "CA-AN-247",
            ShiftName = "Ca 24/7",
            PlannedStartTime = TimeSpan.Zero,
            PlannedEndTime = new TimeSpan(23, 59, 59),
            AllowedCheckInStart = TimeSpan.Zero,
            AllowedCheckInEnd = new TimeSpan(23, 59, 59),
            Status = ScheduleStatus.Scheduled
        };

        var schedRepoMock = new Mock<IScheduleRepository>();
        schedRepoMock.Setup(r => r.GetByIdAsync(schedule.Id, It.IsAny<CancellationToken>())).ReturnsAsync(schedule);

        var empRepoMock = new Mock<IEmployeeRepository>();
        var shiftCfgRepoMock = new Mock<IShiftConfigRepository>();
        var uowMock = new Mock<IUnitOfWork>();

        var scheduleService = new ScheduleService(
            schedRepoMock.Object,
            empRepoMock.Object,
            shiftCfgRepoMock.Object,
            uowMock.Object,
            tenantCtx);

        // 1. Happy path self check-in
        var res = await scheduleService.SelfCheckInAsync(schedule.Id, new SelfCheckInRequest("Vào ca test POS"));
        Assert.NotNull(res.CheckInTime);
        Assert.Equal("SELF_CHECK_IN", res.CheckInMethod);
        Assert.Equal(ScheduleStatus.InProgress, res.Status);

        // 2. Duplicate call must reject ALREADY_CHECKED_IN without overwriting CheckInTime
        var firstCheckIn = schedule.CheckInTime;
        var dupEx = await Assert.ThrowsAsync<BusinessException>(() =>
            scheduleService.SelfCheckInAsync(schedule.Id, new SelfCheckInRequest("Gọi lặp")));
        Assert.Equal("ALREADY_CHECKED_IN", dupEx.ErrorCode);
        Assert.Equal(firstCheckIn, schedule.CheckInTime);

        // 3. Employee with enrolled FaceDescriptor must reject FACE_ID_REQUIRED
        var scheduleWithFace = new EmployeeSchedule
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant_an_nhien_02",
            EmployeeId = emp.Id,
            Employee = new Employee
            {
                Id = emp.Id,
                TenantId = "tenant_an_nhien_02",
                BranchId = _branchId,
                UserId = "usr_employee_annhien_02",
                EmployeeCode = "NV-AN-01",
                FullName = "Phạm Thị Yến",
                FaceDescriptor = "[0.12, -0.34, 0.56]",
                Status = EmployeeStatus.Active
            },
            BranchId = _branchId,
            WorkDate = vnToday,
            PlannedStartTime = TimeSpan.Zero,
            PlannedEndTime = new TimeSpan(23, 59, 59),
            AllowedCheckInStart = TimeSpan.Zero,
            AllowedCheckInEnd = new TimeSpan(23, 59, 59),
            Status = ScheduleStatus.Scheduled
        };
        schedRepoMock.Setup(r => r.GetByIdAsync(scheduleWithFace.Id, It.IsAny<CancellationToken>())).ReturnsAsync(scheduleWithFace);
        var faceEx = await Assert.ThrowsAsync<BusinessException>(() =>
            scheduleService.SelfCheckInAsync(scheduleWithFace.Id, null));
        Assert.Equal("FACE_ID_REQUIRED", faceEx.ErrorCode);

        // 4. Other employee schedule must reject Forbidden
        var otherEmpSchedule = new EmployeeSchedule
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant_an_nhien_02",
            EmployeeId = Guid.NewGuid(),
            Employee = new Employee
            {
                Id = Guid.NewGuid(),
                TenantId = "tenant_an_nhien_02",
                BranchId = _branchId,
                UserId = "usr_employee_annhien_04",
                Status = EmployeeStatus.Active
            },
            BranchId = _branchId,
            WorkDate = vnToday,
            Status = ScheduleStatus.Scheduled
        };
        schedRepoMock.Setup(r => r.GetByIdAsync(otherEmpSchedule.Id, It.IsAny<CancellationToken>())).ReturnsAsync(otherEmpSchedule);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            scheduleService.SelfCheckInAsync(otherEmpSchedule.Id, null));
    }

    [Fact]
    public async Task CreateOrderAsync_StoreOwnerShiftless_BypassesClosingShiftAndRoutesCashToSafeWithoutTouchingTillOrParticipants()
    {
        var orderRepoMock = new Mock<IOrderRepository>();
        var branch = new Branch { Id = _branchId, TenantId = "tenant_an_nhien_02", Name = "CN Ba Đình", IsActive = true };
        var product = new Product { Id = _productId, TenantId = "tenant_an_nhien_02", ProductCode = "SP01", Name = "Gạo Lứt", SellingPrice = 50000m, BaseUnit = "Túi", IsActive = true };
        var stock = new BranchStock
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant_an_nhien_02",
            BranchId = _branchId,
            ProductId = _productId,
            BatchCode = "B01",
            ExpiryDate = DateTime.UtcNow.AddMonths(6),
            QuantityOnHand = 20m,
            LandedCostPerUnit = 35000m
        };

        orderRepoMock.Setup(r => r.GetBranchByIdAsync(_branchId, It.IsAny<CancellationToken>())).ReturnsAsync(branch);
        orderRepoMock.Setup(r => r.GetProductsByIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, Product> { [_productId] = product });
        orderRepoMock.Setup(r => r.GetBranchProductsByProductIdsAsync(_branchId, It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, BranchProduct>
            {
                [_productId] = new BranchProduct { Id = Guid.NewGuid(), TenantId = "tenant_an_nhien_02", BranchId = _branchId, ProductId = _productId, SellingPriceOverride = 50000m, Status = "Active" }
            });
        orderRepoMock.Setup(r => r.GetBranchStocksForProductFefoAsync(_branchId, _productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BranchStock> { stock });

        Order? savedOrder = null;
        orderRepoMock.Setup(r => r.AddAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
            .Callback<Order, CancellationToken>((o, _) => savedOrder = o)
            .Returns(Task.CompletedTask);

        // Cashier shift is currently CLOSING!
        var closingShift = new ShiftZReport
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant_an_nhien_02",
            BranchId = _branchId,
            Status = ShiftState.Closing,
            ExpectedCash = 500000m
        };
        var shiftRepoMock = new Mock<IShiftRepository>();
        shiftRepoMock.Setup(r => r.GetActiveShiftAsync(_branchId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(closingShift);

        var safeAccount = new FinanceAccount
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant_an_nhien_02",
            BranchId = _branchId,
            AccountType = FinanceAccountType.Safe,
            AccountName = "Két Sắt Chi Nhánh"
        };
        var cashbookMock = new Mock<ICashbookService>();
        cashbookMock.Setup(c => c.GetOrCreateSafeAsync(_branchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(safeAccount);

        var ownerCtx = new TenantContext();
        ownerCtx.SetContext("tenant_an_nhien_02", TenantContext.ResolveDeterministicGuid("usr_owner_annhien_02"), null);
        ownerCtx.SetRawUserId("usr_owner_annhien_02");
        ownerCtx.SetRole("STORE_OWNER");

        var uowMock = new Mock<IUnitOfWork>();
        var orderService = new OrderService(
            orderRepoMock.Object,
            uowMock.Object,
            new ThermalPrintService(),
            debtService: null,
            cashbookService: cashbookMock.Object,
            validator: new OrderCreateRequestValidator(),
            shiftRepository: shiftRepoMock.Object,
            promotionRepository: null,
            tenantContext: ownerCtx,
            shiftService: null);

        var response = await orderService.CreateOrderAsync(new OrderCreateRequest(
            BranchId: _branchId,
            CustomerId: null,
            PaymentMethod: "CASH",
            Items: new List<OrderItemCreateDto>
            {
                new OrderItemCreateDto(_productId, 1m, 50000m)
            },
            CashTendered: 50000m,
            ShiftId: null
        ));

        Assert.NotNull(savedOrder);
        Assert.Null(savedOrder!.ShiftId);
        // Per-order creation must NOT record individual cashbook entries; instead, EOD settlement aggregates and records into Safe!
        cashbookMock.Verify(c => c.GetOrCreateTillAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        shiftRepoMock.Verify(r => r.AddParticipantAsync(It.IsAny<ShiftParticipant>(), It.IsAny<CancellationToken>()), Times.Never);

        orderRepoMock.Setup(r => r.GetAllOrdersForSummaryAsync(_branchId, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Order> { savedOrder });
        var settleResult = await orderService.SettleOwnerDailyOrdersAsync(new SettleOwnerDailyRequest(_branchId, "Chốt cuối ngày"));
        Assert.Equal(1, settleResult.SettledOrdersCount);
        Assert.Equal(50000m, settleResult.CashSettledAmount);
        cashbookMock.Verify(c => c.GetOrCreateSafeAsync(_branchId, It.IsAny<CancellationToken>()), Times.Once);
        cashbookMock.Verify(c => c.ReceiveCashAsync(safeAccount.Id, FinanceAccountType.Safe, 50000m, "POS_SALE", It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);

        // Staff spoofing creatorRole = "STORE_OWNER" when no shift is open MUST be rejected with SHIFT_NOT_OPEN
        shiftRepoMock.Setup(r => r.GetActiveShiftAsync(_branchId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ShiftZReport?)null);
        var staffCtx = new TenantContext();
        staffCtx.SetContext("tenant_an_nhien_02", TenantContext.ResolveDeterministicGuid("usr_employee_annhien_02"), _branchId);
        staffCtx.SetRawUserId("usr_employee_annhien_02");
        staffCtx.SetRole("STAFF");

        var staffOrderService = new OrderService(
            orderRepoMock.Object,
            uowMock.Object,
            new ThermalPrintService(),
            debtService: null,
            cashbookService: cashbookMock.Object,
            validator: new OrderCreateRequestValidator(),
            shiftRepository: shiftRepoMock.Object,
            promotionRepository: null,
            tenantContext: staffCtx,
            shiftService: null);

        var staffEx = await Assert.ThrowsAsync<BusinessException>(() =>
            staffOrderService.CreateOrderAsync(new OrderCreateRequest(
                BranchId: _branchId,
                CustomerId: null,
                PaymentMethod: "CASH",
                Items: new List<OrderItemCreateDto>
                {
                    new OrderItemCreateDto(_productId, 1m, 50000m)
                },
                CreatorRole: "STORE_OWNER"
            )));
        Assert.Equal("SHIFT_NOT_OPEN", staffEx.ErrorCode);
    }

    [Fact]
    public async Task GetBranchProductsAsync_SupportsPageSizeAbove100ForPosCatalog()
    {
        var repoMock = new Mock<IBranchProductRepository>();
        repoMock.Setup(r => r.GetBranchByIdAsync(_branchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Branch { Id = _branchId, TenantId = "tenant_an_nhien_02", Name = "CN Ba Đình", IsActive = true });

        int capturedPageSize = 0;
        repoMock.Setup(r => r.GetPagedBranchProductsAsync(_branchId, It.IsAny<BranchProductQueryFilter>(), 1, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, BranchProductQueryFilter, int, int, CancellationToken>((_, _, _, ps, _) => capturedPageSize = ps)
            .ReturnsAsync((Enumerable.Range(1, 250).Select(i => new BranchProductDto { Id = Guid.NewGuid(), ProductId = Guid.NewGuid(), Name = $"SP {i}" }).ToList(), 250));

        var uowMock = new Mock<IUnitOfWork>();
        var tenantCtx = new TenantContext();
        tenantCtx.SetContext("tenant_an_nhien_02", Guid.NewGuid(), _branchId);

        var service = new BranchProductService(repoMock.Object, uowMock.Object, tenantCtx);
        var paged = await service.GetBranchProductsAsync(_branchId, new BranchProductQueryFilter { PageIndex = 1, PageSize = 500, Status = "Active" });

        Assert.Equal(500, capturedPageSize);
        Assert.Equal(250, paged.Items.Count);
    }

    [Fact]
    public async Task LoginAsync_ReturnsEmployeeBranchIdAndEmbedsBranchIdClaimInJwt()
    {
        var user = new User
        {
            Id = "usr_employee_annhien_02",
            UserName = "0917778888",
            Phone = "0917778888",
            Email = "yen.pham@annhienmart.vn",
            FullName = "Phạm Thị Yến",
            Password = "123456",
            IsActive = true,
            IsTwoFactorEnabled = false,
            TenantUsers = new List<TenantUser>
            {
                new TenantUser { TenantId = "tenant_an_nhien_02", UserId = "usr_employee_annhien_02", Role = "STAFF", IsActive = true }
            }
        };

        var userRepoMock = new Mock<IUserRepository>();
        userRepoMock.Setup(r => r.GetByUsernameAsync("0917778888", It.IsAny<CancellationToken>())).ReturnsAsync(user);
        userRepoMock.Setup(r => r.GetEmployeeBranchIdAsync("usr_employee_annhien_02", "tenant_an_nhien_02", It.IsAny<CancellationToken>()))
            .ReturnsAsync(_branchId);

        var authService = new AuthService(userRepoMock.Object);

        var loginRes = await authService.LoginAsync(new LoginRequest("0917778888", "123456", "tenant_an_nhien_02"));
        Assert.Equal(_branchId, loginRes.User.BranchId);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(loginRes.AccessToken);
        var branchClaim = jwt.Claims.FirstOrDefault(c => c.Type == "branch_id")?.Value;
        Assert.Equal(_branchId.ToString(), branchClaim);
    }
}
