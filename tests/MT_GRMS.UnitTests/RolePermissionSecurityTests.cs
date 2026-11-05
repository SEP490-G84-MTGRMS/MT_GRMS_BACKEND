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
/// Unit tests for Role-Based Access Control and Permission Security on Cash and Financial Management.
/// </summary>
public class RolePermissionSecurityTests
{
    private class FakeTenantContext : ITenantContext
    {
        public string CurrentTenantId { get; set; } = "tenant-sec-01";
        public Guid CurrentUserId { get; set; } = Guid.Parse("11111111-1111-1111-1111-111111111111");
        public Guid? CurrentBranchId { get; set; } = Guid.Parse("22222222-2222-2222-2222-222222222222");
        public string CurrentRole { get; set; } = "EMPLOYEE";
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
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);
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

        public Task<decimal> GetShiftExpenseSumAsync(Guid branchId, DateTime shiftStartUtc, CancellationToken ct = default) => Task.FromResult(0m);
        public Task<decimal> GetDailyExpenseSumAsync(Guid branchId, DateTime dateUtc, CancellationToken ct = default) => Task.FromResult(0m);
        public Task<int> GetRecentVouchersCountAsync(Guid branchId, Guid createdByUserId, DateTime sinceUtc, decimal minAmount, CancellationToken ct = default) => Task.FromResult(0);
        public Task<decimal> GetSameVendorDailySumAsync(Guid branchId, string beneficiaryName, string category, DateTime dateUtc, CancellationToken ct = default) => Task.FromResult(0m);
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
    public async Task Employee_AttemptingOwnerDrawOrTaxPayment_ShouldThrowUnauthorized()
    {
        var expenseRepo = new FakeExpenseRepository();
        var cashbookService = new FakeCashbookService();
        var tenantContext = new FakeTenantContext { CurrentRole = "EMPLOYEE" };
        var uow = new FakeUnitOfWork();
        var service = new ExpenseService(expenseRepo, cashbookService, tenantContext, uow);

        var drawReq = new CreateExpenseRequest(Guid.NewGuid(), "Chủ rút vốn", "Rút vốn", 5_000_000m, FinanceAccountType.Till, Guid.NewGuid(), false, null, null, null, null, FinancialNature.OwnerDraw);

        // Employee không được quyền tạo phiếu rút vốn
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.CreateExpenseAsync(drawReq));

        var taxReq = new CreateExpenseRequest(Guid.NewGuid(), "Cục Thuế", "Nộp thuế", 1_000_000m, FinanceAccountType.Till, Guid.NewGuid(), false, null, null, null, null, FinancialNature.TaxPayment);

        // Employee không được quyền tạo phiếu nộp thuế
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.CreateExpenseAsync(taxReq));

        // Employee không được quyền truy cập Triage Inbox
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetPendingTriageAsync(null));
    }

    [Fact]
    public async Task StoreOwner_PerformingOwnerDrawOrTaxPayment_ShouldSucceed()
    {
        var expenseRepo = new FakeExpenseRepository();
        var cashbookService = new FakeCashbookService();
        var tenantContext = new FakeTenantContext { CurrentRole = "STORE_OWNER" };
        var uow = new FakeUnitOfWork();
        var service = new ExpenseService(expenseRepo, cashbookService, tenantContext, uow);

        var drawReq = new CreateExpenseRequest(Guid.NewGuid(), "Chủ tiệm A", "Rút vốn chủ sở hữu", 5_000_000m, FinanceAccountType.Safe, Guid.NewGuid(), false, null, null, null, "Rút vốn định kỳ", FinancialNature.OwnerDraw);

        var result = await service.CreateExpenseAsync(drawReq);
        Assert.NotNull(result);
        Assert.Equal(FinancialNature.OwnerDraw, result.Nature);

        var triageList = await service.GetPendingTriageAsync(null);
        Assert.NotNull(triageList);
    }
}
