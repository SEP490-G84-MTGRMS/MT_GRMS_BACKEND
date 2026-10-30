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

public class CustomerDebtFifoTests
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

        public Task<CustomerDebtProfile?> GetProfileWithLockAsync(Guid customerId, CancellationToken ct = default) =>
            Task.FromResult(Items.FirstOrDefault(p => p.CustomerId == customerId));

        public Task<List<CustomerDebtInvoice>> GetOpenInvoicesAsync(Guid customerId, CancellationToken ct = default) =>
            Task.FromResult(Invoices.Where(i => i.CustomerId == customerId && (i.Status == DebtInvoiceStatus.Unpaid || i.Status == DebtInvoiceStatus.PartiallyPaid)).OrderBy(i => i.CreatedAt).ToList());

        public Task<CustomerDebtInvoice?> GetInvoiceByOrderIdAsync(Guid orderId, CancellationToken ct = default) =>
            Task.FromResult(Invoices.FirstOrDefault(i => i.OrderId == orderId));

        public Task<bool> HasOverdueInvoicesAsync(Guid customerId, DateTime asOfUtc, CancellationToken ct = default) =>
            Task.FromResult(false);
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

    public CustomerDebtFifoTests()
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
    public async Task RepayDebtAsync_FIFO_ShouldPayOldestInvoiceFirst()
    {
        var customerId = Guid.NewGuid();
        var profile = new CustomerDebtProfile { CustomerId = customerId, CurrentDebtBalance = 1_500_000m };
        _debtRepo.Items.Add(profile);

        // Giả lập 3 hóa đơn nợ: 500k (cũ nhất), 400k (ở giữa), 600k (mới nhất)
        var inv1 = new CustomerDebtInvoice { Id = Guid.NewGuid(), CustomerId = customerId, OrderCode = "ORD-01", TotalDebtAmount = 500_000m, RemainingDebtAmount = 500_000m, Status = DebtInvoiceStatus.Unpaid, CreatedAt = DateTime.UtcNow.AddDays(-10) };
        var inv2 = new CustomerDebtInvoice { Id = Guid.NewGuid(), CustomerId = customerId, OrderCode = "ORD-02", TotalDebtAmount = 400_000m, RemainingDebtAmount = 400_000m, Status = DebtInvoiceStatus.Unpaid, CreatedAt = DateTime.UtcNow.AddDays(-5) };
        var inv3 = new CustomerDebtInvoice { Id = Guid.NewGuid(), CustomerId = customerId, OrderCode = "ORD-03", TotalDebtAmount = 600_000m, RemainingDebtAmount = 600_000m, Status = DebtInvoiceStatus.Unpaid, CreatedAt = DateTime.UtcNow.AddDays(-1) };

        _debtRepo.Invoices.AddRange(new[] { inv1, inv2, inv3 });

        // Khách trả 700.000đ
        var request = new RepayDebtRequest(customerId, 700_000m, FinanceAccountType.Till, Guid.NewGuid(), "REC-01", "Trả nợ đợt 1");
        var result = await _service.RepayDebtAsync(request);

        // Hóa đơn 1 (500k) được tất toán hoàn toàn
        Assert.Equal(0, inv1.RemainingDebtAmount);
        Assert.Equal(DebtInvoiceStatus.FullyPaid, inv1.Status);

        // Hóa đơn 2 (400k) được trả 200k, còn nợ 200k
        Assert.Equal(200_000m, inv2.RemainingDebtAmount);
        Assert.Equal(DebtInvoiceStatus.PartiallyPaid, inv2.Status);

        // Hóa đơn 3 (600k) chưa được trả đồng nào
        Assert.Equal(600_000m, inv3.RemainingDebtAmount);
        Assert.Equal(DebtInvoiceStatus.Unpaid, inv3.Status);

        // Tổng nợ còn lại: 200k + 600k = 800k (1.5tr - 700k)
        Assert.Equal(800_000m, profile.CurrentDebtBalance);
        Assert.Equal(800_000m, result.RemainingDebt);
        Assert.Equal(2, result.Allocations.Count);
    }

    [Fact]
    public async Task RepayDebtAsync_FIFO_ExcessPayment_ShouldCreditPrepayment()
    {
        var customerId = Guid.NewGuid();
        var profile = new CustomerDebtProfile { CustomerId = customerId, CurrentDebtBalance = 500_000m, PrepaymentBalance = 0 };
        _debtRepo.Items.Add(profile);

        var inv = new CustomerDebtInvoice { Id = Guid.NewGuid(), CustomerId = customerId, OrderCode = "ORD-01", TotalDebtAmount = 500_000m, RemainingDebtAmount = 500_000m, Status = DebtInvoiceStatus.Unpaid, CreatedAt = DateTime.UtcNow.AddDays(-2) };
        _debtRepo.Invoices.Add(inv);

        // Nợ 500k nhưng trả 600k
        var request = new RepayDebtRequest(customerId, 600_000m, FinanceAccountType.Till, Guid.NewGuid(), "REC-02", "Trả dư tiền");
        var result = await _service.RepayDebtAsync(request);

        Assert.Equal(0, profile.CurrentDebtBalance);
        Assert.Equal(100_000m, profile.PrepaymentBalance);
        Assert.Equal(100_000m, result.ExcessPrepayment);
    }

    [Fact]
    public async Task RepayDebtAsync_ShouldSynchronizeWithCashbookReceipt()
    {
        var customerId = Guid.NewGuid();
        var profile = new CustomerDebtProfile { CustomerId = customerId, CurrentDebtBalance = 300_000m };
        _debtRepo.Items.Add(profile);

        var inv = new CustomerDebtInvoice { Id = Guid.NewGuid(), CustomerId = customerId, OrderCode = "ORD-01", TotalDebtAmount = 300_000m, RemainingDebtAmount = 300_000m, Status = DebtInvoiceStatus.Unpaid };
        _debtRepo.Invoices.Add(inv);

        var request = new RepayDebtRequest(customerId, 300_000m, FinanceAccountType.Till, Guid.NewGuid(), "REC-03", "Thu nợ quầy");
        await _service.RepayDebtAsync(request);

        Assert.Equal(300_000m, _cashbookService.ReceivedCashAmount);
    }

    [Fact]
    public async Task OffsetGoodsReturnDebtAsync_ShouldOffsetOriginalInvoiceFirst()
    {
        var customerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var profile = new CustomerDebtProfile { CustomerId = customerId, CurrentDebtBalance = 800_000m };
        _debtRepo.Items.Add(profile);

        var invOriginal = new CustomerDebtInvoice { Id = Guid.NewGuid(), CustomerId = customerId, OrderId = orderId, OrderCode = "ORD-ORIGINAL", TotalDebtAmount = 500_000m, RemainingDebtAmount = 500_000m, Status = DebtInvoiceStatus.Unpaid };
        _debtRepo.Invoices.Add(invOriginal);

        // Trả hàng trị giá 300k của chính đơn này
        var request = new OffsetGoodsReturnDebtRequest(customerId, orderId, Guid.NewGuid(), 300_000m, false, null, "Trả hàng lỗi");
        var response = await _service.OffsetGoodsReturnDebtAsync(request);

        Assert.Equal(200_000m, invOriginal.RemainingDebtAmount);
        Assert.Equal(500_000m, profile.CurrentDebtBalance);
        Assert.Equal(500_000m, response.BalanceAfter);
    }

    [Fact]
    public async Task OffsetGoodsReturnDebtAsync_WhenReturnExceedsOriginalInvoice_ShouldOffsetOtherInvoicesFIFO()
    {
        var customerId = Guid.NewGuid();
        var orderId1 = Guid.NewGuid();
        var orderId2 = Guid.NewGuid();
        var profile = new CustomerDebtProfile { CustomerId = customerId, CurrentDebtBalance = 900_000m };
        _debtRepo.Items.Add(profile);

        var inv1 = new CustomerDebtInvoice { Id = Guid.NewGuid(), CustomerId = customerId, OrderId = orderId1, OrderCode = "ORD-01", TotalDebtAmount = 500_000m, RemainingDebtAmount = 500_000m, Status = DebtInvoiceStatus.Unpaid, CreatedAt = DateTime.UtcNow.AddDays(-5) };
        var inv2 = new CustomerDebtInvoice { Id = Guid.NewGuid(), CustomerId = customerId, OrderId = orderId2, OrderCode = "ORD-02", TotalDebtAmount = 400_000m, RemainingDebtAmount = 400_000m, Status = DebtInvoiceStatus.Unpaid, CreatedAt = DateTime.UtcNow.AddDays(-1) };
        _debtRepo.Invoices.AddRange(new[] { inv1, inv2 });

        // Trả hàng đơn 1 trị giá 700k (vượt 500k nợ đơn 1) -> Cấn trừ 500k đơn 1, cấn trừ 200k đơn 2
        var request = new OffsetGoodsReturnDebtRequest(customerId, orderId1, Guid.NewGuid(), 700_000m, false, null, "Trả hàng toàn bộ");
        var response = await _service.OffsetGoodsReturnDebtAsync(request);

        Assert.Equal(0, inv1.RemainingDebtAmount);
        Assert.Equal(DebtInvoiceStatus.FullyPaid, inv1.Status);

        Assert.Equal(200_000m, inv2.RemainingDebtAmount);
        Assert.Equal(DebtInvoiceStatus.PartiallyPaid, inv2.Status);

        Assert.Equal(200_000m, profile.CurrentDebtBalance);
    }

    [Fact]
    public async Task OffsetGoodsReturnDebtAsync_WhenCustomerDebtBecomesZeroAndRefundRequested_ShouldDisburseCash()
    {
        var customerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var profile = new CustomerDebtProfile { CustomerId = customerId, CurrentDebtBalance = 300_000m };
        _debtRepo.Items.Add(profile);

        var inv = new CustomerDebtInvoice { Id = Guid.NewGuid(), CustomerId = customerId, OrderId = orderId, OrderCode = "ORD-01", TotalDebtAmount = 300_000m, RemainingDebtAmount = 300_000m, Status = DebtInvoiceStatus.Unpaid };
        _debtRepo.Invoices.Add(inv);

        var tillId = Guid.NewGuid();
        // Trả hàng 500k (nợ chỉ có 300k) và yêu cầu hoàn 200k tiền mặt
        var request = new OffsetGoodsReturnDebtRequest(customerId, orderId, Guid.NewGuid(), 500_000m, true, tillId, "Trả hàng hoàn tiền thừa");
        var response = await _service.OffsetGoodsReturnDebtAsync(request);

        Assert.Equal(0, profile.CurrentDebtBalance);
        Assert.Equal(0, profile.PrepaymentBalance);
        Assert.Equal(200_000m, _cashbookService.DisbursedCashAmount);
    }

    [Fact]
    public async Task OffsetGoodsReturnDebtAsync_WhenCustomerDebtBecomesZeroAndNoRefund_ShouldCreditPrepayment()
    {
        var customerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var profile = new CustomerDebtProfile { CustomerId = customerId, CurrentDebtBalance = 300_000m, PrepaymentBalance = 0 };
        _debtRepo.Items.Add(profile);

        var inv = new CustomerDebtInvoice { Id = Guid.NewGuid(), CustomerId = customerId, OrderId = orderId, OrderCode = "ORD-01", TotalDebtAmount = 300_000m, RemainingDebtAmount = 300_000m, Status = DebtInvoiceStatus.Unpaid };
        _debtRepo.Invoices.Add(inv);

        // Trả hàng 500k, không hoàn tiền mặt -> 200k thừa vào Prepayment
        var request = new OffsetGoodsReturnDebtRequest(customerId, orderId, Guid.NewGuid(), 500_000m, false, null, "Trả hàng để tiền mua sau");
        var response = await _service.OffsetGoodsReturnDebtAsync(request);

        Assert.Equal(0, profile.CurrentDebtBalance);
        Assert.Equal(200_000m, profile.PrepaymentBalance);
    }

    [Fact]
    public async Task GetLedgerAsync_ShouldReturnPaginatedEntries()
    {
        var customerId = Guid.NewGuid();
        _entryRepo.Items.Add(new CustomerDebtEntry { CustomerId = customerId, EntryCode = "E1", Amount = 100_000m, OccurredAtUtc = DateTime.UtcNow.AddMinutes(-5) });
        _entryRepo.Items.Add(new CustomerDebtEntry { CustomerId = customerId, EntryCode = "E2", Amount = 200_000m, OccurredAtUtc = DateTime.UtcNow.AddMinutes(-1) });

        var entries = await _service.GetLedgerAsync(customerId, 1, 10);
        Assert.Equal(2, entries.Count);
        Assert.Equal("E2", entries.First().EntryCode); // Mới nhất lên đầu
    }
}

