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

    public async Task<HouseholdTaxpayerProfileDto> GetTaxpayerProfileAsync(
        Guid? branchId,
        CancellationToken cancellationToken = default)
    {
        var branchQuery = _context.Branches
            .AsNoTracking()
            .Include(b => b.Tenant)
            .Where(b => b.IsActive);

        var branch = branchId.HasValue
            ? await branchQuery.FirstOrDefaultAsync(b => b.Id == branchId.Value, cancellationToken)
            : await branchQuery.OrderByDescending(b => b.IsHeadquarter).ThenBy(b => b.CreatedAt).FirstOrDefaultAsync(cancellationToken);

        var tenant = branch?.Tenant;
        return new HouseholdTaxpayerProfileDto
        {
            TaxpayerName = tenant?.Name ?? string.Empty,
            TaxCode = tenant?.TaxCode ?? string.Empty,
            BusinessLocation = branch?.Address ?? tenant?.Address ?? string.Empty,
            PhoneNumber = branch?.Phone ?? tenant?.Hotline ?? string.Empty,
            TaxAuthorityName = string.Empty
        };
    }

    public async Task<decimal> GetAnnualAccumulatedRevenueAsync(int year, DateTime upToDateUtc, CancellationToken cancellationToken = default)
    {
        var vnTz = HouseholdTaxService.GetVietnamTimeZone();
        var startOfYearVn = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);
        var startOfYearUtc = TimeZoneInfo.ConvertTimeToUtc(startOfYearVn, vnTz);

        var orderGross = await _context.Orders
            .Where(o => o.CreatedAt >= startOfYearUtc && o.CreatedAt < upToDateUtc && (o.PaymentStatus == "PAID" || o.PaymentStatus == "Paid" || o.PaymentStatus == "DEBT" || o.PaymentStatus == "Debt") && o.Status != "Cancelled")
            .SumAsync(o => (decimal?)o.TotalAmount, cancellationToken) ?? 0m;

        var orderDiscounts = await _context.Orders
            .Where(o => o.CreatedAt >= startOfYearUtc && o.CreatedAt < upToDateUtc && (o.PaymentStatus == "PAID" || o.PaymentStatus == "Paid" || o.PaymentStatus == "DEBT" || o.PaymentStatus == "Debt") && o.Status != "Cancelled")
            .SumAsync(o => (decimal?)o.DiscountAmount, cancellationToken) ?? 0m;

        var returns = await _context.Orders
            .Where(o => o.CreatedAt >= startOfYearUtc && o.CreatedAt < upToDateUtc)
            .SumAsync(o => (decimal?)o.ReturnedAmount, cancellationToken) ?? 0m;

        // Manual off-POS revenue is not represented by an Order. Include only
        // entries explicitly classified by the backend and net compensating
        // payments so a voided receipt cannot inflate taxable revenue.
        var manualTaxableReceipts = await _context.CashbookEntries
            .Where(e => e.OccurredAtUtc >= startOfYearUtc && e.OccurredAtUtc < upToDateUtc &&
                        (e.SourceType == "MANUAL" || e.SourceType == "COMPENSATING") &&
                        e.TaxTreatment == CashbookTaxTreatment.TaxableRevenue)
            .SumAsync(e => (decimal?)(e.EntryType == CashbookEntryType.Receipt ? e.Amount : -e.Amount), cancellationToken) ?? 0m;

        var netRevenue = orderGross - orderDiscounts - returns + manualTaxableReceipts;
        return netRevenue;
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
            orderQuery = orderQuery.Where(o => o.BranchId == branchId.Value);
        }

        orderQuery = orderQuery.Where(o => o.CreatedAt >= fromDateUtc && o.CreatedAt < toDateUtc && (o.PaymentStatus == "PAID" || o.PaymentStatus == "Paid" || o.PaymentStatus == "DEBT" || o.PaymentStatus == "Debt") && o.Status != "Cancelled");

        var gross = await orderQuery.SumAsync(o => (decimal?)o.TotalAmount, cancellationToken) ?? 0m;
        var discounts = await orderQuery.SumAsync(o => (decimal?)o.DiscountAmount, cancellationToken) ?? 0m;

        var returnQuery = _context.Orders.AsQueryable();
        if (branchId.HasValue)
        {
            returnQuery = returnQuery.Where(o => o.BranchId == branchId.Value);
        }

        var returns = await returnQuery
            .Where(o => o.CreatedAt >= fromDateUtc && o.CreatedAt < toDateUtc)
            .SumAsync(o => (decimal?)o.ReturnedAmount, cancellationToken) ?? 0m;

        var manualRevenueQuery = _context.CashbookEntries.Where(e =>
            e.OccurredAtUtc >= fromDateUtc && e.OccurredAtUtc < toDateUtc &&
            (e.SourceType == "MANUAL" || e.SourceType == "COMPENSATING") &&
            e.TaxTreatment == CashbookTaxTreatment.TaxableRevenue);
        if (branchId.HasValue)
        {
            manualRevenueQuery = manualRevenueQuery.Where(e => e.BranchId == branchId.Value);
        }
        var manualTaxableRevenue = await manualRevenueQuery
            .SumAsync(e => (decimal?)(e.EntryType == CashbookEntryType.Receipt ? e.Amount : -e.Amount), cancellationToken) ?? 0m;

        return (gross + manualTaxableRevenue, discounts, returns);
    }

    private const decimal DefaultCashlessExpenseThreshold = 5_000_000m;

    private static bool IsCapitalOrEquityCategory(string? category)
    {
        var lower = (category ?? string.Empty).ToLowerInvariant();
        return lower.Contains("capital") ||
               lower.Contains("equity") ||
               lower.Contains("vốn") ||
               lower.Contains("rút vốn");
    }

    private static bool IsOwnerOrFamilySalary(string? category, string? note)
    {
        var text = $"{category} {note}".ToLowerInvariant();
        if (text.Contains("bảo hiểm bắt buộc") || text.Contains("bhxh bắt buộc"))
        {
            return false;
        }

        return text.Contains("lương chủ hộ") ||
               text.Contains("tiền lương chủ hộ") ||
               text.Contains("tiền công chủ hộ") ||
               text.Contains("thù lao chủ hộ") ||
               text.Contains("lương thành viên hộ") ||
               text.Contains("owner salary");
    }

    public async Task<decimal> GetPeriodOpexAsync(
        DateTime fromDateUtc,
        DateTime toDateUtc,
        Guid? branchId,
        CancellationToken cancellationToken = default)
    {
        var voucherQuery = _context.ExpenseVouchers.AsNoTracking().Where(v =>
            v.ExpenseDate >= fromDateUtc &&
            v.ExpenseDate <= toDateUtc &&
            (v.Status == ExpenseVoucherStatus.Paid || v.Status == ExpenseVoucherStatus.Approved) &&
            v.Nature == FinancialNature.OperatingExpense &&
            v.IsTaxDeductible);

        if (branchId.HasValue)
        {
            voucherQuery = voucherQuery.Where(v => v.BranchId == branchId.Value);
        }

        var rawVouchers = await voucherQuery.ToListAsync(cancellationToken);
        // Điều 6 NĐ 68/2026: Loại rút vốn, loại lương chủ hộ/thành viên hộ, loại chi tiền mặt >= 5 triệu (bắt buộc chuyển khoản)
        var voucherExpenses = rawVouchers
            .Where(v => !IsCapitalOrEquityCategory(v.Category) &&
                        !IsOwnerOrFamilySalary(v.Category, v.Note) &&
                        (v.Amount < DefaultCashlessExpenseThreshold || v.PaymentSourceType == FinanceAccountType.Bank))
            .Sum(v => v.Amount);

        var manualExpenseQuery = _context.CashbookEntries.AsNoTracking().Where(e =>
            e.OccurredAtUtc >= fromDateUtc && e.OccurredAtUtc <= toDateUtc &&
            (e.SourceType == "MANUAL" || e.SourceType == "COMPENSATING") &&
            e.TaxTreatment == CashbookTaxTreatment.DeductibleExpense);
        if (branchId.HasValue)
        {
            manualExpenseQuery = manualExpenseQuery.Where(e => e.BranchId == branchId.Value);
        }

        var rawManualEntries = await manualExpenseQuery.ToListAsync(cancellationToken);
        var manualDeductibleExpenses = rawManualEntries
            .Where(e => !IsOwnerOrFamilySalary(e.CategoryCode, e.Description) &&
                        (e.EntryType != CashbookEntryType.Payment ||
                         e.Amount < DefaultCashlessExpenseThreshold ||
                         e.AccountType == FinanceAccountType.Bank))
            .Sum(e => e.EntryType == CashbookEntryType.Payment ? e.Amount : -e.Amount);

        return Math.Max(0m, voucherExpenses + manualDeductibleExpenses);
    }

    public async Task<TaxExpenseComplianceSummaryDto> GetExpenseComplianceSummaryAsync(
        DateTime fromDateUtc,
        DateTime toDateUtc,
        Guid? branchId,
        decimal cashlessThreshold = DefaultCashlessExpenseThreshold,
        CancellationToken cancellationToken = default)
    {
        var summary = new TaxExpenseComplianceSummaryDto();

        var voucherQuery = _context.ExpenseVouchers.AsNoTracking().Where(v =>
            v.ExpenseDate >= fromDateUtc &&
            v.ExpenseDate <= toDateUtc &&
            (v.Status == ExpenseVoucherStatus.Paid || v.Status == ExpenseVoucherStatus.Approved) &&
            v.Nature == FinancialNature.OperatingExpense &&
            v.IsTaxDeductible);
        if (branchId.HasValue)
        {
            voucherQuery = voucherQuery.Where(v => v.BranchId == branchId.Value);
        }

        var vouchers = await voucherQuery.ToListAsync(cancellationToken);
        foreach (var v in vouchers.Where(v => !IsCapitalOrEquityCategory(v.Category)))
        {
            if (IsOwnerOrFamilySalary(v.Category, v.Note))
            {
                summary.ExcludedOwnerSalaryAmount += v.Amount;
                summary.ExcludedOwnerSalaryCount++;
            }
            else if (v.Amount >= cashlessThreshold && v.PaymentSourceType != FinanceAccountType.Bank)
            {
                summary.ExcludedCashExpensesAmount += v.Amount;
                summary.ExcludedCashExpensesCount++;
            }
        }

        var manualQuery = _context.CashbookEntries.AsNoTracking().Where(e =>
            e.OccurredAtUtc >= fromDateUtc &&
            e.OccurredAtUtc <= toDateUtc &&
            (e.SourceType == "MANUAL" || e.SourceType == "COMPENSATING") &&
            e.TaxTreatment == CashbookTaxTreatment.DeductibleExpense &&
            e.EntryType == CashbookEntryType.Payment);
        if (branchId.HasValue)
        {
            manualQuery = manualQuery.Where(e => e.BranchId == branchId.Value);
        }

        var manualPayments = await manualQuery.ToListAsync(cancellationToken);
        foreach (var e in manualPayments)
        {
            if (IsOwnerOrFamilySalary(e.CategoryCode, e.Description))
            {
                summary.ExcludedOwnerSalaryAmount += e.Amount;
                summary.ExcludedOwnerSalaryCount++;
            }
            else if (e.Amount >= cashlessThreshold && e.AccountType != FinanceAccountType.Bank)
            {
                summary.ExcludedCashExpensesAmount += e.Amount;
                summary.ExcludedCashExpensesCount++;
            }
        }

        // Kiểm soát Phiếu nhập kho (GoodsReceipts): cảnh báo phiếu >= 5 triệu trả tiền mặt (CASH) hoặc thiếu số hóa đơn đầu vào
        var receiptQuery = _context.GoodsReceipts
            .AsNoTracking()
            .Where(g => g.ReceivedAtUtc >= fromDateUtc &&
                        g.ReceivedAtUtc <= toDateUtc &&
                        (g.Status == "CONFIRMED" || g.Status == "COMPLETED"));
        if (branchId.HasValue)
        {
            receiptQuery = receiptQuery.Where(g => g.BranchId == branchId.Value);
        }

        var receipts = await receiptQuery
            .OrderByDescending(g => g.ReceivedAtUtc)
            .Select(g => new
            {
                g.Id,
                g.Code,
                g.ReceivedAtUtc,
                SupplierName = g.Supplier != null ? g.Supplier.Name : null,
                g.GrandTotal,
                g.PaymentMethod,
                g.InvoiceNo
            })
            .ToListAsync(cancellationToken);
        foreach (var g in receipts)
        {
            var isCashOverThreshold = g.GrandTotal >= cashlessThreshold
                && string.Equals(g.PaymentMethod, "CASH", StringComparison.OrdinalIgnoreCase);
            var isMissingInvoice = string.IsNullOrWhiteSpace(g.InvoiceNo);

            if (!isCashOverThreshold && !isMissingInvoice) continue;

            var reasons = new List<string>();
            if (isCashOverThreshold)
                reasons.Add($"Thanh toán tiền mặt ≥ {cashlessThreshold:N0}đ (Điều 6 NĐ 68/2026 yêu cầu không dùng tiền mặt)");
            if (isMissingInvoice)
                reasons.Add("Thiếu số hóa đơn đầu vào (InvoiceNo)");

            summary.RiskyGoodsReceipts.Add(new RiskyGoodsReceiptDto
            {
                Id = g.Id,
                Code = g.Code,
                ReceivedAtUtc = g.ReceivedAtUtc,
                SupplierName = g.SupplierName ?? "Nhà cung cấp",
                GrandTotal = g.GrandTotal,
                PaymentMethod = g.PaymentMethod,
                InvoiceNo = g.InvoiceNo,
                RiskReason = string.Join("; ", reasons)
            });
        }

        return summary;
    }

    public Task<decimal> GetPeriodDeductibleExpensesAsync(
        DateTime fromDateUtc,
        DateTime toDateUtc,
        Guid? branchId,
        CancellationToken cancellationToken = default)
    {
        return GetPeriodOpexAsync(fromDateUtc, toDateUtc, branchId, cancellationToken);
    }

    public async Task<List<ExpenseVoucher>> GetDeductibleExpenseVouchersAsync(
        DateTime fromDateUtc,
        DateTime toDateUtc,
        Guid? branchId,
        CancellationToken cancellationToken = default)
    {
        var query = _context.ExpenseVouchers.AsNoTracking().Where(v =>
            v.ExpenseDate >= fromDateUtc &&
            v.ExpenseDate <= toDateUtc &&
            v.Nature == FinancialNature.OperatingExpense &&
            v.IsTaxDeductible &&
            (v.Status == ExpenseVoucherStatus.Paid || v.Status == ExpenseVoucherStatus.Approved));

        if (branchId.HasValue)
        {
            query = query.Where(v => v.BranchId == branchId.Value);
        }

        var vouchers = await query.OrderBy(v => v.ExpenseDate).ToListAsync(cancellationToken);
        return vouchers
            .Where(v => !IsCapitalOrEquityCategory(v.Category) &&
                        !IsOwnerOrFamilySalary(v.Category, v.Note) &&
                        (v.Amount < DefaultCashlessExpenseThreshold || v.PaymentSourceType == FinanceAccountType.Bank))
            .ToList();
    }

    public async Task<(decimal TotalCogs, bool IsEstimated)> GetPeriodCogsAsync(
        DateTime fromDateUtc,
        DateTime toDateUtc,
        Guid? branchId,
        CancellationToken cancellationToken = default)
    {
        var (documentedCogs, hasMissingCostItems, _) = await GetPeriodCogsDetailedAsync(fromDateUtc, toDateUtc, branchId, cancellationToken);
        return (documentedCogs, hasMissingCostItems);
    }

    public async Task<(decimal DocumentedCogs, bool HasMissingCostItems, decimal EstimatedMissingCogsWarning)> GetPeriodCogsDetailedAsync(
        DateTime fromDateUtc,
        DateTime toDateUtc,
        Guid? branchId,
        CancellationToken cancellationToken = default)
    {
        var orderQuery = _context.Orders.Include(o => o.Details).AsQueryable();
        if (branchId.HasValue)
        {
            orderQuery = orderQuery.Where(o => o.BranchId == branchId.Value);
        }
        orderQuery = orderQuery.Where(o => o.CreatedAt >= fromDateUtc && o.CreatedAt < toDateUtc && (o.PaymentStatus == "PAID" || o.PaymentStatus == "Paid" || o.PaymentStatus == "DEBT" || o.PaymentStatus == "Debt") && o.Status != "Cancelled");

        var orders = await orderQuery.ToListAsync(cancellationToken);
        if (orders.Count == 0)
        {
            return (0m, false, 0m);
        }

        // Lấy danh sách Product IDs từ các orders
        var productIds = orders.SelectMany(o => o.Items).Select(i => i.ProductId).Distinct().ToList();
        var productCostDict = await _context.Products
            .AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .Select(p => new { p.Id, CostPrice = p.CostPrice ?? 0m })
            .ToDictionaryAsync(p => p.Id, p => p.CostPrice, cancellationToken);

        decimal documentedCogs = 0m;
        decimal estimatedMissingCogsWarning = 0m;
        int totalItemsCount = 0;
        int itemsWithRealCost = 0;

        foreach (var order in orders)
        {
            foreach (var item in order.Items)
            {
                totalItemsCount++;
                decimal itemCost = 0m;
                bool itemHasCost = false;
                if (item.CostPriceAtSale > 0)
                {
                    itemCost = item.CostPriceAtSale;
                    itemHasCost = true;
                }
                else if (productCostDict.TryGetValue(item.ProductId, out var costPrice) && costPrice > 0)
                {
                    itemCost = costPrice;
                    itemHasCost = true;
                }

                if (itemHasCost)
                {
                    itemsWithRealCost++;
                    documentedCogs += item.Quantity * itemCost;
                }
                else
                {
                    // Điều 6 NĐ 68/2026: KHÔNG đưa giá vốn ước tính 70% vào chi phí được trừ;
                    // chỉ ghi nhận vào cảnh báo nội bộ để chủ hộ bổ sung chứng từ/giá vốn.
                    estimatedMissingCogsWarning += Math.Round(item.Quantity * item.UnitPrice * 0.70m, 0, MidpointRounding.AwayFromZero);
                }
            }
        }

        // Tính hàng trả lại trong kỳ
        var returnQuery = _context.Orders.AsQueryable();
        if (branchId.HasValue)
        {
            returnQuery = returnQuery.Where(o => o.BranchId == branchId.Value);
        }
        var salesReturns = await returnQuery
            .Where(o => o.CreatedAt >= fromDateUtc && o.CreatedAt < toDateUtc)
            .SumAsync(o => (decimal?)o.ReturnedAmount, cancellationToken) ?? 0m;

        // Trừ giá vốn hàng trả lại theo tỷ lệ giá vốn thực tế trên doanh thu
        var totalGrossRevenue = orders.Sum(o => o.TotalAmount);
        decimal returnCogs = 0m;
        if (totalGrossRevenue > 0 && salesReturns > 0 && documentedCogs > 0)
        {
            var cogsRatio = documentedCogs / totalGrossRevenue;
            returnCogs = Math.Round(salesReturns * cogsRatio, 0, MidpointRounding.AwayFromZero);
        }

        var finalCogs = Math.Max(0, documentedCogs - returnCogs);
        var hasMissingCostItems = totalItemsCount > 0 && itemsWithRealCost < totalItemsCount;
        return (finalCogs, hasMissingCostItems, estimatedMissingCogsWarning);
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

        // Truy vấn đơn hàng bán lẻ trong kỳ (Include Details để phục vụ tính số lượng xuất kho S2d)
        var orderQuery = _context.Orders.Include(o => o.Details).AsQueryable();
        if (branchId.HasValue)
        {
            orderQuery = orderQuery.Where(o => o.BranchId == branchId.Value);
        }
        orderQuery = orderQuery.Where(o => o.CreatedAt >= fromDateUtc && o.CreatedAt < toDateUtc && (o.PaymentStatus == "PAID" || o.PaymentStatus == "Paid" || o.PaymentStatus == "DEBT" || o.PaymentStatus == "Debt") && o.Status != "Cancelled");

        var orders = await orderQuery
            .OrderBy(o => o.CreatedAt)
            .ToListAsync(cancellationToken);

        // Truy vấn phiếu thu thủ công / bút toán bù trừ chịu thuế ngoài POS để khớp 100% với tờ khai 01/CNKD
        var manualRevenueQuery = _context.CashbookEntries.AsNoTracking().Where(e =>
            e.OccurredAtUtc >= fromDateUtc &&
            e.OccurredAtUtc < toDateUtc &&
            (e.SourceType == "MANUAL" || e.SourceType == "COMPENSATING") &&
            e.TaxTreatment == CashbookTaxTreatment.TaxableRevenue);
        if (branchId.HasValue)
        {
            manualRevenueQuery = manualRevenueQuery.Where(e => e.BranchId == branchId.Value);
        }
        var manualTaxableEntries = await manualRevenueQuery
            .OrderBy(e => e.OccurredAtUtc)
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
        if ((!bookType.HasValue && group == HouseholdTaxGroup.Group1_Under1B) || bookType == AccountingBookType.S1a_RevenueGroup1)
        {
            result.AddRange(orders.Select(o =>
            {
                var netAmount = Math.Max(0m, o.TotalAmount - o.DiscountAmount);
                return new AccountingBookEntryDto
                {
                    VoucherNumber = o.OrderCode,
                    VoucherDate = o.CreatedAt,
                    Description = $"Bán lẻ hàng hóa (Nhóm 1) {o.OrderCode}",
                    Amount = netAmount,
                    Category = "Doanh thu bán lẻ (Nhóm 1)",
                    SectionGroup = "Doanh thu bán lẻ",
                    BookType = AccountingBookType.S1a_RevenueGroup1,
                    BookCode = "S1a-HKD"
                };
            }));
            result.AddRange(manualTaxableEntries.Select(e =>
            {
                var signedAmount = e.EntryType == CashbookEntryType.Receipt ? e.Amount : -e.Amount;
                return new AccountingBookEntryDto
                {
                    VoucherNumber = e.EntryCode,
                    VoucherDate = e.OccurredAtUtc,
                    Description = !string.IsNullOrWhiteSpace(e.Description) ? e.Description : $"Doanh thu ngoài POS ({e.EntryCode})",
                    Amount = signedAmount,
                    Category = "Doanh thu ngoài POS chịu thuế",
                    SectionGroup = "Doanh thu bán lẻ",
                    BookType = AccountingBookType.S1a_RevenueGroup1,
                    BookCode = "S1a-HKD",
                    InvoiceNumber = e.ReferenceCode
                };
            }));
        }

        // 2. Phân loại theo Nhóm 2 (Phương pháp tỷ lệ % doanh thu): Dùng mẫu S2a-HKD
        if ((!bookType.HasValue && group == HouseholdTaxGroup.Group2_1BTo3B && method == TaxCalculationMethod.RevenuePercentage) || bookType == AccountingBookType.S2a_RevenueGroup2)
        {
            result.AddRange(orders.Select(o =>
            {
                var netAmount = Math.Max(0m, o.TotalAmount - o.DiscountAmount);
                return new AccountingBookEntryDto
                {
                    VoucherNumber = o.OrderCode,
                    VoucherDate = o.CreatedAt,
                    Description = $"Bán lẻ hàng hóa chịu thuế tỷ lệ % {o.OrderCode}",
                    Amount = netAmount,
                    Category = "Doanh thu bán lẻ chịu thuế",
                    SectionGroup = "1. Phân phối, cung cấp hàng hóa",
                    TaxRate = 1.0m,
                    TaxAmount = Math.Round(netAmount * 0.01m, 0, MidpointRounding.AwayFromZero),
                    BookType = AccountingBookType.S2a_RevenueGroup2,
                    BookCode = "S2a-HKD"
                };
            }));
            result.AddRange(manualTaxableEntries.Select(e =>
            {
                var signedAmount = e.EntryType == CashbookEntryType.Receipt ? e.Amount : -e.Amount;
                return new AccountingBookEntryDto
                {
                    VoucherNumber = e.EntryCode,
                    VoucherDate = e.OccurredAtUtc,
                    Description = !string.IsNullOrWhiteSpace(e.Description) ? e.Description : $"Doanh thu ngoài POS chịu thuế ({e.EntryCode})",
                    Amount = signedAmount,
                    Category = "Doanh thu ngoài POS chịu thuế",
                    SectionGroup = "1. Phân phối, cung cấp hàng hóa",
                    TaxRate = 1.0m,
                    TaxAmount = Math.Round(signedAmount * 0.01m, 0, MidpointRounding.AwayFromZero),
                    BookType = AccountingBookType.S2a_RevenueGroup2,
                    BookCode = "S2a-HKD",
                    InvoiceNumber = e.ReferenceCode
                };
            }));
        }

        // 3. Phân loại theo Phương pháp Kê khai (Nhóm 3, 4 hoặc hộ lựa chọn): Bộ sổ S2b, S2c, S2d, S2e, S3a
        // S2b-HKD: Sổ chi tiết doanh thu bán hàng hóa, dịch vụ kê khai
        if ((!bookType.HasValue && (method == TaxCalculationMethod.DeclarationBased || group == HouseholdTaxGroup.Group4_Over50B)) || bookType == AccountingBookType.S2b_RevenueDeclaration)
        {
            result.AddRange(orders.Select(o =>
            {
                var netAmount = Math.Max(0m, o.TotalAmount - o.DiscountAmount);
                return new AccountingBookEntryDto
                {
                    VoucherNumber = o.OrderCode,
                    VoucherDate = o.CreatedAt,
                    Description = $"Doanh thu bán hàng kê khai {o.OrderCode}",
                    Amount = netAmount,
                    Category = "Doanh thu bán lẻ kê khai",
                    SectionGroup = "1. Phân phối, cung cấp hàng hóa",
                    TaxRate = 1.0m,
                    TaxAmount = Math.Round(netAmount * 0.01m, 0, MidpointRounding.AwayFromZero),
                    BookType = AccountingBookType.S2b_RevenueDeclaration,
                    BookCode = "S2b-HKD"
                };
            }));
            result.AddRange(manualTaxableEntries.Select(e =>
            {
                var signedAmount = e.EntryType == CashbookEntryType.Receipt ? e.Amount : -e.Amount;
                return new AccountingBookEntryDto
                {
                    VoucherNumber = e.EntryCode,
                    VoucherDate = e.OccurredAtUtc,
                    Description = !string.IsNullOrWhiteSpace(e.Description) ? e.Description : $"Doanh thu ngoài POS kê khai ({e.EntryCode})",
                    Amount = signedAmount,
                    Category = "Doanh thu ngoài POS kê khai",
                    SectionGroup = "1. Phân phối, cung cấp hàng hóa",
                    TaxRate = 1.0m,
                    TaxAmount = Math.Round(signedAmount * 0.01m, 0, MidpointRounding.AwayFromZero),
                    BookType = AccountingBookType.S2b_RevenueDeclaration,
                    BookCode = "S2b-HKD",
                    InvoiceNumber = e.ReferenceCode
                };
            }));
        }

        // S2c-HKD: Sổ chi tiết doanh thu, chi phí (Thu & Chi)
        if ((!bookType.HasValue && (method == TaxCalculationMethod.DeclarationBased || group == HouseholdTaxGroup.Group4_Over50B)) || bookType == AccountingBookType.S2c_RevenueAndExpense)
        {
            // Doanh thu bán hàng lẻ
            result.AddRange(orders.Select(o =>
            {
                var netAmount = Math.Max(0m, o.TotalAmount - o.DiscountAmount);
                return new AccountingBookEntryDto
                {
                    VoucherNumber = o.OrderCode,
                    VoucherDate = o.CreatedAt,
                    Description = $"[Thu] Bán lẻ đơn hàng {o.OrderCode}",
                    Amount = netAmount,
                    Category = "Doanh thu",
                    SectionGroup = "1. Doanh thu",
                    BookType = AccountingBookType.S2c_RevenueAndExpense,
                    BookCode = "S2c-HKD"
                };
            }));
            result.AddRange(manualTaxableEntries.Select(e =>
            {
                var signedAmount = e.EntryType == CashbookEntryType.Receipt ? e.Amount : -e.Amount;
                return new AccountingBookEntryDto
                {
                    VoucherNumber = e.EntryCode,
                    VoucherDate = e.OccurredAtUtc,
                    Description = $"[Thu] {(!string.IsNullOrWhiteSpace(e.Description) ? e.Description : $"Doanh thu ngoài POS ({e.EntryCode})")}",
                    Amount = signedAmount,
                    Category = "Doanh thu",
                    SectionGroup = "1. Doanh thu",
                    BookType = AccountingBookType.S2c_RevenueAndExpense,
                    BookCode = "S2c-HKD",
                    InvoiceNumber = e.ReferenceCode
                };
            }));

            // Kết chuyển COGS thực tế có chứng từ trong kỳ (Điều 6 NĐ 68/2026: không tự cộng 70% ước tính)
            var cogs = await GetPeriodCogsAsync(fromDateUtc, toDateUtc, branchId, cancellationToken);
            if (cogs.TotalCogs > 0)
            {
                result.Add(new AccountingBookEntryDto
                {
                    VoucherNumber = $"COGS-{fromDateUtc:yyyyMM}",
                    VoucherDate = toDateUtc.AddSeconds(-1),
                    Description = cogs.IsEstimated
                        ? "Giá vốn hàng bán thực tế có chứng từ trong kỳ (lưu ý: còn mặt hàng thiếu giá vốn chưa được trừ)"
                        : "Giá vốn hàng bán thực tế trong kỳ (theo giá vốn hàng xuất kho)",
                    Amount = cogs.TotalCogs,
                    SectionGroup = "2.a) Chi phí nguyên liệu, vật liệu, nhiên liệu, hàng hóa",
                    Category = "Giá vốn hàng bán (COGS)",
                    BookType = AccountingBookType.S2c_RevenueAndExpense,
                    BookCode = "S2c-HKD"
                });
            }

            // Chi phí SXKD hợp lý từ ExpenseVouchers (đáp ứng Điều 6 NĐ 68/2026: không tiền mặt >= 5tr, loại lương chủ hộ)
            result.AddRange(vouchers
                .Where(v => v.Nature == FinancialNature.OperatingExpense &&
                            v.IsTaxDeductible &&
                            !IsOwnerOrFamilySalary(v.Category, v.Note) &&
                            (v.Amount < DefaultCashlessExpenseThreshold || v.PaymentSourceType == FinanceAccountType.Bank))
                .Select(v => new AccountingBookEntryDto
            {
                VoucherNumber = v.VoucherCode ?? string.Empty,
                VoucherDate = v.ExpenseDate,
                Description = $"[Chi] {(!string.IsNullOrEmpty(v.Note) ? v.Note : $"{v.Category} - {v.BeneficiaryName}")}",
                Amount = v.Amount,
                Category = v.Category ?? string.Empty,
                SectionGroup = ClassifyS2cExpenseGroup(v.Category, v.Note),
                BookType = AccountingBookType.S2c_RevenueAndExpense,
                BookCode = "S2c-HKD",
                BeneficiaryName = v.BeneficiaryName,
                InvoiceNumber = v.InvoiceNumber
            }));
        }

        // S2d-HKD: Sổ chi tiết vật liệu, dụng cụ, sản phẩm, hàng hóa (Nhập - Xuất - Tồn)
        if ((!bookType.HasValue && (method == TaxCalculationMethod.DeclarationBased || group == HouseholdTaxGroup.Group4_Over50B)) || bookType == AccountingBookType.S2d_MaterialsAndGoods)
        {
            // 1. Nhập kho từ Nhà cung cấp (GoodsReceipts)
            var receiptQuery = _context.GoodsReceipts.AsNoTracking().AsQueryable();
            if (branchId.HasValue)
            {
                receiptQuery = receiptQuery.Where(g => g.BranchId == branchId.Value);
            }
            receiptQuery = receiptQuery.Where(g => g.ReceivedAtUtc >= fromDateUtc && g.ReceivedAtUtc <= toDateUtc && (g.Status == "CONFIRMED" || g.Status == "COMPLETED"));
            var receipts = await receiptQuery
                .OrderBy(g => g.ReceivedAtUtc)
                .Select(g => new
                {
                    g.Code,
                    g.ReceivedAtUtc,
                    g.GrandTotal,
                    g.InvoiceNo,
                    SupplierName = g.Supplier != null ? g.Supplier.Name : null,
                    Lines = g.Lines.Select(l => new { l.AcceptedQty, l.DeliveredQty, l.OrderedQty }).ToList()
                })
                .ToListAsync(cancellationToken);

            result.AddRange(receipts.Select(g =>
            {
                var inQuantity = g.Lines.Sum(l => l.AcceptedQty > 0 ? l.AcceptedQty : (l.DeliveredQty > 0 ? l.DeliveredQty : l.OrderedQty));
                var inAmount = g.GrandTotal;
                var unitPrice = inQuantity > 0 ? inAmount / inQuantity : 0m;

                return new AccountingBookEntryDto
                {
                    VoucherNumber = g.Code,
                    VoucherDate = g.ReceivedAtUtc,
                    Description = $"[Nhập kho] Nhập hàng từ NCC {(!string.IsNullOrWhiteSpace(g.SupplierName) ? g.SupplierName : "Nhà cung cấp")}",
                    Amount = inAmount,
                    InQuantity = inQuantity,
                    InAmount = inAmount,
                    UnitPrice = Math.Round(unitPrice, 2, MidpointRounding.AwayFromZero),
                    Unit = "Lô/Hàng",
                    SectionGroup = "Nhập kho trong kỳ",
                    Category = "Nhập kho hàng hóa",
                    BookType = AccountingBookType.S2d_MaterialsAndGoods,
                    BookCode = "S2d-HKD",
                    BeneficiaryName = g.SupplierName,
                    InvoiceNumber = g.InvoiceNo
                };
            }));

            // 2. Xuất kho bán lẻ POS
            result.AddRange(orders.Select(o =>
            {
                var outQuantity = o.Details.Sum(d => d.Qty);
                var outAmount = o.Details.Sum(d => d.CostPriceAtSale > 0 ? d.CostPriceAtSale * d.Qty : (d.UnitPrice * d.Qty * 0.7m));
                var unitPrice = outQuantity > 0 ? outAmount / outQuantity : 0m;

                return new AccountingBookEntryDto
                {
                    VoucherNumber = o.OrderCode,
                    VoucherDate = o.CreatedAt,
                    Description = $"[Xuất kho] Xuất bán lẻ đơn hàng {o.OrderCode}",
                    Amount = outAmount,
                    OutQuantity = outQuantity,
                    OutAmount = outAmount,
                    UnitPrice = Math.Round(unitPrice, 2, MidpointRounding.AwayFromZero),
                    Unit = "Sản phẩm",
                    SectionGroup = "Xuất kho trong kỳ",
                    Category = "Xuất kho bán lẻ",
                    BookType = AccountingBookType.S2d_MaterialsAndGoods,
                    BookCode = "S2d-HKD"
                };
            }));

            // 3. Chi phí bao bì, dụng cụ, vật tư tiêu hao
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
                Description = !string.IsNullOrEmpty(v.Note) ? v.Note : $"[Vật tư] {v.Category} - {v.BeneficiaryName}",
                Amount = v.Amount,
                InAmount = v.Amount,
                Unit = "Khoản",
                SectionGroup = "Vật liệu, dụng cụ tiêu hao",
                Category = v.Category ?? string.Empty,
                BookType = AccountingBookType.S2d_MaterialsAndGoods,
                BookCode = "S2d-HKD",
                BeneficiaryName = v.BeneficiaryName,
                InvoiceNumber = v.InvoiceNumber
            }));
        }

        // S2e-HKD: Sổ chi tiết tiền (tiền mặt và tiền gửi ngân hàng - Đọc trực tiếp CashbookEntries)
        if ((!bookType.HasValue && (method == TaxCalculationMethod.DeclarationBased || group == HouseholdTaxGroup.Group4_Over50B)) || bookType == AccountingBookType.S2e_CashAndBank)
        {
            // Truy vấn số dư đầu kỳ trước fromDateUtc
            var priorCashbookQuery = _context.CashbookEntries.AsQueryable();
            if (branchId.HasValue)
            {
                priorCashbookQuery = priorCashbookQuery.Where(c => c.BranchId == branchId.Value || (c.AccountType == FinanceAccountType.Bank && c.BranchId == null));
            }
            priorCashbookQuery = priorCashbookQuery.Where(c => c.OccurredAtUtc < fromDateUtc);

            var openingCashReceipts = await priorCashbookQuery
                .Where(c => c.AccountType == FinanceAccountType.Till || c.AccountType == FinanceAccountType.Safe)
                .Where(c => c.EntryType == CashbookEntryType.Receipt)
                .SumAsync(c => (decimal?)c.Amount, cancellationToken) ?? 0m;

            var openingCashPayments = await priorCashbookQuery
                .Where(c => c.AccountType == FinanceAccountType.Till || c.AccountType == FinanceAccountType.Safe)
                .Where(c => c.EntryType == CashbookEntryType.Payment)
                .SumAsync(c => (decimal?)c.Amount, cancellationToken) ?? 0m;

            var openingCash = openingCashReceipts - openingCashPayments;

            var openingBankReceipts = await priorCashbookQuery
                .Where(c => c.AccountType == FinanceAccountType.Bank)
                .Where(c => c.EntryType == CashbookEntryType.Receipt)
                .SumAsync(c => (decimal?)c.Amount, cancellationToken) ?? 0m;

            var openingBankPayments = await priorCashbookQuery
                .Where(c => c.AccountType == FinanceAccountType.Bank)
                .Where(c => c.EntryType == CashbookEntryType.Payment)
                .SumAsync(c => (decimal?)c.Amount, cancellationToken) ?? 0m;

            var openingBank = openingBankReceipts - openingBankPayments;

            // Truy vấn phát sinh trong kỳ
            var cashbookQuery = _context.CashbookEntries.AsQueryable();
            if (branchId.HasValue)
            {
                cashbookQuery = cashbookQuery.Where(c => c.BranchId == branchId.Value || (c.AccountType == FinanceAccountType.Bank && c.BranchId == null));
            }
            cashbookQuery = cashbookQuery.Where(c => c.OccurredAtUtc >= fromDateUtc && c.OccurredAtUtc <= toDateUtc);
            var cashEntries = await cashbookQuery.OrderBy(c => c.OccurredAtUtc).ToListAsync(cancellationToken);

            // Khối I: Quỹ Tiền mặt
            result.Add(new AccountingBookEntryDto
            {
                VoucherNumber = "DK-CASH",
                VoucherDate = fromDateUtc,
                Description = "Số dư đầu kỳ",
                Amount = openingCash,
                BalanceAmount = openingCash,
                SectionGroup = "Khối I - Quỹ Tiền mặt",
                Category = "Số dư đầu kỳ",
                BookType = AccountingBookType.S2e_CashAndBank,
                BookCode = "S2e-HKD",
                IsSummaryRow = true
            });

            var cashTransactions = cashEntries
                .Where(c => c.AccountType == FinanceAccountType.Till || c.AccountType == FinanceAccountType.Safe || c.AccountType != FinanceAccountType.Bank)
                .ToList();

            decimal runningCash = openingCash;
            foreach (var c in cashTransactions)
            {
                var isReceipt = c.EntryType == CashbookEntryType.Receipt;
                var inAmt = isReceipt ? c.Amount : 0m;
                var outAmt = isReceipt ? 0m : c.Amount;
                runningCash += (inAmt - outAmt);

                var accName = c.AccountType switch
                {
                    FinanceAccountType.Till => "Két quầy",
                    FinanceAccountType.Safe => "Két sắt",
                    _ => "Quỹ tiền mặt"
                };

                result.Add(new AccountingBookEntryDto
                {
                    VoucherNumber = c.EntryCode,
                    VoucherDate = c.OccurredAtUtc,
                    Description = isReceipt ? $"[Thu tiền {accName}] {c.Description}" : $"[Chi tiền {accName}] {c.Description}",
                    Amount = c.Amount,
                    InAmount = inAmt,
                    OutAmount = outAmt,
                    BalanceAmount = runningCash,
                    SectionGroup = "Khối I - Quỹ Tiền mặt",
                    Category = isReceipt ? $"Thu quỹ ({accName})" : $"Chi quỹ ({accName})",
                    BookType = AccountingBookType.S2e_CashAndBank,
                    BookCode = "S2e-HKD",
                    BeneficiaryName = accName,
                    InvoiceNumber = c.ReferenceCode
                });
            }

            // Fallback chi tiền mặt từ ExpenseVouchers nếu CashbookEntries trống hoàn toàn
            if (cashEntries.Count == 0)
            {
                var cashVouchers = vouchers.Where(v => v.PaymentSourceType != FinanceAccountType.Bank).ToList();
                foreach (var v in cashVouchers)
                {
                    runningCash -= v.Amount;
                    result.Add(new AccountingBookEntryDto
                    {
                        VoucherNumber = v.VoucherCode ?? string.Empty,
                        VoucherDate = v.ExpenseDate,
                        Description = $"[Chi tiền mặt] {(!string.IsNullOrEmpty(v.Note) ? v.Note : $"{v.Category} - {v.BeneficiaryName}")}",
                        Amount = v.Amount,
                        InAmount = 0m,
                        OutAmount = v.Amount,
                        BalanceAmount = runningCash,
                        SectionGroup = "Khối I - Quỹ Tiền mặt",
                        Category = "Chi tiền mặt",
                        BookType = AccountingBookType.S2e_CashAndBank,
                        BookCode = "S2e-HKD",
                        BeneficiaryName = v.BeneficiaryName,
                        InvoiceNumber = v.InvoiceNumber
                    });
                }
            }

            // Khối II: Tiền gửi Ngân hàng
            result.Add(new AccountingBookEntryDto
            {
                VoucherNumber = "DK-BANK",
                VoucherDate = fromDateUtc,
                Description = "Số dư đầu kỳ",
                Amount = openingBank,
                BalanceAmount = openingBank,
                SectionGroup = "Khối II - Tiền gửi Ngân hàng",
                Category = "Số dư đầu kỳ",
                BookType = AccountingBookType.S2e_CashAndBank,
                BookCode = "S2e-HKD",
                IsSummaryRow = true
            });

            var bankTransactions = cashEntries
                .Where(c => c.AccountType == FinanceAccountType.Bank)
                .ToList();

            decimal runningBank = openingBank;
            foreach (var c in bankTransactions)
            {
                var isReceipt = c.EntryType == CashbookEntryType.Receipt;
                var inAmt = isReceipt ? c.Amount : 0m;
                var outAmt = isReceipt ? 0m : c.Amount;
                runningBank += (inAmt - outAmt);

                result.Add(new AccountingBookEntryDto
                {
                    VoucherNumber = c.EntryCode,
                    VoucherDate = c.OccurredAtUtc,
                    Description = isReceipt ? $"[Gửi vào NH] {c.Description}" : $"[Rút ra NH] {c.Description}",
                    Amount = c.Amount,
                    InAmount = inAmt,
                    OutAmount = outAmt,
                    BalanceAmount = runningBank,
                    SectionGroup = "Khối II - Tiền gửi Ngân hàng",
                    Category = isReceipt ? "Tiền gửi vào ngân hàng" : "Tiền rút từ ngân hàng",
                    BookType = AccountingBookType.S2e_CashAndBank,
                    BookCode = "S2e-HKD",
                    BeneficiaryName = "Ngân hàng",
                    InvoiceNumber = c.ReferenceCode
                });
            }

            // Fallback chi ngân hàng từ ExpenseVouchers nếu CashbookEntries trống hoàn toàn
            if (cashEntries.Count == 0)
            {
                var bankVouchers = vouchers.Where(v => v.PaymentSourceType == FinanceAccountType.Bank).ToList();
                foreach (var v in bankVouchers)
                {
                    runningBank -= v.Amount;
                    result.Add(new AccountingBookEntryDto
                    {
                        VoucherNumber = v.VoucherCode ?? string.Empty,
                        VoucherDate = v.ExpenseDate,
                        Description = $"[Chi tiền gửi NH] {(!string.IsNullOrEmpty(v.Note) ? v.Note : $"{v.Category} - {v.BeneficiaryName}")}",
                        Amount = v.Amount,
                        InAmount = 0m,
                        OutAmount = v.Amount,
                        BalanceAmount = runningBank,
                        SectionGroup = "Khối II - Tiền gửi Ngân hàng",
                        Category = "Chi tiền gửi ngân hàng",
                        BookType = AccountingBookType.S2e_CashAndBank,
                        BookCode = "S2e-HKD",
                        BeneficiaryName = v.BeneficiaryName,
                        InvoiceNumber = v.InvoiceNumber
                    });
                }
            }
        }

        // S3a-HKD: Sổ theo dõi tình hình thực hiện nghĩa vụ thuế với NSNN
        if ((!bookType.HasValue && (method == TaxCalculationMethod.DeclarationBased || group == HouseholdTaxGroup.Group4_Over50B)) || bookType == AccountingBookType.S3a_TaxObligation)
        {
            // 1. Số dư đầu kỳ thuế phải nộp trước fromDateUtc
            var priorSnapshotQuery = _context.HouseholdTaxSnapshots.AsQueryable();
            if (branchId.HasValue)
            {
                priorSnapshotQuery = priorSnapshotQuery.Where(s => s.BranchId == branchId.Value || s.BranchId == null);
            }
            var priorTaxAccrued = await priorSnapshotQuery
                .Where(s => s.Status == TaxSnapshotStatus.Finalized && s.CreatedAt < fromDateUtc)
                .SumAsync(s => (decimal?)s.TotalTaxAmount, cancellationToken) ?? 0m;

            var priorVoucherQuery = _context.ExpenseVouchers.AsQueryable();
            if (branchId.HasValue)
            {
                priorVoucherQuery = priorVoucherQuery.Where(v => v.BranchId == branchId.Value);
            }
            var priorTaxPaid = await priorVoucherQuery
                .Where(v => v.ExpenseDate < fromDateUtc && (v.Status == ExpenseVoucherStatus.Paid || v.Status == ExpenseVoucherStatus.Approved))
                .Where(v => v.Category.ToLower().Contains("thuế") || v.Category.ToLower().Contains("tax") || v.Category.ToLower().Contains("nsnn") || v.Category.ToLower().Contains("kho bạc"))
                .SumAsync(v => (decimal?)v.Amount, cancellationToken) ?? 0m;

            var openingTaxDue = priorTaxAccrued - priorTaxPaid;

            if (bookType == AccountingBookType.S3a_TaxObligation || openingTaxDue != 0)
            {
                result.Add(new AccountingBookEntryDto
                {
                    VoucherNumber = "DK-TAX",
                    VoucherDate = fromDateUtc,
                    Description = "Số dư nghĩa vụ thuế đầu kỳ",
                    Amount = openingTaxDue,
                    BalanceAmount = openingTaxDue,
                    SectionGroup = "Nghĩa vụ thuế đầu kỳ",
                    Category = "Số dư đầu kỳ",
                    BookType = AccountingBookType.S3a_TaxObligation,
                    BookCode = "S3a-HKD",
                    IsSummaryRow = true
                });
            }

            decimal runningTaxDue = openingTaxDue;

            // 2. Nghĩa vụ thuế phát sinh từ các kỳ đã chốt thuế thuộc phạm vi kỳ kế toán
            var periodFromVn = fromDateUtc.AddHours(7);
            var periodToVn = toDateUtc.AddHours(7);
            var snapshotQuery = _context.HouseholdTaxSnapshots.AsQueryable();
            if (branchId.HasValue)
            {
                snapshotQuery = snapshotQuery.Where(s => s.BranchId == branchId.Value || s.BranchId == null);
            }
            var candidateSnapshots = await snapshotQuery
                .Where(s => s.Status == TaxSnapshotStatus.Finalized && s.Year >= periodFromVn.Year && s.Year <= periodToVn.Year)
                .OrderBy(s => s.Year).ThenBy(s => s.PeriodNumber)
                .ToListAsync(cancellationToken);

            var snapshots = candidateSnapshots.Where(s =>
            {
                var pStart = s.PeriodType switch
                {
                    TaxPeriodType.Month => new DateTime(s.Year, Math.Clamp(s.PeriodNumber, 1, 12), 1),
                    TaxPeriodType.Quarter => new DateTime(s.Year, (Math.Clamp(s.PeriodNumber, 1, 4) - 1) * 3 + 1, 1),
                    _ => new DateTime(s.Year, 1, 1),
                };
                return pStart >= periodFromVn.Date && pStart <= periodToVn.Date;
            }).ToList();

            foreach (var s in snapshots)
            {
                var pCode = s.PeriodType switch
                {
                    TaxPeriodType.Month => $"T{s.PeriodNumber}",
                    TaxPeriodType.Quarter => $"Q{s.PeriodNumber}",
                    _ => "NAM",
                };
                var pText = s.PeriodType switch
                {
                    TaxPeriodType.Month => $"Tháng {s.PeriodNumber}/{s.Year}",
                    TaxPeriodType.Quarter => $"Quý {s.PeriodNumber}/{s.Year}",
                    _ => $"Năm {s.Year}",
                };

                // Thuế GTGT phát sinh
                if (s.VatAmount > 0)
                {
                    runningTaxDue += s.VatAmount;
                    result.Add(new AccountingBookEntryDto
                    {
                        VoucherNumber = $"TAX-VAT-{s.Year}-{pCode}",
                        VoucherDate = s.FinalizedAtUtc != default ? s.FinalizedAtUtc : s.CreatedAt,
                        Description = $"[Nghĩa vụ thuế] Thuế GTGT phát sinh {pText}",
                        Amount = s.VatAmount,
                        InAmount = s.VatAmount,
                        OutAmount = 0m,
                        BalanceAmount = runningTaxDue,
                        TaxAmount = s.VatAmount,
                        SectionGroup = "1. Thuế Giá trị gia tăng (GTGT)",
                        Category = "Thuế GTGT phải nộp",
                        BookType = AccountingBookType.S3a_TaxObligation,
                        BookCode = "S3a-HKD",
                        BeneficiaryName = "Ngân sách Nhà nước"
                    });
                }

                // Thuế TNCN phát sinh
                if (s.PitAmount > 0)
                {
                    runningTaxDue += s.PitAmount;
                    result.Add(new AccountingBookEntryDto
                    {
                        VoucherNumber = $"TAX-PIT-{s.Year}-{pCode}",
                        VoucherDate = s.FinalizedAtUtc != default ? s.FinalizedAtUtc : s.CreatedAt,
                        Description = $"[Nghĩa vụ thuế] Thuế TNCN phát sinh {pText}",
                        Amount = s.PitAmount,
                        InAmount = s.PitAmount,
                        OutAmount = 0m,
                        BalanceAmount = runningTaxDue,
                        TaxAmount = s.PitAmount,
                        SectionGroup = "2. Thuế Thu nhập cá nhân (TNCN)",
                        Category = "Thuế TNCN phải nộp",
                        BookType = AccountingBookType.S3a_TaxObligation,
                        BookCode = "S3a-HKD",
                        BeneficiaryName = "Ngân sách Nhà nước"
                    });
                }

                // Fallback nếu cả 2 = 0 nhưng TotalTaxAmount > 0
                if (s.VatAmount == 0 && s.PitAmount == 0 && s.TotalTaxAmount > 0)
                {
                    runningTaxDue += s.TotalTaxAmount;
                    result.Add(new AccountingBookEntryDto
                    {
                        VoucherNumber = $"TAX-SNP-{s.Year}-Q{s.PeriodNumber}",
                        VoucherDate = s.CreatedAt,
                        Description = $"[Nghĩa vụ thuế] Chốt thuế Q{s.PeriodNumber}/{s.Year}",
                        Amount = s.TotalTaxAmount,
                        InAmount = s.TotalTaxAmount,
                        OutAmount = 0m,
                        BalanceAmount = runningTaxDue,
                        TaxAmount = s.TotalTaxAmount,
                        SectionGroup = "1. Thuế Giá trị gia tăng (GTGT)",
                        Category = "Nghĩa vụ thuế phát sinh",
                        BookType = AccountingBookType.S3a_TaxObligation,
                        BookCode = "S3a-HKD",
                        BeneficiaryName = "Ngân sách Nhà nước"
                    });
                }
            }

            // 3. Chứng từ chi nộp thuế vào NSNN
            var taxVouchers = vouchers.Where(v =>
            {
                var cat = (v.Category ?? string.Empty).ToUpperInvariant();
                var note = (v.Note ?? string.Empty).ToUpperInvariant();
                return cat.Contains("THUẾ") || cat.Contains("TAX") || cat.Contains("NSNN") || cat.Contains("KHO BẠC") ||
                       cat.Contains("MÔN BÀI") || cat.Contains("LỆ PHÍ") ||
                       note.Contains("THUẾ") || note.Contains("TAX") || note.Contains("NSNN") || note.Contains("KHO BẠC") ||
                       note.Contains("MÔN BÀI") || note.Contains("LỆ PHÍ");
            }).ToList();

            foreach (var v in taxVouchers)
            {
                runningTaxDue -= v.Amount;
                var text = $"{v.Category} {v.Note}".ToLowerInvariant();
                string sectionGroup = text.Contains("tncn") || text.Contains("pit") || text.Contains("thu nhập cá nhân")
                    ? "2. Thuế Thu nhập cá nhân (TNCN)"
                    : (text.Contains("gtgt") || text.Contains("vat")
                        ? "1. Thuế Giá trị gia tăng (GTGT)"
                        : "3. Các loại thuế, phí, lệ phí khác");

                result.Add(new AccountingBookEntryDto
                {
                    VoucherNumber = v.VoucherCode ?? string.Empty,
                    VoucherDate = v.ExpenseDate,
                    Description = !string.IsNullOrEmpty(v.Note) ? v.Note : $"[Nộp thuế] {v.Category} - {v.BeneficiaryName}",
                    Amount = v.Amount,
                    InAmount = 0m,
                    OutAmount = v.Amount,
                    BalanceAmount = runningTaxDue,
                    SectionGroup = sectionGroup,
                    Category = "Đã nộp thuế NSNN",
                    BookType = AccountingBookType.S3a_TaxObligation,
                    BookCode = "S3a-HKD",
                    BeneficiaryName = v.BeneficiaryName,
                    InvoiceNumber = v.InvoiceNumber
                });
            }
        }

        return result;
    }

    /// <summary>
    /// Phân loại chi phí hợp lý vào 6 nhóm theo chuẩn S2c-HKD Thông tư 152/2025/TT-BTC.
    /// </summary>
    private static string ClassifyS2cExpenseGroup(string? category, string? note)
    {
        var text = $"{category} {note}".ToLowerInvariant();

        // 2.a) Chi phí nguyên liệu, vật liệu, nhiên liệu, hàng hóa
        if (text.Contains("nguyên liệu") || text.Contains("vật liệu") || text.Contains("nhiên liệu") ||
            text.Contains("hàng hóa") || text.Contains("bao bì") || text.Contains("vật tư") ||
            text.Contains("packaging") || text.Contains("material") || text.Contains("xăng") ||
            text.Contains("dầu") || text.Contains("cogs") || text.Contains("giá vốn"))
        {
            return "2.a) Chi phí nguyên liệu, vật liệu, nhiên liệu, hàng hóa";
        }

        // 2.b) Chi phí tiền lương, tiền công, thù lao
        if (text.Contains("lương") || text.Contains("salary") || text.Contains("wage") ||
            text.Contains("thù lao") || text.Contains("tiền công") || text.Contains("thưởng") ||
            text.Contains("bonus") || text.Contains("phụ cấp") || text.Contains("nhân viên") ||
            text.Contains("nhân sự"))
        {
            return "2.b) Chi phí tiền lương, tiền công, thù lao";
        }

        // 2.c) Chi phí thuê mặt bằng, kho bãi, máy móc
        if (text.Contains("mặt bằng") || text.Contains("kho bãi") || text.Contains("thuê kho") ||
            text.Contains("thuê nhà") || text.Contains("tiền thuê") || text.Contains("rent") ||
            text.Contains("premise") || text.Contains("máy móc") || text.Contains("thiết bị thuê"))
        {
            return "2.c) Chi phí thuê mặt bằng, kho bãi, máy móc";
        }

        // 2.d) Chi phí dịch vụ mua ngoài
        if (text.Contains("điện") || text.Contains("nước") || text.Contains("internet") ||
            text.Contains("viễn thông") || text.Contains("dịch vụ") || text.Contains("mua ngoài") ||
            text.Contains("vệ sinh") || text.Contains("rác") || text.Contains("sửa chữa") ||
            text.Contains("bảo trì") || text.Contains("vận chuyển") || text.Contains("giao hàng") ||
            text.Contains("ship") || text.Contains("quảng cáo") || text.Contains("marketing") ||
            text.Contains("utility"))
        {
            return "2.d) Chi phí dịch vụ mua ngoài";
        }

        // 2.e) Thuế, phí, lệ phí được trừ
        if (text.Contains("thuế") || text.Contains("phí") || text.Contains("lệ phí") ||
            text.Contains("tax") || text.Contains("fee") || text.Contains("môn bài") ||
            text.Contains("công chứng") || text.Contains("hải quan"))
        {
            return "2.e) Thuế, phí, lệ phí được trừ";
        }

        // 2.đ) Chi phí khác liên quan kinh doanh (mặc định)
        return "2.đ) Chi phí khác liên quan kinh doanh";
    }

    public async Task<HouseholdTaxSnapshot?> GetSnapshotAsync(
        int year,
        TaxPeriodType periodType,
        int periodNumber,
        Guid? branchId,
        CancellationToken cancellationToken = default)
    {
        var query = _context.HouseholdTaxSnapshots
            .Where(s =>
                s.Year == year &&
                s.PeriodType == periodType &&
                s.PeriodNumber == periodNumber &&
                s.Status != TaxSnapshotStatus.Superseded);

        if (branchId.HasValue)
        {
            query = query.Where(s => s.BranchId == branchId.Value || s.BranchId == null)
                         .OrderByDescending(s => s.BranchId.HasValue)
                         .ThenByDescending(s => s.RevisionNumber);
        }
        else
        {
            // Tenant-wide: Ưu tiên bản ghi toàn hộ (BranchId == null), fallback về bản ghi theo chi nhánh nếu có
            query = query.OrderBy(s => s.BranchId.HasValue)
                         .ThenByDescending(s => s.RevisionNumber);
        }

        return await query.FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<HouseholdTaxSnapshot?> GetSnapshotByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.HouseholdTaxSnapshots
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
    }

    public Task<HouseholdTaxSnapshot?> GetSnapshotForUpdateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (!_context.Database.IsNpgsql()) return GetSnapshotByIdAsync(id, cancellationToken);

        return _context.HouseholdTaxSnapshots
            .FromSqlInterpolated($"SELECT * FROM \"HouseholdTaxSnapshots\" WHERE \"Id\" = {id} FOR UPDATE")
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);
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
            query = query.Where(s => s.BranchId == branchId.Value || s.BranchId == null);
        }

        return await query
            .OrderByDescending(s => s.Year)
            .ThenByDescending(s => s.PeriodNumber)
            .ThenByDescending(s => s.RevisionNumber)
            .ToListAsync(cancellationToken);
    }

    public async Task<HouseholdTaxSnapshot> AddSnapshotAsync(HouseholdTaxSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        try
        {
            _context.HouseholdTaxSnapshots.Add(snapshot);
            await _context.SaveChangesAsync(cancellationToken);
            return snapshot;
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" } ||
                                           ex.GetBaseException() is Npgsql.PostgresException { SqlState: "23505" })
        {
            // Bắt ngoại lệ vi phạm Unique Index khi có race condition (chốt kỳ song song)
            throw new MT_GRMS.Domain.Exceptions.BusinessException("TAX_PERIOD_LOCKED", $"Kỳ tính thuế {snapshot.PeriodType} {snapshot.PeriodNumber}/{snapshot.Year} đã được chốt và khóa sổ.");
        }
    }

    public async Task UpdateSnapshotAsync(HouseholdTaxSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        var entry = _context.Entry(snapshot);
        if (entry.State == EntityState.Detached)
        {
            _context.HouseholdTaxSnapshots.Attach(snapshot);
        }
        entry.Property(s => s.Status).IsModified = true;
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<(decimal ReducedVatRevenue, decimal StandardVatRevenue)> GetPeriodRevenueSplitAsync(
        DateTime fromDateUtc,
        DateTime toDateUtc,
        Guid? branchId,
        CancellationToken cancellationToken = default)
    {
        var itemQuery = _context.OrderDetails
            .Where(d =>
                d.Order != null &&
                d.Order.CreatedAt >= fromDateUtc &&
                d.Order.CreatedAt <= toDateUtc &&
                (d.Order.PaymentStatus == "PAID" || d.Order.PaymentStatus == "Paid" || d.Order.PaymentStatus == "DEBT" || d.Order.PaymentStatus == "Debt") &&
                d.Order.Status != "Cancelled");

        if (branchId.HasValue)
        {
            itemQuery = itemQuery.Where(d => d.Order!.BranchId == branchId.Value);
        }

        // Ưu tiên tỷ lệ thuế đã áp dụng tại thời điểm bán (d.AppliedVatRate > 0)
        // Fallback về cờ sản phẩm chỉ áp dụng khi AppliedVatRate == 0 (dữ liệu cũ trước migration)
        var grossReduced = await itemQuery
            .Where(d => d.AppliedVatRate == 0.008m || (d.AppliedVatRate == 0m && d.Product != null && d.Product.EligibleForVatReduction))
            .SumAsync(d => (decimal?)(d.Qty * d.UnitPrice), cancellationToken) ?? 0m;

        var grossStandard = await itemQuery
            .Where(d => (d.AppliedVatRate > 0m && d.AppliedVatRate != 0.008m) || (d.AppliedVatRate == 0m && (d.Product == null || !d.Product.EligibleForVatReduction)))
            .SumAsync(d => (decimal?)(d.Qty * d.UnitPrice), cancellationToken) ?? 0m;

        var grossTotal = grossReduced + grossStandard;

        // Phân bổ chiết khấu + trả hàng theo tỷ lệ gross
        // Giả định: Chiết khấu và hàng trả lại phân bổ đều theo tỷ lệ gross
        var (gross, discount, returns) = await GetPeriodRevenueAsync(fromDateUtc, toDateUtc, branchId, cancellationToken);
        var netTotal = gross - discount - returns;

        if (grossTotal <= 0)
        {
            // Nếu kỳ này không phát sinh bán hàng (chỉ có trả hàng hoặc gross = 0)
            // Phân bổ phần âm trả hàng theo tỷ lệ doanh thu bán hàng cả năm YTD
            var vnTz = HouseholdTaxService.GetVietnamTimeZone();
            var startOfYearVn = new DateTime(fromDateUtc.Year, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);
            var startOfYearUtc = TimeZoneInfo.ConvertTimeToUtc(startOfYearVn, vnTz);

            var ytdReduced = await _context.OrderDetails
                .Where(d => d.Order != null && d.Order.CreatedAt >= startOfYearUtc && d.Order.CreatedAt < toDateUtc && (d.Order.PaymentStatus == "PAID" || d.Order.PaymentStatus == "Paid" || d.Order.PaymentStatus == "DEBT" || d.Order.PaymentStatus == "Debt") && d.Order.Status != "Cancelled")
                .Where(d => d.AppliedVatRate == 0.008m || (d.AppliedVatRate == 0m && d.Product != null && d.Product.EligibleForVatReduction))
                .SumAsync(d => (decimal?)(d.Qty * d.UnitPrice), cancellationToken) ?? 0m;

            var ytdStandard = await _context.OrderDetails
                .Where(d => d.Order != null && d.Order.CreatedAt >= startOfYearUtc && d.Order.CreatedAt < toDateUtc && (d.Order.PaymentStatus == "PAID" || d.Order.PaymentStatus == "Paid" || d.Order.PaymentStatus == "DEBT" || d.Order.PaymentStatus == "Debt") && d.Order.Status != "Cancelled")
                .Where(d => (d.AppliedVatRate > 0m && d.AppliedVatRate != 0.008m) || (d.AppliedVatRate == 0m && (d.Product == null || !d.Product.EligibleForVatReduction)))
                .SumAsync(d => (decimal?)(d.Qty * d.UnitPrice), cancellationToken) ?? 0m;

            var ytdGrossTotal = ytdReduced + ytdStandard;
            if (ytdGrossTotal > 0)
            {
                var ytdRatioReduced = ytdReduced / ytdGrossTotal;
                var splitReduced = Math.Round(netTotal * ytdRatioReduced, 0, MidpointRounding.AwayFromZero);
                return (splitReduced, netTotal - splitReduced);
            }

            // Fallback thận trọng khi cả YTD Gross cũng = 0 (ví dụ đầu năm chỉ có khách trả hàng của năm ngoái):
            // Gán toàn bộ số âm vào nhóm 0.8% (netTotal, 0m) theo nguyên tắc thận trọng (giảm trừ thuế ít hơn, không ước tính thiếu thuế)
            return (netTotal, 0m);
        }

        var ratioReduced = grossReduced / grossTotal;
        var reduced = Math.Round(netTotal * ratioReduced, 0, MidpointRounding.AwayFromZero);
        var standard = netTotal - reduced;

        return (reduced, standard);
    }

}
