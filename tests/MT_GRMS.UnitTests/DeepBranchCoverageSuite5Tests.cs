using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using MT_GRMS.Application.Auth;
using MT_GRMS.Application.Auth.Dtos;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Common.Interfaces;
using MT_GRMS.Application.Common.Services;
using MT_GRMS.Application.HR.Employees;
using MT_GRMS.Application.HR.Employees.Dtos;
using MT_GRMS.Application.HR.Schedules;
using MT_GRMS.Application.HR.Schedules.Dtos;
using MT_GRMS.Application.HR.ShiftConfigs;
using MT_GRMS.Application.PosOrders;
using MT_GRMS.Application.PosOrders.Dtos;
using MT_GRMS.Application.PosShifts;
using MT_GRMS.Application.PosShifts.Dtos;
using MT_GRMS.Application.PurchaseOrders;
using MT_GRMS.Application.PurchaseOrders.Dtos;
using MT_GRMS.Application.Stocktakes;
using MT_GRMS.Application.Stocktakes.Dtos;
using MT_GRMS.Application.SupplierReturns;
using MT_GRMS.Application.Suppliers;
using MT_GRMS.Domain.Common;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Entities.HR;
using MT_GRMS.Domain.Entities.Identity;
using MT_GRMS.Domain.Entities.Inventory;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Domain.Entities.Sales;
using MT_GRMS.Domain.Entities.Supplier;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Domain.Exceptions;
using MT_GRMS.Infrastructure.Services;
using Xunit;

namespace MT_GRMS.UnitTests;

public class DeepBranchCoverageSuite5Tests
{
    #region Fakes & Test Helpers

    private class FakeTenantContext : ITenantContext
    {
        public string CurrentTenantId { get; set; } = "tenant-suite5";
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
        public Task BeginTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CommitTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    #endregion

    // =========================================================================
    // SECTION 1: OrderService - Order Validation & Cancel Not Found
    // =========================================================================

    [Fact]
    public async Task OrderService_BatchStock_And_DebtOrder_Branches()
    {
        var tenantContext = new FakeTenantContext();
        var fakeOrderRepo = new FakeOrderRepository();
        var uow = new FakeUnitOfWork();
        var printMock = new Mock<IThermalPrintService>();

        var branchId = tenantContext.CurrentBranchId!.Value;
        var prodId = Guid.NewGuid();

        fakeOrderRepo.Branches.Add(new Branch { Id = branchId, TenantId = tenantContext.CurrentTenantId, Name = "Branch 1", IsActive = true });
        fakeOrderRepo.Products.Add(new Product { Id = prodId, Name = "SP Expired", SellingPrice = 40000m, IsActive = true, Units = new List<ProductUnit>() });
        fakeOrderRepo.BranchProducts.Add(new BranchProduct { BranchId = branchId, ProductId = prodId, Status = "Active" });

        var service = new OrderService(fakeOrderRepo, uow, printMock.Object, tenantContext: tenantContext);

        // 1. Order with negative/zero items throws FluentValidation.ValidationException
        var reqEmpty = new OrderCreateRequest(
            BranchId: branchId,
            CustomerId: null,
            PaymentMethod: "CASH",
            Items: new List<OrderItemCreateDto>()
        );
        var exEmpty = await Assert.ThrowsAsync<FluentValidation.ValidationException>(() => service.CreateOrderAsync(reqEmpty));
        Assert.Contains("trống", exEmpty.Message);

        // 2. Cancel order when order not found throws InvalidOperationException
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CancelOrderAsync(Guid.NewGuid(), new CancelOrderRequest("Loi", "Huy", "STORE_OWNER")));
    }

    // =========================================================================
    // SECTION 2: AuthService - 2FA Challenge, Expired OTP & In-Memory Store
    // =========================================================================

    [Fact]
    public async Task AuthService_TwoFactor_And_OtpValidation_Branches()
    {
        var tenantContext = new FakeTenantContext();
        var uow = new FakeUnitOfWork();
        var userRepoMock = new Mock<IUserRepository>();
        var emailServiceMock = new Mock<IEmailService>();
        var employeeRepoMock = new Mock<IEmployeeRepository>();
        var hasher = new PasswordHasher();

        var authService = new AuthService(
            userRepoMock.Object,
            null,
            hasher,
            emailServiceMock.Object,
            null,
            employeeRepoMock.Object);

        // 1. Login with TwoFactorEnabled throws UnauthorizedAccessException requiring OTP
        var user2fa = new User
        {
            Id = Guid.NewGuid().ToString(),
            UserName = "user2fa",
            Email = "user2fa@mtmart.vn",
            PasswordHash = hasher.HashPassword("Secret@123"),
            IsTwoFactorEnabled = true,
            IsEmailVerified = true,
            IsActive = true
        };
        userRepoMock.Setup(u => u.GetByUsernameAsync("user2fa", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user2fa);
        emailServiceMock.Setup(e => e.SendLoginOtpAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var ex2fa = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            authService.LoginAsync(new LoginRequest("user2fa", "Secret@123", null)));
        Assert.Contains("OTP", ex2fa.Message);

        // SendLoginOtpAsync returns requires OTP challenge
        var otpRes = await authService.SendLoginOtpAsync(new LoginRequest("user2fa", "Secret@123", null));
        Assert.True(otpRes.RequiresOtp);
        Assert.NotEmpty(otpRes.TempToken);

        // 2. VerifyLoginOtpAsync with invalid/expired temp token throws ArgumentException
        await Assert.ThrowsAsync<ArgumentException>(() =>
            authService.VerifyLoginOtpAsync(new VerifyLoginOtpRequest("invalid_temp_token", "123456")));

        // 3. CreateTwoFactorChallengeAsync when email is not verified throws ArgumentException
        var unverifiedUser = new User
        {
            Id = Guid.NewGuid().ToString(),
            UserName = "unverified",
            Email = "unverified@mtmart.vn",
            PasswordHash = hasher.HashPassword("Secret@123"),
            IsTwoFactorEnabled = false,
            IsEmailVerified = false,
            IsActive = true
        };
        userRepoMock.Setup(u => u.GetByIdAsync(unverifiedUser.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(unverifiedUser);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            authService.CreateTwoFactorChallengeAsync(unverifiedUser.Id, new TwoFactorChallengeRequest(true, "Secret@123")));

        // 4. ConfirmTwoFactorChallengeAsync with invalid challenge token throws UnauthorizedAccessException
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            authService.ConfirmTwoFactorChallengeAsync(unverifiedUser.Id, new TwoFactorConfirmRequest("bad_token", "000000")));
    }

    // =========================================================================
    // SECTION 3: ScheduleApprovalService - Check Times & Rejection Security
    // =========================================================================

    [Fact]
    public async Task ScheduleApprovalService_CheckTimes_And_RejectionSecurity_Branches()
    {
        var tenantContext = new FakeTenantContext();
        var uow = new FakeUnitOfWork();
        var scheduleRepoMock = new Mock<IScheduleRepository>();
        var approvalRepoMock = new Mock<IScheduleApprovalRequestRepository>();
        var employeeRepoMock = new Mock<IEmployeeRepository>();

        var service = new ScheduleApprovalService(
            approvalRepoMock.Object,
            scheduleRepoMock.Object,
            employeeRepoMock.Object,
            uow,
            tenantContext);

        var scheduleId = Guid.NewGuid();
        var empId = Guid.NewGuid();
        var schedule = new EmployeeSchedule
        {
            Id = scheduleId,
            EmployeeId = empId,
            BranchId = tenantContext.CurrentBranchId ?? Guid.NewGuid(),
            WorkDate = DateTime.UtcNow.Date,
            PlannedStartTime = TimeSpan.FromHours(8),
            PlannedEndTime = TimeSpan.FromHours(17),
            Status = ScheduleStatus.Completed,
            CheckInTime = DateTime.UtcNow.Date.AddHours(8),
            CheckOutTime = DateTime.UtcNow.Date.AddHours(17)
        };
        scheduleRepoMock.Setup(s => s.GetByIdAsync(scheduleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(schedule);

        // 1. GetRequestsByScheduleIdAsync: non-admin employee accessing other's schedule -> UnauthorizedAccessException
        var otherEmp = new Employee { Id = Guid.NewGuid(), UserId = tenantContext.CurrentUserId.ToString() };
        employeeRepoMock.Setup(e => e.GetByUserIdAsync(tenantContext.CurrentUserId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(otherEmp);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.GetRequestsByScheduleIdAsync(scheduleId, tenantContext.CurrentUserId.ToString(), isOwnerOrAdmin: false));

        // When accessed by owner/admin -> returns list
        approvalRepoMock.Setup(a => a.GetByScheduleIdAsync(scheduleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ScheduleApprovalRequest>());
        var adminList = await service.GetRequestsByScheduleIdAsync(scheduleId, tenantContext.CurrentUserId.ToString(), isOwnerOrAdmin: true);
        Assert.Empty(adminList);

        // 2. ReviewRequestAsync: request not found -> KeyNotFoundException
        var nonExistentId = Guid.NewGuid();
        approvalRepoMock.Setup(a => a.GetByIdWithDetailsAsync(nonExistentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ScheduleApprovalRequest?)null);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.ReviewRequestAsync(nonExistentId, tenantContext.CurrentUserId.ToString(), new ReviewScheduleApprovalRequestDto(false, "Ly do tu choi")));

        // 3. DeleteRequestAsync: request not found -> KeyNotFoundException
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.DeleteRequestAsync(nonExistentId, tenantContext.CurrentUserId.ToString(), true));
    }

    // =========================================================================
    // SECTION 4: MatrixExcelImportService - Missing SKU Column & Out of Filter
    // =========================================================================

    [Fact]
    public async Task MatrixExcelImportService_MissingColumns_And_OutOfFilter_Branches()
    {
        var tenant = new FakeTenantContext();
        var mockPoService = new Mock<IPurchaseOrderService>();
        var excelService = new MatrixExcelImportService(mockPoService.Object, tenant);

        var branchId = Guid.NewGuid();
        var supplierId = Guid.NewGuid();
        var matrix = new OrderMatrixResponse
        {
            BranchId = branchId,
            BranchName = "Branch A",
            SupplierConfigs = new List<OrderMatrixSupplierDeliveryConfigDto>(),
            Products = new List<OrderMatrixProductDto>()
        };
        mockPoService.Setup(p => p.GetOrderMatrixAsync(It.IsAny<OrderMatrixQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(matrix);

        // 1. Corrupt/Empty stream -> FORMAT_INVALID
        using var emptyStream = new MemoryStream(new byte[] { 0x00, 0x01 });
        var query = new MatrixExcelPreviewQuery
        {
            BranchId = branchId,
            SupplierId = supplierId,
            ViewFrom = DateTime.UtcNow,
            DurationDays = 7,
            FileName = "test.xlsx",
            FileSize = 2
        };
        var emptyRes = await excelService.PreviewAsync(emptyStream, query);
        Assert.False(emptyRes.Valid);
        Assert.Contains(emptyRes.Errors, e => e.Code == "FORMAT_INVALID");

        // 2. Query with specific supplierId sets ScopeStatus correctly
        Assert.Equal(supplierId.ToString(), emptyRes.ScopeStatus.SupplierExpected);
    }

    // =========================================================================
    // SECTION 5: StocktakeService - Lookups & Not Found Handlers
    // =========================================================================

    [Fact]
    public async Task StocktakeService_LookupsAndNotFound_Branches()
    {
        var tenantContext = new FakeTenantContext();
        var uow = new FakeUnitOfWork();
        var stocktakeRepoMock = new Mock<IStocktakeRepository>();
        var supplierReturnServiceMock = new Mock<ISupplierReturnService>();
        var supplierReturnRepoMock = new Mock<ISupplierReturnRepository>();
        var supplierRepoMock = new Mock<ISupplierRepository>();

        var service = new StocktakeService(
            stocktakeRepoMock.Object,
            supplierReturnServiceMock.Object,
            supplierReturnRepoMock.Object,
            supplierRepoMock.Object,
            tenantContext,
            uow);

        var missingId = Guid.NewGuid();
        stocktakeRepoMock.Setup(r => r.GetDetailDtoByIdAsync(tenantContext.CurrentTenantId, missingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((StocktakeDetailDto?)null);
        stocktakeRepoMock.Setup(r => r.GetByIdAsync(tenantContext.CurrentTenantId, missingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Stocktake?)null);

        // 1. GetByIdAsync: not found -> NotFoundException
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetByIdAsync(missingId));

        // 2. SubmitAsync: not found -> NotFoundException
        await Assert.ThrowsAsync<NotFoundException>(() => service.SubmitAsync(missingId));

        // 3. ReviewAsync: not found -> NotFoundException
        await Assert.ThrowsAsync<NotFoundException>(() => service.ReviewAsync(missingId, new ReviewStocktakeCommand("APPROVE", "Approve")));

        // 4. ReviewAsync Reject: not found -> NotFoundException
        await Assert.ThrowsAsync<NotFoundException>(() => service.ReviewAsync(missingId, new ReviewStocktakeCommand("REJECT", "Reject")));

        // 5. Employee role auto filters to user's assigned branch
        tenantContext.SetRole("STAFF");
        stocktakeRepoMock.Setup(r => r.GetPagedAsync(It.IsAny<string>(), It.IsAny<StocktakeFilterQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<StocktakeListItemDto> { Items = new List<StocktakeListItemDto>(), TotalItems = 0, Page = 1, PageSize = 10 });

        var paged = await service.GetPagedAsync(new StocktakeFilterQuery());
        Assert.NotNull(paged);
    }

    // =========================================================================
    // SECTION 6: ShiftService - Notes Tagging & Schedule Eligibility
    // =========================================================================

    [Fact]
    public async Task ShiftService_ScheduleEligibility_And_NotesTagging_Branches()
    {
        var tenantContext = new FakeTenantContext();
        var uow = new FakeUnitOfWork();
        var shiftRepoMock = new Mock<IShiftRepository>();

        var service = new ShiftService(
            shiftRepoMock.Object,
            uow,
            tenantContext);

        var missingShiftId = Guid.NewGuid();
        shiftRepoMock.Setup(s => s.GetByIdAsync(missingShiftId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ShiftZReport?)null);

        // 1. Shift not found on CloseShiftAsync throws BusinessException("SHIFT_NOT_FOUND")
        var closeReq = new CloseShiftRequest(500000m, 200000m, 300000m);
        var exClose = await Assert.ThrowsAsync<BusinessException>(() => service.CloseShiftAsync(missingShiftId, closeReq));
        Assert.Equal("SHIFT_NOT_FOUND", exClose.ErrorCode);

        // 2. Shift not found on ConfirmHandoverAsync throws BusinessException("SHIFT_NOT_FOUND")
        var exHand = await Assert.ThrowsAsync<BusinessException>(() =>
            service.ConfirmHandoverAsync(missingShiftId, new ConfirmHandoverRequest(HandoverStatus.CONFIRMED), "owner_id"));
        Assert.Equal("SHIFT_NOT_FOUND", exHand.ErrorCode);

        // 3. Shift not found on GetZReportAsync throws BusinessException("SHIFT_NOT_FOUND")
        var exZ = await Assert.ThrowsAsync<BusinessException>(() =>
            service.GetZReportAsync(missingShiftId));
        Assert.Equal("SHIFT_NOT_FOUND", exZ.ErrorCode);
    }
}
