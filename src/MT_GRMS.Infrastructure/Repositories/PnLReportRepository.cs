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
            query = query.Where(o => o.BranchId == branchId.Value);
        }

        query = query.Where(o => o.CreatedAt >= fromDate && o.CreatedAt <= toDate && (o.PaymentStatus == "PAID" || o.PaymentStatus == "Paid"));

        var gross = await query.SumAsync(o => (decimal?)o.TotalAmount, ct) ?? 0m;
        var discounts = await query.SumAsync(o => (decimal?)o.DiscountAmount, ct) ?? 0m;

        // Doanh thu hàng trả lại từ các bút toán hoàn hàng trong sổ nợ/sổ quỹ
        var returnQuery = _context.CustomerDebtEntries.AsQueryable();
        if (branchId.HasValue)
        {
            returnQuery = returnQuery.Where(e => e.BranchId == branchId.Value);
        }
        var salesReturns = await returnQuery
            .Where(e => e.EntryType == CustomerDebtType.GoodsReturnOffset && e.OccurredAtUtc >= fromDate && e.OccurredAtUtc <= toDate)
            .SumAsync(e => (decimal?)e.Amount, ct) ?? 0m;

        return new RevenueAggregation(gross, discounts, salesReturns);
    }

    public async Task<CogsAggregation> GetCogsAsync(Guid? branchId, DateTime fromDate, DateTime toDate, CancellationToken ct = default)
    {
        var query = _context.Orders.Include(o => o.Details).AsQueryable();

        if (branchId.HasValue)
        {
            query = query.Where(o => o.BranchId == branchId.Value);
        }

        var orders = await query
            .Where(o => o.CreatedAt >= fromDate && o.CreatedAt <= toDate && (o.PaymentStatus == "PAID" || o.PaymentStatus == "Paid"))
            .ToListAsync(ct);

        var productIds = orders.SelectMany(o => o.Details).Select(i => i.ProductId).Distinct().ToList();
        var productCostDict = await _context.Products
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.CostPrice ?? 0m, ct);

        decimal actualCogs = 0m;
        int itemsWithCost = 0;
        int totalItemsCount = 0;

        foreach (var order in orders)
        {
            foreach (var item in order.Details)
            {
                totalItemsCount++;
                if (productCostDict.TryGetValue(item.ProductId, out var costPrice) && costPrice > 0)
                {
                    actualCogs += item.Quantity * costPrice;
                    itemsWithCost++;
                }
                else
                {
                    // Fallback 70% giá bán cho sản phẩm này
                    actualCogs += item.Quantity * item.UnitPrice * 0.70m;
                }
            }
        }

        if (totalItemsCount == 0)
        {
            return new CogsAggregation(0m, "ACTUAL", 0m, null);
        }

        if (itemsWithCost == 0)
        {
            // Hoàn toàn ước tính 70%
            decimal totalCogs = orders.Sum(o => o.Details.Any()
                ? o.Details.Sum(i => i.Quantity * i.UnitPrice * 0.70m)
                : o.TotalAmount * 0.70m);
            return new CogsAggregation(
                totalCogs,
                "ESTIMATED_70_PCT",
                70m,
                "Chưa thiết lập giá vốn cho sản phẩm trong kỳ. Giá vốn đang được ước tính 70% doanh thu theo chuẩn ngành F&B/Tạp hóa bán lẻ."
            );
        }

        if (itemsWithCost < totalItemsCount)
        {
            return new CogsAggregation(
                actualCogs,
                "MIXED",
                Math.Round((decimal)(totalItemsCount - itemsWithCost) / totalItemsCount * 70m, 1),
                $"Có {totalItemsCount - itemsWithCost}/{totalItemsCount} mặt hàng chưa khai báo giá vốn (được ước lượng 70% giá bán)."
            );
        }

        return new CogsAggregation(actualCogs, "ACTUAL", 0m, null);
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
            .SumAsync(e => (decimal?)e.Amount, ct) ?? 0m;
    }

    public async Task<List<DailyPnLItem>> GetDailyTrendAsync(Guid? branchId, DateTime fromDate, DateTime toDate, CancellationToken ct = default)
    {
        // 1. Gom Orders trong toàn kỳ (1 query)
        var orderQuery = _context.Orders.Include(o => o.Details).AsQueryable();
        if (branchId.HasValue) orderQuery = orderQuery.Where(o => o.BranchId == branchId.Value);

        var orders = await orderQuery
            .Where(o => o.CreatedAt >= fromDate && o.CreatedAt <= toDate && (o.PaymentStatus == "PAID" || o.PaymentStatus == "Paid"))
            .ToListAsync(ct);

        // Lấy bảng giá vốn cho các sản phẩm liên quan (1 query)
        var productIds = orders.SelectMany(o => o.Details).Select(i => i.ProductId).Distinct().ToList();
        var productCostDict = await _context.Products
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.CostPrice ?? 0m, ct);

        // 2. Gom chi phí trong toàn kỳ (1 query)
        var expenseQuery = _context.ExpenseVouchers.AsQueryable();
        if (branchId.HasValue) expenseQuery = expenseQuery.Where(e => e.BranchId == branchId.Value);
        var expenses = await expenseQuery
            .Where(e => e.CreatedAt >= fromDate && e.CreatedAt <= toDate &&
                        (e.Status == ExpenseVoucherStatus.Approved || e.Status == ExpenseVoucherStatus.Paid))
            .ToListAsync(ct);

        // 3. Gom nợ xấu và hoàn hàng trong toàn kỳ (1 query)
        var debtQuery = _context.CustomerDebtEntries.AsQueryable();
        if (branchId.HasValue) debtQuery = debtQuery.Where(e => e.BranchId == branchId.Value);
        var debts = await debtQuery
            .Where(e => e.OccurredAtUtc >= fromDate && e.OccurredAtUtc <= toDate)
            .ToListAsync(ct);

        // Grouping in-memory theo Ngày (Date)
        var ordersByDay = orders.GroupBy(o => o.CreatedAt.Date).ToDictionary(g => g.Key, g => g.ToList());
        var expensesByDay = expenses.GroupBy(e => e.CreatedAt.Date).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));
        var returnsByDay = debts.Where(d => d.EntryType == CustomerDebtType.GoodsReturnOffset)
            .GroupBy(d => d.OccurredAtUtc.Date).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));
        var badDebtsByDay = debts.Where(d => d.EntryType == CustomerDebtType.BadDebtWriteOff)
            .GroupBy(d => d.OccurredAtUtc.Date).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));

        var result = new List<DailyPnLItem>();
        var current = fromDate.Date;
        var end = toDate.Date;

        while (current <= end)
        {
            decimal grossRev = 0m;
            decimal discounts = 0m;
            decimal dayCogs = 0m;

            if (ordersByDay.TryGetValue(current, out var dayOrders))
            {
                grossRev = dayOrders.Sum(o => o.TotalAmount);
                discounts = dayOrders.Sum(o => o.DiscountAmount);

                foreach (var order in dayOrders)
                {
                    foreach (var item in order.Details)
                    {
                        if (productCostDict.TryGetValue(item.ProductId, out var cost) && cost > 0)
                        {
                            dayCogs += item.Quantity * cost;
                        }
                        else
                        {
                            dayCogs += item.Quantity * item.UnitPrice * 0.70m;
                        }
                    }
                }
            }

            var dayReturns = returnsByDay.GetValueOrDefault(current, 0m);
            var netRev = Math.Max(0, grossRev - discounts - dayReturns);
            var dayExp = expensesByDay.GetValueOrDefault(current, 0m);
            var dayBadDebt = badDebtsByDay.GetValueOrDefault(current, 0m);

            result.Add(new DailyPnLItem(
                current,
                netRev,
                dayCogs,
                dayExp,
                dayBadDebt
            ));

            current = current.AddDays(1);
        }

        return result;
    }
}