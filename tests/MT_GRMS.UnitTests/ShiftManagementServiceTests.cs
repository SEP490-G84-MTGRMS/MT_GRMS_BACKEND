using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.PosShifts;
using MT_GRMS.Application.PosShifts.Dtos;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Entities.HR;
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
            var shift = Shifts.FirstOrDefault(s => s.BranchId == branchId && (s.Status == ShiftState.Open || s.Status == ShiftState.Closing) &&
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

        public StorePaymentConfig? PaymentConfig { get; set; }

        public Task<ShiftCashMovement?> GetCashMovementByIdAsync(Guid movementId, CancellationToken cancellationToken = default)
        {
            var movement = CashMovements.FirstOrDefault(m => m.Id == movementId);
            if (movement != null && movement.Shift == null)
            {
                movement.Shift = Shifts.FirstOrDefault(s => s.Id == movement.ShiftId);
            }
            return Task.FromResult(movement);
        }

        public Task<IReadOnlyList<ShiftCashMovement>> GetPendingMovementsAsync(Guid? branchId, CancellationToken cancellationToken = default)
        {
            var list = CashMovements
                .Where(m => (m.Status == CashMovementStatus.PendingApproval || (m.Category == CashMovementCategory.CASH_DRAW && !m.IsConfirmedByOwner)))
                .ToList();
            return Task.FromResult<IReadOnlyList<ShiftCashMovement>>(list);
        }

        public Task<StorePaymentConfig?> GetPaymentConfigAsync(string tenantId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(PaymentConfig);
        }

        public Task SavePaymentConfigAsync(StorePaymentConfig config, CancellationToken cancellationToken = default)
        {
            PaymentConfig = config;
            return Task.CompletedTask;
        }

        public List<ShiftParticipant> Participants { get; } = new();
        public List<ShiftCloseConfirmation> CloseConfirmations { get; } = new();

        public Task AddParticipantAsync(ShiftParticipant participant, CancellationToken cancellationToken = default)
        {
            if (participant.Id == Guid.Empty) participant.Id = Guid.NewGuid();
            Participants.Add(participant);
            var shift = Shifts.FirstOrDefault(s => s.Id == participant.ShiftId);
            shift?.Participants.Add(participant);
            return Task.CompletedTask;
        }

        public Task<ShiftParticipant?> GetParticipantAsync(Guid shiftId, Guid userId, CancellationToken cancellationToken = default)
        {
            var p = Participants.FirstOrDefault(x => x.ShiftId == shiftId && x.UserId == userId);
            return Task.FromResult(p);
        }

        public Task AddCloseConfirmationAsync(ShiftCloseConfirmation confirmation, CancellationToken cancellationToken = default)
        {
            if (confirmation.Id == Guid.Empty) confirmation.Id = Guid.NewGuid();
            CloseConfirmations.Add(confirmation);
            var shift = Shifts.FirstOrDefault(s => s.Id == confirmation.ShiftId);
            shift?.CloseConfirmations.Add(confirmation);
            return Task.CompletedTask;
        }

        public Task ClearCloseConfirmationsAsync(Guid shiftId, CancellationToken cancellationToken = default)
        {
            CloseConfirmations.RemoveAll(c => c.ShiftId == shiftId);
            var shift = Shifts.FirstOrDefault(s => s.Id == shiftId);
            shift?.CloseConfirmations.Clear();
            return Task.CompletedTask;
        }

        public Task<Guid?> ResolveBranchIdAsync(string? branchIdOrCode, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(branchIdOrCode))
            {
                return Task.FromResult<Guid?>(Guid.NewGuid());
            }
            if (Guid.TryParse(branchIdOrCode, out var g) && g != Guid.Empty)
            {
                return Task.FromResult<Guid?>(g);
            }
            return Task.FromResult<Guid?>(Guid.NewGuid());
        }

        public List<Employee> Employees { get; } = new();
        public List<EmployeeSchedule> Schedules { get; } = new();
        public bool AutoProvideValidSchedule { get; set; } = true;

        public Task<Employee?> GetEmployeeByUserIdAsync(string userId, CancellationToken cancellationToken = default)
        {
            var emp = Employees.FirstOrDefault(e => e.UserId == userId || e.Id.ToString() == userId);
            if (emp == null && AutoProvideValidSchedule)
            {
                emp = new Employee
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    FullName = "Test Cashier",
                    Status = EmployeeStatus.Active
                };
            }
            return Task.FromResult(emp);
        }

        public Task<IReadOnlyList<EmployeeSchedule>> GetCashierSchedulesAsync(Guid employeeId, Guid branchId, DateTime workDate, CancellationToken cancellationToken = default)
        {
            var matched = Schedules.Where(s => s.EmployeeId == employeeId).ToList();
            if (matched.Count == 0 && AutoProvideValidSchedule)
            {
                matched.Add(new EmployeeSchedule
                {
                    EmployeeId = employeeId,
                    BranchId = branchId,
                    WorkDate = workDate.Date,
                    PlannedStartTime = TimeSpan.Zero,
                    PlannedEndTime = new TimeSpan(23, 59, 59),
                    AllowedCheckInStart = TimeSpan.Zero,
                    AllowedCheckInEnd = new TimeSpan(23, 59, 59),
                    Status = ScheduleStatus.Scheduled
                });
            }
            return Task.FromResult<IReadOnlyList<EmployeeSchedule>>(matched);
        }

        public Task<IReadOnlyList<EmployeeSchedule>> GetBranchCashierSchedulesAsync(Guid branchId, DateTime workDate, CancellationToken cancellationToken = default)
        {
            var matched = Schedules.Where(s => s.BranchId == branchId && s.WorkDate.Date == workDate.Date).ToList();
            return Task.FromResult<IReadOnlyList<EmployeeSchedule>>(matched);
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
    public async Task AddCashMovement_DerivesMovementTypeFromCategoryInsteadOfTrustingClient()
    {
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

        // Client gửi sai OUT nhưng CASH_DEPOSIT bắt buộc phải là IN.
        var request = new CashMovementRequest(
            CashMovementType.OUT,
            CashMovementCategory.CASH_DEPOSIT,
            200_000m,
            "Nạp tiền lẻ");

        var result = await _service.AddCashMovementAsync(shiftId, request);

        Assert.Equal(CashMovementType.IN, result.MovementType);
        Assert.Equal(200_000m, shift.TotalCashIn);
        Assert.Equal(0m, shift.TotalCashOut);
        Assert.Equal(1_200_000m, shift.ExpectedCash);
    }

    [Fact]
    public async Task AddCashMovement_DebtCollectionCannotBeCreatedManually()
    {
        var request = new CashMovementRequest(
            CashMovementType.IN,
            CashMovementCategory.DEBT_COLLECT,
            200_000m,
            "Thu nợ khách hàng");

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.AddCashMovementAsync(Guid.NewGuid(), request));

        Assert.Equal("INVALID_MANUAL_CASH_CATEGORY", ex.ErrorCode);
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
    public async Task AddCashMovement_WhenExpenseExceedsDrawerCash_ThrowsBusinessException()
    {
        // Arrange
        var shiftId = Guid.NewGuid();
        var shift = new ShiftZReport
        {
            Id = shiftId,
            TenantId = _tenantContext.CurrentTenantId,
            OpeningFloat = 200_000m,
            ExpectedCash = 200_000m,
            Status = ShiftState.Open
        };
        _shiftRepository.Shifts.Add(shift);

        var request = new CashMovementRequest(
            MovementType: CashMovementType.OUT,
            Category: CashMovementCategory.EXPENSE,
            Amount: 500_000m, // Vượt quá 200.000 đ
            Reason: "Chi quá tiền két",
            CreatedByName: "Thu ngân A"
        );

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BusinessException>(() => _service.AddCashMovementAsync(shiftId, request));
        Assert.Equal("EXCEED_DRAWER_CASH", ex.ErrorCode);
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
        _tenantContext.CurrentRole = "STORE_OWNER";
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
        _tenantContext.CurrentRole = "STORE_OWNER";
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

    [Fact]
    public async Task AddCashMovement_WhenExpenseExceedsThreshold_SetsStatusPendingApprovalAndDoesNotAffectExpectedCash()
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

        // Act: Chi 350.000đ (vượt hạn mức mặc định 200.000đ)
        var request = new CashMovementRequest(
            CashMovementType.OUT,
            CashMovementCategory.EXPENSE,
            350_000m,
            "Sửa bóng đèn quầy thu ngân",
            "Thu ngân A"
        );
        var result = await _service.AddCashMovementAsync(shiftId, request);

        // Assert
        Assert.Equal(CashMovementStatus.PendingApproval, result.Status);
        Assert.Equal(0m, shift.TotalCashOut);
        Assert.Equal(1_000_000m, shift.ExpectedCash); // Chưa bị trừ
    }

    [Fact]
    public async Task AddCashMovement_WhenExpenseWithinThreshold_SetsStatusRecordedAndReducesExpectedCash()
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

        // Act: Chi 150.000đ (nhỏ hơn hạn mức mặc định 200.000đ)
        var request = new CashMovementRequest(
            CashMovementType.OUT,
            CashMovementCategory.EXPENSE,
            150_000m,
            "Mua giấy in bill",
            "Thu ngân A"
        );
        var result = await _service.AddCashMovementAsync(shiftId, request);

        // Assert
        Assert.Equal(CashMovementStatus.Recorded, result.Status);
        Assert.Equal(150_000m, shift.TotalCashOut);
        Assert.Equal(850_000m, shift.ExpectedCash); // Đã trừ
    }

    [Fact]
    public async Task ApproveCashMovement_WhenPending_UpdatesStatusToRecordedAndDeductsCash()
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

        var movementId = Guid.NewGuid();
        var movement = new ShiftCashMovement
        {
            Id = movementId,
            ShiftId = shiftId,
            Shift = shift,
            MovementType = CashMovementType.OUT,
            Category = CashMovementCategory.EXPENSE,
            Amount = 350_000m,
            Reason = "Sửa đèn",
            Status = CashMovementStatus.PendingApproval
        };
        _shiftRepository.CashMovements.Add(movement);

        // Act
        var result = await _service.ApproveCashMovementAsync(movementId, "Chủ shop Nguyễn Văn A");

        // Assert
        Assert.Equal(CashMovementStatus.Recorded, result.Status);
        Assert.Equal("Chủ shop Nguyễn Văn A", result.ApprovedBy);
        Assert.Equal(350_000m, shift.TotalCashOut);
        Assert.Equal(650_000m, shift.ExpectedCash);
    }

    [Fact]
    public async Task RejectCashMovement_WhenPending_UpdatesStatusToRejectedAndKeepsReason()
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

        var movementId = Guid.NewGuid();
        var movement = new ShiftCashMovement
        {
            Id = movementId,
            ShiftId = shiftId,
            Shift = shift,
            MovementType = CashMovementType.OUT,
            Category = CashMovementCategory.EXPENSE,
            Amount = 500_000m,
            Reason = "Mua đồ không rõ nguồn gốc",
            Status = CashMovementStatus.PendingApproval
        };
        _shiftRepository.CashMovements.Add(movement);

        // Act
        var result = await _service.RejectCashMovementAsync(movementId, "Chủ Shop", "Chi không hợp lệ");

        // Assert
        Assert.Equal(CashMovementStatus.Rejected, result.Status);
        Assert.Equal("Chi không hợp lệ", result.RejectionReason);
        Assert.Equal(0m, shift.TotalCashOut); // Không trừ két
    }

    [Fact]
    public async Task CloseShift_WhenShiftHasPendingCashMovement_ThrowsBusinessException()
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
        shift.CashMovements.Add(new ShiftCashMovement
        {
            Id = Guid.NewGuid(),
            ShiftId = shiftId,
            MovementType = CashMovementType.OUT,
            Category = CashMovementCategory.EXPENSE,
            Amount = 400_000m,
            Reason = "Sửa máy in",
            Status = CashMovementStatus.PendingApproval
        });
        _shiftRepository.Shifts.Add(shift);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.CloseShiftAsync(shiftId, new CloseShiftRequest(1_000_000m, 500_000m, 500_000m)));

        Assert.Equal("PENDING_CASH_MOVEMENTS", ex.ErrorCode);
    }

    [Fact]
    public async Task OpenShift_WhenActiveShiftAlreadyExists_ThrowsShiftAlreadyOpen()
    {
        // Arrange (REQ-SHIFT-SHARED-01)
        var branchId = Guid.NewGuid();
        _shiftRepository.Shifts.Add(new ShiftZReport
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.CurrentTenantId,
            BranchId = branchId,
            ShiftCode = "CA-20261107-01",
            ShiftName = "Ca Sáng",
            Status = ShiftState.Open
        });

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.OpenShiftAsync(new OpenShiftRequest(branchId, Guid.Empty, "Ca Trưa", 1_500_000m), Guid.NewGuid()));

        Assert.Equal("SHIFT_ALREADY_OPEN", ex.ErrorCode);
    }

    [Fact]
    public async Task StartClosingShift_WhenShiftIsOpen_SetsStatusToClosing_AndRecordsClosingUser()
    {
        // Arrange (REQ-SHIFT-SHARED-03)
        var shiftId = Guid.NewGuid();
        var closingUserId = Guid.NewGuid();
        var shift = new ShiftZReport
        {
            Id = shiftId,
            TenantId = _tenantContext.CurrentTenantId,
            CashierUserId = closingUserId,
            OpeningFloat = 2_000_000m,
            ExpectedCash = 2_000_000m,
            Status = ShiftState.Open
        };
        _shiftRepository.Shifts.Add(shift);

        // Act
        var result = await _service.StartClosingShiftAsync(shiftId, closingUserId, new StartClosingShiftRequest("Kiểm đếm chốt ca"));

        // Assert
        Assert.Equal(ShiftState.Closing, result.Status);
        Assert.Equal(closingUserId, result.ClosingStartedByUserId);
        Assert.NotNull(result.ClosingStartedAtUtc);
    }

    [Fact]
    public async Task StartClosingShift_WhenShiftNotOpen_ThrowsInvalidShiftState()
    {
        // Arrange
        var shiftId = Guid.NewGuid();
        var shift = new ShiftZReport
        {
            Id = shiftId,
            TenantId = _tenantContext.CurrentTenantId,
            Status = ShiftState.Closing
        };
        _shiftRepository.Shifts.Add(shift);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.StartClosingShiftAsync(shiftId, Guid.NewGuid(), new StartClosingShiftRequest()));

        Assert.Equal("INVALID_SHIFT_STATE", ex.ErrorCode);
    }

    [Fact]
    public async Task CancelClosingShift_WhenShiftIsClosing_RevertsToOpen_AndClearsClosingInfo()
    {
        // Arrange (REQ-SHIFT-SHARED-03)
        var shiftId = Guid.NewGuid();
        var cashierUserId = Guid.NewGuid();
        var shift = new ShiftZReport
        {
            Id = shiftId,
            TenantId = _tenantContext.CurrentTenantId,
            CashierUserId = cashierUserId,
            Status = ShiftState.Closing,
            ClosingStartedByUserId = Guid.NewGuid(),
            ClosingStartedAtUtc = DateTime.UtcNow,
            ActualCashCounted = 3_000_000m,
            Discrepancy = 1_000_000m
        };
        _shiftRepository.Shifts.Add(shift);

        // Act
        var result = await _service.CancelClosingShiftAsync(shiftId, cashierUserId, new CancelClosingShiftRequest("Có khách gấp cần bán"));

        // Assert
        Assert.Equal(ShiftState.Open, result.Status);
        Assert.Null(result.ClosingStartedByUserId);
        Assert.Null(result.ClosingStartedAtUtc);
        Assert.Null(result.ActualCashCounted);
    }

    [Fact]
    public async Task UpdateCountedCash_WhenShiftIsClosing_UpdatesCountedCashAndDiscrepancy()
    {
        // Arrange (REQ-SHIFT-SHARED-04)
        var shiftId = Guid.NewGuid();
        var cashierUserId = Guid.NewGuid();
        var shift = new ShiftZReport
        {
            Id = shiftId,
            TenantId = _tenantContext.CurrentTenantId,
            CashierUserId = cashierUserId,
            OpeningFloat = 2_000_000m,
            ExpectedCash = 2_000_000m,
            Status = ShiftState.Closing
        };
        _shiftRepository.Shifts.Add(shift);

        // Act
        var result = await _service.UpdateCountedCashAsync(shiftId, cashierUserId, new UpdateCountedCashRequest(2_200_000m));

        // Assert
        Assert.Equal(2_200_000m, result.ActualCashCounted);
        Assert.Equal(200_000m, result.Discrepancy);
    }

    [Fact]
    public async Task ConfirmCloseShift_WhenShiftHasPendingExpense_ThrowsPendingCashMovements()
    {
        // Arrange (REQ-SHIFT-SHARED-04)
        var shiftId = Guid.NewGuid();
        var cashierUserId = Guid.NewGuid();
        var shift = new ShiftZReport
        {
            Id = shiftId,
            TenantId = _tenantContext.CurrentTenantId,
            OpeningFloat = 1_000_000m,
            ExpectedCash = 1_000_000m,
            Status = ShiftState.Closing
            ,CashierUserId = cashierUserId
        };
        shift.CashMovements.Add(new ShiftCashMovement
        {
            Id = Guid.NewGuid(),
            ShiftId = shiftId,
            MovementType = CashMovementType.OUT,
            Category = CashMovementCategory.EXPENSE,
            Amount = 300_000m,
            Reason = "Chi mua rơm cuộn",
            Status = CashMovementStatus.PendingApproval
        });
        _shiftRepository.Shifts.Add(shift);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.ConfirmCloseShiftAsync(shiftId, cashierUserId, "Thu ngân A", new ConfirmCloseShiftRequest(1_000_000m, 500_000m, 500_000m)));

        Assert.Equal("PENDING_CASH_MOVEMENTS", ex.ErrorCode);
    }

    [Fact]
    public async Task ConfirmCloseShift_WhenValid_ClosesShift_AndRecordsConfirmation()
    {
        // Arrange (REQ-SHIFT-SHARED-04)
        var shiftId = Guid.NewGuid();
        var cashierUserId = Guid.NewGuid();
        var shift = new ShiftZReport
        {
            Id = shiftId,
            TenantId = _tenantContext.CurrentTenantId,
            OpeningFloat = 2_000_000m,
            ExpectedCash = 2_000_000m,
            Status = ShiftState.Closing
            ,CashierUserId = cashierUserId
        };
        _shiftRepository.Shifts.Add(shift);

        // Act
        var result = await _service.ConfirmCloseShiftAsync(
            shiftId,
            cashierUserId,
            "Thu ngân A",
            new ConfirmCloseShiftRequest(2_000_000m, 1_500_000m, 500_000m, "Thu ngân B"));

        // Assert
        Assert.Equal(ShiftState.Closed, result.Status);
        Assert.Equal(2_000_000m, result.ActualCashCounted);
        Assert.Equal(1_500_000m, result.RetainedFloatAmount);
        Assert.Equal(500_000m, result.DroppedToSafeAmount);
        Assert.Equal(cashierUserId, result.ClosedByUserId);
        Assert.NotNull(result.CloseConfirmations);
        Assert.Single(result.CloseConfirmations);
    }

    [Fact]
    public async Task ConfirmCloseShift_WhenCashDiffers_RequiresAndStoresSeparateReason()
    {
        var shiftId = Guid.NewGuid();
        var cashierUserId = Guid.NewGuid();
        _shiftRepository.Shifts.Add(new ShiftZReport
        {
            Id = shiftId,
            TenantId = _tenantContext.CurrentTenantId,
            OpeningFloat = 2_000_000m,
            Status = ShiftState.Closing,
            CashierUserId = cashierUserId,
            BusinessDate = new DateOnly(2026, 11, 14)
        });

        var missingReason = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.ConfirmCloseShiftAsync(
                shiftId,
                cashierUserId,
                "Thu ngân A",
                new ConfirmCloseShiftRequest(1_950_000m, 1_500_000m, 450_000m)));
        Assert.Equal("DISCREPANCY_REASON_REQUIRED", missingReason.ErrorCode);

        var result = await _service.ConfirmCloseShiftAsync(
            shiftId,
            cashierUserId,
            "Thu ngân A",
            new ConfirmCloseShiftRequest(
                1_950_000m,
                1_500_000m,
                450_000m,
                DiscrepancyReason: "Thiếu 50.000đ khi kiểm đếm"));

        Assert.Equal(-50_000m, result.Discrepancy);
        Assert.Equal("Thiếu 50.000đ khi kiểm đếm", result.DiscrepancyReason);
        Assert.Equal(new DateOnly(2026, 11, 14), result.BusinessDate);
        Assert.DoesNotContain("Thiếu 50.000đ", result.Notes ?? string.Empty);
    }

    [Fact]
    public async Task ForceCloseShift_ByOwnerWithReason_ClosesShift_AndSetsForceCloseReason()
    {
        // Arrange (REQ-SHIFT-SHARED-05)
        var shiftId = Guid.NewGuid();
        var ownerUserId = Guid.NewGuid();
        var shift = new ShiftZReport
        {
            Id = shiftId,
            TenantId = _tenantContext.CurrentTenantId,
            OpeningFloat = 2_000_000m,
            ExpectedCash = 2_000_000m,
            Status = ShiftState.Open
        };
        _shiftRepository.Shifts.Add(shift);

        // Act
        var result = await _service.ForceCloseShiftAsync(
            shiftId,
            ownerUserId,
            new ForceCloseShiftRequest(2_000_000m, 1_500_000m, 500_000m, "Nhân viên về hết, chủ kiểm đếm đóng ca"));

        // Assert
        Assert.Equal(ShiftState.ForceClosed, result.Status);
        Assert.Equal(ownerUserId, result.ClosedByUserId);
        Assert.Equal("Nhân viên về hết, chủ kiểm đếm đóng ca", result.ForceCloseReason);
    }

    [Fact]
    public async Task ForceCloseShift_WithoutReason_ThrowsReasonRequired()
    {
        // Arrange (REQ-SHIFT-SHARED-05)
        var shiftId = Guid.NewGuid();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.ForceCloseShiftAsync(
                shiftId,
                Guid.NewGuid(),
                new ForceCloseShiftRequest(2_000_000m, 1_500_000m, 500_000m, "")));

        Assert.Equal("REASON_REQUIRED", ex.ErrorCode);
    }

    [Fact]
    public async Task RecordParticipantAsync_AddsOrUpdatesParticipant()
    {
        // Arrange (REQ-SHIFT-SHARED-02)
        var shiftId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        // Act 1: Thêm mới người tham gia
        await _service.RecordParticipantAsync(shiftId, userId, "cashier1", "Thu Ngân Một");
        var p1 = await _shiftRepository.GetParticipantAsync(shiftId, userId);

        Assert.NotNull(p1);
        Assert.Equal("Thu Ngân Một", p1.FullName);

        // Act 2: Cập nhật hoạt động
        await _service.RecordParticipantAsync(shiftId, userId, "cashier1", "Thu Ngân Một Updated");
        var p2 = await _shiftRepository.GetParticipantAsync(shiftId, userId);

        Assert.NotNull(p2);
        Assert.Equal("Thu Ngân Một Updated", p2.FullName);
    }

    [Fact]
    public async Task GetShiftHistoryAsync_ReturnsShiftsWithParticipantsAndConfirmations()
    {
        // Arrange (REQ-SHIFT-HIST-01)
        var branchId = Guid.NewGuid();
        var shift = new ShiftZReport
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant_test_mart",
            BranchId = branchId,
            ShiftCode = "CA-20261108-01",
            ShiftName = "Ca Sáng",
            CashierUserId = Guid.NewGuid(),
            Status = ShiftState.Closed,
            OpenedAtUtc = DateTime.UtcNow.AddHours(-4),
            ClosedAtUtc = DateTime.UtcNow,
            OpeningFloat = 1_000_000m,
            ActualCashCounted = 2_500_000m,
            HandoverStatus = HandoverStatus.PENDING
        };
        shift.Participants.Add(new ShiftParticipant
        {
            Id = Guid.NewGuid(),
            ShiftId = shift.Id,
            UserId = Guid.NewGuid(),
            UserName = "thu_ngan_1",
            FullName = "Nguyễn Thu Ngân",
            JoinedAtUtc = DateTime.UtcNow.AddHours(-4),
            LastActiveAtUtc = DateTime.UtcNow.AddMinutes(-10)
        });
        shift.CloseConfirmations.Add(new ShiftCloseConfirmation
        {
            Id = Guid.NewGuid(),
            ShiftId = shift.Id,
            UserId = Guid.NewGuid(),
            UserName = "thu_ngan_1",
            ConfirmedCountedCash = 2_500_000m,
            ConfirmedAtUtc = DateTime.UtcNow.AddMinutes(-5)
        });
        _shiftRepository.Shifts.Add(shift);

        // Act
        var result = await _service.GetShiftHistoryAsync(branchId);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result);
        var item = result.First();
        Assert.Equal("CA-20261108-01", item.ShiftCode);
        Assert.Single(item.Participants);
        Assert.Equal("Nguyễn Thu Ngân", item.Participants[0].FullName);
        Assert.Single(item.CloseConfirmations);
        Assert.Equal(2_500_000m, item.CloseConfirmations[0].ConfirmedCountedCash);
    }

    [Fact]
    public async Task ConfirmHandoverAsync_Confirmed_UpdatesStatusSuccessfully()
    {
        _tenantContext.CurrentRole = "STORE_OWNER";
        // Arrange (REQ-SHIFT-HIST-02)
        var shift = new ShiftZReport
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant_test_mart",
            BranchId = Guid.NewGuid(),
            ShiftCode = "CA-20261108-02",
            ShiftName = "Ca Chiều",
            CashierUserId = Guid.NewGuid(),
            Status = ShiftState.Closed,
            OpeningFloat = 1_000_000m,
            ActualCashCounted = 3_000_000m,
            HandoverStatus = HandoverStatus.PENDING
        };
        _shiftRepository.Shifts.Add(shift);

        var request = new ConfirmHandoverRequest(HandoverStatus.CONFIRMED, null);

        // Act
        var result = await _service.ConfirmHandoverAsync(shift.Id, request, "cashier_next_user");

        // Assert
        Assert.Equal(HandoverStatus.CONFIRMED, result.HandoverStatus);
        Assert.Equal("cashier_next_user", result.ReceiverUserId);
    }

    [Fact]
    public async Task ConfirmHandoverAsync_Disputed_SetsStatusAndNotes()
    {
        _tenantContext.CurrentRole = "STORE_OWNER";
        // Arrange (REQ-SHIFT-HIST-02)
        var shift = new ShiftZReport
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant_test_mart",
            BranchId = Guid.NewGuid(),
            ShiftCode = "CA-20261108-03",
            ShiftName = "Ca Tối",
            CashierUserId = Guid.NewGuid(),
            Status = ShiftState.Closed,
            OpeningFloat = 1_000_000m,
            ActualCashCounted = 2_800_000m,
            HandoverStatus = HandoverStatus.PENDING
        };
        _shiftRepository.Shifts.Add(shift);

        var request = new ConfirmHandoverRequest(HandoverStatus.DISPUTED, "Thiếu 200.000đ tiền mặt trong két");

        // Act
        var result = await _service.ConfirmHandoverAsync(shift.Id, request, "cashier_next_user");

        // Assert
        Assert.Equal(HandoverStatus.DISPUTED, result.HandoverStatus);
        Assert.Equal("cashier_next_user", result.ReceiverUserId);
        Assert.Contains("Thiếu 200.000đ tiền mặt trong két", result.Notes);
    }

    [Fact]
    public async Task CheckCashierShiftEligibility_WhenStoreOwnerRole_ReturnsAllowedAndExempt()
    {
        // Arrange
        var branchId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        // Act
        var result = await _service.CheckCashierShiftEligibilityAsync(branchId, userId, "STORE_OWNER");

        // Assert
        Assert.True(result.IsAllowed);
        Assert.True(result.IsExempt);
        Assert.Null(result.Reason);
    }

    [Fact]
    public async Task CheckCashierShiftEligibility_WhenNoEmployeeProfile_ReturnsNotAllowed()
    {
        // Arrange
        var branchId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        _shiftRepository.AutoProvideValidSchedule = false;

        // Act
        var result = await _service.CheckCashierShiftEligibilityAsync(branchId, userId, "EMPLOYEE");

        // Assert
        Assert.False(result.IsAllowed);
        Assert.False(result.IsExempt);
        Assert.Contains("Không tìm thấy hồ sơ nhân viên", result.Reason);
    }

    [Fact]
    public async Task CheckCashierShiftEligibility_WhenNoScheduleToday_ReturnsNotAllowed()
    {
        // Arrange
        var branchId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        _shiftRepository.AutoProvideValidSchedule = false;
        var emp = new Employee
        {
            Id = Guid.NewGuid(),
            UserId = userId.ToString(),
            FullName = "Nguyễn Văn Minh",
            Status = EmployeeStatus.Active
        };
        _shiftRepository.Employees.Add(emp);

        // Act
        var result = await _service.CheckCashierShiftEligibilityAsync(branchId, userId, "EMPLOYEE");

        // Assert
        Assert.False(result.IsAllowed);
        Assert.False(result.IsExempt);
        Assert.Contains("Bạn không có lịch làm việc trong ca hiện tại", result.Reason);
    }

    [Fact]
    public async Task CheckCashierShiftEligibility_WhenHasValidSchedule_ReturnsAllowed()
    {
        // Arrange
        var branchId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        _shiftRepository.AutoProvideValidSchedule = false;
        var emp = new Employee
        {
            Id = Guid.NewGuid(),
            UserId = userId.ToString(),
            FullName = "Phạm Thị Yến",
            Status = EmployeeStatus.Active
        };
        _shiftRepository.Employees.Add(emp);

        var nowVn = DateTime.UtcNow.AddHours(7);
        _shiftRepository.Schedules.Add(new EmployeeSchedule
        {
            EmployeeId = emp.Id,
            BranchId = branchId,
            WorkDate = nowVn.Date,
            PlannedStartTime = TimeSpan.Zero,
            PlannedEndTime = new TimeSpan(23, 59, 59),
            Status = ScheduleStatus.Scheduled
        });

        // Act
        var result = await _service.CheckCashierShiftEligibilityAsync(branchId, userId, "EMPLOYEE");

        // Assert
        Assert.True(result.IsAllowed);
        Assert.False(result.IsExempt);
        Assert.Null(result.Reason);
        Assert.NotNull(result.ActiveSchedule);
    }
}

