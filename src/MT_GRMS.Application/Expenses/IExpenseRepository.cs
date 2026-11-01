using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Entities.Finance;

namespace MT_GRMS.Application.Expenses;

public interface IExpenseRepository : IRepository<ExpenseVoucher>
{
    Task<decimal> GetShiftExpenseSumAsync(Guid branchId, DateTime shiftStartUtc, CancellationToken ct = default);
    Task<decimal> GetDailyExpenseSumAsync(Guid branchId, DateTime dateUtc, CancellationToken ct = default);
    Task<int> GetRecentVouchersCountAsync(Guid branchId, Guid createdByUserId, DateTime sinceUtc, decimal minAmount, CancellationToken ct = default);
    Task<decimal> GetSameVendorDailySumAsync(Guid branchId, string beneficiaryName, string category, DateTime dateUtc, CancellationToken ct = default);
}