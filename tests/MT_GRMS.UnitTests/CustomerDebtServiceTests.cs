using System.Linq.Expressions;
using MT_GRMS.Application.Cashbook;
using MT_GRMS.Application.Cashbook.Dtos;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Debt;
using MT_GRMS.Application.Debt.Dtos;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Domain.Exceptions;
using Xunit;

namespace MT_GRMS.UnitTests;

public class CustomerDebtServiceTests
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

    private class FakeCustomerDebtRepository : FakeGenericRepository<CustomerDebtProfile>, ICustomerDebtRepository
    {
        public List<CustomerDebtInvoice> Invoices { get; } = new();
        public bool SimulateOverdue { get; set; }

        public Task<CustomerDebtProfile?> GetProfileWithLockAsync(Guid customerId, CancellationToken ct = default) =>
            Task.FromResult(Items.FirstOrDefault(p => p.CustomerId == customerId));

        public Task<List<CustomerDebtInvoice>> GetOpenInvoicesAsync(Guid customerId, CancellationToken ct = default) =>
            Task.FromResult(Invoices.Where(i => i.CustomerId == customerId && (i.Status == DebtInvoiceStatus.Unpaid || i.Status == DebtInvoiceStatus.PartiallyPaid)).OrderBy(i => i.CreatedAt).ToList());

        public Task<CustomerDebtInvoice?> GetInvoiceByOrderIdAsync(Guid orderId, CancellationToken ct = default) =>
            Task.FromResult(Invoices.FirstOrDefault(i => i.OrderId == orderId));

        public Task<bool> HasOverdueInvoicesAsync(Guid customerId, DateTime asOfUtc, CancellationToken ct = default) =>
            Task.FromResult(SimulateOverdue || Invoices.Any(i => i.CustomerId == customerId && (i.Status == DebtInvoiceStatus.Unpaid || i.Status == DebtInvoiceStatus.PartiallyPaid) && i.DueDate < asOfUtc));
    }

    private class FakeCashbookService : ICashbookService
    {
        public decimal ReceivedCashAmount { get; private set; }
        public decimal DisbursedCashAmount { get; private set; }

        public Task<CashbookEntryResponse> ReceiveCashAsync(Guid accountId, FinanceAccountType accountType, decimal amount, string sourceType, Guid? sourceId, string referenceCode, string description, CancellationToken ct = default)
        {
            ReceivedCashAmount += amount;
            return Task.FromResult(new CashbookEntryResponse(Guid.NewGuid(), "CB-01", accountId, accountType.ToString(), null, "Receipt", amount, 1_000_000m, sourceType, sourceId, referenceCode, "HASH-01", description, DateTime.UtcNow));
        }

        public Task<CashbookEntryResponse> DisburseCashAsync(Guid accountId, FinanceAccountType accountType, decimal amount, string sourceType, Guid? sourceId, string referenceCode, string description, CancellationToken ct = default)
        {
            DisbursedCashAmount += amount;
            return Task.FromResult(new CashbookEntryResponse(Guid.NewGuid(), "CB-02", accountId, accountType.ToString(), null, "Payment", amount, 500_000m, sourceType, sourceId, referenceCode, "HASH-02", description, DateTime.UtcNow));
        }

        public Task<ShiftResponse> OpenShiftAsync(OpenShiftRequest request, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ShiftResponse> CloseShiftZReportAsync(CloseShiftRequest request, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> ProcessCashDropAsync(CashDropRequest request, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<CashInTransitResponse> CreateDepositToBankAsync(DepositToBankRequest request, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<CashInTransitResponse> ConfirmBankDepositAsync(ConfirmBankDepositRequest request, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<CashbookEntryResponse>> GetEntriesAsync(Guid? accountId, Guid? branchId, DateTime? fromDate, DateTime? toDate, int page = 1, int pageSize = 50, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<BranchCashTill> GetOrCreateTillAsync(Guid branchId, string tillName, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<BranchSafe> GetOrCreateSafeAsync(Guid branchId, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private readonly FakeCustomerDebtRepository _debtRepo = new();
    private readonly FakeGenericRepository<CustomerDebtInvoice> _invoiceRepo = new();
    private readonly FakeGenericRepository<CustomerDebtEntry> _entryRepo = new();
    private readonly FakeGenericRepository<CustomerDebtAllocation> _allocationRepo = new();
    private readonly FakeCashbookService _cashbookService = new();
    private readonly FakeTenantContext _tenantContext = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly CustomerDebtService _service;

    public CustomerDebtServiceTests()
    {
        _service = new CustomerDebtService(
            _debtRepo,
            _invoiceRepo,
            _entryRepo,
            _allocationRepo,
            _cashbookService,
            _tenantContext,
            _unitOfWork
        );
    }

    [Fact]
    public async Task RecordSaleDebtAsync_WhenWithinLimit_ShouldSucceedAndCreateInvoice()
    {
        var customerId = Guid.NewGuid();
        var profile = new CustomerDebtProfile { CustomerId = customerId, CreditLimit = 2_000_000m, CurrentDebtBalance = 500_000m };
        _debtRepo.Items.Add(profile);

        var request = new RecordSaleDebtRequest(customerId, Guid.NewGuid(), "ORD-001", Guid.NewGuid(), 300_000m, null, "Mua chịu đợt 1");
        var response = await _service.RecordSaleDebtAsync(request);

        Assert.NotNull(response);
        Assert.Equal(300_000m, response.RemainingDebtAmount);
        Assert.Equal(800_000m, profile.CurrentDebtBalance);
        Assert.Single(_entryRepo.Items);
    }

    [Fact]
    public async Task RecordSaleDebtAsync_WhenExceedsCreditLimit_ShouldThrowBusinessException()
    {
        var customerId = Guid.NewGuid();
        var profile = new CustomerDebtProfile { CustomerId = customerId, CreditLimit = 1_000_000m, CurrentDebtBalance = 800_000m };
        _debtRepo.Items.Add(profile);

        // Nợ hiện tại 800k + đơn mới 300k = 1.1tr > hạn mức 1tr
        var request = new RecordSaleDebtRequest(customerId, Guid.NewGuid(), "ORD-002", Guid.NewGuid(), 300_000m, null, "Mua vượt mức");
        var ex = await Assert.ThrowsAsync<BusinessException>(() => _service.RecordSaleDebtAsync(request));
        Assert.Equal("CREDIT_LIMIT_EXCEEDED", ex.ErrorCode);
    }

    [Fact]
    public async Task RecordSaleDebtAsync_WhenExceedsLimitWithManagerBypass_ShouldSucceed()
    {
        var customerId = Guid.NewGuid();
        var profile = new CustomerDebtProfile { CustomerId = customerId, CreditLimit = 1_000_000m, CurrentDebtBalance = 800_000m };
        _debtRepo.Items.Add(profile);

        var request = new RecordSaleDebtRequest(customerId, Guid.NewGuid(), "ORD-003", Guid.NewGuid(), 500_000m, "MGR-BYPASS-2026", "Quản lý duyệt bảo lãnh");
        var response = await _service.RecordSaleDebtAsync(request);

        Assert.NotNull(response);
        Assert.Equal(1_300_000m, profile.CurrentDebtBalance);
    }

    [Fact]
    public async Task RecordSaleDebtAsync_WhenHasOverdueInvoices_ShouldThrowBusinessException()
    {
        var customerId = Guid.NewGuid();
        var profile = new CustomerDebtProfile { CustomerId = customerId, CreditLimit = 5_000_000m, CurrentDebtBalance = 500_000m };
        _debtRepo.Items.Add(profile);
        _debtRepo.SimulateOverdue = true;

        var request = new RecordSaleDebtRequest(customerId, Guid.NewGuid(), "ORD-004", Guid.NewGuid(), 200_000m, null, "Khách có nợ quá hạn");
        var ex = await Assert.ThrowsAsync<BusinessException>(() => _service.RecordSaleDebtAsync(request));
        Assert.Equal("DEBT_OVERDUE_BLOCKED", ex.ErrorCode);
    }

    [Fact]
    public async Task RecordSaleDebtAsync_WhenHasOverdueWithManagerBypass_ShouldSucceed()
    {
        var customerId = Guid.NewGuid();
        var profile = new CustomerDebtProfile { CustomerId = customerId, CreditLimit = 5_000_000m, CurrentDebtBalance = 500_000m };
        _debtRepo.Items.Add(profile);
        _debtRepo.SimulateOverdue = true;

        var request = new RecordSaleDebtRequest(customerId, Guid.NewGuid(), "ORD-005", Guid.NewGuid(), 200_000m, "MGR-BYPASS-2026", "Quản lý duyệt bảo lãnh quá hạn");
        var response = await _service.RecordSaleDebtAsync(request);

        Assert.NotNull(response);
    }

    [Fact]
    public async Task RecordSaleDebtAsync_WhenCustomerHasPrepayment_ShouldOffsetPrepaymentFirst()
    {
        var customerId = Guid.NewGuid();
        var profile = new CustomerDebtProfile { CustomerId = customerId, CreditLimit = 2_000_000m, CurrentDebtBalance = 0, PrepaymentBalance = 200_000m };
        _debtRepo.Items.Add(profile);

        // Đơn nợ 500k, khách có sẵn 200k tiền đặt cọc -> chỉ ghi nợ 300k, Prepayment về 0
        var request = new RecordSaleDebtRequest(customerId, Guid.NewGuid(), "ORD-006", Guid.NewGuid(), 500_000m, null, "Mua cấn trừ tiền cọc");
        var response = await _service.RecordSaleDebtAsync(request);

        Assert.Equal(300_000m, response.RemainingDebtAmount);
        Assert.Equal(300_000m, profile.CurrentDebtBalance);
        Assert.Equal(0, profile.PrepaymentBalance);
    }

    [Fact]
    public async Task AdjustDebtAsync_WhenIncrease_ShouldIncreaseDebtBalance()
    {
        var customerId = Guid.NewGuid();
        var profile = new CustomerDebtProfile { CustomerId = customerId, CurrentDebtBalance = 1_000_000m };
        _debtRepo.Items.Add(profile);

        var request = new AdjustDebtRequest(customerId, 100_000m, true, "Bù sai sót tính thiếu", "MGR-ADJUST-2026");
        var response = await _service.AdjustDebtAsync(request);

        Assert.Equal(1_100_000m, profile.CurrentDebtBalance);
        Assert.Equal(1_100_000m, response.BalanceAfter);
    }

    [Fact]
    public async Task AdjustDebtAsync_WhenDecrease_ShouldDecreaseDebtBalance()
    {
        var customerId = Guid.NewGuid();
        var profile = new CustomerDebtProfile { CustomerId = customerId, CurrentDebtBalance = 1_000_000m };
        _debtRepo.Items.Add(profile);

        var request = new AdjustDebtRequest(customerId, 100_000m, false, "Giảm do tính nhầm", "MGR-ADJUST-2026");
        var response = await _service.AdjustDebtAsync(request);

        Assert.Equal(900_000m, profile.CurrentDebtBalance);
        Assert.Equal(900_000m, response.BalanceAfter);
    }

    [Fact]
    public async Task AdjustDebtAsync_WhenInvalidApprovalCode_ShouldThrowBusinessException()
    {
        var customerId = Guid.NewGuid();
        var profile = new CustomerDebtProfile { CustomerId = customerId, CurrentDebtBalance = 1_000_000m };
        _debtRepo.Items.Add(profile);

        var request = new AdjustDebtRequest(customerId, 100_000m, true, "Lỗi", "WRONG-CODE");
        var ex = await Assert.ThrowsAsync<BusinessException>(() => _service.AdjustDebtAsync(request));
        Assert.Equal("UNAUTHORIZED_ADJUSTMENT", ex.ErrorCode);
    }

    [Fact]
    public async Task WriteOffBadDebtAsync_WhenValidAmount_ShouldDecreaseDebtBalance()
    {
        var customerId = Guid.NewGuid();
        var profile = new CustomerDebtProfile { CustomerId = customerId, CurrentDebtBalance = 500_000m };
        _debtRepo.Items.Add(profile);

        var request = new WriteOffBadDebtRequest(customerId, 500_000m, "Khách chuyển đi nơi khác không đòi được", "https://docs.store.vn/bien-ban-xoa-no.pdf");
        var response = await _service.WriteOffBadDebtAsync(request);

        Assert.Equal(0, profile.CurrentDebtBalance);
        Assert.Equal(0, response.BalanceAfter);
        Assert.Equal(CustomerDebtType.BadDebtWriteOff.ToString(), response.EntryType);
    }

    [Fact]
    public async Task WriteOffBadDebtAsync_WhenAmountExceedsDebt_ShouldThrowBusinessException()
    {
        var customerId = Guid.NewGuid();
        var profile = new CustomerDebtProfile { CustomerId = customerId, CurrentDebtBalance = 300_000m };
        _debtRepo.Items.Add(profile);

        var request = new WriteOffBadDebtRequest(customerId, 500_000m, "Xóa quá tay", "link");
        var ex = await Assert.ThrowsAsync<BusinessException>(() => _service.WriteOffBadDebtAsync(request));
        Assert.Equal("INVALID_WRITEOFF", ex.ErrorCode);
    }

    [Fact]
    public async Task GetProfileAsync_ShouldReturnProfileWithOpenInvoices()
    {
        var customerId = Guid.NewGuid();
        var profile = new CustomerDebtProfile { CustomerId = customerId, CreditLimit = 3_000_000m, CurrentDebtBalance = 700_000m };
        var inv = new CustomerDebtInvoice { CustomerId = customerId, OrderCode = "ORD-99", TotalDebtAmount = 700_000m, RemainingDebtAmount = 700_000m, Status = DebtInvoiceStatus.Unpaid, DueDate = DateTime.UtcNow.AddDays(10) };
        _debtRepo.Items.Add(profile);
        _debtRepo.Invoices.Add(inv);

        var response = await _service.GetProfileAsync(customerId);
        Assert.NotNull(response);
        Assert.Equal(700_000m, response.CurrentDebtBalance);
        Assert.Single(response.OpenInvoices);
    }
}
