using System.Linq.Expressions;
using MT_GRMS.Application.Cashbook;
using MT_GRMS.Application.Cashbook.Dtos;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Expenses;
using MT_GRMS.Application.Expenses.Dtos;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Enums;
using Xunit;

namespace MT_GRMS.UnitTests;

/// <summary>
/// Unit tests for Cashier Shift Cap (C1 = 500k), Anti-Splitting, and Triage Inbox resolution.
/// </summary>
public class ShiftCapAntiSplittingTests
{
    private class FakeTenantContext : ITenantContext
    {
        public string CurrentTenantId { get; set; } = "tenant-shift-01";
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

        public Task<decimal> GetShiftExpenseSumAsync(Guid branchId, DateTime shiftStartUtc, CancellationToken ct = default)
        {
            var sum = Items.Where(e => e.BranchId == branchId &&
                                       e.Nature == FinancialNature.OperatingExpense &&
                                       e.Status != ExpenseVoucherStatus.Cancelled &&
                                       e.Status != ExpenseVoucherStatus.Reversed &&
                                       e.Status != ExpenseVoucherStatus.Unresolved)
                           .Sum(e => e.Amount);
            return Task.FromResult(sum);
        }

        public Task<decimal> GetDailyExpenseSumAsync(Guid branchId, DateTime dateUtc, CancellationToken ct = default) =>
            Task.FromResult(0m);

        public Task<int> GetRecentVouchersCountAsync(Guid branchId, Guid createdByUserId, DateTime sinceUtc, decimal minAmount, CancellationToken ct = default) =>
            Task.FromResult(0);

        public Task<decimal> GetSameVendorDailySumAsync(Guid branchId, string beneficiaryName, string category, DateTime dateUtc, CancellationToken ct = default) =>
            Task.FromResult(0m);
    }

    private class FakeCashbookService : ICashbookService
    {
        public Task<CashbookEntryResponse> ReceiveCashAsync(Guid accountId, FinanceAccountType accountType, decimal amount, string sourceType, Guid? sourceId, string referenceCode, string description, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<CashbookEntryResponse> DisburseCashAsync(Guid accountId, FinanceAccountType accountType, decimal amount, string sourceType, Guid? sourceId, string referenceCode, string description, CancellationToken ct = default) => throw new NotImplementedException();
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
    public async Task ShiftCap500k_ThreeConsecutiveDisbursements_ThirdShouldBecomeUnresolvedAndTriaged()
    {
        var expenseRepo = new FakeExpenseRepository();
        var cashbookService = new FakeCashbookService();
        var tenantContext = new FakeTenantContext();
        var uow = new FakeUnitOfWork();
        var service = new ExpenseService(expenseRepo, cashbookService, tenantContext, uow);

        var branchId = Guid.NewGuid();
        var tillId = Guid.NewGuid();

        // 1. Chi lần 1: 190k (Tổng ca = 190k <= 500k) -> Auto-Approved
        var req1 = new CreateExpenseRequest(branchId, "Tiệm đá A", "Mua đá lạnh", 190_000m, FinanceAccountType.Till, tillId, false, null, null, null, "Mua đá lần 1");
        var resp1 = await service.CreateExpenseAsync(req1);
        Assert.Equal("Approved", resp1.Status);
        Assert.False(resp1.IsFlaggedAsSuspiciousSplit);

        // 2. Chi lần 2: 190k (Tổng ca = 380k <= 500k) -> Auto-Approved
        var req2 = new CreateExpenseRequest(branchId, "Tiệm túi B", "Mua túi bóng", 190_000m, FinanceAccountType.Till, tillId, false, null, null, null, "Mua túi lần 2");
        var resp2 = await service.CreateExpenseAsync(req2);
        Assert.Equal("Approved", resp2.Status);
        Assert.False(resp2.IsFlaggedAsSuspiciousSplit);

        // 3. Chi lần 3: 190k (Tổng ca = 380k + 190k = 570k > 500k) -> Chuyển Unresolved (Triage Inbox)
        var req3 = new CreateExpenseRequest(branchId, "Thợ sửa C", "Sửa vòi nước", 190_000m, FinanceAccountType.Till, tillId, false, null, null, null, "Sửa vòi nước lần 3");
        var resp3 = await service.CreateExpenseAsync(req3);
        Assert.Equal(ExpenseVoucherStatus.Unresolved.ToString(), resp3.Status);
        Assert.True(resp3.IsFlaggedAsSuspiciousSplit);
        Assert.Contains("vượt trần ca cho phép", resp3.SuspiciousReason);

        // 4. Chủ vào xem Triage Inbox -> Thấy phiếu 3 đang chờ xử lý
        var triageItems = await service.GetPendingTriageAsync(branchId);
        Assert.Single(triageItems);
        Assert.Equal(resp3.Id, triageItems[0].Id);

        // 5. Chủ tiệm phê duyệt phiếu trong Triage Inbox
        var resolveReq = new ResolveTriageRequest(FinancialNature.OperatingExpense, false, "Chi phí sửa chữa vòi nước", "Chủ đồng ý duyệt chi khẩn cấp cho thợ sửa nước");
        var resolvedVoucher = await service.ResolveTriageAsync(resp3.Id, resolveReq);

        Assert.Equal("Approved", resolvedVoucher.Status);
        Assert.Contains("TRIAGE ĐÃ XỬ LÝ", resolvedVoucher.Note);

        // 6. Triage Inbox sau khi duyệt -> Trống
        var triageAfterResolve = await service.GetPendingTriageAsync(branchId);
        Assert.Empty(triageAfterResolve);
    }
}
