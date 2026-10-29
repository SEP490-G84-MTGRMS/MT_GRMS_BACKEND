using System.Linq.Expressions;
using MT_GRMS.Application.Cashbook;
using MT_GRMS.Application.Cashbook.Dtos;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Common.Services;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Domain.Exceptions;
using Xunit;

namespace MT_GRMS.UnitTests;

public class CashbookServiceTests
{
    private class FakeTenantContext : ITenantContext
    {
        public string CurrentTenantId { get; set; } = "tenant-test-01";
        public Guid CurrentUserId { get; set; } = Guid.Parse("11111111-1111-1111-1111-111111111111");
        public Guid? CurrentBranchId { get; set; } = Guid.Parse("22222222-2222-2222-2222-222222222222");
        public void SetContext(string tenantId, Guid userId, Guid? branchId)
        {
            CurrentTenantId = tenantId;
            CurrentUserId = userId;
            CurrentBranchId = branchId;
        }
        public string CurrentRole { get; set; } = "ADMIN";
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

    private class FakeGenericRepository<T> : IRepository<T> where T : MT_GRMS.Domain.Common.BaseTenantEntity
    {
        public List<T> Items { get; } = new();

        public Task<T?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.FirstOrDefault(x => x.Id == id));

        public Task<IReadOnlyList<T>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult((IReadOnlyList<T>)Items.ToList());

        public Task<IReadOnlyList<T>> FindAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default) =>
            Task.FromResult((IReadOnlyList<T>)Items.AsQueryable().Where(predicate).ToList());

        public Task AddAsync(T entity, CancellationToken cancellationToken = default)
        {
            if (entity.Id == Guid.Empty) entity.Id = Guid.NewGuid();
            Items.Add(entity);
            return Task.CompletedTask;
        }

        public void Update(T entity) { }
        public void Delete(T entity) => Items.Remove(entity);
    }

    private class FakeCashbookRepository : FakeGenericRepository<CashbookEntry>, ICashbookRepository
    {
        public List<BranchCashTill> Tills { get; } = new();
        public List<BranchSafe> Safes { get; } = new();
        public List<TenantBankAccount> BankAccounts { get; } = new();
        public List<ShiftZReport> Shifts { get; } = new();

        public Task<BranchCashTill?> GetTillWithLockAsync(Guid tillId, CancellationToken ct = default) =>
            Task.FromResult(Tills.FirstOrDefault(t => t.Id == tillId));

        public Task<BranchSafe?> GetSafeWithLockAsync(Guid safeId, CancellationToken ct = default) =>
            Task.FromResult(Safes.FirstOrDefault(s => s.Id == safeId));

        public Task<TenantBankAccount?> GetBankAccountWithLockAsync(Guid bankAccountId, CancellationToken ct = default) =>
            Task.FromResult(BankAccounts.FirstOrDefault(b => b.Id == bankAccountId));

        public Task<string> GetLastAuditHashAsync(Guid accountId, CancellationToken ct = default)
        {
            var last = Items.Where(e => e.AccountId == accountId).OrderByDescending(e => e.OccurredAtUtc).FirstOrDefault();
            return Task.FromResult(last?.AuditHash ?? "GENESIS_HASH");
        }

        public Task<ShiftZReport?> GetActiveShiftByTillIdAsync(Guid tillId, CancellationToken ct = default) =>
            Task.FromResult(Shifts.FirstOrDefault(s => s.TillId == tillId && s.Status == ShiftState.Open));

        public Task<ShiftZReport?> GetLastClosedShiftByTillIdAsync(Guid tillId, CancellationToken ct = default) =>
            Task.FromResult(Shifts.Where(s => s.TillId == tillId && s.Status == ShiftState.Closed).OrderByDescending(s => s.ClosedAtUtc).FirstOrDefault());
    }

    private class FakeAuditHashService : IAuditHashService
    {
        public string ComputeEntryHash(string prevHash, CashbookEntry entry) =>
            $"HASH-{prevHash[..Math.Min(8, prevHash.Length)]}-{entry.Amount}-{entry.BalanceAfter}";

        public bool VerifyEntryHash(string prevHash, CashbookEntry entry) => true;
    }

    private readonly FakeCashbookRepository _cashbookRepo = new();
    private readonly FakeGenericRepository<BranchCashTill> _tillRepo = new();
    private readonly FakeGenericRepository<BranchSafe> _safeRepo = new();
    private readonly FakeGenericRepository<CashInTransit> _transitRepo = new();
    private readonly FakeGenericRepository<ShiftZReport> _shiftRepo = new();
    private readonly FakeGenericRepository<TenantBankAccount> _bankRepo = new();
    private readonly FakeAuditHashService _auditHashService = new();
    private readonly FakeTenantContext _tenantContext = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly CashbookService _service;

    public CashbookServiceTests()
    {
        _service = new CashbookService(
            _cashbookRepo,
            _tillRepo,
            _safeRepo,
            _transitRepo,
            _shiftRepo,
            _bankRepo,
            _auditHashService,
            _tenantContext,
            _unitOfWork
        );
    }

    [Fact]
    public async Task OpenShiftAsync_WhenNoActiveShift_ShouldSucceedAndSetOpeningFloat()
    {
        var branchId = Guid.NewGuid();
        var till = new BranchCashTill { Id = Guid.NewGuid(), BranchId = branchId, TillName = "Quầy 01", CurrentBalance = 500_000m };
        _cashbookRepo.Tills.Add(till);
        _tillRepo.Items.Add(till);

        var request = new OpenShiftRequest(till.Id, 500_000m, "Mở ca đầu ngày");
        var response = await _service.OpenShiftAsync(request);

        Assert.NotNull(response);
        Assert.Equal(ShiftState.Open.ToString(), response.Status);
        Assert.Equal(500_000m, response.OpeningFloat);
        Assert.Equal(till.Id, response.TillId);
        Assert.Equal(_shiftRepo.Items.First().Id, till.ActiveShiftId);
    }

    [Fact]
    public async Task OpenShiftAsync_WhenActiveShiftExists_ShouldThrowBusinessException()
    {
        var till = new BranchCashTill { Id = Guid.NewGuid(), BranchId = Guid.NewGuid(), TillName = "Quầy 01", CurrentBalance = 500_000m };
        var activeShift = new ShiftZReport { Id = Guid.NewGuid(), TillId = till.Id, Status = ShiftState.Open };
        _cashbookRepo.Tills.Add(till);
        _cashbookRepo.Shifts.Add(activeShift);

        var request = new OpenShiftRequest(till.Id, 500_000m, null);
        var ex = await Assert.ThrowsAsync<BusinessException>(() => _service.OpenShiftAsync(request));
        Assert.Equal("ACTIVE_SHIFT_EXISTS", ex.ErrorCode);
    }

    [Fact]
    public async Task OpenShiftAsync_InheritsRetainedFloatFromLastClosedShift()
    {
        var till = new BranchCashTill { Id = Guid.NewGuid(), BranchId = Guid.NewGuid(), TillName = "Quầy 01", CurrentBalance = 0 };
        var lastClosed = new ShiftZReport { Id = Guid.NewGuid(), TillId = till.Id, Status = ShiftState.Closed, RetainedFloatAmount = 300_000m, ClosedAtUtc = DateTime.UtcNow.AddHours(-1) };
        _cashbookRepo.Tills.Add(till);
        _cashbookRepo.Shifts.Add(lastClosed);
        _tillRepo.Items.Add(till);

        var request = new OpenShiftRequest(till.Id, 300_000m, "Kế thừa ca trước");
        var response = await _service.OpenShiftAsync(request);

        Assert.Equal(300_000m, response.OpeningFloat);
        Assert.Equal(0, response.Discrepancy);
    }

    [Fact]
    public async Task CloseShiftZReportAsync_WithExactCash_ShouldCloseSuccessfully()
    {
        var till = new BranchCashTill { Id = Guid.NewGuid(), BranchId = Guid.NewGuid(), CurrentBalance = 1_500_000m };
        var shift = new ShiftZReport
        {
            Id = Guid.NewGuid(),
            TillId = till.Id,
            OpeningFloat = 500_000m,
            TotalCashIn = 1_000_000m,
            TotalCashOut = 0,
            Status = ShiftState.Open
        };
        _shiftRepo.Items.Add(shift);
        _cashbookRepo.Tills.Add(till);

        var request = new CloseShiftRequest(shift.Id, 1_500_000m, 300_000m, "Khớp tiền ca");
        var response = await _service.CloseShiftZReportAsync(request);

        Assert.Equal(ShiftState.Closed.ToString(), response.Status);
        Assert.Equal(0, response.Discrepancy);
        Assert.Equal(300_000m, till.CurrentBalance); // Tiền để lại ca sau
        Assert.Null(till.ActiveShiftId);
    }

    [Fact]
    public async Task CloseShiftZReportAsync_WithSurplusCash_ShouldRecordSuspenseEntry()
    {
        var till = new BranchCashTill { Id = Guid.NewGuid(), BranchId = Guid.NewGuid(), CurrentBalance = 1_500_000m };
        var shift = new ShiftZReport
        {
            Id = Guid.NewGuid(),
            TillId = till.Id,
            OpeningFloat = 500_000m,
            TotalCashIn = 1_000_000m,
            TotalCashOut = 0,
            Status = ShiftState.Open
        };
        _shiftRepo.Items.Add(shift);
        _cashbookRepo.Tills.Add(till);

        // Đếm thực tế 1.550.000đ (thừa 50.000đ > dung sai 2.000đ)
        var request = new CloseShiftRequest(shift.Id, 1_550_000m, 300_000m, "Thừa tiền");
        var response = await _service.CloseShiftZReportAsync(request);

        Assert.Equal(50_000m, response.Discrepancy);
        var entry = _cashbookRepo.Items.FirstOrDefault(e => e.SourceType == "CASH_OVER_SUSPENSE");
        Assert.NotNull(entry);
        Assert.Equal(50_000m, entry.Amount);
        Assert.Equal(CashbookEntryType.Receipt, entry.EntryType);
    }

    [Fact]
    public async Task CloseShiftZReportAsync_WithShortageCash_ShouldRecordShortageEntry()
    {
        var till = new BranchCashTill { Id = Guid.NewGuid(), BranchId = Guid.NewGuid(), CurrentBalance = 1_500_000m };
        var shift = new ShiftZReport
        {
            Id = Guid.NewGuid(),
            TillId = till.Id,
            OpeningFloat = 500_000m,
            TotalCashIn = 1_000_000m,
            TotalCashOut = 0,
            Status = ShiftState.Open
        };
        _shiftRepo.Items.Add(shift);
        _cashbookRepo.Tills.Add(till);

        // Đếm thực tế 1.480.000đ (thiếu 20.000đ > dung sai 2.000đ)
        var request = new CloseShiftRequest(shift.Id, 1_480_000m, 300_000m, "Thiếu tiền");
        var response = await _service.CloseShiftZReportAsync(request);

        Assert.Equal(-20_000m, response.Discrepancy);
        var entry = _cashbookRepo.Items.FirstOrDefault(e => e.SourceType == "CASH_SHORTAGE_PENDING");
        Assert.NotNull(entry);
        Assert.Equal(20_000m, entry.Amount);
        Assert.Equal(CashbookEntryType.Payment, entry.EntryType);
    }

    [Fact]
    public async Task ProcessCashDropAsync_WhenTillHasSufficientBalance_ShouldTransferMoneyToSafe()
    {
        var branchId = Guid.NewGuid();
        var till = new BranchCashTill { Id = Guid.NewGuid(), BranchId = branchId, CurrentBalance = 2_000_000m };
        var safe = new BranchSafe { Id = Guid.NewGuid(), BranchId = branchId, SafeName = "Két Sắt Chi Nhánh", CurrentBalance = 5_000_000m };
        var shift = new ShiftZReport { Id = Guid.NewGuid(), TillId = till.Id, Status = ShiftState.Open, DroppedToSafeAmount = 0 };

        _cashbookRepo.Tills.Add(till);
        _cashbookRepo.Safes.Add(safe);
        _shiftRepo.Items.Add(shift);
        _safeRepo.Items.Add(safe);

        var request = new CashDropRequest(shift.Id, 1_000_000m, Guid.NewGuid(), "Cash drop giữa ca");
        var result = await _service.ProcessCashDropAsync(request);

        Assert.True(result);
        Assert.Equal(1_000_000m, till.CurrentBalance);
        Assert.Equal(6_000_000m, safe.CurrentBalance);
        Assert.Equal(1_000_000m, shift.DroppedToSafeAmount);
        Assert.Equal(2, _cashbookRepo.Items.Count); // 1 chi quầy + 1 thu két sắt
    }

    [Fact]
    public async Task ProcessCashDropAsync_WhenTillHasInsufficientBalance_ShouldThrowInsufficientStockException()
    {
        var branchId = Guid.NewGuid();
        var till = new BranchCashTill { Id = Guid.NewGuid(), BranchId = branchId, CurrentBalance = 200_000m };
        var shift = new ShiftZReport { Id = Guid.NewGuid(), TillId = till.Id, Status = ShiftState.Open };

        _cashbookRepo.Tills.Add(till);
        _shiftRepo.Items.Add(shift);

        var request = new CashDropRequest(shift.Id, 500_000m, Guid.NewGuid(), "Drop quá tay");
        await Assert.ThrowsAsync<InsufficientStockException>(() => _service.ProcessCashDropAsync(request));
    }

    [Fact]
    public async Task CreateDepositToBankAsync_WhenSafeHasSufficientBalance_ShouldCreateTransitRecord()
    {
        var safe = new BranchSafe { Id = Guid.NewGuid(), BranchId = Guid.NewGuid(), CurrentBalance = 10_000_000m };
        var bankId = Guid.NewGuid();
        _cashbookRepo.Safes.Add(safe);

        var request = new DepositToBankRequest(safe.Id, bankId, 8_000_000m, Guid.NewGuid(), "Nộp doanh thu cuối ngày");
        var response = await _service.CreateDepositToBankAsync(request);

        Assert.NotNull(response);
        Assert.Equal(CashInTransitStatus.PendingBankConfirmation.ToString(), response.Status);
        Assert.Equal(8_000_000m, response.AmountSent);
        Assert.Equal(2_000_000m, safe.CurrentBalance); // 10tr - 8tr = 2tr
        Assert.Single(_transitRepo.Items);
    }

    [Fact]
    public async Task CreateDepositToBankAsync_WhenSafeHasInsufficientBalance_ShouldThrowInsufficientStockException()
    {
        var safe = new BranchSafe { Id = Guid.NewGuid(), BranchId = Guid.NewGuid(), CurrentBalance = 1_000_000m };
        _cashbookRepo.Safes.Add(safe);

        var request = new DepositToBankRequest(safe.Id, Guid.NewGuid(), 5_000_000m, Guid.NewGuid(), "Nộp quá số dư");
        await Assert.ThrowsAsync<InsufficientStockException>(() => _service.CreateDepositToBankAsync(request));
    }

    [Fact]
    public async Task ConfirmBankDepositAsync_WhenAmountMatches_ShouldMarkCompletedAndIncreaseBankBalance()
    {
        var bank = new TenantBankAccount { Id = Guid.NewGuid(), AccountNumber = "123456789", CurrentBalance = 50_000_000m };
        var transit = new CashInTransit
        {
            Id = Guid.NewGuid(),
            TransitCode = "CIT-001",
            TargetBankAccountId = bank.Id,
            AmountSent = 5_000_000m,
            Status = CashInTransitStatus.PendingBankConfirmation
        };
        _transitRepo.Items.Add(transit);
        _cashbookRepo.BankAccounts.Add(bank);

        var request = new ConfirmBankDepositRequest(transit.Id, 5_000_000m, "Tiền đã vào tài khoản");
        var response = await _service.ConfirmBankDepositAsync(request);

        Assert.Equal(CashInTransitStatus.Completed.ToString(), response.Status);
        Assert.Equal(0, response.DiscrepancyAmount);
        Assert.Equal(55_000_000m, bank.CurrentBalance);
        var entry = _cashbookRepo.Items.FirstOrDefault(e => e.ReferenceCode == "CIT-001");
        Assert.NotNull(entry);
        Assert.Equal(CashbookEntryType.Receipt, entry.EntryType);
    }

    [Fact]
    public async Task ConfirmBankDepositAsync_WhenAmountDiffers_ShouldMarkDiscrepancy()
    {
        var bank = new TenantBankAccount { Id = Guid.NewGuid(), AccountNumber = "123456789", CurrentBalance = 50_000_000m };
        var transit = new CashInTransit
        {
            Id = Guid.NewGuid(),
            TransitCode = "CIT-002",
            TargetBankAccountId = bank.Id,
            AmountSent = 5_000_000m,
            Status = CashInTransitStatus.PendingBankConfirmation
        };
        _transitRepo.Items.Add(transit);
        _cashbookRepo.BankAccounts.Add(bank);

        // Thực tế ngân hàng báo vào 4.900.000đ (lệch 100k)
        var request = new ConfirmBankDepositRequest(transit.Id, 4_900_000m, "Bị trừ phí nộp");
        var response = await _service.ConfirmBankDepositAsync(request);

        Assert.Equal(CashInTransitStatus.Discrepancy.ToString(), response.Status);
        Assert.Equal(-100_000m, response.DiscrepancyAmount);
        Assert.Equal(54_900_000m, bank.CurrentBalance);
    }

    [Fact]
    public async Task DisburseCashAsync_WhenTillHasInsufficientBalance_ShouldThrowInsufficientStockException()
    {
        var till = new BranchCashTill { Id = Guid.NewGuid(), BranchId = Guid.NewGuid(), CurrentBalance = 100_000m };
        _cashbookRepo.Tills.Add(till);

        // Chi 150k trong khi két chỉ có 100k -> Chống âm két tức thì
        await Assert.ThrowsAsync<InsufficientStockException>(() =>
            _service.DisburseCashAsync(till.Id, FinanceAccountType.Till, 150_000m, "EXPENSE", Guid.NewGuid(), "REF01", "Chi tiền thừa"));
    }

    [Fact]
    public async Task DisburseCashAsync_WhenSufficientBalance_ShouldDecreaseBalanceAndComputeAuditHash()
    {
        var till = new BranchCashTill { Id = Guid.NewGuid(), BranchId = Guid.NewGuid(), CurrentBalance = 500_000m };
        _cashbookRepo.Tills.Add(till);
        _tillRepo.Items.Add(till);

        var response = await _service.DisburseCashAsync(till.Id, FinanceAccountType.Till, 200_000m, "EXPENSE", Guid.NewGuid(), "REF02", "Mua băng keo");

        Assert.NotNull(response);
        Assert.Equal(300_000m, till.CurrentBalance);
        Assert.Equal(300_000m, response.BalanceAfter);
        Assert.StartsWith("HASH-", response.AuditHash);
        Assert.Single(_cashbookRepo.Items);
    }

    [Fact]
    public async Task ReceiveCashAsync_ShouldIncreaseBalanceAndComputeAuditHash()
    {
        var safe = new BranchSafe { Id = Guid.NewGuid(), BranchId = Guid.NewGuid(), CurrentBalance = 1_000_000m };
        _cashbookRepo.Safes.Add(safe);
        _safeRepo.Items.Add(safe);

        var response = await _service.ReceiveCashAsync(safe.Id, FinanceAccountType.Safe, 500_000m, "CAPITAL", Guid.NewGuid(), "REF03", "Chủ rót thêm vốn");

        Assert.NotNull(response);
        Assert.Equal(1_500_000m, safe.CurrentBalance);
        Assert.Equal(1_500_000m, response.BalanceAfter);
        Assert.StartsWith("HASH-", response.AuditHash);
        Assert.Single(_cashbookRepo.Items);
    }

    [Fact]
    public async Task GetEntriesAsync_ShouldFilterAndPageCorrectly()
    {
        var accountId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        _cashbookRepo.Items.Add(new CashbookEntry { Id = Guid.NewGuid(), AccountId = accountId, BranchId = branchId, OccurredAtUtc = DateTime.UtcNow.AddMinutes(-10), AuditHash = "H1", EntryCode = "C1", Description = "D1" });
        _cashbookRepo.Items.Add(new CashbookEntry { Id = Guid.NewGuid(), AccountId = accountId, BranchId = branchId, OccurredAtUtc = DateTime.UtcNow.AddMinutes(-5), AuditHash = "H2", EntryCode = "C2", Description = "D2" });

        var entries = await _service.GetEntriesAsync(accountId, branchId, null, null, 1, 10);
        Assert.Equal(2, entries.Count);
    }

    [Fact]
    public async Task GetOrCreateTillAndSafe_ShouldWorkCorrectly()
    {
        var branchId = Guid.NewGuid();
        var till = await _service.GetOrCreateTillAsync(branchId, "Quầy VIP");
        Assert.NotNull(till);
        Assert.Equal("Quầy VIP", till.TillName);

        var existingTill = await _service.GetOrCreateTillAsync(branchId, "Quầy VIP");
        Assert.Equal(till.Id, existingTill.Id);

        var safe = await _service.GetOrCreateSafeAsync(branchId);
        Assert.NotNull(safe);
        var existingSafe = await _service.GetOrCreateSafeAsync(branchId);
        Assert.Equal(safe.Id, existingSafe.Id);
    }

    [Fact]
    public async Task DisburseAndReceive_FromBankAndSafe_ShouldUpdateBalances()
    {
        var bank = new TenantBankAccount { Id = Guid.NewGuid(), CurrentBalance = 10_000_000m };
        var safe = new BranchSafe { Id = Guid.NewGuid(), CurrentBalance = 5_000_000m };
        _cashbookRepo.BankAccounts.Add(bank);
        _cashbookRepo.Safes.Add(safe);
        _bankRepo.Items.Add(bank);
        _safeRepo.Items.Add(safe);

        var disburseSafe = await _service.DisburseCashAsync(safe.Id, FinanceAccountType.Safe, 1_000_000m, "RENT", null, "REF-R", "Trả tiền thuê nhà");
        Assert.Equal(4_000_000m, safe.CurrentBalance);

        var disburseBank = await _service.DisburseCashAsync(bank.Id, FinanceAccountType.Bank, 2_000_000m, "TAX", null, "REF-T", "Nộp thuế điện tử");
        Assert.Equal(8_000_000m, bank.CurrentBalance);

        var receiveBank = await _service.ReceiveCashAsync(bank.Id, FinanceAccountType.Bank, 3_000_000m, "TRANSFER", null, "REF-B", "Chuyển khoản nộp tiền");
        Assert.Equal(11_000_000m, bank.CurrentBalance);
    }
}
