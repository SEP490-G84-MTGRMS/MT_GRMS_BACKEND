using System.Linq.Expressions;
using MT_GRMS.Application.Cashbook;
using MT_GRMS.Application.Cashbook.Dtos;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Expenses;
using MT_GRMS.Application.Expenses.Dtos;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Domain.Exceptions;
using Xunit;

namespace MT_GRMS.UnitTests;

/// <summary>
/// Unit tests for Deposit Tracking and Bidirectional Reference Code Workflow.
/// </summary>
public class DepositReferenceWorkflowTests
{
    private class FakeTenantContext : ITenantContext
    {
        public string CurrentTenantId { get; set; } = "tenant-dep-01";
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
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveChangesCount++;
            return Task.FromResult(1);
        }
    }

    private class FakeExpenseRepository : IExpenseRepository
    {
        public List<ExpenseVoucher> Items { get; } = new();

        public Task<ExpenseVoucher?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.FirstOrDefault(x => x.Id == id));

        public Task<IReadOnlyList<ExpenseVoucher>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult((IReadOnlyList<ExpenseVoucher>)Items.ToList());

        public Task<IReadOnlyList<ExpenseVoucher>> FindAsync(Expression<Func<ExpenseVoucher, bool>> predicate, CancellationToken cancellationToken = default) =>
            Task.FromResult((IReadOnlyList<ExpenseVoucher>)Items.AsQueryable().Where(predicate).ToList());

        public Task AddAsync(ExpenseVoucher entity, CancellationToken cancellationToken = default)
        {
            if (entity.Id == Guid.Empty) entity.Id = Guid.NewGuid();
            Items.Add(entity);
            return Task.CompletedTask;
        }

        public void Update(ExpenseVoucher entity) { }
        public void Delete(ExpenseVoucher entity) => Items.Remove(entity);

        public Task<decimal> GetShiftExpenseSumAsync(Guid branchId, DateTime shiftStartUtc, CancellationToken ct = default) =>
            Task.FromResult(0m);

        public Task<decimal> GetDailyExpenseSumAsync(Guid branchId, DateTime dateUtc, CancellationToken ct = default) =>
            Task.FromResult(0m);

        public Task<int> GetRecentVouchersCountAsync(Guid branchId, Guid createdByUserId, DateTime sinceUtc, decimal minAmount, CancellationToken ct = default) =>
            Task.FromResult(0);

        public Task<decimal> GetSameVendorDailySumAsync(Guid branchId, string beneficiaryName, string category, DateTime dateUtc, CancellationToken ct = default) =>
            Task.FromResult(0m);
    }

    private class FakeCashbookService : ICashbookService
    {
        public decimal TotalReceived { get; private set; }
        public decimal TotalDisbursed { get; private set; }

        public Task<CashbookEntryResponse> ReceiveCashAsync(Guid accountId, FinanceAccountType accountType, decimal amount, string sourceType, Guid? sourceId, string referenceCode, string description, CancellationToken ct = default)
        {
            TotalReceived += amount;
            return Task.FromResult(new CashbookEntryResponse(Guid.NewGuid(), "CB-REC", accountId, accountType.ToString(), null, "Receipt", amount, 1_000_000m, sourceType, sourceId, referenceCode, "HASH", description, DateTime.UtcNow));
        }

        public Task<CashbookEntryResponse> DisburseCashAsync(Guid accountId, FinanceAccountType accountType, decimal amount, string sourceType, Guid? sourceId, string referenceCode, string description, CancellationToken ct = default)
        {
            TotalDisbursed += amount;
            return Task.FromResult(new CashbookEntryResponse(Guid.NewGuid(), "CB-PAY", accountId, accountType.ToString(), null, "Payment", amount, 500_000m, sourceType, sourceId, referenceCode, "HASH", description, DateTime.UtcNow));
        }

        public Task<ShiftResponse> OpenShiftAsync(OpenShiftRequest request, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ShiftResponse> CloseShiftZReportAsync(CloseShiftRequest request, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> ProcessCashDropAsync(CashDropRequest request, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<CashInTransitResponse> CreateDepositToBankAsync(DepositToBankRequest request, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<CashInTransitResponse> ConfirmBankDepositAsync(ConfirmBankDepositRequest request, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<CashbookEntryResponse>> GetEntriesAsync(Guid? accountId, Guid? branchId, DateTime? fromDate, DateTime? toDate, int page = 1, int pageSize = 50, CashbookEntryType? entryType = null, string? sourceType = null, string? keyword = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<FinanceAccount> GetOrCreateTillAsync(Guid branchId, string tillName, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<FinanceAccount> GetOrCreateSafeAsync(Guid branchId, CancellationToken ct = default) => throw new NotImplementedException();
    }

    [Fact]
    public async Task MultiStageDepositRefund_FullLifecycle_ShouldSucceedAndPreventOverRefund()
    {
        var expenseRepo = new FakeExpenseRepository();
        var cashbookService = new FakeCashbookService();
        var tenantContext = new FakeTenantContext();
        var uow = new FakeUnitOfWork();
        var service = new ExpenseService(expenseRepo, cashbookService, tenantContext, uow);

        var tillId = Guid.NewGuid();
        var branchId = Guid.NewGuid();

        // 1. Tạo phiếu thu cọc 250k: "Bác A cọc 5 bình vỏ Lavie"
        var createDepReq = new CreateDepositRequest(
            branchId,
            "Bác A",
            250_000m,
            FinanceAccountType.Till,
            tillId,
            "Cọc 5 bình vỏ nước Lavie"
        );

        var depositVoucher = await service.CreateDepositAsync(createDepReq);

        Assert.Equal("Paid", depositVoucher.Status);
        Assert.Equal(FinancialNature.Deposit, depositVoucher.Nature);
        Assert.False(depositVoucher.IsTaxDeductible);
        Assert.Equal(250_000m, depositVoucher.Amount);
        Assert.Equal(0m, depositVoucher.RefundedAmount);
        Assert.False(depositVoucher.IsSettled);
        Assert.Equal(250_000m, cashbookService.TotalReceived);

        // 2. Tra cứu phiếu cọc chưa tất toán
        var searchResults = await service.SearchDepositsAsync("Bác A", branchId);
        Assert.Single(searchResults);
        Assert.Equal(250_000m, searchResults[0].RemainingAmount);

        // 3. Hoàn cọc đợt 1: Bác A trả 2 bình vỏ -> Hoàn 100k
        var refund1Req = new RefundDepositRequest(100_000m, FinanceAccountType.Till, tillId, "Trả 2 bình vỏ đợt 1");
        var refund1Voucher = await service.RefundDepositAsync(depositVoucher.Id, refund1Req);

        Assert.Equal(FinancialNature.RefundDeposit, refund1Voucher.Nature);
        Assert.Equal(depositVoucher.VoucherCode, refund1Voucher.ReferenceCode);
        Assert.Equal(100_000m, refund1Voucher.Amount);
        Assert.False(refund1Voucher.IsTaxDeductible);
        Assert.Equal(100_000m, cashbookService.TotalDisbursed);

        var depInRepo = await expenseRepo.GetByIdAsync(depositVoucher.Id);
        Assert.NotNull(depInRepo);
        Assert.Equal(100_000m, depInRepo.RefundedAmount);
        Assert.False(depInRepo.IsSettled);

        // 4. Hoàn cọc đợt 2: Cố hoàn 160k (vượt dư 150k còn lại) -> Bị chặn
        var overRefundReq = new RefundDepositRequest(160_000m, FinanceAccountType.Till, tillId, "Cố hoàn quá số dư");
        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.RefundDepositAsync(depositVoucher.Id, overRefundReq));
        Assert.Equal("EXCEEDS_REMAINING_DEPOSIT", ex.ErrorCode);

        // 5. Hoàn cọc đợt 2 đúng: Bác A trả 3 bình vỏ còn lại -> Hoàn 150k -> Tất toán
        var refund2Req = new RefundDepositRequest(150_000m, FinanceAccountType.Till, tillId, "Trả 3 bình vỏ đợt 2 - Tất toán");
        var refund2Voucher = await service.RefundDepositAsync(depositVoucher.Id, refund2Req);

        Assert.Equal(150_000m, refund2Voucher.Amount);
        Assert.Equal(250_000m, cashbookService.TotalDisbursed);
        Assert.Equal(250_000m, depInRepo.RefundedAmount);
        Assert.True(depInRepo.IsSettled);

        // 6. Cố hoàn lần 3 khi đã tất toán 100% -> Bị chặn
        var refund3Req = new RefundDepositRequest(50_000m, FinanceAccountType.Till, tillId, "Cố hoàn sau tất toán");
        var settledEx = await Assert.ThrowsAsync<BusinessException>(() => service.RefundDepositAsync(depositVoucher.Id, refund3Req));
        Assert.Equal("ALREADY_SETTLED", settledEx.ErrorCode);

        // 7. Tìm kiếm lại: Phiếu đã tất toán không xuất hiện trong danh sách cọc chờ hoàn
        var searchAfterSettled = await service.SearchDepositsAsync("Bác A", branchId);
        Assert.Empty(searchAfterSettled);
    }
}
