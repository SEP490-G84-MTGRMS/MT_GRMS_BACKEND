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
            .SumAsync(o => (decimal?)o.SubTotal, cancellationToken) ?? 0;

        var orderDiscounts = await _context.Orders
            .Where(o => o.CreatedAt >= startOfYearUtc && o.CreatedAt <= upToDateUtc && o.PaymentStatus == PaymentStatus.Paid)
            .SumAsync(o => (decimal?)o.DiscountAmount, cancellationToken) ?? 0;

        var returns = await _context.CustomerDebtEntries
            .Where(e => e.EntryType == CustomerDebtType.GoodsReturnOffset && e.OccurredAtUtc >= startOfYearUtc && e.OccurredAtUtc <= upToDateUtc)
            .SumAsync(e => (decimal?)e.Amount, cancellationToken) ?? 0;

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

        var gross = await orderQuery.SumAsync(o => (decimal?)o.SubTotal, cancellationToken) ?? 0;
        var discounts = await orderQuery.SumAsync(o => (decimal?)o.DiscountAmount, cancellationToken) ?? 0;

        var returnQuery = _context.CustomerDebtEntries.AsQueryable();
        if (branchId.HasValue)
        {
            returnQuery = returnQuery.Where(e => e.BranchId == branchId.Value);
        }

        var returns = await returnQuery
            .Where(e => e.EntryType == CustomerDebtType.GoodsReturnOffset && e.OccurredAtUtc >= fromDateUtc && e.OccurredAtUtc <= toDateUtc)
            .SumAsync(e => (decimal?)e.Amount, cancellationToken) ?? 0;

        return (gross, discounts, returns);
    }

    public async Task<decimal> GetPeriodDeductibleExpensesAsync(
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

        var vouchers = await voucherQuery.ToListAsync(cancellationToken);

        // Loại trừ 100% chi phí Vốn chủ sở hữu
        var validVouchers = vouchers.Where(v =>
        {
            var cat = (v.Category ?? string.Empty).ToUpperInvariant();
            return !cat.Contains("CAPITAL") && !cat.Contains("EQUITY") && !cat.Contains("VỐN") && !cat.Contains("RÚT VỐN");
        });

        return validVouchers.Sum(v => v.Amount);
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

        // Truy vấn phiếu chi hợp lệ trong kỳ (loại trừ vốn chủ sở hữu)
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
            .OrderBy(v => v.ExpenseDate)
            .ToListAsync(cancellationToken);

        var validVouchers = vouchers.Where(v =>
        {
            var cat = (v.Category ?? string.Empty).ToUpperInvariant();
            return !cat.Contains("CAPITAL") && !cat.Contains("EQUITY") && !cat.Contains("VỐN") && !cat.Contains("RÚT VỐN");
        }).ToList();

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
                result.AddRange(validVouchers.Select(v => new AccountingBookEntryDto
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
                var materialVouchers = validVouchers.Where(v =>
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
                result.AddRange(validVouchers.Select(v => new AccountingBookEntryDto
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
