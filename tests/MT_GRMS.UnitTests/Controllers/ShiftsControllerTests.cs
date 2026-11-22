using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using MT_GRMS.Api.Controllers;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.PosShifts;
using MT_GRMS.Application.PosShifts.Dtos;
using MT_GRMS.Domain.Enums;
using Xunit;

namespace MT_GRMS.UnitTests.Controllers;

public class ShiftsControllerTests
{
    private readonly Mock<IShiftService> _mockShiftService;
    private readonly Mock<ITenantContext> _mockTenant;
    private readonly ShiftsController _controller;
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _branchId = Guid.NewGuid();

    public ShiftsControllerTests()
    {
        _mockShiftService = new Mock<IShiftService>();
        _mockTenant = new Mock<ITenantContext>();
        _mockTenant.SetupGet(t => t.CurrentUserId).Returns(_userId);
        _mockTenant.SetupGet(t => t.CurrentBranchId).Returns(_branchId);
        _mockTenant.SetupGet(t => t.CurrentRole).Returns("CASHIER");

        _controller = new ShiftsController(_mockShiftService.Object, _mockTenant.Object);

        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, _userId.ToString()),
            new Claim(ClaimTypes.Name, "Cashier 01"),
            new Claim(ClaimTypes.Role, "STORE_OWNER")
        }, "TestAuth"));

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };
    }

    private ShiftDetailResponse CreateTestShift(Guid id, ShiftState state = ShiftState.Open)
    {
        return new ShiftDetailResponse(
            Id: id,
            TenantId: "tenant-01",
            BranchId: _branchId,
            BranchName: "Branch 1",
            TillId: Guid.NewGuid(),
            ShiftCode: "SH-001",
            ShiftName: "Ca sang",
            CashierUserId: _userId,
            ReceiverUserId: null,
            PrevShiftId: null,
            OpenedAtUtc: DateTime.UtcNow,
            ClosedAtUtc: null,
            OpeningFloatExpected: 1_000_000m,
            OpeningFloat: 1_000_000m,
            OpeningDiscrepancy: 0m,
            TotalCashIn: 0m,
            TotalCashOut: 0m,
            TotalBankRevenue: 0m,
            TotalOrdersCount: 0,
            ExpectedCash: 1_000_000m,
            ActualCashCounted: null,
            Discrepancy: null,
            RetainedFloatAmount: 1_000_000m,
            DroppedToSafeAmount: 0m,
            HandoverStatus: HandoverStatus.PENDING,
            Status: state,
            Notes: null,
            CashMovements: new List<ShiftCashMovementDto>());
    }

    [Fact]
    public async Task OpenShift_ShouldHandleUnauthorized_AndSuccess()
    {
        var request = new OpenShiftRequest(_branchId, Guid.NewGuid(), "Ca sang", 1_000_000m, null);
        var shift = CreateTestShift(Guid.NewGuid());

        _mockShiftService
            .Setup(s => s.OpenShiftAsync(request, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(shift);

        var res = await _controller.OpenShift(request, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(res.Result);
        var apiRes = Assert.IsType<ApiResponse<ShiftDetailResponse>>(ok.Value);
        Assert.True(apiRes.Success);

        // Test empty user id -> Unauthorized
        _mockTenant.SetupGet(t => t.CurrentUserId).Returns(Guid.Empty);
        var resUnauth = await _controller.OpenShift(request, CancellationToken.None);
        Assert.IsType<UnauthorizedResult>(resUnauth.Result);
    }

    [Fact]
    public async Task GetCurrentShift_ShouldHandleBadRequest_NotFound_AndSuccess()
    {
        _mockShiftService
            .Setup(s => s.ResolveBranchIdAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_branchId);

        var shift = CreateTestShift(Guid.NewGuid());
        _mockShiftService
            .Setup(s => s.GetCurrentShiftAsync(_branchId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(shift);

        var resFound = await _controller.GetCurrentShift(null, null, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(resFound.Result);
        var apiRes = Assert.IsType<ApiResponse<ShiftDetailResponse>>(ok.Value);
        Assert.True(apiRes.Success);

        // NotFound
        _mockShiftService
            .Setup(s => s.GetCurrentShiftAsync(_branchId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ShiftDetailResponse?)null);
        var resNotFound = await _controller.GetCurrentShift(null, null, CancellationToken.None);
        Assert.IsType<NotFoundObjectResult>(resNotFound.Result);
    }

    [Fact]
    public async Task GetShiftById_ShouldHandleFound_AndNotFound()
    {
        var id = Guid.NewGuid();
        var shift = CreateTestShift(id);

        _mockShiftService
            .Setup(s => s.GetShiftByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(shift);

        var res = await _controller.GetShiftById(id, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(res.Result);
        var apiRes = Assert.IsType<ApiResponse<ShiftDetailResponse>>(ok.Value);
        Assert.True(apiRes.Success);

        _mockShiftService
            .Setup(s => s.GetShiftByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ShiftDetailResponse?)null);
        var resNotFound = await _controller.GetShiftById(id, CancellationToken.None);
        Assert.IsType<NotFoundObjectResult>(resNotFound.Result);
    }

    [Fact]
    public async Task CloseShift_ShouldReturnOk()
    {
        var id = Guid.NewGuid();
        var request = new CloseShiftRequest(1_500_000m, 500_000m, 1_000_000m, null, "Chot ca");
        var shift = CreateTestShift(id, ShiftState.Closed);

        _mockShiftService
            .Setup(s => s.CloseShiftAsync(id, request, _userId, CancellationToken.None))
            .ReturnsAsync(shift);

        var res = await _controller.CloseShift(id, request, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(res.Result);
        var apiRes = Assert.IsType<ApiResponse<ShiftDetailResponse>>(ok.Value);
        Assert.True(apiRes.Success);
        Assert.Equal(ShiftState.Closed, apiRes.Data?.Status);

        _mockTenant.SetupGet(t => t.CurrentUserId).Returns(Guid.Empty);
        var unauthorized = await _controller.CloseShift(id, request, CancellationToken.None);
        Assert.IsType<UnauthorizedResult>(unauthorized.Result);
        _mockShiftService.Verify(s => s.CloseShiftAsync(id, request, _userId, CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task ForceCloseShift_ShouldHandleForbid_AndSuccess()
    {
        var id = Guid.NewGuid();
        var request = new ForceCloseShiftRequest(1_000_000m, 500_000m, 500_000m, "Nhan vien ve quen chot ca", null);
        var shift = CreateTestShift(id, ShiftState.Closed);

        _mockShiftService
            .Setup(s => s.ForceCloseShiftAsync(id, _userId, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(shift);

        var res = await _controller.ForceCloseShift(id, request, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(res.Result);
        var apiRes = Assert.IsType<ApiResponse<ShiftDetailResponse>>(ok.Value);
        Assert.True(apiRes.Success);

        // Test non-owner role -> Forbid
        var cashierUser = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Role, "CASHIER")
        }, "TestAuth"));

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = cashierUser }
        };

        var resForbid = await _controller.ForceCloseShift(id, request, CancellationToken.None);
        Assert.IsType<ForbidResult>(resForbid.Result);
    }

    private static ShiftCashMovementDto CreateSampleCashMovement(Guid id, Guid shiftId)
    {
        return new ShiftCashMovementDto(
            Id: id,
            ShiftId: shiftId,
            MovementType: CashMovementType.IN,
            Category: CashMovementCategory.CASH_DEPOSIT,
            Amount: 100_000m,
            Reason: "Nap tien le",
            CreatedByName: "Cashier",
            OccurredAtUtc: DateTime.UtcNow
        );
    }

    [Fact]
    public async Task AddCashMovement_ShouldReturnOk()
    {
        var shiftId = Guid.NewGuid();
        var req = new CashMovementRequest(CashMovementType.IN, CashMovementCategory.CASH_DEPOSIT, 100_000m, "Nap tien le");
        var dto = CreateSampleCashMovement(Guid.NewGuid(), shiftId);

        _mockShiftService
            .Setup(s => s.AddCashMovementAsync(shiftId, req, It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);

        var res = await _controller.AddCashMovement(shiftId, req, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(res.Result);
        var apiRes = Assert.IsType<ApiResponse<ShiftCashMovementDto>>(ok.Value);
        Assert.True(apiRes.Success);
    }

    [Fact]
    public async Task ConfirmHandover_ShouldHandleUnauthorized_AndSuccess()
    {
        var shiftId = Guid.NewGuid();
        var req = new ConfirmHandoverRequest(HandoverStatus.CONFIRMED, "Bàn giao ok");
        var shift = CreateTestShift(shiftId, ShiftState.Closed);

        _mockShiftService
            .Setup(s => s.ConfirmHandoverAsync(shiftId, req, _userId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(shift);

        var res = await _controller.ConfirmHandover(shiftId, req, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(res.Result);
        var apiRes = Assert.IsType<ApiResponse<ShiftDetailResponse>>(ok.Value);
        Assert.True(apiRes.Success);

        // Unauthorized when current user empty
        _mockTenant.SetupGet(t => t.CurrentUserId).Returns(Guid.Empty);
        var resUnauth = await _controller.ConfirmHandover(shiftId, req, CancellationToken.None);
        Assert.IsType<UnauthorizedResult>(resUnauth.Result);
        _mockTenant.SetupGet(t => t.CurrentUserId).Returns(_userId);
    }

    [Fact]
    public async Task GetZReport_ShouldReturnOk()
    {
        var shiftId = Guid.NewGuid();
        var zReport = new ZReportResponse(
            ShiftId: shiftId,
            ShiftCode: "SH-001",
            ShiftName: "Ca sang",
            BranchId: _branchId,
            BranchName: "Branch 1",
            TillId: Guid.NewGuid(),
            OpenedAtUtc: DateTime.UtcNow,
            ClosedAtUtc: DateTime.UtcNow,
            OpeningFloat: 1_000_000m,
            CashSales: 500_000m,
            TotalCashIn: 0m,
            TotalCashOut: 0m,
            TotalBankRevenue: 200_000m,
            TotalOrdersCount: 5,
            ExpectedCashInTill: 1_500_000m,
            ActualCashCounted: 1_500_000m,
            Discrepancy: 0m,
            RetainedFloatAmount: 1_000_000m,
            DroppedToSafeAmount: 500_000m,
            HandoverStatus: HandoverStatus.CONFIRMED,
            ReceiverUserId: null,
            Notes: null,
            CashMovements: new List<ShiftCashMovementDto>()
        );

        _mockShiftService
            .Setup(s => s.GetZReportAsync(shiftId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(zReport);

        var res = await _controller.GetZReport(shiftId, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(res.Result);
        var apiRes = Assert.IsType<ApiResponse<ZReportResponse>>(ok.Value);
        Assert.True(apiRes.Success);
    }

    [Fact]
    public async Task GetShiftHistory_ShouldHandleBadRequest_AndSuccess()
    {
        _mockShiftService
            .Setup(s => s.ResolveBranchIdAsync("BR-01", It.IsAny<CancellationToken>()))
            .ReturnsAsync(_branchId);
        _mockShiftService
            .Setup(s => s.GetShiftHistoryAsync(_branchId, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ShiftDetailResponse> { CreateTestShift(Guid.NewGuid()) });

        var resOk = await _controller.GetShiftHistory("BR-01", null, null, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(resOk.Result);
        var apiRes = Assert.IsType<ApiResponse<IReadOnlyList<ShiftDetailResponse>>>(ok.Value);
        Assert.True(apiRes.Success);

        // BadRequest when branchId cannot be resolved and tenant branch is empty
        _mockShiftService
            .Setup(s => s.ResolveBranchIdAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null);
        _mockTenant.SetupGet(t => t.CurrentBranchId).Returns((Guid?)null);

        var resBad = await _controller.GetShiftHistory(null, null, null, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(resBad.Result);
        _mockTenant.SetupGet(t => t.CurrentBranchId).Returns(_branchId);
    }

    [Fact]
    public async Task ApproveCashMovement_ShouldReturnOk()
    {
        var id = Guid.NewGuid();
        var dto = CreateSampleCashMovement(id, Guid.NewGuid());
        _mockShiftService
            .Setup(s => s.ApproveCashMovementAsync(id, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);

        var res = await _controller.ApproveCashMovement(id, new ApproveCashMovementRequest("Duyet"), CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(res.Result);
        var apiRes = Assert.IsType<ApiResponse<ShiftCashMovementDto>>(ok.Value);
        Assert.True(apiRes.Success);
    }

    [Fact]
    public async Task RejectCashMovement_ShouldReturnOk()
    {
        var id = Guid.NewGuid();
        var dto = CreateSampleCashMovement(id, Guid.NewGuid());
        _mockShiftService
            .Setup(s => s.RejectCashMovementAsync(id, It.IsAny<string>(), "Khong duyet", It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);

        var res = await _controller.RejectCashMovement(id, new RejectCashMovementRequest("Khong duyet"), CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(res.Result);
        var apiRes = Assert.IsType<ApiResponse<ShiftCashMovementDto>>(ok.Value);
        Assert.True(apiRes.Success);
    }

    [Fact]
    public async Task CancelCashMovement_ShouldReturnOk()
    {
        var id = Guid.NewGuid();
        var dto = CreateSampleCashMovement(id, Guid.NewGuid());
        _mockShiftService
            .Setup(s => s.CancelCashMovementAsync(id, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);

        var res = await _controller.CancelCashMovement(id, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(res.Result);
        var apiRes = Assert.IsType<ApiResponse<ShiftCashMovementDto>>(ok.Value);
        Assert.True(apiRes.Success);
    }

    [Fact]
    public async Task ConfirmCashDrop_ShouldReturnOk()
    {
        var id = Guid.NewGuid();
        var dto = CreateSampleCashMovement(id, Guid.NewGuid());
        _mockShiftService
            .Setup(s => s.ConfirmCashDropAsync(id, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);

        var res = await _controller.ConfirmCashDrop(id, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(res.Result);
        var apiRes = Assert.IsType<ApiResponse<ShiftCashMovementDto>>(ok.Value);
        Assert.True(apiRes.Success);
    }

    [Fact]
    public async Task GetPendingMovements_ShouldReturnOk()
    {
        var summary = new PendingMovementsSummaryResponse(1, 0, new List<ShiftCashMovementDto>());
        _mockShiftService
            .Setup(s => s.GetPendingMovementsSummaryAsync(It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(summary);

        var res = await _controller.GetPendingMovements(null, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(res.Result);
        var apiRes = Assert.IsType<ApiResponse<PendingMovementsSummaryResponse>>(ok.Value);
        Assert.True(apiRes.Success);
    }

    [Fact]
    public async Task GetCashMovementSettings_ShouldReturnOk()
    {
        var settings = new CashMovementSettingsDto(500_000m, 2_000_000m);
        _mockShiftService
            .Setup(s => s.GetCashMovementSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(settings);

        var res = await _controller.GetCashMovementSettings(CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(res.Result);
        var apiRes = Assert.IsType<ApiResponse<CashMovementSettingsDto>>(ok.Value);
        Assert.True(apiRes.Success);
    }

    [Fact]
    public async Task UpdateCashMovementSettings_ShouldReturnOk()
    {
        var req = new UpdateCashMovementSettingsRequest(1_000_000m, 3_000_000m);
        var settings = new CashMovementSettingsDto(1_000_000m, 3_000_000m);
        _mockShiftService
            .Setup(s => s.UpdateCashMovementSettingsAsync(req, It.IsAny<CancellationToken>()))
            .ReturnsAsync(settings);

        var res = await _controller.UpdateCashMovementSettings(req, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(res.Result);
        var apiRes = Assert.IsType<ApiResponse<CashMovementSettingsDto>>(ok.Value);
        Assert.True(apiRes.Success);
    }

    [Fact]
    public async Task StartClosingShift_ShouldReturnOk()
    {
        var shiftId = Guid.NewGuid();
        var req = new StartClosingShiftRequest("Start closing");
        var shift = CreateTestShift(shiftId, ShiftState.Closing);

        _mockShiftService
            .Setup(s => s.StartClosingShiftAsync(shiftId, _userId, req, It.IsAny<CancellationToken>()))
            .ReturnsAsync(shift);

        var res = await _controller.StartClosingShift(shiftId, req, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(res.Result);
        var apiRes = Assert.IsType<ApiResponse<ShiftDetailResponse>>(ok.Value);
        Assert.True(apiRes.Success);
    }

    [Fact]
    public async Task CancelClosingShift_ShouldReturnOk()
    {
        var shiftId = Guid.NewGuid();
        var req = new CancelClosingShiftRequest("Resume selling");
        var shift = CreateTestShift(shiftId, ShiftState.Open);

        _mockShiftService
            .Setup(s => s.CancelClosingShiftAsync(shiftId, _userId, req, It.IsAny<CancellationToken>()))
            .ReturnsAsync(shift);

        var res = await _controller.CancelClosingShift(shiftId, req, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(res.Result);
        var apiRes = Assert.IsType<ApiResponse<ShiftDetailResponse>>(ok.Value);
        Assert.True(apiRes.Success);
    }

    [Fact]
    public async Task UpdateCountedCash_ShouldHandleUnauthorized_AndSuccess()
    {
        var shiftId = Guid.NewGuid();
        var req = new UpdateCountedCashRequest(1_500_000m);
        var shift = CreateTestShift(shiftId, ShiftState.Closing);

        _mockShiftService
            .Setup(s => s.UpdateCountedCashAsync(shiftId, _userId, req, It.IsAny<CancellationToken>()))
            .ReturnsAsync(shift);

        var res = await _controller.UpdateCountedCash(shiftId, req, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(res.Result);
        var apiRes = Assert.IsType<ApiResponse<ShiftDetailResponse>>(ok.Value);
        Assert.True(apiRes.Success);

        _mockTenant.SetupGet(t => t.CurrentUserId).Returns(Guid.Empty);
        var resUnauth = await _controller.UpdateCountedCash(shiftId, req, CancellationToken.None);
        Assert.IsType<UnauthorizedResult>(resUnauth.Result);
        _mockTenant.SetupGet(t => t.CurrentUserId).Returns(_userId);
    }

    [Fact]
    public async Task ConfirmCloseShift_ShouldHandleUnauthorized_AndSuccess()
    {
        var shiftId = Guid.NewGuid();
        var req = new ConfirmCloseShiftRequest(1_500_000m, 1_000_000m, 500_000m);
        var shift = CreateTestShift(shiftId, ShiftState.Closed);

        _mockShiftService
            .Setup(s => s.ConfirmCloseShiftAsync(shiftId, _userId, It.IsAny<string>(), req, It.IsAny<CancellationToken>()))
            .ReturnsAsync(shift);

        var res = await _controller.ConfirmCloseShift(shiftId, req, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(res.Result);
        var apiRes = Assert.IsType<ApiResponse<ShiftDetailResponse>>(ok.Value);
        Assert.True(apiRes.Success);

        _mockTenant.SetupGet(t => t.CurrentUserId).Returns(Guid.Empty);
        var resUnauth = await _controller.ConfirmCloseShift(shiftId, req, CancellationToken.None);
        Assert.IsType<UnauthorizedResult>(resUnauth.Result);
        _mockTenant.SetupGet(t => t.CurrentUserId).Returns(_userId);
    }

    [Fact]
    public async Task CheckCashierEligibility_ShouldReturnOk()
    {
        _mockShiftService
            .Setup(s => s.ResolveBranchIdAsync("BR-01", It.IsAny<CancellationToken>()))
            .ReturnsAsync(_branchId);

        var eligibility = new CashierShiftEligibilityDto(true, false, "CASHIER", "Nguyen Van A", null, null);
        _mockShiftService
            .Setup(s => s.CheckCashierShiftEligibilityAsync(_branchId, _userId, "CASHIER", It.IsAny<CancellationToken>()))
            .ReturnsAsync(eligibility);

        var res = await _controller.CheckCashierEligibility("BR-01", CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(res.Result);
        var apiRes = Assert.IsType<ApiResponse<CashierShiftEligibilityDto>>(ok.Value);
        Assert.True(apiRes.Success);
    }
}
