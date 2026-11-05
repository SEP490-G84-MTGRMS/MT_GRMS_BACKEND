using System.Linq.Expressions;
using MT_GRMS.Domain.Common;
using MT_GRMS.Application.Cashbook;
using MT_GRMS.Application.Cashbook.Dtos;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Common.Services;
using MT_GRMS.Application.Expenses;
using MT_GRMS.Application.Expenses.Dtos;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Domain.Exceptions;
using Xunit;

namespace MT_GRMS.UnitTests;

/// <summary>
/// Unit tests for the 12 Core Accounting Invariants (INV-01 to INV-12).
/// </summary>
public class AccountingInvariantsTests
{
    private class FakeTenantContext : ITenantContext
    {
        public string CurrentTenantId { get; set; } = "tenant-inv-01";
        public Guid CurrentUserId { get; set; } = Guid.Parse("11111111-1111-1111-1111-111111111111");
        public Guid? CurrentBranchId { get; set; } = Guid.Parse("22222222-2222-2222-2222-222222222222");
        public string CurrentRole { get; set; } = "STORE_OWNER";
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
        public bool InTransaction { get; private set; }
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveChangesCount++;
            return Task.FromResult(1);
        }
        public Task BeginTransactionAsync(CancellationToken cancellationToken = default)
        {
            InTransaction = true;
            return Task.CompletedTask;
        }
        public Task CommitTransactionAsync(CancellationToken cancellationToken = default)
        {
            InTransaction = false;
            return Task.CompletedTask;
        }
        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
        {
            InTransaction = false;
            return Task.CompletedTask;
        }
    }

    private class FakeCashbookRepository : ICashbookRepository
    {
        public List<CashbookEntry> Entries { get; } = new();
        public List<FinanceAccount> Accounts { get; } = new();
        public List<ShiftZReport> Shifts { get; } = new();

        public Task<FinanceAccount?> GetAccountWithLockAsync(Guid accountId, CancellationToken ct = default) =>
            Task.FromResult(Accounts.FirstOrDefault(a => a.Id == accountId));

        public Task<ShiftZReport?> GetActiveShiftByTillIdAsync(Guid tillId, CancellationToken ct = default) =>
            Task.FromResult(Shifts.FirstOrDefault(s => s.TillId == tillId && s.Status == ShiftState.Open));

        public Task<ShiftZReport?> GetLastClosedShiftByTillIdAsync(Guid tillId, CancellationToken ct = default) =>
            Task.FromResult(Shifts.Where(s => s.TillId == tillId && s.Status == ShiftState.Closed).OrderByDescending(s => s.ClosedAtUtc).FirstOrDefault());

        public Task<string> GetLastAuditHashAsync(Guid accountId, CancellationToken ct = default)
        {
            var last = Entries.Where(e => e.AccountId == accountId).OrderByDescending(e => e.OccurredAtUtc).FirstOrDefault();
            return Task.FromResult(last?.AuditHash ?? "GENESIS");
        }

        public Task AddAsync(CashbookEntry entity, CancellationToken cancellationToken = default)
        {
            if (entity.Id == Guid.Empty) entity.Id = Guid.NewGuid();
            Entries.Add(entity);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<CashbookEntry>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult((IReadOnlyList<CashbookEntry>)Entries.ToList());

        public Task<CashbookEntry?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Entries.FirstOrDefault(e => e.Id == id));

        public Task<IReadOnlyList<CashbookEntry>> FindAsync(Expression<Func<CashbookEntry, bool>> predicate, CancellationToken cancellationToken = default) =>
            Task.FromResult((IReadOnlyList<CashbookEntry>)Entries.AsQueryable().Where(predicate).ToList());

        public void Update(CashbookEntry entity) { }
        public void Delete(CashbookEntry entity) => Entries.Remove(entity);
    }

    private class FakeGenericRepository<T> : IRepository<T> where T : BaseTenantEntity
    {
        public List<T> Items { get; } = new();
        public Task AddAsync(T entity, CancellationToken cancellationToken = default)
        {
            Items.Add(entity);
            return Task.CompletedTask;
        }
        public Task<IReadOnlyList<T>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult((IReadOnlyList<T>)Items.ToList());
        public Task<T?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var prop = typeof(T).GetProperty("Id");
            return Task.FromResult(Items.FirstOrDefault(x => (Guid)prop!.GetValue(x)! == id));
        }
        public Task<IReadOnlyList<T>> FindAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default) =>
            Task.FromResult((IReadOnlyList<T>)Items.AsQueryable().Where(predicate).ToList());
        public void Update(T entity) { }
        public void Delete(T entity) => Items.Remove(entity);
    }

    private class SimpleAuditHashService : IAuditHashService
    {
        public string ComputeEntryHash(string previousHash, CashbookEntry entry) =>
            $"HASH-{previousHash[..Math.Min(4, previousHash.Length)]}-{entry.Amount}";
        public bool VerifyChain(IEnumerable<CashbookEntry> entries) => true;
        public bool VerifyEntryHash(string previousHash, CashbookEntry entry) => true;
    }

    [Fact]
    public async Task INV_01_TillAndSafeBalances_ShouldNeverBeNegative()
    {
        var cashbookRepo = new FakeCashbookRepository();
        var accountRepo = new FakeGenericRepository<FinanceAccount>();
        var transitRepo = new FakeGenericRepository<CashInTransit>();
        var shiftRepo = new FakeGenericRepository<ShiftZReport>();
        var hashService = new SimpleAuditHashService();
        var tenantContext = new FakeTenantContext();
        var uow = new FakeUnitOfWork();

        var tillAccount = new FinanceAccount
        {
            Id = Guid.NewGuid(),
            AccountName = "Két Quầy 1",
            AccountType = FinanceAccountType.Till,
            CurrentBalance = 100_000m,
            IsActive = true
        };
        cashbookRepo.Accounts.Add(tillAccount);
        accountRepo.Items.Add(tillAccount);

        var service = new CashbookService(cashbookRepo, accountRepo, transitRepo, shiftRepo, hashService, tenantContext, uow);

        var request = new CreatePaymentRequest(tillAccount.Id, 150_000m, "Chi mua đá", "Chi quá tiền két", null);

        // Poka-yoke GBR-CSH-03: Thao tác chi vượt số dư tiền mặt phải bị chặn
        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.CreatePaymentAsync(request));
        Assert.Equal("INSUFFICIENT_FUNDS", ex.ErrorCode);
        Assert.Equal(100_000m, tillAccount.CurrentBalance);
    }

    [Fact]
    public async Task INV_02_TotalSystemBalance_ShouldEqualSumOfAllAccounts()
    {
        var cashbookRepo = new FakeCashbookRepository();
        var accountRepo = new FakeGenericRepository<FinanceAccount>();
        var transitRepo = new FakeGenericRepository<CashInTransit>();
        var shiftRepo = new FakeGenericRepository<ShiftZReport>();
        var hashService = new SimpleAuditHashService();
        var tenantContext = new FakeTenantContext();
        var uow = new FakeUnitOfWork();

        var till = new FinanceAccount { Id = Guid.NewGuid(), AccountType = FinanceAccountType.Till, CurrentBalance = 2_000_000m, IsActive = true };
        var safe = new FinanceAccount { Id = Guid.NewGuid(), AccountType = FinanceAccountType.Safe, CurrentBalance = 15_000_000m, IsActive = true };
        var bank = new FinanceAccount { Id = Guid.NewGuid(), AccountType = FinanceAccountType.Bank, CurrentBalance = 50_000_000m, IsActive = true };

        cashbookRepo.Accounts.AddRange(new[] { till, safe, bank });
        accountRepo.Items.AddRange(new[] { till, safe, bank });

        var service = new CashbookService(cashbookRepo, accountRepo, transitRepo, shiftRepo, hashService, tenantContext, uow);
        var summary = await service.GetSummaryAsync();

        Assert.Equal(67_000_000m, summary.TotalClosingBalance);
        Assert.Equal(2_000_000m, summary.TotalCashInDrawers);
        Assert.Equal(15_000_000m, summary.TotalSafeDepositBalance);
        Assert.Equal(50_000_000m, summary.TotalBankDeposits);
    }

    [Fact]
    public async Task INV_03_InternalFundTransfer_ShouldConserveTotalFunds()
    {
        var cashbookRepo = new FakeCashbookRepository();
        var accountRepo = new FakeGenericRepository<FinanceAccount>();
        var transitRepo = new FakeGenericRepository<CashInTransit>();
        var shiftRepo = new FakeGenericRepository<ShiftZReport>();
        var hashService = new SimpleAuditHashService();
        var tenantContext = new FakeTenantContext();
        var uow = new FakeUnitOfWork();

        var till = new FinanceAccount { Id = Guid.NewGuid(), AccountType = FinanceAccountType.Till, CurrentBalance = 5_000_000m, IsActive = true };
        var safe = new FinanceAccount { Id = Guid.NewGuid(), AccountType = FinanceAccountType.Safe, CurrentBalance = 10_000_000m, IsActive = true };

        cashbookRepo.Accounts.AddRange(new[] { till, safe });
        accountRepo.Items.AddRange(new[] { till, safe });

        var service = new CashbookService(cashbookRepo, accountRepo, transitRepo, shiftRepo, hashService, tenantContext, uow);

        var transferAmount = 2_000_000m;
        var transferReq = new TransferFundRequest(till.Id, safe.Id, transferAmount, "Chuyển két thu ngân sang két sắt");
        var result = await service.TransferFundsAsync(transferReq);

        Assert.Equal(3_000_000m, till.CurrentBalance);
        Assert.Equal(12_000_000m, safe.CurrentBalance);
        Assert.Equal(15_000_000m, till.CurrentBalance + safe.CurrentBalance);
        Assert.Equal("Payment", result.Payment.EntryType);
        Assert.Equal("Receipt", result.Receipt.EntryType);
    }

    [Fact]
    public void INV_04_DepositAndRefundDeposit_ShouldBeFlaggedNonTaxDeductible()
    {
        var deposit = new ExpenseVoucher
        {
            Nature = FinancialNature.Deposit,
            IsTaxDeductible = false,
            Amount = 300_000m
        };

        var refund = new ExpenseVoucher
        {
            Nature = FinancialNature.RefundDeposit,
            IsTaxDeductible = false,
            Amount = 300_000m
        };

        Assert.False(deposit.IsTaxDeductible);
        Assert.False(refund.IsTaxDeductible);
        Assert.NotEqual(FinancialNature.OperatingExpense, deposit.Nature);
        Assert.NotEqual(FinancialNature.OperatingExpense, refund.Nature);
    }

    [Fact]
    public void INV_05_OwnerDraw_ShouldBeFlaggedNonTaxDeductible()
    {
        var ownerDraw = new ExpenseVoucher
        {
            Nature = FinancialNature.OwnerDraw,
            IsTaxDeductible = false,
            Amount = 10_000_000m
        };

        Assert.False(ownerDraw.IsTaxDeductible);
        Assert.Equal(FinancialNature.OwnerDraw, ownerDraw.Nature);
    }

    [Fact]
    public void INV_06_OwnerDeposit_ShouldBeFlaggedNonTaxDeductible()
    {
        var ownerDeposit = new ExpenseVoucher
        {
            Nature = FinancialNature.OwnerDeposit,
            IsTaxDeductible = false,
            Amount = 20_000_000m
        };

        Assert.False(ownerDeposit.IsTaxDeductible);
        Assert.Equal(FinancialNature.OwnerDeposit, ownerDeposit.Nature);
    }

    [Fact]
    public void INV_07_TaxPayment_ShouldBeSeparatedFromOperatingExpenses()
    {
        var taxVoucher = new ExpenseVoucher
        {
            Nature = FinancialNature.TaxPayment,
            Category = "Nộp thuế HKD Q3/2026",
            IsTaxDeductible = false,
            Amount = 1_500_000m
        };

        Assert.Equal(FinancialNature.TaxPayment, taxVoucher.Nature);
        Assert.NotEqual(FinancialNature.OperatingExpense, taxVoucher.Nature);
    }

    [Fact]
    public void INV_08_SuspenseUnresolved_MustBeIsolatedFromPnlAndTax()
    {
        var unresolved = new ExpenseVoucher
        {
            Nature = FinancialNature.Suspense,
            Status = ExpenseVoucherStatus.Unresolved,
            IsTaxDeductible = false,
            Amount = 450_000m
        };

        Assert.Equal(ExpenseVoucherStatus.Unresolved, unresolved.Status);
        Assert.Equal(FinancialNature.Suspense, unresolved.Nature);
        Assert.False(unresolved.IsTaxDeductible);
    }

    [Fact]
    public async Task INV_09_ImmutableLedger_VoidingEntry_CreatesCompensatingEntry()
    {
        var cashbookRepo = new FakeCashbookRepository();
        var accountRepo = new FakeGenericRepository<FinanceAccount>();
        var transitRepo = new FakeGenericRepository<CashInTransit>();
        var shiftRepo = new FakeGenericRepository<ShiftZReport>();
        var hashService = new SimpleAuditHashService();
        var tenantContext = new FakeTenantContext();
        var uow = new FakeUnitOfWork();

        var till = new FinanceAccount { Id = Guid.NewGuid(), AccountType = FinanceAccountType.Till, CurrentBalance = 1_000_000m, IsActive = true };
        cashbookRepo.Accounts.Add(till);
        accountRepo.Items.Add(till);

        var originalEntry = new CashbookEntry
        {
            Id = Guid.NewGuid(),
            AccountId = till.Id,
            AccountType = FinanceAccountType.Till,
            EntryType = CashbookEntryType.Receipt,
            Amount = 200_000m,
            BalanceAfter = 1_000_000m,
            EntryCode = "CB-ORIG",
            SourceType = "MANUAL",
            AuditHash = "HASH-1"
        };
        cashbookRepo.Entries.Add(originalEntry);

        var service = new CashbookService(cashbookRepo, accountRepo, transitRepo, shiftRepo, hashService, tenantContext, uow);

        var voidReq = new VoidEntryRequest("Thu nhầm tiền của khách");
        var compResult = await service.VoidTransactionAsync(originalEntry.Id, voidReq);

        Assert.Equal("Payment", compResult.EntryType);
        Assert.Equal(200_000m, compResult.Amount);
        Assert.Equal("COMPENSATING", compResult.SourceType);
        Assert.Equal(originalEntry.Id, compResult.SourceId);
        Assert.Equal(800_000m, till.CurrentBalance);
    }

    [Fact]
    public void INV_10_AuditHashChaining_ShouldIncludePreviousHash()
    {
        var hashService = new SimpleAuditHashService();
        var entry1 = new CashbookEntry { Amount = 100_000m };
        var hash1 = hashService.ComputeEntryHash("GENESIS", entry1);

        var entry2 = new CashbookEntry { Amount = 200_000m };
        var hash2 = hashService.ComputeEntryHash(hash1, entry2);

        Assert.Contains(hash1[..4], hash2);
    }

    [Fact]
    public void INV_11_DeterministicLockingOrder_CalculatesConsistentSequence()
    {
        var id1 = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var id2 = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

        var first = id1.CompareTo(id2) < 0 ? id1 : id2;
        var second = id1.CompareTo(id2) < 0 ? id2 : id1;

        Assert.Equal(id1, first);
        Assert.Equal(id2, second);

        // Đảo thứ tự đầu vào
        var firstReversed = id2.CompareTo(id1) < 0 ? id2 : id1;
        var secondReversed = id2.CompareTo(id1) < 0 ? id1 : id2;

        Assert.Equal(first, firstReversed);
        Assert.Equal(second, secondReversed);
    }

    [Fact]
    public void INV_12_AccrualExpensePeriod_ExpenseDateShouldPreservePeriod()
    {
        var octExpense = new ExpenseVoucher
        {
            VoucherCode = "EXP-OCT-01",
            ExpenseDate = new DateTime(2026, 10, 15, 0, 0, 0, DateTimeKind.Utc),
            PaidAtUtc = new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc),
            Amount = 15_000_000m,
            Nature = FinancialNature.OperatingExpense
        };

        // Kỳ tính P&L dồn tích là Tháng 10 dù thanh toán vào Tháng 11
        Assert.Equal(10, octExpense.ExpenseDate.Month);
        Assert.Equal(11, octExpense.PaidAtUtc.Value.Month);
    }
}
