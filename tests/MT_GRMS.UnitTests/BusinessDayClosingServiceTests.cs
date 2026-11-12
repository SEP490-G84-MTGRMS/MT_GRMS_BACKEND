using Moq;
using MT_GRMS.Application.BusinessDays;
using MT_GRMS.Application.BusinessDays.Dtos;
using MT_GRMS.Application.Cashbook;
using MT_GRMS.Application.Cashbook.Dtos;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.BusinessDays.Validators;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Entities.Sales;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Domain.Exceptions;
using Xunit;

namespace MT_GRMS.UnitTests;

public class BusinessDayClosingServiceTests
{
    private sealed class TenantContext : ITenantContext
    {
        public string CurrentTenantId { get; set; } = "tenant-test";
        public Guid CurrentUserId { get; set; } = Guid.NewGuid();
        public Guid? CurrentBranchId { get; set; }
        public string CurrentRole { get; set; } = "STORE_OWNER";
        public void SetContext(string tenantId, Guid userId, Guid? branchId) => (CurrentTenantId, CurrentUserId, CurrentBranchId) = (tenantId, userId, branchId);
        public void SetRole(string? role) => CurrentRole = role ?? string.Empty;
    }

    private sealed class UnitOfWork : IUnitOfWork
    {
        public int Saves { get; private set; }
        public bool HasActiveTransaction { get; private set; }
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) { Saves++; return Task.FromResult(1); }
        public Task BeginTransactionAsync(CancellationToken cancellationToken = default) { HasActiveTransaction = true; return Task.CompletedTask; }
        public Task CommitTransactionAsync(CancellationToken cancellationToken = default) { HasActiveTransaction = false; return Task.CompletedTask; }
        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) { HasActiveTransaction = false; return Task.CompletedTask; }
    }

    private sealed class Repository : IBusinessDayClosingRepository
    {
        public BusinessDayClosing? Existing { get; set; }
        public List<ShiftZReport> Shifts { get; } = [];
        public List<Order> Orders { get; } = [];
        public bool HasPendingPayment { get; set; }
        public BusinessDayClosing? Added { get; private set; }
        public Task<TimeSpan> GetBusinessDayStartTimeAsync(Guid branchId, CancellationToken cancellationToken = default) => Task.FromResult(new TimeSpan(6, 0, 0));
        public Task<BusinessDayClosing?> GetAsync(Guid branchId, DateOnly businessDate, CancellationToken cancellationToken = default) => Task.FromResult(Existing);
        public Task<IReadOnlyList<ShiftZReport>> GetShiftsAsync(Guid branchId, DateOnly businessDate, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ShiftZReport>>(Shifts);
        public Task<IReadOnlyList<Order>> GetShiftlessOrdersAsync(Guid branchId, DateOnly businessDate, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Order>>(Orders);
        public Task<bool> HasPendingPaymentOrdersAsync(Guid branchId, DateOnly businessDate, CancellationToken cancellationToken = default) => Task.FromResult(HasPendingPayment);
        public Task AddAsync(BusinessDayClosing closing, CancellationToken cancellationToken = default) { Added = closing; return Task.CompletedTask; }
    }

    private static CashbookEntryResponse Entry(Guid accountId, FinanceAccountType type, decimal amount, string reference) =>
        new(Guid.NewGuid(), "CB", accountId, type.ToString(), null, "Receipt", amount, amount, "BUSINESS_DAY_CLOSE", null, reference, "hash", "test", DateTime.UtcNow);

    private static (BusinessDayClosingService Service, Repository Repo, Mock<ICashbookService> Cashbook) Create()
    {
        var repo = new Repository();
        var uow = new UnitOfWork();
        var tenant = new TenantContext();
        var cashbook = new Mock<ICashbookService>();
        var safe = new FinanceAccount { Id = Guid.NewGuid(), AccountType = FinanceAccountType.Safe, CurrentBalance = 1_000_000m };
        var bank = new FinanceAccount { Id = Guid.NewGuid(), AccountType = FinanceAccountType.Bank, CurrentBalance = 0m };
        cashbook.Setup(x => x.GetOrCreateSafeAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(safe);
        cashbook.Setup(x => x.GetOrCreateBankAccountAsync(It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync(bank);
        cashbook.Setup(x => x.ReceiveCashAsync(It.IsAny<Guid>(), It.IsAny<FinanceAccountType>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid a, FinanceAccountType t, decimal m, string _, Guid? _, string r, string _, CancellationToken _) => Entry(a, t, m, r));
        cashbook.Setup(x => x.DisburseCashAsync(It.IsAny<Guid>(), It.IsAny<FinanceAccountType>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid a, FinanceAccountType t, decimal m, string _, Guid? _, string r, string _, CancellationToken _) => Entry(a, t, m, r));
        return (new BusinessDayClosingService(repo, cashbook.Object, uow, tenant), repo, cashbook);
    }

    [Fact]
    public async Task CloseAsync_PostsRevenueOnce_AndDoesNotCountRetainedFloatAsRevenue()
    {
        var (service, repo, cashbook) = Create();
        var branchId = Guid.NewGuid();
        repo.Shifts.Add(new ShiftZReport
        {
            Id = Guid.NewGuid(), BranchId = branchId, BusinessDate = new DateOnly(2026, 11, 12),
            Status = ShiftState.Closed, HandoverStatus = HandoverStatus.CONFIRMED, SnapshotLocked = true,
            TotalCashSales = 1_000_000m, TotalBankRevenue = 500_000m, RetainedFloatAmount = 1_500_000m,
            DroppedToSafeAmount = 1_000_000m, TotalOrdersCount = 2
        });
        repo.Orders.Add(new Order { BranchId = branchId, BusinessDate = new DateOnly(2026, 11, 12), Status = "Completed", PaymentMethod = "CASH", FinalAmount = 300_000m });

        var result = await service.CloseAsync(new CloseBusinessDayRequest(branchId, new DateOnly(2026, 11, 12), 1_300_000m));

        Assert.Equal(1_300_000m, result.TotalCashSales);
        Assert.Equal(1_500_000m, result.TotalRetainedFloat);
        Assert.Equal(0m, result.CashDiscrepancy);
        Assert.Equal(CashbookPostingStatus.Posted, repo.Shifts[0].CashbookPostingStatus);
        cashbook.Verify(x => x.ReceiveCashAsync(It.IsAny<Guid>(), FinanceAccountType.Safe, 1_300_000m, "BUSINESS_DAY_CLOSE", It.IsAny<Guid?>(), It.Is<string>(s => s.EndsWith("-CASH")), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        cashbook.Verify(x => x.ReceiveCashAsync(It.IsAny<Guid>(), FinanceAccountType.Safe, 1_500_000m, It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CloseAsync_WhenShiftStillOpen_IsRejected()
    {
        var (service, repo, _) = Create();
        repo.Shifts.Add(new ShiftZReport { Status = ShiftState.Open, BusinessDate = new DateOnly(2026, 11, 12) });
        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.CloseAsync(new CloseBusinessDayRequest(Guid.NewGuid(), new DateOnly(2026, 11, 12), 0)));
        Assert.Equal("OPEN_SHIFTS_EXIST", ex.ErrorCode);
    }

    [Fact]
    public async Task CloseAsync_WhenHandoverNotConfirmed_IsRejected()
    {
        var (service, repo, _) = Create();
        repo.Shifts.Add(new ShiftZReport { Status = ShiftState.Closed, HandoverStatus = HandoverStatus.PENDING, SnapshotLocked = true });
        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.CloseAsync(new CloseBusinessDayRequest(Guid.NewGuid(), new DateOnly(2026, 11, 12), 0)));
        Assert.Equal("UNCONFIRMED_HANDOVERS", ex.ErrorCode);
    }

    [Fact]
    public async Task CloseAsync_WhenPendingPaymentExists_IsRejected()
    {
        var (service, repo, _) = Create();
        repo.HasPendingPayment = true;
        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.CloseAsync(new CloseBusinessDayRequest(Guid.NewGuid(), new DateOnly(2026, 11, 12), 0)));
        Assert.Equal("PENDING_PAYMENT_ORDERS", ex.ErrorCode);
    }

    [Fact]
    public async Task CloseAsync_WhenDayAlreadyExists_IsIdempotentlyRejected()
    {
        var (service, repo, cashbook) = Create();
        repo.Existing = new BusinessDayClosing();
        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.CloseAsync(new CloseBusinessDayRequest(Guid.NewGuid(), new DateOnly(2026, 11, 12), 0)));
        Assert.Equal("BUSINESS_DAY_ALREADY_CLOSED", ex.ErrorCode);
        cashbook.VerifyNoOtherCalls();
    }

    [Fact]
    public void CloseRequestValidator_ValidatesEveryInputField()
    {
        var validator = new CloseBusinessDayRequestValidator();
        var invalid = new CloseBusinessDayRequest(Guid.Empty, default, -1m, new string('x', 501));
        var result = validator.Validate(invalid);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => x.PropertyName == nameof(CloseBusinessDayRequest.BranchId));
        Assert.Contains(result.Errors, x => x.PropertyName == nameof(CloseBusinessDayRequest.BusinessDate));
        Assert.Contains(result.Errors, x => x.PropertyName == nameof(CloseBusinessDayRequest.ActualCashCounted));
        Assert.Contains(result.Errors, x => x.PropertyName == nameof(CloseBusinessDayRequest.Note));
    }

    [Theory]
    [InlineData("2026-11-12T15:00:00Z", 2026, 11, 12)] // 22:00 giờ Việt Nam
    [InlineData("2026-11-12T18:30:00Z", 2026, 11, 12)] // 01:30 hôm sau, vẫn thuộc ngày trước
    [InlineData("2026-11-12T23:30:00Z", 2026, 11, 13)] // 06:30 hôm sau, sang ngày nghiệp vụ mới
    public void BusinessDateResolver_SupportsOvernightShifts(string utc, int year, int month, int day)
    {
        var result = BusinessDateResolver.Resolve(DateTime.Parse(utc).ToUniversalTime(), new TimeSpan(6, 0, 0));
        Assert.Equal(new DateOnly(year, month, day), result);
    }
}
