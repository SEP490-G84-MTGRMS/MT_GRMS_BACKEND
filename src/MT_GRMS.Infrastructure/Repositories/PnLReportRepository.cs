using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Reports;
using MT_GRMS.Domain.Entities;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Infrastructure.Persistence;

namespace MT_GRMS.Infrastructure.Repositories;

public class PnLReportRepository : IPnLReportRepository
{
    private readonly AppDbContext _context;

    public PnLReportRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<RevenueAggregation> GetRevenueAsync(Guid? branchId, DateTime fromDate, DateTime toDate, CancellationToken ct = default)
    {
        var query = _context.Orders.AsQueryable();

        if (branchId.HasValue)
        {
            query = query.Where(o => o.StoreId == branchId.Value);
        }

        query = query.Where(o => o.CreatedAt >= fromDate && o.CreatedAt <= toDate && o.PaymentStatus == PaymentStatus.Paid);

        var gross = await query.SumAsync(o => (decimal?)o.SubTotal, ct) ?? 0;
        var discounts = await query.SumAsync(o => (decimal?)o.DiscountAmount, ct) ?? 0;

        // Doanh thu hàng trả lại từ các bút toán hoàn hàng trong sổ nợ/sổ quỹ
        var returnQuery = _context.CustomerDebtEntries.AsQueryable();
        if (branchId.HasValue)
        {
            returnQuery = returnQuery.Where(e => e.BranchId == branchId.Value);
        }
        var salesReturns = await returnQuery
            .Where(e => e.EntryType == CustomerDebtType.GoodsReturnOffset && e.OccurredAtUtc >= fromDate && e.OccurredAtUtc <= toDate)
            .SumAsync(e => (decimal?)e.Amount, ct) ?? 0;

        return new RevenueAggregation(gross, discounts, salesReturns);
    }

    public async Task<CogsAggregation> GetCogsAsync(Guid? branchId, DateTime fromDate, DateTime toDate, CancellationToken ct = default)
    {
        var query = _context.Orders.Include(o => o.Items).AsQueryable();

        if (branchId.HasValue)
        {
            query = query.Where(o => o.StoreId == branchId.Value);
        }

        var orders = await query
            .Where(o => o.CreatedAt >= fromDate && o.CreatedAt <= toDate && o.PaymentStatus == PaymentStatus.Paid)
            .ToListAsync(ct);

        // Giả lập giá vốn 70% doanh thu theo chuẩn ngành tạp hóa bán lẻ nếu chưa liên kết lô
        decimal totalCogs = orders.Sum(o => o.Items.Any()
            ? o.Items.Sum(i => i.Quantity * i.UnitPrice * 0.70m)
            : o.SubTotal * 0.70m);

        return new CogsAggregation(totalCogs);
    }

    public async Task<List<CategoryExpenseItem>> GetOperatingExpensesAsync(Guid? branchId, DateTime fromDate, DateTime toDate, CancellationToken ct = default)
    {
        var query = _context.ExpenseVouchers.AsQueryable();

        if (branchId.HasValue)
        {
            query = query.Where(e => e.BranchId == branchId.Value);
        }

        query = query.Where(e => e.CreatedAt >= fromDate && e.CreatedAt <= toDate &&
                                 (e.Status == ExpenseVoucherStatus.Approved || e.Status == ExpenseVoucherStatus.Paid));

        var groups = await query
            .GroupBy(e => e.Category)
            .Select(g => new CategoryExpenseItem(g.Key, g.Sum(x => x.Amount)))
            .ToListAsync(ct);

        return groups;
    }

    public async Task<decimal> GetBadDebtExpenseAsync(Guid? branchId, DateTime fromDate, DateTime toDate, CancellationToken ct = default)
    {
        var query = _context.CustomerDebtEntries.AsQueryable();

        if (branchId.HasValue)
        {
            query = query.Where(e => e.BranchId == branchId.Value);
        }

        return await query
            .Where(e => e.EntryType == CustomerDebtType.BadDebtWriteOff && e.OccurredAtUtc >= fromDate && e.OccurredAtUtc <= toDate)
            .SumAsync(e => (decimal?)e.Amount, ct) ?? 0;
    }
}
