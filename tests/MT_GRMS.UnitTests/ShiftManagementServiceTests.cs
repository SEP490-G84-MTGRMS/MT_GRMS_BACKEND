using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.PosShifts;
using MT_GRMS.Application.PosShifts.Dtos;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Domain.Exceptions;
using Xunit;

namespace MT_GRMS.UnitTests;

public class ShiftManagementServiceTests
{
    private class FakeTenantContext : ITenantContext
    {
        public string CurrentTenantId { get; set; } = "tenant_test_mart";
        public Guid CurrentUserId { get; set; } = Guid.Parse("11111111-1111-1111-1111-111111111111");
        public Guid? CurrentBranchId { get; set; } = Guid.Parse("22222222-2222-2222-2222-222222222222");
        public string CurrentRole { get; set; } = "STAFF";

        public void SetContext(string tenantId, Guid userId, Guid? branchId)
        {
            CurrentTenantId = tenantId;
            CurrentUserId = userId;
            CurrentBranchId = branchId;
        }

        public void SetRole(string? role) => CurrentRole = role?.Trim() ?? string.Empty;
    }

    private class FakeUnitOfWork : IUnitOfWork
    {
        public int SaveChangesCount { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveChangesCount++;
            return Task.FromResult(1);
        }
    }

    private class FakeShiftRepository : IShiftRepository
    {
        public List<ShiftZReport> Shifts { get; } = new();
        public List<ShiftCashMovement> CashMovements { get; } = new();

        public decimal MockCashSales { get; set; } = 0m;
        public decimal MockBankRevenue { get; set; } = 0m;
        public int MockOrdersCount { get; set; } = 0;

        public Task<ShiftZReport?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var shift = Shifts.FirstOrDefault(s => s.Id == id);
            return Task.FromResult(shift);
        }

        public Task<ShiftZReport?> GetActiveShiftAsync(Guid branchId, Guid? tillId = null, CancellationToken cancellationToken = default)
        {
            var shift = Shifts.FirstOrDefault(s => s.BranchId == branchId && s.Status == ShiftState.Open &&
                (!tillId.HasValue || tillId.Value == Guid.Empty || s.TillId == tillId.Value));
            return Task.FromResult(shift);
        }

        public Task<ShiftZReport?> GetLatestClosedShiftAsync(Guid branchId, Guid? tillId = null, CancellationToken cancellationToken = default)
        {
            var shift = Shifts
                .Where(s => s.BranchId == branchId && s.Status == ShiftState.Closed &&
                    (!tillId.HasValue || tillId.Value == Guid.Empty || s.TillId == tillId.Value))
                .OrderByDescending(s => s.ClosedAtUtc ?? s.OpenedAtUtc)
                .FirstOrDefault();
            return Task.FromResult(shift);
        }

        public Task<IReadOnlyList<ShiftZReport>> GetHistoryAsync(Guid branchId, DateTime? fromUtc = null, DateTime? toUtc = null, CancellationToken cancellationToken = default)
        {
            var list = Shifts.Where(s => s.BranchId == branchId).OrderByDescending(s => s.OpenedAtUtc).ToList();
            return Task.FromResult<IReadOnlyList<ShiftZReport>>(list);
        }

        public Task AddShiftAsync(ShiftZReport shift, CancellationToken cancellationToken = default)
        {
            if (shift.Id == Guid.Empty) shift.Id = Guid.NewGuid();
            Shifts.Add(shift);
            return Task.CompletedTask;
        }

        public Task AddCashMovementAsync(ShiftCashMovement movement, CancellationToken cancellationToken = default)
        {
            if (movement.Id == Guid.Empty) movement.Id = Guid.NewGuid();
            CashMovements.Add(movement);
            var shift = Shifts.FirstOrDefault(s => s.Id == movement.ShiftId);
            shift?.CashMovements.Add(movement);
            return Task.CompletedTask;
        }

        public Task<(decimal CashSales, decimal BankRevenue, int OrdersCount)> GetShiftSalesMetricsAsync(Guid shiftId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult((MockCashSales, MockBankRevenue, MockOrdersCount));
        }

        public Task<string> GenerateNextShiftCodeAsync(Guid branchId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult($"CA-{DateTime.UtcNow:yyyyMMdd}-{Shifts.Count + 1:D2}");
        }
    }

    private readonly FakeTenantContext _tenantContext;
    private readonly FakeUnitOfWork _unitOfWork;
    private readonly FakeShiftRepository _shiftRepository;
    private readonly ShiftService _service;

    public ShiftManagementServiceTests()
    {
        _tenantContext = new FakeTenantContext();
        _unitOfWork = new FakeUnitOfWork();
        _shiftRepository = new FakeShiftRepository();
        _service = new ShiftService(_shiftRepository, _unitOfWork, _tenantContext);
    }

    [Fact]
    public async Task OpenShift_WhenNoPreviousShift_UsesRequestedOpeningFloat()
    {
        // Arrange
        var branchId = Guid.NewGuid();
        var tillId = Guid.NewGuid();
        var request = new OpenShiftRequest(branchId, tillId, "Ca Sáng Thứ 2", 1_500_000m, "Bắt đầu ngày mới");

        // Act
        var result = await _service.OpenShiftAsync(request, _tenantContext.CurrentUserId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1_500_000m, result.OpeningFloat);
        Assert.Equal(1_500_000m, result.OpeningFloatExpected);
        Assert.Equal(0m, result.OpeningDiscrepancy);
        Assert.Equal(ShiftState.Open, result.Status);
        Assert.Equal(HandoverStatus.PENDING, result.HandoverStatus);
        Assert.Null(result.PrevShiftId);
        Assert.StartsWith("CA-", result.ShiftCode);
        Assert.Equal(1, _unitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task OpenShift_WhenPreviousClosedShiftExists_ChainsOpeningFloatExpectedAndTracksDiscrepancy()
    {
        // Arrange
        var branchId = Guid.NewGuid();
        var tillId = Guid.NewGuid();

        var prevShift = new ShiftZReport
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.CurrentTenantId,
            BranchId = branchId,
            TillId = tillId,
            ShiftCode = "CA-20261105-01",
            ShiftName = "Ca Tối Hôm Qua",
            Status = ShiftState.Closed,
            RetainedFloatAmount = 2_000_000m, // Ca trước để lại 2 triệu
            ClosedAtUtc = DateTime.UtcNow.AddHours(-10)
        };
        _shiftRepository.Shifts.Add(prevShift);

        // Thu ngân ca sau đếm thực tế chỉ thấy 1.950.000đ (thiếu 50.000đ)
        var request = new OpenShiftRequest(branchId, tillId, "Ca Sáng Hôm Nay", 1_950_000m, "Kiểm két thấy thiếu 50k");

        // Act
        var result = await _service.OpenShiftAsync(request, _tenantContext.CurrentUserId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(prevShift.Id, result.PrevShiftId);
        Assert.Equal(2_000_000m, result.OpeningFloatExpected);
        Assert.Equal(1_950_000m, result.OpeningFloat);
        Assert.Equal(-50_000m, result.OpeningDiscrepancy);
        Assert.Equal(ShiftState.Open, result.Status);
    }

    [Fact]
    public async Task OpenShift_WhenActiveShiftAlreadyOpen_ThrowsBusinessException()
    {
        // Arrange
        var branchId = Guid.NewGuid();
        var tillId = Guid.NewGuid();

        _shiftRepository.Shifts.Add(new ShiftZReport
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.CurrentTenantId,
            BranchId = branchId,
            TillId = tillId,
            ShiftCode = "CA-20261106-01",
            ShiftName = "Ca Đang Chạy",
            Status = ShiftState.Open
        });

        var request = new OpenShiftRequest(branchId, tillId, "Ca Mới Cố Mở Trùng", 1_000_000m);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BusinessException>(() => _service.OpenShiftAsync(request, _tenantContext.CurrentUserId));
        Assert.Equal("SHIFT_ALREADY_OPEN", ex.ErrorCode);
    }

    [Fact]
    public async Task OpenShift_WhenNegativeOpeningFloat_ThrowsBusinessException()
    {
        // Arrange
        var request = new OpenShiftRequest(Guid.NewGuid(), Guid.NewGuid(), "Ca Âm", -500_000m);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BusinessException>(() => _service.OpenShiftAsync(request, _tenantContext.CurrentUserId));
        Assert.Equal("INVALID_OPENING_FLOAT", ex.ErrorCode);
    }

    [Fact]
    public async Task AddCashMovement_CashIn_IncreasesTotalCashInAndExpectedCash()
    {
        // Arrange
        var shiftId = Guid.NewGuid();
        var shift = new ShiftZReport
        {
            Id = shiftId,
            TenantId = _tenantContext.CurrentTenantId,
            OpeningFloat = 1_000_000m,
            ExpectedCash = 1_000_000m,
            Status = ShiftState.Open
        };
        _shiftRepository.Shifts.Add(shift);

        var request = new CashMovementRequest(
            MovementType: CashMovementType.IN,
            Category: CashMovementCategory.CASH_DEPOSIT,
            Amount: 300_000m,
            Reason: "Đổi tiền lẻ nạp thêm két",
            CreatedByName: "Thu ngân Yến"
        );

        // Act
        var result = await _service.AddCashMovementAsync(shiftId, request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(CashMovementType.IN, result.MovementType);
        Assert.Equal(300_000m, result.Amount);
        Assert.Equal(300_000m, shift.TotalCashIn);
        Assert.Equal(0m, shift.TotalCashOut);
        Assert.Equal(1_300_000m, shift.ExpectedCash);
    }

    [Fact]
    public async Task AddCashMovement_CashOut_IncreasesTotalCashOutAndDecreasesExpectedCash()
    {
        // Arrange
        var shiftId = Guid.NewGuid();
        var shift = new ShiftZReport
        {
            Id = shiftId,
            TenantId = _tenantContext.CurrentTenantId,
            OpeningFloat = 1_000_000m,
            ExpectedCash = 1_000_000m,
            Status = ShiftState.Open
        };
        _shiftRepository.Shifts.Add(shift);

        var request = new CashMovementRequest(
            MovementType: CashMovementType.OUT,
            Category: CashMovementCategory.EXPENSE,
            Amount: 150_000m,
            Reason: "Mua túi bóng và nước rửa tay",
            CreatedByName: "Thu ngân Yến"
        );

        // Act
        var result = await _service.AddCashMovementAsync(shiftId, request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(CashMovementType.OUT, result.MovementType);
        Assert.Equal(150_000m, result.Amount);
        Assert.Equal(150_000m, shift.TotalCashOut);
        Assert.Equal(0m, shift.TotalCashIn);
        Assert.Equal(850_000m, shift.ExpectedCash);
    }

    [Fact]
    public async Task AddCashMovement_WhenShiftClosed_ThrowsBusinessException()
    {
        // Arrange
        var shiftId = Guid.NewGuid();
        _shiftRepository.Shifts.Add(new ShiftZReport
        {
            Id = shiftId,
            TenantId = _tenantContext.CurrentTenantId,
            Status = ShiftState.Closed
        });

        var request = new CashMovementRequest(CashMovementType.IN, CashMovementCategory.CASH_DEPOSIT, 100_000m, "Nạp muộn");

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BusinessException>(() => _service.AddCashMovementAsync(shiftId, request));
        Assert.Equal("SHIFT_NOT_OPEN", ex.ErrorCode);
    }

    [Fact]
    public async Task CloseShift_WhenCashAllocationDoesNotMatchActualCashCounted_ThrowsBusinessException()
    {
        // Arrange
        var shiftId = Guid.NewGuid();
        _shiftRepository.Shifts.Add(new ShiftZReport
        {
            Id = shiftId,
            TenantId = _tenantContext.CurrentTenantId,
            OpeningFloat = 1_000_000m,
            Status = ShiftState.Open
        });

        // Actual đếm được 3.000.000đ nhưng Retained (1.000.000) + Dropped (1.500.000) = 2.500.000đ (lệch 500k)
        var request = new CloseShiftRequest(
            ActualCashCounted: 3_000_000m,
            RetainedFloatAmount: 1_000_000m,
            DroppedToSafeAmount: 1_500_000m
        );

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BusinessException>(() => _service.CloseShiftAsync(shiftId, request));
        Assert.Equal("INVALID_CASH_ALLOCATION", ex.ErrorCode);
    }

    [Fact]
    public async Task CloseShift_CalculatesExpectedCashAndDiscrepancyAccurately()
    {
        // Arrange
        var shiftId = Guid.NewGuid();
        var shift = new ShiftZReport
        {
            Id = shiftId,
            TenantId = _tenantContext.CurrentTenantId,
            OpeningFloat = 1_000_000m,
            TotalCashIn = 200_000m,
            TotalCashOut = 100_000m,
            Status = ShiftState.Open
        };
        _shiftRepository.Shifts.Add(shift);

        // Giả lập doanh số bán hàng trong ca
        _shiftRepository.MockCashSales = 2_500_000m;
        _shiftRepository.MockBankRevenue = 1_200_000m;
        _shiftRepository.MockOrdersCount = 8;

        // Tiền lý thuyết G = Opening (1M) + CashSales (2.5M) + CashIn (200k) - CashOut (100k) = 3.600.000đ
        // Tiền thực tế đếm được H = 3.650.000đ (thừa 50k)
        // Phân bổ: Giữ lại 1M, nộp két sắt 2.65M -> Tổng 3.65M = H (Hợp lệ)
        var request = new CloseShiftRequest(
            ActualCashCounted: 3_650_000m,
            RetainedFloatAmount: 1_000_000m,
            DroppedToSafeAmount: 2_650_000m,
            ReceiverUserId: "usr_next_cashier",
            Notes: "Kiểm két kỹ càng, thừa 50k tiền lẻ"
        );

        // Act
        var result = await _service.CloseShiftAsync(shiftId, request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(ShiftState.Closed, result.Status);
        Assert.Equal(3_600_000m, result.ExpectedCash);
        Assert.Equal(3_650_000m, result.ActualCashCounted);
        Assert.Equal(50_000m, result.Discrepancy); // H - G = +50.000đ
        Assert.Equal(1_000_000m, result.RetainedFloatAmount);
        Assert.Equal(2_650_000m, result.DroppedToSafeAmount);
        Assert.Equal(1_200_000m, result.TotalBankRevenue);
        Assert.Equal(8, result.TotalOrdersCount);
        Assert.NotNull(result.ClosedAtUtc);
    }

    [Fact]
    public async Task ConfirmHandover_Confirmed_SetsStatusConfirmed()
    {
        // Arrange
        var shiftId = Guid.NewGuid();
        _shiftRepository.Shifts.Add(new ShiftZReport
        {
            Id = shiftId,
            TenantId = _tenantContext.CurrentTenantId,
            Status = ShiftState.Closed,
            HandoverStatus = HandoverStatus.PENDING
        });

        var request = new ConfirmHandoverRequest(HandoverStatus.CONFIRMED);

        // Act
        var result = await _service.ConfirmHandoverAsync(shiftId, request, "cashier_nam");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(HandoverStatus.CONFIRMED, result.HandoverStatus);
        Assert.Equal("cashier_nam", result.ReceiverUserId);
    }

    [Fact]
    public async Task ConfirmHandover_Disputed_SetsStatusDisputedAndAppendsReason()
    {
        // Arrange
        var shiftId = Guid.NewGuid();
        _shiftRepository.Shifts.Add(new ShiftZReport
        {
            Id = shiftId,
            TenantId = _tenantContext.CurrentTenantId,
            Status = ShiftState.Closed,
            HandoverStatus = HandoverStatus.PENDING,
            Notes = "Giao ca lúc 14h"
        });

        var request = new ConfirmHandoverRequest(
            HandoverStatus.DISPUTED,
            DisputeReason: "Két thực tế thiếu 50,000đ so với số tiền ghi trên biên bản"
        );

        // Act
        var result = await _service.ConfirmHandoverAsync(shiftId, request, "cashier_nam");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(HandoverStatus.DISPUTED, result.HandoverStatus);
        Assert.Contains("[KHIẾU NẠI BÀN GIAO]", result.Notes);
        Assert.Contains("thiếu 50,000đ", result.Notes);
    }

    [Fact]
    public async Task GetZReport_ReturnsAccurateReportSummary()
    {
        // Arrange
        var shiftId = Guid.NewGuid();
        var shift = new ShiftZReport
        {
            Id = shiftId,
            TenantId = _tenantContext.CurrentTenantId,
            ShiftCode = "CA-20261106-01",
            ShiftName = "Ca Sáng",
            OpeningFloat = 1_500_000m,
            TotalCashIn = 300_000m,
            TotalCashOut = 100_000m,
            ExpectedCash = 3_700_000m,
            ActualCashCounted = 3_700_000m,
            Discrepancy = 0m,
            RetainedFloatAmount = 1_500_000m,
            DroppedToSafeAmount = 2_200_000m,
            HandoverStatus = HandoverStatus.CONFIRMED,
            Status = ShiftState.Closed
        };
        shift.CashMovements.Add(new ShiftCashMovement
        {
            Id = Guid.NewGuid(),
            ShiftId = shiftId,
            MovementType = CashMovementType.IN,
            Category = CashMovementCategory.CASH_DEPOSIT,
            Amount = 300_000m,
            Reason = "Nạp tiền lẻ",
            OccurredAtUtc = DateTime.UtcNow
        });
        _shiftRepository.Shifts.Add(shift);
        _shiftRepository.MockCashSales = 2_000_000m;
        _shiftRepository.MockBankRevenue = 1_500_000m;
        _shiftRepository.MockOrdersCount = 10;

        // Act
        var report = await _service.GetZReportAsync(shiftId);

        // Assert
        Assert.NotNull(report);
        Assert.Equal(shiftId, report.ShiftId);
        Assert.Equal("CA-20261106-01", report.ShiftCode);
        Assert.Equal(1_500_000m, report.OpeningFloat);
        Assert.Equal(2_000_000m, report.CashSales);
        Assert.Equal(1_500_000m, report.TotalBankRevenue);
        Assert.Equal(10, report.TotalOrdersCount);
        Assert.Equal(300_000m, report.TotalCashIn);
        Assert.Equal(100_000m, report.TotalCashOut);
        Assert.Equal(3_700_000m, report.ExpectedCashInTill);
        Assert.Equal(3_700_000m, report.ActualCashCounted);
        Assert.Equal(0m, report.Discrepancy);
        Assert.Single(report.CashMovements);
    }
}

