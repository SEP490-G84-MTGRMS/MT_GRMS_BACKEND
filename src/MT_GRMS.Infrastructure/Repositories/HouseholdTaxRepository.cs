using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Tax;
using MT_GRMS.Application.Tax.Dtos;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Infrastructure.Persistence;

namespace MT_GRMS.Infrastructure.Repositories;

public class HouseholdTaxRepository : IHouseholdTaxRepository
{
    private readonly AppDbContext _context;

    public HouseholdTaxRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<decimal> GetAnnualAccumulatedRevenueAsync(int year, DateTime upToDateUtc, CancellationToken cancellationToken = default)
    {
        var startOfYearUtc = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var orderGross = await _context.Orders
            .Where(o => o.CreatedAt >= startOfYearUtc && o.CreatedAt <= upToDateUtc && o.PaymentStatus == PaymentStatus.Paid)
            .SumAsync(o => (decimal?)o.SubTotal, cancellationToken) ?? 0m;

        var orderDiscounts = await _context.Orders
            .Where(o => o.CreatedAt >= startOfYearUtc && o.CreatedAt <= upToDateUtc && o.PaymentStatus == PaymentStatus.Paid)
            .SumAsync(o => (decimal?)o.DiscountAmount, cancellationToken) ?? 0m;

        var returns = await _context.CustomerDebtEntries
            .Where(e => e.EntryType == CustomerDebtType.GoodsReturnOffset && e.OccurredAtUtc >= startOfYearUtc && e.OccurredAtUtc <= upToDateUtc)
            .SumAsync(e => (decimal?)e.Amount, cancellationToken) ?? 0m;

        var netRevenue = orderGross - orderDiscounts - returns;
        return Math.Max(0, netRevenue);
    }

    public async Task<(decimal GrossRevenue, decimal DiscountAmount, decimal ReturnAmount)> GetPeriodRevenueAsync(
        DateTime fromDateUtc,
        DateTime toDateUtc,
        Guid? branchId,
        CancellationToken cancellationToken = default)
    {
        var orderQuery = _context.Orders.AsQueryable();
        if (branchId.HasValue)
        {
            orderQuery = orderQuery.Where(o => o.StoreId == branchId.Value);
        }

        orderQuery = orderQuery.Where(o => o.CreatedAt >= fromDateUtc && o.CreatedAt <= toDateUtc && o.PaymentStatus == PaymentStatus.Paid);

        var gross = await orderQuery.SumAsync(o => (decimal?)o.SubTotal, cancellationToken) ?? 0m;
        var discounts = await orderQuery.SumAsync(o => (decimal?)o.DiscountAmount, cancellationToken) ?? 0m;

        var returnQuery = _context.CustomerDebtEntries.AsQueryable();
        if (branchId.HasValue)
        {
            returnQuery = returnQuery.Where(e => e.BranchId == branchId.Value);
        }

        var returns = await returnQuery
            .Where(e => e.EntryType == CustomerDebtType.GoodsReturnOffset && e.OccurredAtUtc >= fromDateUtc && e.OccurredAtUtc <= toDateUtc)
            .SumAsync(e => (decimal?)e.Amount, cancellationToken) ?? 0m;

        return (gross, discounts, returns);
    }

    public async Task<decimal> GetPeriodOpexAsync(
        DateTime fromDateUtc,
        DateTime toDateUtc,
        Guid? branchId,
        CancellationToken cancellationToken = default)
    {
        var voucherQuery = _context.ExpenseVouchers.AsQueryable();
        if (branchId.HasValue)
        {
            voucherQuery = voucherQuery.Where(v => v.BranchId == branchId.Value);
        }

        voucherQuery = voucherQuery.Where(v =>
            v.ExpenseDate >= fromDateUtc &&
            v.ExpenseDate <= toDateUtc &&
            (v.Status == ExpenseVoucherStatus.Paid || v.Status == ExpenseVoucherStatus.Approved));

        // Lọc chi phí rút vốn chủ sở hữu (Equity/Capital) trực tiếp trên câu query SQL phía Database
        return await voucherQuery
            .Where(v => !v.Category.ToLower().Contains("capital") &&
                        !v.Category.ToLower().Contains("equity") &&
                        !v.Category.ToLower().Contains("vốn") &&
                        !v.Category.ToLower().Contains("rút vốn"))
            .SumAsync(v => (decimal?)v.Amount, cancellationToken) ?? 0m;
    }

    public Task<decimal> GetPeriodDeductibleExpensesAsync(
        DateTime fromDateUtc,
        DateTime toDateUtc,
        Guid? branchId,
        CancellationToken cancellationToken = default)
    {
        return GetPeriodOpexAsync(fromDateUtc, toDateUtc, branchId, cancellationToken);
    }

    public async Task<(decimal TotalCogs, bool IsEstimated)> GetPeriodCogsAsync(
        DateTime fromDateUtc,
        DateTime toDateUtc,
        Guid? branchId,
        CancellationToken cancellationToken = default)
    {
        var orderQuery = _context.Orders.Include(o => o.Items).AsQueryable();
        if (branchId.HasValue)
        {
            orderQuery = orderQuery.Where(o => o.StoreId == branchId.Value);
        }
        orderQuery = orderQuery.Where(o => o.CreatedAt >= fromDateUtc && o.CreatedAt <= toDateUtc && o.PaymentStatus == PaymentStatus.Paid);

        var orders = await orderQuery.ToListAsync(cancellationToken);

        // Lấy danh sách Product IDs từ các orders
        var productIds = orders.SelectMany(o => o.Items).Select(i => i.ProductId).Distinct().ToList();
        var productCostDict = await _context.Products
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.CostPrice, cancellationToken);

        decimal actualCogs = 0m;
        bool hasAnyRealCost = false;

        foreach (var order in orders)
        {
            foreach (var item in order.Items)
            {
                if (productCostDict.TryGetValue(item.ProductId, out var costPrice) && costPrice > 0)
                {
                    actualCogs += item.Quantity * costPrice;
                    hasAnyRealCost = true;
                }
            }
        }

        // Tính hàng trả lại trong kỳ
        var returnQuery = _context.CustomerDebtEntries.AsQueryable();
        if (branchId.HasValue)
        {
            returnQuery = returnQuery.Where(e => e.BranchId == branchId.Value);
        }
        var salesReturns = await returnQuery
            .Where(e => e.EntryType == CustomerDebtType.GoodsReturnOffset && e.OccurredAtUtc >= fromDateUtc && e.OccurredAtUtc <= toDateUtc)
            .SumAsync(e => (decimal?)e.Amount, cancellationToken) ?? 0m;

        if (!hasAnyRealCost)
        {
            // Fallback: Ước tính 70% doanh thu thuần nếu sản phẩm chưa liên kết giá vốn
            var gross = orders.Sum(o => o.SubTotal);
            var discounts = orders.Sum(o => o.DiscountAmount);
            var netRevenue = Math.Max(0, gross - discounts - salesReturns);
            var estimatedCogs = Math.Round(netRevenue * 0.70m, 0, MidpointRounding.AwayFromZero);
            return (estimatedCogs, true);
        }

        // Trừ giá vốn hàng trả lại theo tỷ lệ giá vốn thực tế trên doanh thu
        var totalGrossRevenue = orders.Sum(o => o.SubTotal);
        decimal returnCogs = 0m;
        if (totalGrossRevenue > 0 && salesReturns > 0)
        {
            var cogsRatio = actualCogs / totalGrossRevenue;
            returnCogs = Math.Round(salesReturns * cogsRatio, 0, MidpointRounding.AwayFromZero);
        }

        var finalCogs = Math.Max(0, actualCogs - returnCogs);
        return (finalCogs, false);
    }

    public async Task<List<AccountingBookEntryDto>> GetBookEntriesAsync(
        DateTime fromDateUtc,
        DateTime toDateUtc,
        Guid? branchId,
        HouseholdTaxGroup group,
        TaxCalculationMethod method,
        AccountingBookType? bookType,
        CancellationToken cancellationToken = default)
    {
        var result = new List<AccountingBookEntryDto>();

        // Truy vấn đơn hàng bán lẻ trong kỳ
        var orderQuery = _context.Orders.AsQueryable();
        if (branchId.HasValue)
        {
            orderQuery = orderQuery.Where(o => o.StoreId == branchId.Value);
        }
        orderQuery = orderQuery.Where(o => o.CreatedAt >= fromDateUtc && o.CreatedAt <= toDateUtc && o.PaymentStatus == PaymentStatus.Paid);

        var orders = await orderQuery
            .OrderBy(o => o.CreatedAt)
            .ToListAsync(cancellationToken);

        // Truy vấn phiếu chi trong kỳ (loại trừ vốn chủ sở hữu)
        var voucherQuery = _context.ExpenseVouchers.AsQueryable();
        if (branchId.HasValue)
        {
            voucherQuery = voucherQuery.Where(v => v.BranchId == branchId.Value);
        }
        voucherQuery = voucherQuery.Where(v =>
            v.ExpenseDate >= fromDateUtc &&
            v.ExpenseDate <= toDateUtc &&
            (v.Status == ExpenseVoucherStatus.Paid || v.Status == ExpenseVoucherStatus.Approved));

        var vouchers = await voucherQuery
            .Where(v => !v.Category.ToLower().Contains("capital") &&
                        !v.Category.ToLower().Contains("equity") &&
                        !v.Category.ToLower().Contains("vốn") &&
                        !v.Category.ToLower().Contains("rút vốn"))
            .OrderBy(v => v.ExpenseDate)
            .ToListAsync(cancellationToken);

        // 1. Phân loại theo Nhóm 1 (Dưới 1 tỷ): Dùng mẫu S1a-HKD
        if (group == HouseholdTaxGroup.Group1_Under1B && (!bookType.HasValue || bookType == AccountingBookType.S1a_RevenueGroup1))
        {
            result.AddRange(orders.Select(o => new AccountingBookEntryDto
            {
                VoucherNumber = o.OrderCode,
                VoucherDate = o.CreatedAt,
                Description = $"Bán lẻ hàng hóa (Nhóm 1) {o.OrderCode}",
                Amount = o.TotalAmount,
                Category = "Doanh thu bán lẻ",
                BookType = AccountingBookType.S1a_RevenueGroup1,
                BookCode = "S1a-HKD"
            }));
        }

        // 2. Phân loại theo Nhóm 2 (Phương pháp tỷ lệ % doanh thu): Dùng mẫu S2a-HKD
        if (group == HouseholdTaxGroup.Group2_1BTo3B && method == TaxCalculationMethod.RevenuePercentage &&
            (!bookType.HasValue || bookType == AccountingBookType.S2a_RevenueGroup2))
        {
            result.AddRange(orders.Select(o => new AccountingBookEntryDto
            {
                VoucherNumber = o.OrderCode,
                VoucherDate = o.CreatedAt,
                Description = $"Bán lẻ hàng hóa chịu thuế tỷ lệ % {o.OrderCode}",
                Amount = o.TotalAmount,
                Category = "Doanh thu bán lẻ chịu thuế",
                BookType = AccountingBookType.S2a_RevenueGroup2,
                BookCode = "S2a-HKD"
            }));
        }

        // 3. Phân loại theo Phương pháp Kê khai (Nhóm 3, 4 hoặc hộ lựa chọn): Bộ sổ S2b, S2c, S2d, S2e
        if (method == TaxCalculationMethod.DeclarationBased || group == HouseholdTaxGroup.Group4_Over50B)
        {
            // S2b-HKD: Sổ chi tiết doanh thu bán hàng hóa, dịch vụ
            if (!bookType.HasValue || bookType == AccountingBookType.S2b_RevenueDeclaration)
            {
                result.AddRange(orders.Select(o => new AccountingBookEntryDto
                {
                    VoucherNumber = o.OrderCode,
                    VoucherDate = o.CreatedAt,
                    Description = $"Doanh thu bán hàng kê khai {o.OrderCode}",
                    Amount = o.TotalAmount,
                    Category = "Doanh thu bán lẻ kê khai",
                    BookType = AccountingBookType.S2b_RevenueDeclaration,
                    BookCode = "S2b-HKD"
                }));
            }

            // S2c-HKD: Sổ chi tiết doanh thu, chi phí
            if (!bookType.HasValue || bookType == AccountingBookType.S2c_RevenueAndExpense)
            {
                // Doanh thu
                result.AddRange(orders.Select(o => new AccountingBookEntryDto
                {
                    VoucherNumber = o.OrderCode,
                    VoucherDate = o.CreatedAt,
                    Description = $"[Thu] Bán lẻ đơn hàng {o.OrderCode}",
                    Amount = o.TotalAmount,
                    Category = "Doanh thu",
                    BookType = AccountingBookType.S2c_RevenueAndExpense,
                    BookCode = "S2c-HKD"
                }));

                // Chi phí
                result.AddRange(vouchers.Select(v => new AccountingBookEntryDto
                {
                    VoucherNumber = v.VoucherCode ?? string.Empty,
                    VoucherDate = v.ExpenseDate,
                    Description = $"[Chi] {(!string.IsNullOrEmpty(v.Note) ? v.Note : $"{v.Category} - {v.BeneficiaryName}")}",
                    Amount = v.Amount,
                    Category = v.Category ?? string.Empty,
                    BookType = AccountingBookType.S2c_RevenueAndExpense,
                    BookCode = "S2c-HKD",
                    BeneficiaryName = v.BeneficiaryName,
                    InvoiceNumber = v.InvoiceNumber
                }));
            }

            // S2d-HKD: Sổ chi tiết vật liệu, dụng cụ, sản phẩm, hàng hóa
            if (!bookType.HasValue || bookType == AccountingBookType.S2d_MaterialsAndGoods)
            {
                var materialVouchers = vouchers.Where(v =>
                {
                    var cat = (v.Category ?? string.Empty).ToUpperInvariant();
                    return cat.Contains("BAO BÌ") || cat.Contains("PACKAGING") || cat.Contains("VẬT TƯ") ||
                           cat.Contains("CONSUMABLE") || cat.Contains("DỤNG CỤ") || cat.Contains("TOOL") ||
                           cat.Contains("VẬT LIỆU") || cat.Contains("HÀNG HÓA");
                });

                result.AddRange(materialVouchers.Select(v => new AccountingBookEntryDto
                {
                    VoucherNumber = v.VoucherCode ?? string.Empty,
                    VoucherDate = v.ExpenseDate,
                    Description = !string.IsNullOrEmpty(v.Note) ? v.Note : $"{v.Category} - {v.BeneficiaryName}",
                    Amount = v.Amount,
                    Category = v.Category ?? string.Empty,
                    BookType = AccountingBookType.S2d_MaterialsAndGoods,
                    BookCode = "S2d-HKD",
                    BeneficiaryName = v.BeneficiaryName,
                    InvoiceNumber = v.InvoiceNumber
                }));
            }

            // S2e-HKD: Sổ chi tiết tiền (tiền mặt và tiền gửi)
            if (!bookType.HasValue || bookType == AccountingBookType.S2e_CashAndBank)
            {
                result.AddRange(vouchers.Select(v => new AccountingBookEntryDto
                {
                    VoucherNumber = v.VoucherCode ?? string.Empty,
                    VoucherDate = v.ExpenseDate,
                    Description = $"Chi tiền: {(!string.IsNullOrEmpty(v.Note) ? v.Note : $"{v.Category} - {v.BeneficiaryName}")}",
                    Amount = v.Amount,
                    Category = v.Category ?? string.Empty,
                    BookType = AccountingBookType.S2e_CashAndBank,
                    BookCode = "S2e-HKD",
                    BeneficiaryName = v.BeneficiaryName,
                    InvoiceNumber = v.InvoiceNumber
                }));
            }
        }

        return result;
    }

    public async Task<HouseholdTaxSnapshot?> GetSnapshotAsync(
        int year,
        TaxPeriodType periodType,
        int periodNumber,
        Guid? branchId,
        CancellationToken cancellationToken = default)
    {
        return await _context.HouseholdTaxSnapshots
            .FirstOrDefaultAsync(s =>
                s.Year == year &&
                s.PeriodType == periodType &&
                s.PeriodNumber == periodNumber &&
                s.BranchId == branchId,
                cancellationToken);
    }

    public async Task<HouseholdTaxSnapshot?> GetSnapshotByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.HouseholdTaxSnapshots
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
    }

    public async Task<List<HouseholdTaxSnapshot>> GetSnapshotsAsync(int? year, Guid? branchId, CancellationToken cancellationToken = default)
    {
        var query = _context.HouseholdTaxSnapshots.AsQueryable();

        if (year.HasValue)
        {
            query = query.Where(s => s.Year == year.Value);
        }

        if (branchId.HasValue)
        {
            query = query.Where(s => s.BranchId == branchId.Value);
        }

        return await query
            .OrderByDescending(s => s.Year)
            .ThenByDescending(s => s.PeriodNumber)
            .ToListAsync(cancellationToken);
    }

    public async Task<HouseholdTaxSnapshot> AddSnapshotAsync(HouseholdTaxSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        _context.HouseholdTaxSnapshots.Add(snapshot);
        await _context.SaveChangesAsync(cancellationToken);
        return snapshot;
    }
}
