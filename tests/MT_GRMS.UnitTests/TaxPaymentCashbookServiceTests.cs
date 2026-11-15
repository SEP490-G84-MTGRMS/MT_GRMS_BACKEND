using System.Linq.Expressions;
using MT_GRMS.Application.Cashbook;
using MT_GRMS.Application.Cashbook.Dtos;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Common.Services;
using MT_GRMS.Application.Tax;
using MT_GRMS.Domain.Common;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Domain.Exceptions;

namespace MT_GRMS.UnitTests;

public class TaxPaymentCashbookServiceTests
{
    [Fact]
    public async Task RecordTaxPaymentAsync_WhenActorIsNotOwnerOrAdmin_RejectsBeforeMutation()
    {
        var tenantContext = new FakeTenantContext { CurrentRole = "EMPLOYEE" };
        var service = CreateService(tenantContext, out _, out _, out _);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.RecordTaxPaymentAsync(
            new RecordTaxPaymentRequest(Guid.NewGuid(), 100_000m, "Kho bạc", "TAX-REF-001", null, null, null)));
    }

    [Fact]
    public async Task RecordTaxPaymentAsync_WhenSourceIsTill_RejectsPayment()
    {
        var tenantContext = new FakeTenantContext();
        var account = new FinanceAccount
        {
            Id = Guid.NewGuid(),
            TenantId = tenantContext.CurrentTenantId,
            BranchId = tenantContext.CurrentBranchId,
            AccountType = FinanceAccountType.Till,
            AccountName = "Két thu ngân",
            CurrentBalance = 1_000_000m,
            IsActive = true
        };
        var service = CreateService(tenantContext, out var cashbookRepo, out _, out _);
        cashbookRepo.Accounts.Add(account);

        var exception = await Assert.ThrowsAsync<BusinessException>(() => service.RecordTaxPaymentAsync(
            new RecordTaxPaymentRequest(account.Id, 100_000m, "Kho bạc", "TAX-REF-002", null, null, null)));

        Assert.Equal("TAX_PAYMENT_ACCOUNT_DENIED", exception.ErrorCode);
        Assert.Equal(1_000_000m, account.CurrentBalance);
    }

    [Fact]
    public async Task RecordTaxPaymentAsync_CreatesTaxVoucherAndAppendOnlyLedgerEntryWithoutChangingSnapshot()
    {
        var tenantContext = new FakeTenantContext();
        var snapshot = new HouseholdTaxSnapshot
        {
            Id = Guid.NewGuid(),
            TenantId = tenantContext.CurrentTenantId,
            BranchId = tenantContext.CurrentBranchId,
            Year = 2026,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 1,
            TotalTaxAmount = 750_000m,
            FinalizedBy = "owner"
        };
        var account = new FinanceAccount
        {
            Id = Guid.NewGuid(),
            TenantId = tenantContext.CurrentTenantId,
            BranchId = tenantContext.CurrentBranchId,
            AccountType = FinanceAccountType.Bank,
            AccountName = "Ngân hàng cửa hàng",
            CurrentBalance = 2_000_000m,
            IsActive = true
        };
        var service = CreateService(tenantContext, out var cashbookRepo, out var accountRepo, out var expenseRepo, snapshot);
        cashbookRepo.Accounts.Add(account);
        accountRepo.Items.Add(account);

        var result = await service.RecordTaxPaymentAsync(new RecordTaxPaymentRequest(
            account.Id,
            500_000m,
            "Kho bạc Nhà nước",
            "TAX-REF-003",
            "https://evidence.example/tax-receipt.pdf",
            snapshot.Id,
            "Nộp thuế quý 1"));

        var voucher = Assert.Single(expenseRepo.Items);
        Assert.Equal(FinancialNature.TaxPayment, voucher.Nature);
        Assert.Equal(ExpenseVoucherStatus.Paid, voucher.Status);
        Assert.Equal(snapshot.Id, voucher.HouseholdTaxSnapshotId);
        Assert.Equal(account.Id, voucher.PaymentSourceAccountId);
        Assert.Equal(1_500_000m, account.CurrentBalance);

        var entry = Assert.Single(cashbookRepo.Entries);
        Assert.Equal("TAX_PAYMENT", entry.SourceType);
        Assert.Equal(voucher.Id, entry.SourceId);
        Assert.Equal(CashbookEntryType.Payment, entry.EntryType);
        Assert.Equal(1_500_000m, entry.BalanceAfter);
        Assert.Equal(snapshot.Id, result.HouseholdTaxSnapshotId);
        Assert.Equal(750_000m, snapshot.TotalTaxAmount);
    }

    private static CashbookService CreateService(
        FakeTenantContext tenantContext,
        out FakeCashbookRepository cashbookRepo,
        out FakeRepository<FinanceAccount> accountRepo,
        out FakeRepository<ExpenseVoucher> expenseRepo,
        HouseholdTaxSnapshot? snapshot = null)
    {
        cashbookRepo = new FakeCashbookRepository();
        accountRepo = new FakeRepository<FinanceAccount>();
        expenseRepo = new FakeRepository<ExpenseVoucher>();
        var taxRepo = new FakeTaxRepository(snapshot);

        return new CashbookService(
            cashbookRepo,
            accountRepo,
            new FakeRepository<CashInTransit>(),
            new FakeRepository<ShiftZReport>(),
            new FakeAuditHashService(),
            tenantContext,
            new FakeUnitOfWork(),
            expenseRepo,
            taxRepo);
    }

    private sealed class FakeTenantContext : ITenantContext
    {
        public string CurrentTenantId { get; set; } = "tenant-tax-payment";
        public Guid CurrentUserId { get; set; } = Guid.NewGuid();
        public Guid? CurrentBranchId { get; set; } = Guid.NewGuid();
        public string CurrentRole { get; set; } = "STORE_OWNER";
        public void SetContext(string tenantId, Guid userId, Guid? branchId) => (CurrentTenantId, CurrentUserId, CurrentBranchId) = (tenantId, userId, branchId);
        public void SetRole(string? role) => CurrentRole = role?.Trim() ?? string.Empty;
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);
    }

    private sealed class FakeAuditHashService : IAuditHashService
    {
        public string ComputeEntryHash(string previousHash, CashbookEntry entry) => $"hash:{previousHash}:{entry.Id}";
        public bool VerifyEntryHash(string previousHash, CashbookEntry entry) => true;
    }

    private class FakeRepository<T> : IRepository<T> where T : BaseTenantEntity
    {
        public List<T> Items { get; } = [];
        public Task<T?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(Items.SingleOrDefault(x => x.Id == id));
        public Task<IReadOnlyList<T>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<T>>(Items);
        public Task<IReadOnlyList<T>> FindAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<T>>(Items.AsQueryable().Where(predicate).ToList());
        public Task AddAsync(T entity, CancellationToken cancellationToken = default)
        {
            Items.Add(entity);
            return Task.CompletedTask;
        }
        public void Update(T entity) { }
        public void Delete(T entity) => Items.Remove(entity);
    }

    private sealed class FakeCashbookRepository : FakeRepository<CashbookEntry>, ICashbookRepository
    {
        public List<FinanceAccount> Accounts { get; } = [];
        public List<CashbookEntry> Entries => Items;
        public Task<FinanceAccount?> GetAccountWithLockAsync(Guid accountId, CancellationToken ct = default) => Task.FromResult(Accounts.SingleOrDefault(a => a.Id == accountId));
        public Task<string> GetLastAuditHashAsync(Guid accountId, CancellationToken ct = default) => Task.FromResult(Items.LastOrDefault(e => e.AccountId == accountId)?.AuditHash ?? "GENESIS_HASH");
        public Task<ShiftZReport?> GetActiveShiftByTillIdAsync(Guid tillId, CancellationToken ct = default) => Task.FromResult<ShiftZReport?>(null);
        public Task<ShiftZReport?> GetLastClosedShiftByTillIdAsync(Guid tillId, CancellationToken ct = default) => Task.FromResult<ShiftZReport?>(null);
    }

    private sealed class FakeTaxRepository(HouseholdTaxSnapshot? snapshot) : IHouseholdTaxRepository
    {
        public Task<HouseholdTaxSnapshot?> GetSnapshotByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(snapshot?.Id == id ? snapshot : null);
        public Task<decimal> GetAnnualAccumulatedRevenueAsync(int year, DateTime upToDateUtc, CancellationToken cancellationToken = default) => Task.FromResult(0m);
        public Task<(decimal GrossRevenue, decimal DiscountAmount, decimal ReturnAmount)> GetPeriodRevenueAsync(DateTime fromDateUtc, DateTime toDateUtc, Guid? branchId, CancellationToken cancellationToken = default) => Task.FromResult((0m, 0m, 0m));
        public Task<(decimal ReducedVatRevenue, decimal StandardVatRevenue)> GetPeriodRevenueSplitAsync(DateTime fromDateUtc, DateTime toDateUtc, Guid? branchId, CancellationToken cancellationToken = default) => Task.FromResult((0m, 0m));
        public Task<decimal> GetPeriodDeductibleExpensesAsync(DateTime fromDateUtc, DateTime toDateUtc, Guid? branchId, CancellationToken cancellationToken = default) => Task.FromResult(0m);
        public Task<List<ExpenseVoucher>> GetDeductibleExpenseVouchersAsync(DateTime fromDateUtc, DateTime toDateUtc, Guid? branchId, CancellationToken cancellationToken = default) => Task.FromResult(new List<ExpenseVoucher>());
        public Task<(decimal TotalCogs, bool IsEstimated)> GetPeriodCogsAsync(DateTime fromDateUtc, DateTime toDateUtc, Guid? branchId, CancellationToken cancellationToken = default) => Task.FromResult((0m, false));
        public Task<decimal> GetPeriodOpexAsync(DateTime fromDateUtc, DateTime toDateUtc, Guid? branchId, CancellationToken cancellationToken = default) => Task.FromResult(0m);
        public Task<List<MT_GRMS.Application.Tax.Dtos.AccountingBookEntryDto>> GetBookEntriesAsync(DateTime fromDateUtc, DateTime toDateUtc, Guid? branchId, HouseholdTaxGroup group, TaxCalculationMethod method, AccountingBookType? bookType, CancellationToken cancellationToken = default) => Task.FromResult(new List<MT_GRMS.Application.Tax.Dtos.AccountingBookEntryDto>());
        public Task<HouseholdTaxSnapshot?> GetSnapshotAsync(int year, TaxPeriodType periodType, int periodNumber, Guid? branchId, CancellationToken cancellationToken = default) => Task.FromResult<HouseholdTaxSnapshot?>(null);
        public Task<List<HouseholdTaxSnapshot>> GetSnapshotsAsync(int? year, Guid? branchId, CancellationToken cancellationToken = default) => Task.FromResult(new List<HouseholdTaxSnapshot>());
        public Task<MT_GRMS.Application.Tax.Dtos.HouseholdTaxpayerProfileDto> GetTaxpayerProfileAsync(Guid? branchId, CancellationToken cancellationToken = default) => Task.FromResult(new MT_GRMS.Application.Tax.Dtos.HouseholdTaxpayerProfileDto());
        public Task<HouseholdTaxSnapshot> AddSnapshotAsync(HouseholdTaxSnapshot taxSnapshot, CancellationToken cancellationToken = default) => Task.FromResult(taxSnapshot);
        public Task UpdateSnapshotAsync(HouseholdTaxSnapshot taxSnapshot, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<HouseholdTaxYearSetting?> GetYearSettingAsync(int year, CancellationToken cancellationToken = default) => Task.FromResult<HouseholdTaxYearSetting?>(null);
        public Task UpsertYearSettingAsync(HouseholdTaxYearSetting setting, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
