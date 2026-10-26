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

    public async Task<List<AccountingBookEntryDto>> GetBookEntriesAsync(
        DateTime fromDateUtc,
        DateTime toDateUtc,
        Guid? branchId,
        AccountingBookType? bookType,
        CancellationToken cancellationToken = default)
    {
        var result = new List<AccountingBookEntryDto>();

        // 1. Sổ S2b-HKD: Doanh thu bán hàng hóa
        if (!bookType.HasValue || bookType == AccountingBookType.S2b_Revenue)
        {
            var orderQuery = _context.Orders.AsQueryable();
            if (branchId.HasValue)
            {
                orderQuery = orderQuery.Where(o => o.StoreId == branchId.Value);
            }
            orderQuery = orderQuery.Where(o => o.CreatedAt >= fromDateUtc && o.CreatedAt <= toDateUtc && o.PaymentStatus == PaymentStatus.Paid);

            var orders = await orderQuery
                .OrderBy(o => o.CreatedAt)
                .Select(o => new AccountingBookEntryDto
                {
                    VoucherNumber = o.OrderCode,
                    VoucherDate = o.CreatedAt,
                    Description = $"Bán lẻ đơn hàng {o.OrderCode}",
                    Amount = o.TotalAmount,
                    Category = "Bán lẻ hàng hóa",
                    BookType = AccountingBookType.S2b_Revenue,
                    BookCode = "S2b-HKD",
                    BeneficiaryName = null,
                    InvoiceNumber = null
                })
                .ToListAsync(cancellationToken);

            result.AddRange(orders);
        }

        // 2, 3, 4. Các sổ chi phí TT 152: S2c, S2d, S2e (từ ExpenseVouchers)
        if (!bookType.HasValue || bookType != AccountingBookType.S2b_Revenue)
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

            var vouchers = await voucherQuery
                .OrderBy(v => v.ExpenseDate)
                .ToListAsync(cancellationToken);

            foreach (var v in vouchers)
            {
                var catUpper = (v.Category ?? string.Empty).ToUpperInvariant();

                // LOẠI TRỪ 100% VỐN CHỦ SỞ HỮU
                if (catUpper.Contains("CAPITAL") ||
                    catUpper.Contains("EQUITY") ||
                    catUpper.Contains("VỐN") ||
                    catUpper.Contains("RÚT VỐN"))
                {
                    continue;
                }

                // Phân loại sổ TT 152
                AccountingBookType assignedBookType;
                string bookCode;

                if (catUpper.Contains("LƯƠNG") ||
                    catUpper.Contains("SALARY") ||
                    catUpper.Contains("WAGE") ||
                    catUpper.Contains("THƯỞNG") ||
                    catUpper.Contains("BONUS") ||
                    catUpper.Contains("PHỤ CẤP") ||
                    catUpper.Contains("ALLOWANCE"))
                {
                    assignedBookType = AccountingBookType.S2c_Salary;
                    bookCode = "S2c-HKD";
                }
                else if (catUpper.Contains("BAO BÌ") ||
                         catUpper.Contains("PACKAGING") ||
                         catUpper.Contains("VẬT TƯ") ||
                         catUpper.Contains("CONSUMABLE") ||
                         catUpper.Contains("DỤNG CỤ") ||
                         catUpper.Contains("TOOL") ||
                         catUpper.Contains("VẬT LIỆU") ||
                         catUpper.Contains("TÚI"))
                {
                    assignedBookType = AccountingBookType.S2d_Supplies;
                    bookCode = "S2d-HKD";
                }
                else
                {
                    // Dịch vụ mua ngoài và chi phí bằng tiền khác
                    assignedBookType = AccountingBookType.S2e_Services;
                    bookCode = "S2e-HKD";
                }

                if (bookType.HasValue && bookType.Value != assignedBookType)
                {
                    continue;
                }

                result.Add(new AccountingBookEntryDto
                {
                    VoucherNumber = v.VoucherCode,
                    VoucherDate = v.ExpenseDate,
                    Description = string.IsNullOrEmpty(v.Note) ? $"{v.Category} - {v.BeneficiaryName}" : v.Note,
                    Amount = v.Amount,
                    Category = v.Category ?? string.Empty,
                    BookType = assignedBookType,
                    BookCode = bookCode,
                    BeneficiaryName = v.BeneficiaryName,
                    InvoiceNumber = v.InvoiceNumber
                });
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

