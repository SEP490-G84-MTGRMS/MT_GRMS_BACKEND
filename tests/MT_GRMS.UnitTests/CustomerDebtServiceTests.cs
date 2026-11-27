using MT_GRMS.Application.Cashbook;
using MT_GRMS.Application.Cashbook.Dtos;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Debt;
using MT_GRMS.Application.Debt.Dtos;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Entities.Sales;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Domain.Exceptions;
using System.Linq.Expressions;
using Xunit;

namespace MT_GRMS.UnitTests;

public class CustomerDebtServiceTests
{
    private sealed class Tenant : ITenantContext
    {
        public string CurrentTenantId => "tenant-test";
        public Guid CurrentUserId => Guid.NewGuid();
        public Guid? CurrentBranchId => null;
        public string CurrentRole { get; private set; } = "STORE_OWNER";
        public void SetContext(string tenantId, Guid userId, Guid? branchId) { }
        public void SetRole(string? role) => CurrentRole = role ?? "";
    }

    private sealed class UnitOfWork : IUnitOfWork
    {
        public int SaveCount { get; private set; }
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return Task.FromResult(1);
        }
    }

    private sealed class Customers : ICustomerDebtRepository
    {
        public Customer? Customer { get; set; }
        public int ReadOnlyCalls { get; private set; }
        public Task<Customer?> GetByIdReadOnlyAsync(Guid id, CancellationToken ct = default)
        {
            ReadOnlyCalls++;
            return Task.FromResult(Customer?.Id == id ? Customer : null);
        }
        public Task<Customer?> GetCustomerWithLockAsync(Guid id, CancellationToken ct = default) => Task.FromResult(Customer?.Id == id ? Customer : null);
        public Task<List<CustomerDebtLedgerItemResponse>> GetLedgerAsync(Guid id, int page, int pageSize, CancellationToken ct = default) => Task.FromResult(new List<CustomerDebtLedgerItemResponse>());
        public Task<Customer?> GetByIdAsync(Guid id, CancellationToken ct = default) => GetCustomerWithLockAsync(id, ct);
        public Task<IReadOnlyList<Customer>> GetAllAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Customer>>(Customer == null ? [] : [Customer]);
        public Task<IReadOnlyList<Customer>> FindAsync(Expression<Func<Customer, bool>> predicate, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Customer>>(Customer == null ? [] : [Customer]);
        public Task AddAsync(Customer entity, CancellationToken ct = default) { Customer = entity; return Task.CompletedTask; }
        public void Update(Customer entity) => Customer = entity;
        public void Delete(Customer entity) => Customer = null;
    }

    private sealed class Cashbook : ICashbookService
    {
        public decimal Received { get; private set; }
        public Guid? SourceId { get; private set; }
        public Task<CashbookEntryResponse> ReceiveCashAsync(Guid accountId, FinanceAccountType accountType, decimal amount, string sourceType, Guid? sourceId, string referenceCode, string description, CancellationToken ct = default)
        {
            Received += amount;
            SourceId = sourceId;
            return Task.FromResult(new CashbookEntryResponse(Guid.NewGuid(), "PT-001", accountId, accountType.ToString(), null, "Receipt", amount, amount, sourceType, sourceId, referenceCode, "HASH", description, DateTime.UtcNow));
        }
        public Task<CashbookEntryResponse> DisburseCashAsync(Guid accountId, FinanceAccountType accountType, decimal amount, string sourceType, Guid? sourceId, string referenceCode, string description, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ShiftResponse> OpenShiftAsync(OpenShiftRequest request, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ShiftResponse> CloseShiftZReportAsync(CloseShiftRequest request, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> ProcessCashDropAsync(CashDropRequest request, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<CashbookEntryResponse>> GetEntriesAsync(Guid? accountId, Guid? branchId, DateTime? fromDate, DateTime? toDate, int page = 1, int pageSize = 50, CashbookEntryType? entryType = null, string? sourceType = null, string? keyword = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<FinanceAccount> GetOrCreateTillAsync(Guid branchId, string tillName, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<FinanceAccount> GetOrCreateSafeAsync(Guid branchId, CancellationToken ct = default) => throw new NotImplementedException();
    }

    [Fact]
    public async Task Repayment_UpdatesTotalDebtAndCreatesOneReceipt()
    {
        var customer = new Customer { Id = Guid.NewGuid(), Name = "Khách", TotalDebt = 700_000m };
        var cashbook = new Cashbook();
        var uow = new UnitOfWork();
        var service = new CustomerDebtService(new Customers { Customer = customer }, cashbook, new Tenant(), uow);

        var result = await service.RepayDebtAsync(new RepayDebtRequest(customer.Id, 250_000m, FinanceAccountType.Safe, Guid.NewGuid(), null, null));

        Assert.Equal(450_000m, customer.TotalDebt);
        Assert.Equal(250_000m, cashbook.Received);
        Assert.Equal(customer.Id, cashbook.SourceId);
        Assert.Equal(450_000m, result.RemainingDebt);
        Assert.Equal(1, uow.SaveCount);
    }

    [Fact]
    public async Task Repayment_ExceedingBalance_DoesNotChangeDebt()
    {
        var customer = new Customer { Id = Guid.NewGuid(), TotalDebt = 100_000m };
        var cashbook = new Cashbook();
        var service = new CustomerDebtService(new Customers { Customer = customer }, cashbook, new Tenant(), new UnitOfWork());
        var request = new RepayDebtRequest(customer.Id, 150_000m, FinanceAccountType.Safe, Guid.NewGuid(), null, null);

        var error = await Assert.ThrowsAsync<BusinessException>(() => service.RepayDebtAsync(request));

        Assert.Equal("REPAYMENT_EXCEEDS_DEBT", error.ErrorCode);
        Assert.Equal(100_000m, customer.TotalDebt);
        Assert.Equal(0, cashbook.Received);
    }

    [Fact]
    public async Task Repayment_RequiresStoreOwner()
    {
        var tenant = new Tenant();
        tenant.SetRole("Cashier");
        var service = new CustomerDebtService(new Customers(), new Cashbook(), tenant, new UnitOfWork());
        var request = new RepayDebtRequest(Guid.NewGuid(), 1m, FinanceAccountType.Safe, Guid.NewGuid(), null, null);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.RepayDebtAsync(request));
    }

    [Fact]
    public async Task GetProfile_UsesReadOnlyRepositoryMethod()
    {
        var customer = new Customer { Id = Guid.NewGuid(), Name = "Khách", TotalDebt = 300_000m };
        var repo = new Customers { Customer = customer };
        var service = new CustomerDebtService(repo, new Cashbook(), new Tenant(), new UnitOfWork());

        var profile = await service.GetProfileAsync(customer.Id);

        Assert.Equal(300_000m, profile.CurrentDebtBalance);
        Assert.Equal(1, repo.ReadOnlyCalls);
    }
}
