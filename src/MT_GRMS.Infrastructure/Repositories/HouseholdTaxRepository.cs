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
        var vnTz = HouseholdTaxService.GetVietnamTimeZone();
        var startOfYearVn = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);
        var startOfYearUtc = TimeZoneInfo.ConvertTimeToUtc(startOfYearVn, vnTz);

        var orderGross = await _context.Orders
            .Where(o => o.CreatedAt >= startOfYearUtc && o.CreatedAt < upToDateUtc && (o.PaymentStatus == "PAID" || o.PaymentStatus == "Paid" || o.PaymentStatus == "DEBT" || o.PaymentStatus == "Debt") && o.Status != "Cancelled")
            .SumAsync(o => (decimal?)o.TotalAmount, cancellationToken) ?? 0m;

        var orderDiscounts = await _context.Orders
            .Where(o => o.CreatedAt >= startOfYearUtc && o.CreatedAt < upToDateUtc && (o.PaymentStatus == "PAID" || o.PaymentStatus == "Paid" || o.PaymentStatus == "DEBT" || o.PaymentStatus == "Debt") && o.Status != "Cancelled")
            .SumAsync(o => (decimal?)o.DiscountAmount, cancellationToken) ?? 0m;

        var returns = await _context.CustomerDebtEntries
            .Where(e => e.EntryType == CustomerDebtType.GoodsReturnOffset && e.OccurredAtUtc >= startOfYearUtc && e.OccurredAtUtc < upToDateUtc)
            .SumAsync(e => (decimal?)e.Amount, cancellationToken) ?? 0m;

        var netRevenue = orderGross - orderDiscounts - returns;
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

        var returnQuery = _context.CustomerDebtEntries.AsQueryable();
        if (branchId.HasValue)
        {
            returnQuery = returnQuery.Where(e => e.BranchId == branchId.Value);
        }

        var returns = await returnQuery
            .Where(e => e.EntryType == CustomerDebtType.GoodsReturnOffset && e.OccurredAtUtc >= fromDateUtc && e.OccurredAtUtc < toDateUtc)
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

        // Lọc chi phí vận hành hợp lệ và loại trừ rút vốn chủ sở hữu / tiền cọc / nộp thuế
        return await voucherQuery
            .Where(v => v.Nature == FinancialNature.OperatingExpense &&
                        !v.Category.ToLower().Contains("capital") &&
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

    public async Task<List<ExpenseVoucher>> GetDeductibleExpenseVouchersAsync(
        DateTime fromDateUtc,
        DateTime toDateUtc,
        Guid? branchId,
        CancellationToken cancellationToken = default)
    {
        var query = _context.ExpenseVouchers.AsNoTracking().Where(v =>
            v.ExpenseDate >= fromDateUtc &&
            v.ExpenseDate <= toDateUtc &&
            v.IsTaxDeductible &&
            (v.Status == ExpenseVoucherStatus.Paid || v.Status == ExpenseVoucherStatus.Approved));

        if (branchId.HasValue)
        {
            query = query.Where(v => v.BranchId == branchId.Value);
        }

        return await query.OrderBy(v => v.ExpenseDate).ToListAsync(cancellationToken);
    }

    public async Task<(decimal TotalCogs, bool IsEstimated)> GetPeriodCogsAsync(
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

        // Lấy danh sách Product IDs từ các orders
        var productIds = orders.SelectMany(o => o.Items).Select(i => i.ProductId).Distinct().ToList();
        var productCostDict = await _context.Products
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.CostPrice ?? 0m, cancellationToken);

        decimal actualCogs = 0m;
        bool hasAnyRealCost = false;

        foreach (var order in orders)
        {
            foreach (var item in order.Items)
            {
                decimal itemCost = 0m;
                if (item.CostPriceAtSale > 0)
                {
                    itemCost = item.CostPriceAtSale;
                    hasAnyRealCost = true;
                }
                else if (productCostDict.TryGetValue(item.ProductId, out var costPrice) && costPrice > 0)
                {
                    itemCost = costPrice;
                    hasAnyRealCost = true;
                }

                if (hasAnyRealCost)
                {
                    actualCogs += item.Quantity * itemCost;
                }
                else
                {
                    actualCogs += item.Quantity * item.UnitPrice * 0.70m;
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
            .Where(e => e.EntryType == CustomerDebtType.GoodsReturnOffset && e.OccurredAtUtc >= fromDateUtc && e.OccurredAtUtc < toDateUtc)
            .SumAsync(e => (decimal?)e.Amount, cancellationToken) ?? 0m;

        if (!hasAnyRealCost)
        {
            // Fallback: Ước tính 70% doanh thu thuần nếu sản phẩm chưa liên kết giá vốn
            var gross = orders.Sum(o => o.TotalAmount);
            var discounts = orders.Sum(o => o.DiscountAmount);
            var netRevenue = Math.Max(0, gross - discounts - salesReturns);
            var estimatedCogs = Math.Round(netRevenue * 0.70m, 0, MidpointRounding.AwayFromZero);
            return (estimatedCogs, true);
        }

        // Trừ giá vốn hàng trả lại theo tỷ lệ giá vốn thực tế trên doanh thu
        var totalGrossRevenue = orders.Sum(o => o.TotalAmount);
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

            // Kết chuyển COGS trong kỳ
            var cogs = await GetPeriodCogsAsync(fromDateUtc, toDateUtc, branchId, cancellationToken);
            if (cogs.TotalCogs > 0)
            {
                result.Add(new AccountingBookEntryDto
                {
                    VoucherNumber = $"COGS-{fromDateUtc:yyyyMM}",
                    VoucherDate = toDateUtc.AddSeconds(-1),
                    Description = $"Giá vốn hàng bán thực tế trong kỳ {(cogs.IsEstimated ? "(ước tính 70%)" : "(theo giá vốn hàng xuất kho)")}",
                    Amount = cogs.TotalCogs,
                    SectionGroup = "2.a) Chi phí nguyên liệu, vật liệu, nhiên liệu, hàng hóa",
                    Category = "Giá vốn hàng bán (COGS)",
                    BookType = AccountingBookType.S2c_RevenueAndExpense,
                    BookCode = "S2c-HKD"
                });
            }

            // Chi phí SXKD hợp lý từ ExpenseVouchers (chỉ lấy OperatingExpense, loại trừ cọc vỏ / rút vốn / tạm ứng)
            result.AddRange(vouchers
                .Where(v => v.Nature == FinancialNature.OperatingExpense)
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
            var receiptQuery = _context.GoodsReceipts.Include(g => g.Supplier).Include(g => g.Lines).AsQueryable();
            if (branchId.HasValue)
            {
                receiptQuery = receiptQuery.Where(g => g.BranchId == branchId.Value);
            }
            receiptQuery = receiptQuery.Where(g => g.ReceivedAtUtc >= fromDateUtc && g.ReceivedAtUtc <= toDateUtc && (g.Status == "CONFIRMED" || g.Status == "COMPLETED" || g.Status == "DRAFT"));
            var receipts = await receiptQuery.OrderBy(g => g.ReceivedAtUtc).ToListAsync(cancellationToken);

            result.AddRange(receipts.Select(g =>
            {
                var inQuantity = g.Lines.Sum(l => l.AcceptedQty > 0 ? l.AcceptedQty : (l.DeliveredQty > 0 ? l.DeliveredQty : l.OrderedQty));
                var inAmount = g.GrandTotal;
                var unitPrice = inQuantity > 0 ? inAmount / inQuantity : 0m;

                return new AccountingBookEntryDto
                {
                    VoucherNumber = g.Code,
                    VoucherDate = g.ReceivedAtUtc,
                    Description = $"[Nhập kho] Nhập hàng từ NCC {(g.Supplier != null ? g.Supplier.Name : "Nhà cung cấp")}",
                    Amount = inAmount,
                    InQuantity = inQuantity,
                    InAmount = inAmount,
                    UnitPrice = Math.Round(unitPrice, 2, MidpointRounding.AwayFromZero),
                    Unit = "Lô/Hàng",
                    SectionGroup = "Nhập kho trong kỳ",
                    Category = "Nhập kho hàng hóa",
                    BookType = AccountingBookType.S2d_MaterialsAndGoods,
                    BookCode = "S2d-HKD",
                    BeneficiaryName = g.Supplier?.Name,
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
                priorSnapshotQuery = priorSnapshotQuery.Where(s => s.BranchId == branchId.Value);
            }
            var priorTaxAccrued = await priorSnapshotQuery
                .Where(s => s.CreatedAt < fromDateUtc)
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

            // 2. Nghĩa vụ thuế phát sinh từ các kỳ đã chốt thuế trong kỳ (Snapshots)
            var snapshotQuery = _context.HouseholdTaxSnapshots.AsQueryable();
            if (branchId.HasValue)
            {
                snapshotQuery = snapshotQuery.Where(s => s.BranchId == branchId.Value);
            }
            snapshotQuery = snapshotQuery.Where(s => s.CreatedAt >= fromDateUtc && s.CreatedAt <= toDateUtc);
            var snapshots = await snapshotQuery.OrderBy(s => s.CreatedAt).ToListAsync(cancellationToken);

            foreach (var s in snapshots)
            {
                // Thuế GTGT phát sinh
                if (s.VatAmount > 0)
                {
                    runningTaxDue += s.VatAmount;
                    result.Add(new AccountingBookEntryDto
                    {
                        VoucherNumber = $"TAX-VAT-{s.Year}-Q{s.PeriodNumber}",
                        VoucherDate = s.CreatedAt,
                        Description = $"[Nghĩa vụ thuế] Thuế GTGT phát sinh Q{s.PeriodNumber}/{s.Year}",
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
                        VoucherNumber = $"TAX-PIT-{s.Year}-Q{s.PeriodNumber}",
                        VoucherDate = s.CreatedAt,
                        Description = $"[Nghĩa vụ thuế] Thuế TNCN phát sinh Q{s.PeriodNumber}/{s.Year}",
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
        try
        {
            _context.HouseholdTaxSnapshots.Add(snapshot);
            await _context.SaveChangesAsync(cancellationToken);
            return snapshot;
        }
        catch (DbUpdateException)
        {
            // Bắt ngoại lệ vi phạm Unique Index khi có race condition (chốt kỳ song song)
            throw new MT_GRMS.Domain.Exceptions.BusinessException("TAX_PERIOD_LOCKED", $"Kỳ tính thuế {snapshot.PeriodType} {snapshot.PeriodNumber}/{snapshot.Year} đã được chốt và khóa sổ.");
        }
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

    public async Task<HouseholdTaxYearSetting?> GetYearSettingAsync(int year, CancellationToken cancellationToken = default)
    {
        return await _context.HouseholdTaxYearSettings
            .Where(s => s.Year == year || (s.Year <= year && s.LockedUntilYear >= year))
            .OrderByDescending(s => s.Year)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task UpsertYearSettingAsync(HouseholdTaxYearSetting setting, CancellationToken cancellationToken = default)
    {
        var existing = await _context.HouseholdTaxYearSettings
            .FirstOrDefaultAsync(s => s.Year == setting.Year, cancellationToken);

        if (existing == null)
        {
            _context.HouseholdTaxYearSettings.Add(setting);
        }
        else
        {
            existing.PitMethod = setting.PitMethod;
            existing.LockedUntilYear = setting.LockedUntilYear;
            existing.LockReason = setting.LockReason;
            existing.LockedBySystem = setting.LockedBySystem;
            existing.LockedAtUtc = setting.LockedAtUtc;
        }

        await _context.SaveChangesAsync(cancellationToken);
    }
}
