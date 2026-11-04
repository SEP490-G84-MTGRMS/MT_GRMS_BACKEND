using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Expenses;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Infrastructure.Persistence;

namespace MT_GRMS.Infrastructure.Repositories;

public class ExpenseRepository : GenericRepository<ExpenseVoucher>, IExpenseRepository
{
    public ExpenseRepository(AppDbContext context) : base(context)
    {
    }

    public async Task<decimal> GetShiftExpenseSumAsync(Guid branchId, DateTime shiftStartUtc, CancellationToken ct = default)
    {
        return await _context.ExpenseVouchers
            .Where(e => e.BranchId == branchId &&
                        e.CreatedAt >= shiftStartUtc &&
                        e.Status != ExpenseVoucherStatus.Cancelled &&
                        e.Status != ExpenseVoucherStatus.Reversed)
            .SumAsync(e => (decimal?)e.Amount, ct) ?? 0;
    }

    public async Task<decimal> GetDailyExpenseSumAsync(Guid branchId, DateTime dateUtc, CancellationToken ct = default)
    {
        var dayStart = dateUtc.Date;
        var dayEnd = dayStart.AddDays(1);

        return await _context.ExpenseVouchers
            .Where(e => e.BranchId == branchId &&
                        e.CreatedAt >= dayStart &&
                        e.CreatedAt < dayEnd &&
                        e.Status != ExpenseVoucherStatus.Cancelled &&
                        e.Status != ExpenseVoucherStatus.Reversed)
            .SumAsync(e => (decimal?)e.Amount, ct) ?? 0;
    }

    public async Task<int> GetRecentVouchersCountAsync(Guid branchId, Guid createdByUserId, DateTime sinceUtc, decimal minAmount, CancellationToken ct = default)
    {
        return await _context.ExpenseVouchers
            .Where(e => e.BranchId == branchId &&
                        e.CreatedBy == createdByUserId &&
                        e.CreatedAt >= sinceUtc &&
                        e.Amount >= minAmount &&
                        e.Status != ExpenseVoucherStatus.Cancelled)
            .CountAsync(ct);
    }

    public async Task<decimal> GetSameVendorDailySumAsync(Guid branchId, string beneficiaryName, string category, DateTime dateUtc, CancellationToken ct = default)
    {
        var dayStart = dateUtc.Date;
        var dayEnd = dayStart.AddDays(1);

        return await _context.ExpenseVouchers
            .Where(e => e.BranchId == branchId &&
                        e.BeneficiaryName == beneficiaryName &&
                        e.Category == category &&
                        e.CreatedAt >= dayStart &&
                        e.CreatedAt < dayEnd &&
                        e.Status != ExpenseVoucherStatus.Cancelled &&
                        e.Status != ExpenseVoucherStatus.Reversed)
            .SumAsync(e => (decimal?)e.Amount, ct) ?? 0;
    }
}
