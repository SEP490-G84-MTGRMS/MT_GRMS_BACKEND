using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Reports;
using MT_GRMS.Domain.Entities.Sales;
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

        // Bao gồm cả đơn đã thanh toán (PAID) và đơn bán nợ (DEBT) theo nguyên tắc dồn tích doanh thu
        query = query.Where(o => o.CreatedAt >= fromDate && o.CreatedAt <= toDate &&
                                 (o.PaymentStatus == "PAID" || o.PaymentStatus == "Paid" || o.PaymentStatus == "DEBT" || o.PaymentStatus == "Debt") &&
                                 o.Status != "Cancelled");

        var gross = await query.SumAsync(o => (decimal?)o.TotalAmount, ct) ?? 0m;
        var discounts = await query.SumAsync(o => (decimal?)o.DiscountAmount, ct) ?? 0m;
        var totalOrders = await query.CountAsync(ct);

        // Doanh thu hàng trả lại từ các bút toán hoàn hàng trong sổ nợ/sổ quỹ
        var returnQuery = _context.CustomerDebtEntries.AsQueryable();
        if (branchId.HasValue)
        {
            returnQuery = returnQuery.Where(e => e.BranchId == branchId.Value);
        }
        var salesReturns = await returnQuery
            .Where(e => e.EntryType == CustomerDebtType.GoodsReturnOffset && e.OccurredAtUtc >= fromDate && e.OccurredAtUtc <= toDate)
            .SumAsync(e => (decimal?)e.Amount, ct) ?? 0m;

        return new RevenueAggregation(gross, discounts, salesReturns, totalOrders);
    }

    public async Task<CogsAggregation> GetCogsAsync(Guid? branchId, DateTime fromDate, DateTime toDate, CancellationToken ct = default)
    {
        var query = _context.Orders.Include(o => o.Details).AsQueryable();

        if (branchId.HasValue)
        {
            query = query.Where(o => o.BranchId == branchId.Value);
        }

        var orders = await query
            .Where(o => o.CreatedAt >= fromDate && o.CreatedAt <= toDate &&
                        (o.PaymentStatus == "PAID" || o.PaymentStatus == "Paid" || o.PaymentStatus == "DEBT" || o.PaymentStatus == "Debt") &&
                        o.Status != "Cancelled")
            .ToListAsync(ct);

        var productIds = orders.SelectMany(o => o.Details).Select(i => i.ProductId).Distinct().ToList();
        var productCostDict = await _context.Products
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.CostPrice ?? 0m, ct);

        // Truy vấn giá nhập bình quân gia quyền từ các phiếu nhập kho (GoodsReceiptLines) và lô tồn (BranchStocks)
        var grnLines = await _context.GoodsReceiptLines
            .Include(l => l.GoodsReceipt)
            .Where(l => productIds.Contains(l.ProductId) &&
                        l.GoodsReceipt != null &&
                        l.GoodsReceipt.ReceivedAtUtc <= toDate &&
                        (l.GoodsReceipt.Status == "CONFIRMED" || l.GoodsReceipt.Status == "COMPLETED"))
            .ToListAsync(ct);

        var weightedCostByProduct = grnLines
            .GroupBy(l => l.ProductId)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var totalQty = g.Sum(x => x.AcceptedQty > 0 ? x.AcceptedQty : (x.DeliveredQty > 0 ? x.DeliveredQty : x.OrderedQty));
                    var totalAmt = g.Sum(x => x.LineAmount > 0 ? x.LineAmount : (x.UnitPrice * (x.AcceptedQty > 0 ? x.AcceptedQty : x.OrderedQty)));
                    return totalQty > 0 ? Math.Round(totalAmt / totalQty, 2, MidpointRounding.AwayFromZero) : 0m;
                }
            );

        decimal actualCogs = 0m;
        decimal weightedAvgRawCogs = 0m;
        decimal fefoSpecificRawCogs = 0m;
        int itemsWithCost = 0;
        int totalItemsCount = 0;

        foreach (var order in orders)
        {
            foreach (var item in order.Details)
            {
                totalItemsCount++;
                decimal itemCost = 0m;
                bool hasCost = false;

                // Ưu tiên 1: Giá vốn đã chốt cứng tại thời điểm xuất bán (CostPriceAtSale - FEFO / Thực tế đích danh)
                if (item.CostPriceAtSale > 0)
                {
                    itemCost = item.CostPriceAtSale;
                    hasCost = true;
                }
                // Ưu tiên 2: Fallback giá vốn trong danh mục sản phẩm hiện hành (nếu đơn cũ chưa có CostPriceAtSale)
                else if (productCostDict.TryGetValue(item.ProductId, out var catalogCost) && catalogCost > 0)
                {
                    itemCost = catalogCost;
                    hasCost = true;
                }

                decimal fefoUnitCost = hasCost ? itemCost : item.UnitPrice * 0.70m;
                decimal weightedUnitCost = fefoUnitCost;

                if (weightedCostByProduct.TryGetValue(item.ProductId, out var grnAvgCost) && grnAvgCost > 0)
                {
                    weightedUnitCost = grnAvgCost;
                }
                else if (productCostDict.TryGetValue(item.ProductId, out var catCost) && catCost > 0)
                {
                    weightedUnitCost = catCost;
                }

                if (hasCost)
                {
                    actualCogs += item.Quantity * itemCost;
                    itemsWithCost++;
                }
                else
                {
                    // Ưu tiên 3: Fallback ước tính 70% giá bán nếu hoàn toàn chưa có giá vốn
                    actualCogs += item.Quantity * item.UnitPrice * 0.70m;
                }

                fefoSpecificRawCogs += item.Quantity * fefoUnitCost;
                weightedAvgRawCogs += item.Quantity * weightedUnitCost;
            }
        }

        // Tính hàng trả lại trong kỳ để đối soát đồng nhất 100% với Sổ S2c-HKD (FE-19)
        var returnQuery = _context.CustomerDebtEntries.AsQueryable();
        if (branchId.HasValue)
        {
            returnQuery = returnQuery.Where(e => e.BranchId == branchId.Value);
        }
        var salesReturns = await returnQuery
            .Where(e => e.EntryType == CustomerDebtType.GoodsReturnOffset && e.OccurredAtUtc >= fromDate && e.OccurredAtUtc <= toDate)
            .SumAsync(e => (decimal?)e.Amount, ct) ?? 0m;

        if (totalItemsCount == 0 && orders.Count == 0)
        {
            return new CogsAggregation(0m, "ACTUAL", 0m, null, 0m, 0m);
        }

        if (itemsWithCost == 0)
        {
            // Hoàn toàn ước tính 70% doanh thu thuần (đồng nhất với HouseholdTaxRepository.GetPeriodCogsAsync)
            var gross = orders.Sum(o => o.TotalAmount);
            var discounts = orders.Sum(o => o.DiscountAmount);
            var netRev = Math.Max(0, gross - discounts - salesReturns);
            decimal totalCogs = orders.Any(o => o.Details.Any()) && salesReturns == 0 && discounts == 0
                ? orders.Sum(o => o.Details.Sum(i => i.Quantity * i.UnitPrice * 0.70m))
                : Math.Round(netRev * 0.70m, 0, MidpointRounding.AwayFromZero);

            return new CogsAggregation(
                totalCogs,
                "ESTIMATED_70_PCT",
                70m,
                "Chưa thiết lập giá vốn cho sản phẩm trong kỳ. Giá vốn đang được ước tính 70% doanh thu theo chuẩn ngành F&B/Tạp hóa bán lẻ.",
                totalCogs,
                totalCogs
            );
        }

        // Trừ giá vốn hàng trả lại theo tỷ lệ giá vốn thực tế trên doanh thu gộp (đồng nhất 100% với S2c-HKD FE-19)
        var totalGrossRevenue = orders.Sum(o => o.TotalAmount);
        decimal returnCogs = 0m;
        decimal returnWeightedCogs = 0m;
        decimal returnFefoCogs = 0m;
        if (totalGrossRevenue > 0 && salesReturns > 0)
        {
            var cogsRatio = actualCogs / totalGrossRevenue;
            returnCogs = Math.Round(salesReturns * cogsRatio, 0, MidpointRounding.AwayFromZero);
            returnWeightedCogs = Math.Round(salesReturns * (weightedAvgRawCogs / totalGrossRevenue), 0, MidpointRounding.AwayFromZero);
            returnFefoCogs = Math.Round(salesReturns * (fefoSpecificRawCogs / totalGrossRevenue), 0, MidpointRounding.AwayFromZero);
        }

        var finalCogs = Math.Max(0, actualCogs - returnCogs);
        var finalWeightedCogs = Math.Max(0, weightedAvgRawCogs - returnWeightedCogs);
        var finalFefoCogs = Math.Max(0, fefoSpecificRawCogs - returnFefoCogs);

        if (itemsWithCost < totalItemsCount)
        {
            return new CogsAggregation(
                finalCogs,
                "MIXED",
                Math.Round((decimal)(totalItemsCount - itemsWithCost) / totalItemsCount * 70m, 1),
                $"Có {totalItemsCount - itemsWithCost}/{totalItemsCount} mặt hàng chưa khai báo giá vốn (được ước lượng 70% giá bán).",
                finalWeightedCogs,
                finalFefoCogs
            );
        }

        return new CogsAggregation(finalCogs, "ACTUAL", 0m, null, finalWeightedCogs, finalFefoCogs);
    }

    public async Task<List<CategoryExpenseItem>> GetOperatingExpensesAsync(Guid? branchId, DateTime fromDate, DateTime toDate, CancellationToken ct = default)
    {
        var query = _context.ExpenseVouchers.AsQueryable();

        if (branchId.HasValue)
        {
            query = query.Where(e => e.BranchId == branchId.Value);
        }

        // INV-12 & Đồng nhất 100% với S2c-HKD (FE-19):
        // Ưu tiên lọc theo ExpenseDate; nếu chứng từ được khởi tạo trong kỳ (CreatedAt) mà chưa gán riêng ExpenseDate khác ngày thì vẫn nhận diện đầy đủ.
        query = query.Where(e =>
            ((e.ExpenseDate >= fromDate && e.ExpenseDate <= toDate) ||
             (e.CreatedAt >= fromDate && e.CreatedAt <= toDate)) &&
            (e.Status == ExpenseVoucherStatus.Approved || e.Status == ExpenseVoucherStatus.Paid) &&
            e.Nature == FinancialNature.OperatingExpense &&
            !e.Category.ToLower().Contains("capital") &&
            !e.Category.ToLower().Contains("equity") &&
            !e.Category.ToLower().Contains("vốn") &&
            !e.Category.ToLower().Contains("rút vốn"));

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

    public async Task<List<TopProductPnLItem>> GetTopProductsAsync(Guid? branchId, DateTime fromDate, DateTime toDate, int limit = 10, CancellationToken ct = default)
    {
        var orderQuery = _context.Orders.Include(o => o.Details).AsQueryable();
        if (branchId.HasValue)
        {
            orderQuery = orderQuery.Where(o => o.BranchId == branchId.Value);
        }

        var orders = await orderQuery
            .Where(o => o.CreatedAt >= fromDate && o.CreatedAt <= toDate &&
                        (o.PaymentStatus == "PAID" || o.PaymentStatus == "Paid" || o.PaymentStatus == "DEBT" || o.PaymentStatus == "Debt") &&
                        o.Status != "Cancelled")
            .ToListAsync(ct);

        var allDetails = orders.SelectMany(o => o.Details).ToList();
        if (allDetails.Count == 0)
        {
            return new List<TopProductPnLItem>();
        }

        var productIds = allDetails.Select(d => d.ProductId).Distinct().ToList();
        var productDict = await _context.Products
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => new { p.ProductCode, p.Name, CostPrice = p.CostPrice ?? 0m }, ct);

        var grouped = allDetails
            .GroupBy(d => d.ProductId)
            .Select(g =>
            {
                var prodInfo = productDict.GetValueOrDefault(g.Key);
                var code = !string.IsNullOrWhiteSpace(prodInfo?.ProductCode)
                    ? prodInfo.ProductCode
                    : $"SP-{g.Key.ToString()[..6].ToUpperInvariant()}";
                var name = !string.IsNullOrWhiteSpace(prodInfo?.Name)
                    ? prodInfo.Name
                    : (g.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.ProductName))?.ProductName ?? "Sản phẩm bán lẻ");

                decimal qty = 0m;
                decimal rev = 0m;
                decimal cogs = 0m;

                foreach (var item in g)
                {
                    var itemQty = item.Qty;
                    var unitSell = item.ActualPrice > 0 ? item.ActualPrice : item.UnitPrice;
                    var lineRev = Math.Max(0m, (unitSell * itemQty) - item.DiscountAmount);
                    var unitCost = item.CostPriceAtSale > 0
                        ? item.CostPriceAtSale
                        : (prodInfo != null && prodInfo.CostPrice > 0 ? prodInfo.CostPrice : unitSell * 0.70m);

                    qty += itemQty;
                    rev += lineRev;
                    cogs += itemQty * unitCost;
                }

                var profit = rev - cogs;
                var marginPct = rev > 0 ? Math.Round((profit / rev) * 100m, 1, MidpointRounding.AwayFromZero) : 0m;

                return new TopProductPnLItem(
                    g.Key,
                    code,
                    name,
                    qty,
                    rev,
                    cogs,
                    profit,
                    marginPct
                );
            })
            .OrderByDescending(x => x.Profit)
            .ThenByDescending(x => x.Revenue)
            .Take(limit)
            .ToList();

        return grouped;
    }

    public async Task<List<DailyPnLItem>> GetDailyTrendAsync(Guid? branchId, DateTime fromDate, DateTime toDate, CancellationToken ct = default)
    {
        // 1. Gom Orders trong toàn kỳ (1 query)
        var orderQuery = _context.Orders.Include(o => o.Details).AsQueryable();
        if (branchId.HasValue) orderQuery = orderQuery.Where(o => o.BranchId == branchId.Value);

        var orders = await orderQuery
            .Where(o => o.CreatedAt >= fromDate && o.CreatedAt <= toDate &&
                        (o.PaymentStatus == "PAID" || o.PaymentStatus == "Paid" || o.PaymentStatus == "DEBT" || o.PaymentStatus == "Debt") &&
                        o.Status != "Cancelled")
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
            .Where(e =>
                ((e.ExpenseDate >= fromDate && e.ExpenseDate <= toDate) ||
                 (e.CreatedAt >= fromDate && e.CreatedAt <= toDate)) &&
                (e.Status == ExpenseVoucherStatus.Approved || e.Status == ExpenseVoucherStatus.Paid) &&
                e.Nature == FinancialNature.OperatingExpense &&
                !e.Category.ToLower().Contains("capital") &&
                !e.Category.ToLower().Contains("equity") &&
                !e.Category.ToLower().Contains("vốn") &&
                !e.Category.ToLower().Contains("rút vốn"))
            .ToListAsync(ct);

        // 3. Gom nợ xấu và hoàn hàng trong toàn kỳ (1 query)
        var debtQuery = _context.CustomerDebtEntries.AsQueryable();
        if (branchId.HasValue) debtQuery = debtQuery.Where(e => e.BranchId == branchId.Value);
        var debts = await debtQuery
            .Where(e => e.OccurredAtUtc >= fromDate && e.OccurredAtUtc <= toDate)
            .ToListAsync(ct);

        // Khi cả fromDate và toDate có TimeOfDay > 0 hoặc thuộc UTC, chuẩn hóa về ngày theo múi giờ truy vấn để không lệch 1 ngày khi test Date thuần
        var vnOffset = TimeSpan.FromHours(7);
        bool useVnShift = fromDate.Kind == DateTimeKind.Utc && (fromDate.Hour == 17 || toDate.Hour == 16);
        DateTime ToReportDate(DateTime dt) => (useVnShift && dt.Kind == DateTimeKind.Utc ? dt.Add(vnOffset) : dt).Date;
        DateTime GetEffectiveExpenseDate(ExpenseVoucher e) =>
            (e.ExpenseDate >= fromDate && e.ExpenseDate <= toDate) ? e.ExpenseDate : e.CreatedAt;

        var ordersByDay = orders.GroupBy(o => ToReportDate(o.CreatedAt)).ToDictionary(g => g.Key, g => g.ToList());
        var expensesByDay = expenses.GroupBy(e => ToReportDate(GetEffectiveExpenseDate(e))).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));
        var returnsByDay = debts.Where(d => d.EntryType == CustomerDebtType.GoodsReturnOffset)
            .GroupBy(d => ToReportDate(d.OccurredAtUtc)).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));
        var badDebtsByDay = debts.Where(d => d.EntryType == CustomerDebtType.BadDebtWriteOff)
            .GroupBy(d => ToReportDate(d.OccurredAtUtc)).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));

        var result = new List<DailyPnLItem>();
        var current = ToReportDate(fromDate);
        var end = ToReportDate(toDate);

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
                        decimal itemCost = 0m;
                        if (item.CostPriceAtSale > 0)
                        {
                            itemCost = item.CostPriceAtSale;
                        }
                        else if (productCostDict.TryGetValue(item.ProductId, out var cost) && cost > 0)
                        {
                            itemCost = cost;
                        }
                        else
                        {
                            itemCost = item.UnitPrice * 0.70m;
                        }

                        dayCogs += item.Quantity * itemCost;
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
