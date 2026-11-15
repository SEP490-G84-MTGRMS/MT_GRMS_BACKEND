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
        public List<BusinessDayClosing> History { get; } = [];
        public List<ShiftZReport> Shifts { get; } = [];
        public List<Order> Orders { get; } = [];
        public bool HasPendingPayment { get; set; }
        public bool BranchAccessible { get; set; } = true;
        public BusinessDayClosing? Added { get; private set; }
        public List<FinanceAccount> Funds { get; } = [];
        public Task<bool> IsBranchAccessibleAsync(string tenantId, Guid branchId, CancellationToken cancellationToken = default) => Task.FromResult(BranchAccessible);
        public Task<TimeSpan> GetBusinessDayStartTimeAsync(string tenantId, Guid branchId, CancellationToken cancellationToken = default) => Task.FromResult(new TimeSpan(6, 0, 0));
        public Task<BusinessDayClosing?> GetAsync(string tenantId, Guid branchId, DateOnly businessDate, CancellationToken cancellationToken = default) => Task.FromResult(Existing);
        public Task<BusinessDayClosing?> GetLatestAsync(string tenantId, Guid branchId, DateOnly businessDate, CancellationToken cancellationToken = default) =>
            Task.FromResult(History.Where(x => x.BranchId == branchId && x.BusinessDate == businessDate).OrderByDescending(x => x.Version).FirstOrDefault());
        public Task<BusinessDayClosing?> GetByIdAsync(string tenantId, Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(History.FirstOrDefault(x => x.TenantId == tenantId && x.Id == id));
        public Task<IReadOnlyList<BusinessDayClosing>> GetHistoryAsync(string tenantId, Guid branchId, DateOnly businessDate, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<BusinessDayClosing>>(History.Where(x => x.TenantId == tenantId && x.BranchId == branchId && x.BusinessDate == businessDate).OrderByDescending(x => x.Version).ToList());
        public Task<IReadOnlyList<ShiftZReport>> GetShiftsAsync(string tenantId, Guid branchId, DateOnly businessDate, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ShiftZReport>>(Shifts);
        public Task<IReadOnlyList<Order>> GetShiftlessOrdersAsync(string tenantId, Guid branchId, DateOnly businessDate, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Order>>(Orders);
        public Task<bool> HasPendingPaymentOrdersAsync(string tenantId, Guid branchId, DateOnly businessDate, CancellationToken cancellationToken = default) => Task.FromResult(HasPendingPayment);
        public Task<IReadOnlyList<FinanceAccount>> GetFundsAsync(string tenantId, Guid branchId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<FinanceAccount>>(Funds);
        public Task AddAsync(BusinessDayClosing closing, CancellationToken cancellationToken = default) { Added = closing; History.Add(closing); return Task.CompletedTask; }
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
        repo.Funds.Add(safe);
        return (new BusinessDayClosingService(repo, cashbook.Object, uow, tenant), repo, cashbook);
    }

    private static async Task<CloseBusinessDayRequest> ValidRequestAsync(
        BusinessDayClosingService service, Repository repo, Guid branchId, DateOnly businessDate, decimal actualCash)
    {
        var preview = await service.PreviewAsync(branchId, businessDate);
        var counts = preview.Funds.Where(x => x.RequiresPhysicalCount)
            .Select(x => new BusinessDayFundCount(x.FundId, actualCash)).ToList();
        return new CloseBusinessDayRequest(branchId, businessDate, counts, preview.PreviewToken);
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

        var result = await service.CloseAsync(await ValidRequestAsync(service, repo, branchId, new DateOnly(2026, 11, 12), 1_300_000m));

        Assert.Equal(1_300_000m, result.TotalCashSales);
        Assert.Equal(1_500_000m, result.TotalRetainedFloat);
        Assert.Equal(0m, result.CashDiscrepancy);
        Assert.Equal(CashbookPostingStatus.Posted, repo.Shifts[0].CashbookPostingStatus);
        cashbook.Verify(x => x.ReceiveCashAsync(It.IsAny<Guid>(), FinanceAccountType.Safe, 1_300_000m, "BUSINESS_DAY_CLOSE", It.IsAny<Guid?>(), It.Is<string>(s => s.EndsWith("-CASH")), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        cashbook.Verify(x => x.ReceiveCashAsync(It.IsAny<Guid>(), FinanceAccountType.Safe, 1_500_000m, It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CloseAsync_WhenShiftRevenueAlreadyPosted_OnlySnapshotsAndDoesNotPostAgain()
    {
        var (service, repo, cashbook) = Create();
        var branchId = Guid.NewGuid();
        repo.Shifts.Add(new ShiftZReport
        {
            Id = Guid.NewGuid(), BranchId = branchId, BusinessDate = new DateOnly(2026, 11, 14),
            Status = ShiftState.Closed, HandoverStatus = HandoverStatus.CONFIRMED, SnapshotLocked = true,
            CashbookPostingStatus = CashbookPostingStatus.Posted,
            TotalCashSales = 1_000_000m, TotalBankRevenue = 500_000m, TotalOrdersCount = 2
        });

        var result = await service.CloseAsync(await ValidRequestAsync(service, repo, branchId, new DateOnly(2026, 11, 14), 1_000_000m));

        Assert.Equal(1_000_000m, result.TotalCashSales);
        Assert.Equal(500_000m, result.TotalBankSales);
        cashbook.Verify(x => x.ReceiveCashAsync(
            It.IsAny<Guid>(), It.IsAny<FinanceAccountType>(), It.IsAny<decimal>(),
            "BUSINESS_DAY_CLOSE", It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CloseAsync_WhenShiftStillOpen_IsRejected()
    {
        var (service, repo, _) = Create();
        repo.Shifts.Add(new ShiftZReport { Status = ShiftState.Open, BusinessDate = new DateOnly(2026, 11, 12) });
        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.CloseAsync(new CloseBusinessDayRequest(Guid.NewGuid(), new DateOnly(2026, 11, 12), [], "preview")));
        Assert.Equal("OPEN_SHIFTS_EXIST", ex.ErrorCode);
    }

    [Fact]
    public async Task CloseAsync_WhenBranchIsOutsideTenant_IsNotDisclosed()
    {
        var (service, repo, cashbook) = Create();
        repo.BranchAccessible = false;

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            service.CloseAsync(new CloseBusinessDayRequest(Guid.NewGuid(), new DateOnly(2026, 11, 14), [], "preview")));

        Assert.Equal("BRANCH_NOT_FOUND", ex.ErrorCode);
        Assert.Null(repo.Added);
        cashbook.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task PreviewAsync_ReturnsFundBreakdownAndActionableBlockers()
    {
        var (service, repo, _) = Create();
        var branchId = Guid.NewGuid();
        repo.Funds.Clear();
        repo.Funds.Add(new FinanceAccount
        {
            Id = Guid.NewGuid(), TenantId = "tenant-test", BranchId = branchId,
            AccountName = "Két chủ", AccountType = FinanceAccountType.Safe,
            CurrentBalance = 2_000_000m, IsActive = true
        });
        var shift = new ShiftZReport
        {
            Id = Guid.NewGuid(), BranchId = branchId, ShiftCode = "CA-01",
            BusinessDate = new DateOnly(2026, 11, 14), Status = ShiftState.Open,
            HandoverStatus = HandoverStatus.PENDING, TotalCashSales = 500_000m
        };
        shift.CashMovements.Add(new ShiftCashMovement
        {
            Id = Guid.NewGuid(), ShiftId = shift.Id, Amount = 300_000m,
            Reason = "Mua đá", MovementType = CashMovementType.OUT,
            Status = CashMovementStatus.PendingApproval
        });
        repo.Shifts.Add(shift);

        var result = await service.PreviewAsync(branchId, new DateOnly(2026, 11, 14));

        Assert.Equal(500_000m, result.TotalCashSales);
        Assert.Single(result.Funds);
        Assert.Contains(result.Blockers, x => x.Code == "OPEN_SHIFTS_EXIST" && x.ActionPath != null);
        Assert.Contains(result.Blockers, x => x.Code == "PENDING_CASH_MOVEMENT" && x.Amount == 300_000m);
        Assert.False(string.IsNullOrWhiteSpace(result.PreviewToken));
    }

    [Fact]
    public async Task CloseAsync_WhenHandoverNotConfirmed_IsRejected()
    {
        var (service, repo, _) = Create();
        repo.Shifts.Add(new ShiftZReport { Status = ShiftState.Closed, HandoverStatus = HandoverStatus.PENDING, SnapshotLocked = true });
        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.CloseAsync(new CloseBusinessDayRequest(Guid.NewGuid(), new DateOnly(2026, 11, 12), [], "preview")));
        Assert.Equal("UNCONFIRMED_HANDOVER", ex.ErrorCode);
    }

    [Fact]
    public async Task CloseAsync_WhenPendingPaymentExists_IsRejected()
    {
        var (service, repo, _) = Create();
        repo.HasPendingPayment = true;
        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.CloseAsync(new CloseBusinessDayRequest(Guid.NewGuid(), new DateOnly(2026, 11, 12), [], "preview")));
        Assert.Equal("PENDING_PAYMENT_ORDERS", ex.ErrorCode);
    }

    [Fact]
    public async Task CloseAsync_WhenDayAlreadyExists_IsIdempotentlyRejected()
    {
        var (service, repo, cashbook) = Create();
        repo.Existing = new BusinessDayClosing();
        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.CloseAsync(new CloseBusinessDayRequest(Guid.NewGuid(), new DateOnly(2026, 11, 12), [], "preview")));
        Assert.Equal("BUSINESS_DAY_ALREADY_CLOSED", ex.ErrorCode);
        cashbook.VerifyNoOtherCalls();
    }

    [Fact]
    public void CloseRequestValidator_ValidatesEveryInputField()
    {
        var validator = new CloseBusinessDayRequestValidator();
        var invalid = new CloseBusinessDayRequest(Guid.Empty, default,
            [new BusinessDayFundCount(Guid.Empty, -1m), new BusinessDayFundCount(Guid.Empty, 0m)],
            "", new string('x', 501));
        var result = validator.Validate(invalid);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => x.PropertyName == nameof(CloseBusinessDayRequest.BranchId));
        Assert.Contains(result.Errors, x => x.PropertyName == nameof(CloseBusinessDayRequest.BusinessDate));
        Assert.Contains(result.Errors, x => x.PropertyName.Contains(nameof(CloseBusinessDayRequest.FundCounts)));
        Assert.Contains(result.Errors, x => x.PropertyName == nameof(CloseBusinessDayRequest.PreviewToken));
        Assert.Contains(result.Errors, x => x.PropertyName == nameof(CloseBusinessDayRequest.Note));
    }

    [Fact]
    public async Task CloseAsync_WhenPreviewSourceChanges_IsRejectedAsStale()
    {
        var (service, repo, _) = Create();
        var branchId = Guid.NewGuid();
        var date = new DateOnly(2026, 11, 14);
        var request = await ValidRequestAsync(service, repo, branchId, date, 1_000_000m);
        repo.Funds[0].CurrentBalance += 1;

        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.CloseAsync(request));

        Assert.Equal("STALE_PREVIEW", ex.ErrorCode);
        Assert.Null(repo.Added);
    }

    [Fact]
    public async Task CloseAsync_WhenRequiredFundCountIsMissing_IsRejected()
    {
        var (service, repo, _) = Create();
        var branchId = Guid.NewGuid();
        var date = new DateOnly(2026, 11, 14);
        var preview = await service.PreviewAsync(branchId, date);
        var request = new CloseBusinessDayRequest(branchId, date, [], preview.PreviewToken);

        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.CloseAsync(request));

        Assert.Equal("INCOMPLETE_FUND_COUNTS", ex.ErrorCode);
        Assert.Null(repo.Added);
    }

    [Fact]
    public async Task ReopenAsync_PreservesSnapshotAndRecordsAudit()
    {
        var (service, repo, _) = Create();
        var closing = new BusinessDayClosing
        {
            Id = Guid.NewGuid(), TenantId = "tenant-test", BranchId = Guid.NewGuid(),
            BusinessDate = new DateOnly(2026, 11, 14), Version = 1,
            Status = BusinessDayClosingStatus.Closed, IsActive = true, ReferenceCode = "EOD-1"
        };
        repo.History.Add(closing);

        var result = await service.ReopenAsync(closing.Id, new ReopenBusinessDayRequest("Bổ sung phiếu chi bị thiếu"));

        Assert.Equal("Reopened", result.Status);
        Assert.False(result.IsActive);
        Assert.Equal("Bổ sung phiếu chi bị thiếu", result.ReopenReason);
        Assert.NotNull(result.ReopenedAtUtc);
    }

    [Fact]
    public async Task CloseAsync_AfterReopen_CreatesNextVersion()
    {
        var (service, repo, _) = Create();
        var branchId = Guid.NewGuid();
        var date = new DateOnly(2026, 11, 14);
        var previous = new BusinessDayClosing
        {
            Id = Guid.NewGuid(), TenantId = "tenant-test", BranchId = branchId, BusinessDate = date,
            Version = 1, Status = BusinessDayClosingStatus.Reopened, IsActive = false, ReferenceCode = "EOD-OLD"
        };
        repo.History.Add(previous);

        var result = await service.CloseAsync(await ValidRequestAsync(service, repo, branchId, date, 0));

        Assert.Equal(2, repo.Added!.Version);
        Assert.Equal(previous.Id, repo.Added.SupersedesClosingId);
        Assert.EndsWith("-V2", result.ReferenceCode);
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
