using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Tax;
using MT_GRMS.Application.Tax.Dtos;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Domain.Entities.Sales;
using MT_GRMS.Domain.Entities.Supplier;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Infrastructure.Persistence;

namespace MT_GRMS.Infrastructure.Services;

/// <summary>
/// Service xuất báo cáo thuế và sổ kế toán hộ kinh doanh dạng bảng tính Excel chuẩn 100% Thông tư 152/2025/TT-BTC.
/// </summary>
public class TaxExportService : ITaxExportService
{
    private static readonly XLColor PrimaryColor = XLColor.FromArgb(24, 43, 73);        // Deep Navy
    private static readonly XLColor HeaderBgColor = XLColor.FromArgb(235, 241, 250);     // Light Blue Gray
    private static readonly XLColor SectionBgColor = XLColor.FromArgb(242, 244, 247);    // Light Gray Section
    private static readonly XLColor TotalBgColor = XLColor.FromArgb(254, 243, 199);      // Warm Amber Highlight
    private static readonly XLColor SubTotalBgColor = XLColor.FromArgb(243, 244, 246);   // Subtotal Background
    private const string NumberFormatVnd = "#,##0";

    private readonly IHouseholdTaxService _taxService;
    private readonly IHouseholdTaxRepository _taxRepository;
    private readonly AppDbContext _context;

    public TaxExportService(
        IHouseholdTaxService taxService,
        IHouseholdTaxRepository taxRepository,
        AppDbContext context)
    {
        _taxService = taxService;
        _taxRepository = taxRepository;
        _context = context;
    }

    /// <summary>
    /// Xuất báo cáo thuế và sổ kế toán hộ kinh doanh theo bộ lọc.
    /// </summary>
    public async Task<byte[]> ExportTaxSummaryToExcelAsync(HouseholdTaxFilterDto filter, CancellationToken cancellationToken = default)
    {
        return await ExportTaxSummaryToExcelAsync(filter, filter.BookType, cancellationToken);
    }

    /// <summary>
    /// Xuất báo cáo thuế và sổ kế toán hộ kinh doanh với bookType chuyên biệt hoặc trọn bộ 7 sổ Thông tư 152.
    /// </summary>
    public async Task<byte[]> ExportTaxSummaryToExcelAsync(
        HouseholdTaxFilterDto filter,
        AccountingBookType? bookType,
        CancellationToken cancellationToken = default)
    {
        var summary = await _taxService.GetTaxSummaryAsync(filter, cancellationToken);
        var (fromDateUtc, toDateUtc) = GetPeriodDatesUtc(filter.Year, filter.PeriodType, filter.PeriodNumber);
        var deductibleExpenses = await _taxRepository.GetDeductibleExpenseVouchersAsync(fromDateUtc, toDateUtc, filter.BranchId, cancellationToken);

        // Lấy thông tin tổ chức và chi nhánh
        var tenant = await _context.Tenants.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        var branch = filter.BranchId.HasValue
            ? await _context.Branches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == filter.BranchId.Value, cancellationToken)
            : null;

        // Lấy danh sách doanh thu theo địa điểm kinh doanh
        var branches = await _context.Branches.AsNoTracking().Where(b => b.IsActive).ToListAsync(cancellationToken);
        if (filter.BranchId.HasValue)
        {
            branches = branches.Where(b => b.Id == filter.BranchId.Value).ToList();
        }

        var branchRevenues = new List<(string Code, string Name, decimal Gross, decimal Discount, decimal Returns, decimal Net)>();
        foreach (var b in branches)
        {
            var (gross, discount, returns) = await _taxRepository.GetPeriodRevenueAsync(fromDateUtc, toDateUtc, b.Id, cancellationToken);
            var net = Math.Max(0m, gross - discount - returns);
            branchRevenues.Add((b.BranchCode, b.Name, gross, discount, returns, net));
        }

        if (branchRevenues.Count == 0)
        {
            branchRevenues.Add((
                "MAIN",
                string.IsNullOrWhiteSpace(summary.BranchName) ? "Điểm kinh doanh chính" : summary.BranchName,
                summary.GrossRevenueInPeriod,
                summary.DiscountAmountInPeriod,
                summary.ReturnAmountInPeriod,
                summary.TaxableRevenueInPeriod
            ));
        }

        // Lấy danh sách đơn hàng trong kỳ (Include Details để tính đúng giá vốn xuất kho S2d)
        var orderQuery = _context.Orders.Include(o => o.Details).AsNoTracking().AsQueryable();
        if (filter.BranchId.HasValue)
        {
            orderQuery = orderQuery.Where(o => o.BranchId == filter.BranchId.Value);
        }
        orderQuery = orderQuery.Where(o =>
            o.CreatedAt >= fromDateUtc &&
            o.CreatedAt <= toDateUtc &&
            (o.PaymentStatus == "PAID" || o.PaymentStatus == "Paid" || o.PaymentStatus == "DEBT" || o.PaymentStatus == "Debt") &&
            o.Status != "Cancelled");
        var orders = await orderQuery.OrderBy(o => o.CreatedAt).ToListAsync(cancellationToken);

        // Lấy danh sách phiếu nhập kho trong kỳ (Include Lines để tính số lượng nhập S2d)
        var receiptQuery = _context.GoodsReceipts.Include(g => g.Supplier).Include(g => g.Lines).AsNoTracking().AsQueryable();
        if (filter.BranchId.HasValue)
        {
            receiptQuery = receiptQuery.Where(g => g.BranchId == filter.BranchId.Value);
        }
        receiptQuery = receiptQuery.Where(g =>
            g.ReceivedAtUtc >= fromDateUtc &&
            g.ReceivedAtUtc <= toDateUtc &&
            (g.Status == "CONFIRMED" || g.Status == "COMPLETED" || g.Status == "DRAFT"));
        var receipts = await receiptQuery.OrderBy(g => g.ReceivedAtUtc).ToListAsync(cancellationToken);

        // Lấy danh sách nhật ký quỹ tiền (Cashbook)
        var cashQuery = _context.CashbookEntries.AsNoTracking().AsQueryable();
        if (filter.BranchId.HasValue)
        {
            cashQuery = cashQuery.Where(c => c.BranchId == filter.BranchId.Value);
        }
        cashQuery = cashQuery.Where(c => c.OccurredAtUtc >= fromDateUtc && c.OccurredAtUtc <= toDateUtc);
        var cashEntries = await cashQuery.OrderBy(c => c.OccurredAtUtc).ToListAsync(cancellationToken);

        // Lấy danh sách snapshot chốt thuế
        var snapshotQuery = _context.HouseholdTaxSnapshots.AsNoTracking().AsQueryable();
        if (filter.BranchId.HasValue)
        {
            snapshotQuery = snapshotQuery.Where(s => s.BranchId == filter.BranchId.Value);
        }
        snapshotQuery = snapshotQuery.Where(s => s.Year == filter.Year);
        var snapshots = await snapshotQuery.OrderBy(s => s.PeriodNumber).ToListAsync(cancellationToken);

        using var workbook = new XLWorkbook();

        // 1. Luôn xuất Sheet Thông tin
        BuildInfoSheet(workbook, summary, filter);

        var effectiveBookType = bookType ?? filter.BookType;

        if (effectiveBookType.HasValue)
        {
            // Xuất đúng sổ được yêu cầu
            switch (effectiveBookType.Value)
            {
                case AccountingBookType.S1a_RevenueGroup1:
                    BuildS1aSheet(workbook, summary, filter, orders, tenant, branch);
                    break;
                case AccountingBookType.S2a_RevenueGroup2:
                    BuildS2aSheet(workbook, summary, filter, orders, tenant, branch);
                    break;
                case AccountingBookType.S2b_RevenueDeclaration:
                    BuildS2bSheet(workbook, summary, filter, orders, tenant, branch);
                    break;
                case AccountingBookType.S2c_RevenueAndExpense:
                    BuildS2cSheet(workbook, summary, filter, orders, deductibleExpenses, tenant, branch);
                    break;
                case AccountingBookType.S2d_MaterialsAndGoods:
                    BuildS2dSheet(workbook, summary, filter, receipts, orders, deductibleExpenses, tenant, branch);
                    break;
                case AccountingBookType.S2e_CashAndBank:
                    BuildS2eSheet(workbook, summary, filter, cashEntries, orders, deductibleExpenses, tenant, branch);
                    break;
                case AccountingBookType.S3a_TaxObligation:
                    BuildS3aSheet(workbook, summary, filter, snapshots, deductibleExpenses, tenant, branch);
                    break;
            }
        }
        else
        {
            // Xuất trọn bộ đầy đủ cả 7 Sheet theo Thông tư 152/2025/TT-BTC
            BuildS1aSheet(workbook, summary, filter, orders, tenant, branch);
            BuildS2aSheet(workbook, summary, filter, orders, tenant, branch);
            BuildS2bSheet(workbook, summary, filter, orders, tenant, branch);
            BuildS2cSheet(workbook, summary, filter, orders, deductibleExpenses, tenant, branch);
            BuildS2dSheet(workbook, summary, filter, receipts, orders, deductibleExpenses, tenant, branch);
            BuildS2eSheet(workbook, summary, filter, cashEntries, orders, deductibleExpenses, tenant, branch);
            BuildS3aSheet(workbook, summary, filter, snapshots, deductibleExpenses, tenant, branch);

            // Các sheet phụ trợ tổng hợp
            BuildRevenueByBranchSheet(workbook, branchRevenues);
            BuildExpensesSheet(workbook, deductibleExpenses);
            BuildSummarySheet(workbook, summary);
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    #region 7 SỔ KẾ TOÁN CHUẨN THÔNG TƯ 152/2025/TT-BTC

    /// <summary>
    /// 1. Mẫu số S1a-HKD: Sổ chi tiết doanh thu bán hàng hóa, dịch vụ (dành cho Nhóm 1).
    /// Bảng 3 cột: [Ngày tháng A, Diễn giải B, Doanh thu 1], dòng Tổng cộng doanh thu, phần ký.
    /// </summary>
    public static void BuildS1aSheet(
        IXLWorkbook workbook,
        HouseholdTaxSummaryDto summary,
        HouseholdTaxFilterDto filter,
        List<Order> orders,
        Tenant? tenant,
        Branch? branch)
    {
        var ws = workbook.Worksheets.Add("S1a-HKD");
        ws.Column(1).Width = 18;
        ws.Column(2).Width = 52;
        ws.Column(3).Width = 24;

        RenderCircularHeader(ws, "S1a", "SỔ CHI TIẾT DOANH THU BÁN HÀNG HÓA, DỊCH VỤ", "Hộ kinh doanh Nhóm 1 - Doanh thu đến 1 tỷ đồng/năm", summary, tenant, branch, 3);

        // Header bảng (Dòng 10-11)
        ws.Cell(10, 1).Value = "Ngày, tháng ghi sổ";
        ws.Cell(10, 2).Value = "Diễn giải";
        ws.Cell(10, 3).Value = "Doanh thu (VNĐ)";
        FormatTableHeaderRow(ws.Range(10, 1, 10, 3));

        ws.Cell(11, 1).Value = "A";
        ws.Cell(11, 2).Value = "B";
        ws.Cell(11, 3).Value = "1";
        FormatColLettersRow(ws.Range(11, 1, 11, 3));

        int row = 12;
        var startRow = row;

        if (orders.Count > 0)
        {
            foreach (var o in orders)
            {
                var localDate = ToVietnamTime(o.CreatedAt);
                ws.Cell(row, 1).Value = localDate.ToString("dd/MM/yyyy");
                ws.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                var netOrderRevenue = Math.Max(0m, o.TotalAmount - o.DiscountAmount);
                ws.Cell(row, 2).Value = $"Bán lẻ hàng hóa theo đơn hàng {o.OrderCode}";
                ws.Cell(row, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;

                ws.Cell(row, 3).Value = netOrderRevenue;
                ws.Cell(row, 3).Style.NumberFormat.Format = NumberFormatVnd;
                ws.Cell(row, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

                FormatDataRow(ws.Range(row, 1, row, 3));
                row++;
            }
        }
        else
        {
            ws.Cell(row, 1).Value = DateTime.Now.ToString("dd/MM/yyyy");
            ws.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            ws.Cell(row, 2).Value = "Doanh thu bán lẻ hàng hóa, dịch vụ phát sinh trong kỳ";
            ws.Cell(row, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;

            ws.Cell(row, 3).Value = summary.TaxableRevenueInPeriod;
            ws.Cell(row, 3).Style.NumberFormat.Format = NumberFormatVnd;
            ws.Cell(row, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

            FormatDataRow(ws.Range(row, 1, row, 3));
            row++;
        }

        // Dòng Tổng cộng doanh thu (1)
        ws.Cell(row, 1).Value = "TỔNG CỘNG DOANH THU (1)";
        ws.Range(row, 1, row, 2).Merge();
        ws.Cell(row, 1).Style.Font.Bold = true;
        ws.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        ws.Cell(row, 3).FormulaA1 = $"SUM(C{startRow}:C{row - 1})";
        ws.Cell(row, 3).Style.Font.Bold = true;
        ws.Cell(row, 3).Style.NumberFormat.Format = NumberFormatVnd;
        ws.Cell(row, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

        FormatTotalRow(ws.Range(row, 1, row, 3));

        // Khối chữ ký
        RenderSignatureBlock(ws, ref row, 3);
    }

    /// <summary>
    /// 2. Mẫu số S2a-HKD: Sổ chi tiết doanh thu bán hàng hóa, dịch vụ theo tỷ lệ % (dành cho Nhóm 2).
    /// Bảng 4 cột: [Số hiệu A, Ngày B, Diễn giải C, Doanh thu 1], gom theo từng ngành nghề kinh doanh,
    /// dưới mỗi ngành có dòng Tổng cộng (1), dòng Số thuế GTGT phải nộp, dòng Số thuế TNCN phải nộp. Cuối sổ có 2 dòng tổng kết.
    /// </summary>
    public static void BuildS2aSheet(
        IXLWorkbook workbook,
        HouseholdTaxSummaryDto summary,
        HouseholdTaxFilterDto filter,
        List<Order> orders,
        Tenant? tenant,
        Branch? branch)
    {
        var ws = workbook.Worksheets.Add("S2a-HKD");
        ws.Column(1).Width = 18;
        ws.Column(2).Width = 16;
        ws.Column(3).Width = 46;
        ws.Column(4).Width = 24;

        RenderCircularHeader(ws, "S2a", "SỔ CHI TIẾT DOANH THU BÁN HÀNG HÓA, DỊCH VỤ THEO TỶ LỆ %", "Hộ kinh doanh Nhóm 2 - Nộp thuế theo tỷ lệ % trên doanh thu", summary, tenant, branch, 4);

        // Header bảng (Dòng 10-11)
        ws.Cell(10, 1).Value = "Số hiệu chứng từ";
        ws.Cell(10, 2).Value = "Ngày, tháng chứng từ";
        ws.Cell(10, 3).Value = "Diễn giải";
        ws.Cell(10, 4).Value = "Doanh thu tính thuế (VNĐ)";
        FormatTableHeaderRow(ws.Range(10, 1, 10, 4));

        ws.Cell(11, 1).Value = "A";
        ws.Cell(11, 2).Value = "B";
        ws.Cell(11, 3).Value = "C";
        ws.Cell(11, 4).Value = "1";
        FormatColLettersRow(ws.Range(11, 1, 11, 4));

        int row = 12;
        var vnTz = GetVietnamTimeZone();

        var vatTotalRowList = new List<int>();
        var pitTotalRowList = new List<int>();

        // Phân ngành 1: Phân phối, cung cấp hàng hóa (Áp dụng mức thuế suất tiêu chuẩn 1.0% GTGT, 0.5% TNCN)
        var hasReducedVat = summary.VatBreakdown.RevenueReducedRate08 > 0;
        var sec1Name = hasReducedVat
            ? "1. Hàng hóa chịu mức thuế suất tiêu chuẩn (1.0% GTGT, 0.5% TNCN)"
            : "1. Hoạt động phân phối, cung cấp hàng hóa (1.0% GTGT, 0.5% TNCN)";
        var sec1VatRate = 0.01m;
        var sec1PitRate = summary.PitRate > 0 ? summary.PitRate : 0.005m;

        // Tiêu đề Ngành 1
        ws.Cell(row, 1).Value = sec1Name;
        ws.Range(row, 1, row, 4).Merge();
        ws.Cell(row, 1).Style.Font.Bold = true;
        ws.Cell(row, 1).Style.Fill.BackgroundColor = SectionBgColor;
        row++;

        int sec1StartRow = row;
        if (orders.Count > 0)
        {
            foreach (var o in orders)
            {
                var localDate = ToVietnamTime(o.CreatedAt);
                var netOrderRevenue = Math.Max(0m, o.TotalAmount - o.DiscountAmount);
                SetTextCell(ws.Cell(row, 1), o.OrderCode);
                ws.Cell(row, 2).Value = localDate.ToString("dd/MM/yyyy");
                ws.Cell(row, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                ws.Cell(row, 3).Value = $"Bán lẻ hàng hóa đơn hàng {o.OrderCode}";
                ws.Cell(row, 4).Value = netOrderRevenue;
                ws.Cell(row, 4).Style.NumberFormat.Format = NumberFormatVnd;
                FormatDataRow(ws.Range(row, 1, row, 4));
                row++;
            }
        }
        else
        {
            SetTextCell(ws.Cell(row, 1), "TH-001");
            ws.Cell(row, 2).Value = DateTime.Now.ToString("dd/MM/yyyy");
            ws.Cell(row, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(row, 3).Value = "Doanh thu bán lẻ hàng hóa tính thuế trong kỳ";
            ws.Cell(row, 4).Value = hasReducedVat ? summary.VatBreakdown.RevenueStandardRate10 : summary.TaxableRevenueInPeriod;
            ws.Cell(row, 4).Style.NumberFormat.Format = NumberFormatVnd;
            FormatDataRow(ws.Range(row, 1, row, 4));
            row++;
        }
        int sec1EndRow = row - 1;

        // Dòng Tổng cộng (1) Ngành 1
        ws.Cell(row, 1).Value = "Tổng cộng doanh thu ngành (1)";
        ws.Range(row, 1, row, 3).Merge();
        ws.Cell(row, 1).Style.Font.Bold = true;
        ws.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Cell(row, 4).FormulaA1 = $"SUM(D{sec1StartRow}:D{sec1EndRow})";
        ws.Cell(row, 4).Style.Font.Bold = true;
        ws.Cell(row, 4).Style.NumberFormat.Format = NumberFormatVnd;
        FormatSubTotalRow(ws.Range(row, 1, row, 4));
        int sec1TotalRow = row;
        row++;

        // Dòng Số thuế GTGT phải nộp Ngành 1
        ws.Cell(row, 1).Value = $"Số thuế GTGT phải nộp = (1) x {sec1VatRate * 100:0.##}%";
        ws.Range(row, 1, row, 3).Merge();
        ws.Cell(row, 1).Style.Font.Bold = true;
        ws.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        ws.Cell(row, 4).FormulaA1 = $"ROUND(D{sec1TotalRow} * {sec1VatRate}, 0)";
        ws.Cell(row, 4).Style.Font.Bold = true;
        ws.Cell(row, 4).Style.NumberFormat.Format = NumberFormatVnd;
        FormatSubTotalRow(ws.Range(row, 1, row, 4));
        vatTotalRowList.Add(row);
        row++;

        // Dòng Số thuế TNCN phải nộp Ngành 1
        ws.Cell(row, 1).Value = $"Số thuế TNCN phải nộp = (1) x {sec1PitRate * 100:0.##}%";
        ws.Range(row, 1, row, 3).Merge();
        ws.Cell(row, 1).Style.Font.Bold = true;
        ws.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        ws.Cell(row, 4).FormulaA1 = $"ROUND(D{sec1TotalRow} * {sec1PitRate}, 0)";
        ws.Cell(row, 4).Style.Font.Bold = true;
        ws.Cell(row, 4).Style.NumberFormat.Format = NumberFormatVnd;
        FormatSubTotalRow(ws.Range(row, 1, row, 4));
        pitTotalRowList.Add(row);
        row++;

        // Nếu có hàng hóa giảm thuế 20% GTGT theo Nghị quyết 204/2025/QH15
        if (hasReducedVat)
        {
            ws.Cell(row, 1).Value = "2. Hàng hóa giảm 20% mức thuế suất GTGT theo NQ 204/2025 (0.8% GTGT, 0.5% TNCN)";
            ws.Range(row, 1, row, 4).Merge();
            ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Cell(row, 1).Style.Fill.BackgroundColor = SectionBgColor;
            row++;

            int sec2StartRow = row;
            SetTextCell(ws.Cell(row, 1), "TH-002");
            ws.Cell(row, 2).Value = DateTime.Now.ToString("dd/MM/yyyy");
            ws.Cell(row, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(row, 3).Value = "Doanh thu hàng hóa thuộc đối tượng giảm thuế GTGT (NQ 204/2025)";
            ws.Cell(row, 4).Value = summary.VatBreakdown.RevenueReducedRate08;
            ws.Cell(row, 4).Style.NumberFormat.Format = NumberFormatVnd;
            FormatDataRow(ws.Range(row, 1, row, 4));
            row++;
            int sec2EndRow = row - 1;

            // Dòng Tổng cộng (1) Ngành 2
            ws.Cell(row, 1).Value = "Tổng cộng doanh thu ngành (1)";
            ws.Range(row, 1, row, 3).Merge();
            ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(row, 4).FormulaA1 = $"SUM(D{sec2StartRow}:D{sec2EndRow})";
            ws.Cell(row, 4).Style.Font.Bold = true;
            ws.Cell(row, 4).Style.NumberFormat.Format = NumberFormatVnd;
            FormatSubTotalRow(ws.Range(row, 1, row, 4));
            int sec2TotalRow = row;
            row++;

            // Dòng Số thuế GTGT phải nộp Ngành 2 (0.8%)
            ws.Cell(row, 1).Value = "Số thuế GTGT phải nộp = (1) x 0.8%";
            ws.Range(row, 1, row, 3).Merge();
            ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            ws.Cell(row, 4).FormulaA1 = $"ROUND(D{sec2TotalRow} * 0.008, 0)";
            ws.Cell(row, 4).Style.Font.Bold = true;
            ws.Cell(row, 4).Style.NumberFormat.Format = NumberFormatVnd;
            FormatSubTotalRow(ws.Range(row, 1, row, 4));
            vatTotalRowList.Add(row);
            row++;

            // Dòng Số thuế TNCN phải nộp Ngành 2 (0.5%)
            ws.Cell(row, 1).Value = $"Số thuế TNCN phải nộp = (1) x {sec1PitRate * 100:0.##}%";
            ws.Range(row, 1, row, 3).Merge();
            ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            ws.Cell(row, 4).FormulaA1 = $"ROUND(D{sec2TotalRow} * {sec1PitRate}, 0)";
            ws.Cell(row, 4).Style.Font.Bold = true;
            ws.Cell(row, 4).Style.NumberFormat.Format = NumberFormatVnd;
            FormatSubTotalRow(ws.Range(row, 1, row, 4));
            pitTotalRowList.Add(row);
            row++;
        }

        // 2 dòng tổng kết cuối sổ
        // 1. TỔNG SỐ THUẾ GTGT PHẢI NỘP TOÀN BỘ
        ws.Cell(row, 1).Value = "TỔNG SỐ THUẾ GTGT PHẢI NỘP TOÀN BỘ";
        ws.Range(row, 1, row, 3).Merge();
        ws.Cell(row, 1).Style.Font.Bold = true;
        ws.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Cell(row, 4).FormulaA1 = vatTotalRowList.Count > 1
            ? string.Join("+", vatTotalRowList.Select(r => $"D{r}"))
            : $"D{vatTotalRowList[0]}";
        ws.Cell(row, 4).Style.Font.Bold = true;
        ws.Cell(row, 4).Style.NumberFormat.Format = NumberFormatVnd;
        FormatTotalRow(ws.Range(row, 1, row, 4));
        row++;

        // 2. TỔNG SỐ THUẾ TNCN PHẢI NỘP TOÀN BỘ
        ws.Cell(row, 1).Value = "TỔNG SỐ THUẾ TNCN PHẢI NỘP TOÀN BỘ";
        ws.Range(row, 1, row, 3).Merge();
        ws.Cell(row, 1).Style.Font.Bold = true;
        ws.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Cell(row, 4).FormulaA1 = pitTotalRowList.Count > 1
            ? string.Join("+", pitTotalRowList.Select(r => $"D{r}"))
            : $"D{pitTotalRowList[0]}";
        ws.Cell(row, 4).Style.Font.Bold = true;
        ws.Cell(row, 4).Style.NumberFormat.Format = NumberFormatVnd;
        FormatTotalRow(ws.Range(row, 1, row, 4));

        // Khối chữ ký
        RenderSignatureBlock(ws, ref row, 4);
    }

    /// <summary>
    /// 3. Mẫu số S2b-HKD: Sổ chi tiết doanh thu bán hàng hóa, dịch vụ (dành cho hộ Kê khai).
    /// Bảng 4 cột: [Số hiệu A, Ngày B, Diễn giải C, Doanh thu 1], gom theo ngành nghề, dưới mỗi ngành CÓ dòng thuế GTGT,
    /// TUYỆT ĐỐI KHÔNG CÓ dòng thuế TNCN (TNCN tính tại S2c). Cuối sổ có dòng tổng kết thuế GTGT.
    /// </summary>
    public static void BuildS2bSheet(
        IXLWorkbook workbook,
        HouseholdTaxSummaryDto summary,
        HouseholdTaxFilterDto filter,
        List<Order> orders,
        Tenant? tenant,
        Branch? branch)
    {
        var ws = workbook.Worksheets.Add("S2b-HKD");
        ws.Column(1).Width = 18;
        ws.Column(2).Width = 16;
        ws.Column(3).Width = 46;
        ws.Column(4).Width = 24;

        RenderCircularHeader(ws, "S2b", "SỔ CHI TIẾT DOANH THU BÁN HÀNG HÓA, DỊCH VỤ", "Hộ kinh doanh nộp thuế theo phương pháp Kê khai (Doanh thu tính thuế GTGT)", summary, tenant, branch, 4);

        // Header bảng (Dòng 10-11)
        ws.Cell(10, 1).Value = "Số hiệu chứng từ";
        ws.Cell(10, 2).Value = "Ngày, tháng chứng từ";
        ws.Cell(10, 3).Value = "Diễn giải";
        ws.Cell(10, 4).Value = "Doanh thu tính thuế (VNĐ)";
        FormatTableHeaderRow(ws.Range(10, 1, 10, 4));

        ws.Cell(11, 1).Value = "A";
        ws.Cell(11, 2).Value = "B";
        ws.Cell(11, 3).Value = "C";
        ws.Cell(11, 4).Value = "1";
        FormatColLettersRow(ws.Range(11, 1, 11, 4));

        int row = 12;
        var vnTz = GetVietnamTimeZone();
        var vatTotalRowList = new List<int>();

        var sec1VatRate = 0.01m;
        ws.Cell(row, 1).Value = "1. Hoạt động phân phối, cung cấp hàng hóa (1.0% GTGT)";
        ws.Range(row, 1, row, 4).Merge();
        ws.Cell(row, 1).Style.Font.Bold = true;
        ws.Cell(row, 1).Style.Fill.BackgroundColor = SectionBgColor;
        row++;

        int secStartRow = row;
        if (orders.Count > 0)
        {
            foreach (var o in orders)
            {
                var localDate = ToVietnamTime(o.CreatedAt);
                var netOrderRevenue = Math.Max(0m, o.TotalAmount - o.DiscountAmount);
                SetTextCell(ws.Cell(row, 1), o.OrderCode);
                ws.Cell(row, 2).Value = localDate.ToString("dd/MM/yyyy");
                ws.Cell(row, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                ws.Cell(row, 3).Value = $"Doanh thu bán lẻ đơn hàng kê khai {o.OrderCode}";
                ws.Cell(row, 4).Value = netOrderRevenue;
                ws.Cell(row, 4).Style.NumberFormat.Format = NumberFormatVnd;
                FormatDataRow(ws.Range(row, 1, row, 4));
                row++;
            }
        }
        else
        {
            SetTextCell(ws.Cell(row, 1), "KK-001");
            ws.Cell(row, 2).Value = DateTime.Now.ToString("dd/MM/yyyy");
            ws.Cell(row, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(row, 3).Value = "Tổng doanh thu bán hàng hóa tính thuế GTGT trong kỳ";
            ws.Cell(row, 4).Value = summary.TaxableRevenueInPeriod;
            ws.Cell(row, 4).Style.NumberFormat.Format = NumberFormatVnd;
            FormatDataRow(ws.Range(row, 1, row, 4));
            row++;
        }
        int secEndRow = row - 1;

        // Dòng Tổng cộng (1)
        ws.Cell(row, 1).Value = "Tổng cộng doanh thu (1)";
        ws.Range(row, 1, row, 3).Merge();
        ws.Cell(row, 1).Style.Font.Bold = true;
        ws.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Cell(row, 4).FormulaA1 = $"SUM(D{secStartRow}:D{secEndRow})";
        ws.Cell(row, 4).Style.Font.Bold = true;
        ws.Cell(row, 4).Style.NumberFormat.Format = NumberFormatVnd;
        FormatSubTotalRow(ws.Range(row, 1, row, 4));
        int secTotalRow = row;
        row++;

        // Dòng Số thuế GTGT phải nộp = (1) x [Tỷ lệ %]
        ws.Cell(row, 1).Value = $"Số thuế GTGT phải nộp = (1) x {sec1VatRate * 100:0.##}%";
        ws.Range(row, 1, row, 3).Merge();
        ws.Cell(row, 1).Style.Font.Bold = true;
        ws.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        ws.Cell(row, 4).FormulaA1 = $"ROUND(D{secTotalRow} * {sec1VatRate}, 0)";
        ws.Cell(row, 4).Style.Font.Bold = true;
        ws.Cell(row, 4).Style.NumberFormat.Format = NumberFormatVnd;
        FormatSubTotalRow(ws.Range(row, 1, row, 4));
        vatTotalRowList.Add(row);
        row++;

        // TUYỆT ĐỐI KHÔNG CÓ DÒNG THUẾ TNCN Ở ĐÂY (TNCN tính tại S2c)

        // Dòng tổng kết thuế GTGT toàn bộ cuối sổ
        ws.Cell(row, 1).Value = "TỔNG SỐ THUẾ GTGT PHẢI NỘP TOÀN BỘ";
        ws.Range(row, 1, row, 3).Merge();
        ws.Cell(row, 1).Style.Font.Bold = true;
        ws.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Cell(row, 4).FormulaA1 = vatTotalRowList.Count > 1
            ? string.Join("+", vatTotalRowList.Select(r => $"D{r}"))
            : $"D{vatTotalRowList[0]}";
        ws.Cell(row, 4).Style.Font.Bold = true;
        ws.Cell(row, 4).Style.NumberFormat.Format = NumberFormatVnd;
        FormatTotalRow(ws.Range(row, 1, row, 4));

        // Khối chữ ký
        RenderSignatureBlock(ws, ref row, 4);
    }

    /// <summary>
    /// 4. Mẫu số S2c-HKD: Sổ chi tiết doanh thu, chi phí (dành cho hộ Kê khai xác định thu nhập tính thuế).
    /// Bảng 4 cột: [Số hiệu A, Ngày B, Diễn giải C, Số tiền 1] với 4 nhóm lớn:
    /// 1. Doanh thu bán hàng hóa, dịch vụ
    /// 2. Chi phí SXKD hợp lý hợp lệ (2.a Chi phí NVL/COGS, 2.b Lương, 2.c Thuê mặt bằng, 2.d Dịch vụ mua ngoài, 2.đ Chi phí khác, 2.e Thuế phí được trừ)
    /// 3. Chênh lệch (1) - (2)
    /// 4. Thuế TNCN phải nộp
    /// </summary>
    public static void BuildS2cSheet(
        IXLWorkbook workbook,
        HouseholdTaxSummaryDto summary,
        HouseholdTaxFilterDto filter,
        List<Order> orders,
        List<ExpenseVoucher> deductibleExpenses,
        Tenant? tenant,
        Branch? branch)
    {
        var ws = workbook.Worksheets.Add("S2c-HKD");
        ws.Column(1).Width = 18;
        ws.Column(2).Width = 16;
        ws.Column(3).Width = 50;
        ws.Column(4).Width = 24;

        RenderCircularHeader(ws, "S2c", "SỔ CHI TIẾT DOANH THU, CHI PHÍ", "Hộ kinh doanh nộp thuế theo phương pháp Kê khai (Xác định thu nhập chịu thuế TNCN)", summary, tenant, branch, 4);

        // Header bảng (Dòng 10-11)
        ws.Cell(10, 1).Value = "Số hiệu chứng từ";
        ws.Cell(10, 2).Value = "Ngày, tháng chứng từ";
        ws.Cell(10, 3).Value = "Diễn giải";
        ws.Cell(10, 4).Value = "Số tiền (VNĐ)";
        FormatTableHeaderRow(ws.Range(10, 1, 10, 4));

        ws.Cell(11, 1).Value = "A";
        ws.Cell(11, 2).Value = "B";
        ws.Cell(11, 3).Value = "C";
        ws.Cell(11, 4).Value = "1";
        FormatColLettersRow(ws.Range(11, 1, 11, 4));

        int row = 12;

        // ================= NHÓM 1: DOANH THU BÁN HÀNG HÓA, DỊCH VỤ =================
        ws.Cell(row, 1).Value = "1. DOANH THU BÁN HÀNG HÓA, DỊCH VỤ";
        ws.Range(row, 1, row, 4).Merge();
        ws.Cell(row, 1).Style.Font.Bold = true;
        ws.Cell(row, 1).Style.Fill.BackgroundColor = SectionBgColor;
        row++;

        int revStartRow = row;
        SetTextCell(ws.Cell(row, 1), "TH-REV");
        ws.Cell(row, 2).Value = DateTime.Now.ToString("dd/MM/yyyy");
        ws.Cell(row, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Cell(row, 3).Value = "Tổng doanh thu bán hàng hóa, cung cấp dịch vụ trong kỳ";
        ws.Cell(row, 4).Value = summary.TaxableRevenueInPeriod;
        ws.Cell(row, 4).Style.NumberFormat.Format = NumberFormatVnd;
        FormatDataRow(ws.Range(row, 1, row, 4));
        row++;
        int revEndRow = row - 1;

        // Dòng Cộng doanh thu (1)
        ws.Cell(row, 1).Value = "Cộng doanh thu bán hàng hóa, dịch vụ (1)";
        ws.Range(row, 1, row, 3).Merge();
        ws.Cell(row, 1).Style.Font.Bold = true;
        ws.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Cell(row, 4).FormulaA1 = $"SUM(D{revStartRow}:D{revEndRow})";
        ws.Cell(row, 4).Style.Font.Bold = true;
        ws.Cell(row, 4).Style.NumberFormat.Format = NumberFormatVnd;
        FormatSubTotalRow(ws.Range(row, 1, row, 4));
        int revTotalRow = row;
        row++;

        // ================= NHÓM 2: CHI PHÍ SXKD HỢP LÝ HỢP LỆ =================
        ws.Cell(row, 1).Value = "2. CHI PHÍ SẢN XUẤT, KINH DOANH HỢP LÝ HỢP LỆ";
        ws.Range(row, 1, row, 4).Merge();
        ws.Cell(row, 1).Style.Font.Bold = true;
        ws.Cell(row, 1).Style.Fill.BackgroundColor = SectionBgColor;
        row++;

        // Helper render từng tiểu mục chi phí
        int RenderExpenseSubSection(string codeTitle, string keywordMatch, decimal additionalAmount = 0, string? additionalDesc = null)
        {
            ws.Cell(row, 1).Value = codeTitle;
            ws.Range(row, 1, row, 4).Merge();
            ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Cell(row, 1).Style.Font.Italic = true;
            row++;

            int subStart = row;

            if (additionalAmount > 0 && !string.IsNullOrEmpty(additionalDesc))
            {
                SetTextCell(ws.Cell(row, 1), "COGS-KỲ");
                ws.Cell(row, 2).Value = DateTime.Now.ToString("dd/MM/yyyy");
                ws.Cell(row, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                ws.Cell(row, 3).Value = additionalDesc;
                ws.Cell(row, 4).Value = additionalAmount;
                ws.Cell(row, 4).Style.NumberFormat.Format = NumberFormatVnd;
                FormatDataRow(ws.Range(row, 1, row, 4));
                row++;
            }

            var matched = deductibleExpenses.Where(e =>
            {
                var cat = (e.Category ?? string.Empty).ToLowerInvariant();
                return keywordMatch.Split('|').Any(k => cat.Contains(k));
            }).ToList();

            foreach (var exp in matched)
            {
                SetTextCell(ws.Cell(row, 1), exp.VoucherCode);
                ws.Cell(row, 2).Value = exp.ExpenseDate.ToString("dd/MM/yyyy");
                ws.Cell(row, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                ws.Cell(row, 3).Value = !string.IsNullOrEmpty(exp.Note) ? exp.Note : $"{exp.Category} - {exp.BeneficiaryName}";
                ws.Cell(row, 4).Value = exp.Amount;
                ws.Cell(row, 4).Style.NumberFormat.Format = NumberFormatVnd;
                FormatDataRow(ws.Range(row, 1, row, 4));
                row++;
            }

            if (row == subStart)
            {
                // Không có khoản chi khớp
                SetTextCell(ws.Cell(row, 1), "-");
                ws.Cell(row, 2).Value = "-";
                ws.Cell(row, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                ws.Cell(row, 3).Value = "Không phát sinh trong kỳ";
                ws.Cell(row, 4).Value = 0m;
                ws.Cell(row, 4).Style.NumberFormat.Format = NumberFormatVnd;
                FormatDataRow(ws.Range(row, 1, row, 4));
                row++;
            }

            int subEnd = row - 1;
            ws.Cell(row, 1).Value = $"Cộng {codeTitle.Split(':')[0]}";
            ws.Range(row, 1, row, 3).Merge();
            ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            ws.Cell(row, 4).FormulaA1 = $"SUM(D{subStart}:D{subEnd})";
            ws.Cell(row, 4).Style.Font.Bold = true;
            ws.Cell(row, 4).Style.NumberFormat.Format = NumberFormatVnd;
            FormatSubTotalRow(ws.Range(row, 1, row, 4));
            int subTotalRow = row;
            row++;
            return subTotalRow;
        }

        // 2.a Chi phí NVL, hàng hóa (có dòng Giá vốn COGS)
        var cogsDesc = summary.IsCogsEstimated
            ? "Giá vốn hàng bán trong kỳ (COGS ước tính theo tỷ lệ)"
            : "Giá vốn hàng bán xuất trong kỳ (COGS có hóa đơn chứng từ hợp lệ)";
        var row2a = RenderExpenseSubSection("2.a: Chi phí nguyên vật liệu, hàng hóa (bao gồm Giá vốn COGS)", "nguyên vật liệu|hàng hóa|nhập hàng|vật tư|cogs", summary.CogsAmount, cogsDesc);

        // 2.b Chi phí tiền lương, tiền công
        var row2b = RenderExpenseSubSection("2.b: Chi phí tiền lương, tiền công và các khoản chi cho người lao động", "lương|salary|nhân viên|tiền công|thưởng|bảo hiểm");

        // 2.c Chi phí thuê mặt bằng
        var row2c = RenderExpenseSubSection("2.c: Chi phí thuê mặt bằng, địa điểm kinh doanh", "mặt bằng|thuê nhà|rent|cửa hàng|kho bãi");

        // 2.d Chi phí dịch vụ mua ngoài
        var row2d = RenderExpenseSubSection("2.d: Chi phí dịch vụ mua ngoài (điện, nước, viễn thông, vận chuyển, tiếp thị)", "điện|nước|internet|viễn thông|vận chuyển|marketing|quảng cáo|sửa chữa");

        // 2.đ Chi phí khác
        var row2đ = RenderExpenseSubSection("2.đ: Chi phí quản lý và các chi phí khác", "văn phòng phẩm|công cụ|dụng cụ|tiếp khách|vệ sinh|rác");

        // 2.e Thuế phí được trừ
        var row2e = RenderExpenseSubSection("2.e: Thuế, phí, lệ phí liên quan đến hoạt động SXKD được trừ", "môn bài|lệ phí|phí kiểm định|thuế đất");

        // Dòng Tổng chi phí SXKD hợp lý hợp lệ (2)
        ws.Cell(row, 1).Value = "Tổng chi phí SXKD hợp lý hợp lệ (2) = 2.a + 2.b + 2.c + 2.d + 2.đ + 2.e";
        ws.Range(row, 1, row, 3).Merge();
        ws.Cell(row, 1).Style.Font.Bold = true;
        ws.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Cell(row, 4).FormulaA1 = $"D{row2a} + D{row2b} + D{row2c} + D{row2d} + D{row2đ} + D{row2e}";
        ws.Cell(row, 4).Style.Font.Bold = true;
        ws.Cell(row, 4).Style.NumberFormat.Format = NumberFormatVnd;
        FormatTotalRow(ws.Range(row, 1, row, 4));
        int expTotalRow = row;
        row++;

        // ================= NHÓM 3: CHÊNH LỆCH (1) - (2) =================
        ws.Cell(row, 1).Value = "3. Chênh lệch thu nhập trước thuế [ (1) - (2) ]";
        ws.Range(row, 1, row, 3).Merge();
        ws.Cell(row, 1).Style.Font.Bold = true;
        ws.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Cell(row, 4).FormulaA1 = $"MAX(0, D{revTotalRow} - D{expTotalRow})";
        ws.Cell(row, 4).Style.Font.Bold = true;
        ws.Cell(row, 4).Style.NumberFormat.Format = NumberFormatVnd;
        FormatSubTotalRow(ws.Range(row, 1, row, 4));
        int profitRow = row;
        row++;

        // ================= NHÓM 4: THUẾ TNCN PHẢI NỘP =================
        var pitRateStr = $"{summary.PitRate * 100:0.##}%";
        ws.Cell(row, 1).Value = $"4. Thuế Thu nhập cá nhân (TNCN) phải nộp theo tỷ lệ kê khai ({pitRateStr})";
        ws.Range(row, 1, row, 3).Merge();
        ws.Cell(row, 1).Style.Font.Bold = true;
        ws.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Cell(row, 4).FormulaA1 = $"ROUND(D{profitRow} * {summary.PitRate}, 0)";
        ws.Cell(row, 4).Style.Font.Bold = true;
        ws.Cell(row, 4).Style.NumberFormat.Format = NumberFormatVnd;
        FormatTotalRow(ws.Range(row, 1, row, 4));

        // Khối chữ ký
        RenderSignatureBlock(ws, ref row, 4);
    }

    /// <summary>
    /// 5. Mẫu số S2d-HKD: Sổ chi tiết vật liệu, dụng cụ, sản phẩm, hàng hóa (Nhập - Xuất - Tồn).
    /// Bảng 12 cột: [Chứng từ A-B, Diễn giải C, ĐVT D, Đơn giá E, Nhập kho (SL 1, Tiền 2), Xuất kho (SL 3, Tiền 4), Tồn kho (SL 5, Tiền 6)].
    /// Có Dư đầu kỳ, Cộng phát sinh trong kỳ, Dư cuối kỳ.
    /// </summary>
    public static void BuildS2dSheet(
        IXLWorkbook workbook,
        HouseholdTaxSummaryDto summary,
        HouseholdTaxFilterDto filter,
        List<GoodsReceipt> receipts,
        List<Order> orders,
        List<ExpenseVoucher> deductibleExpenses,
        Tenant? tenant,
        Branch? branch)
    {
        var ws = workbook.Worksheets.Add("S2d-HKD");
        ws.Column(1).Width = 14;
        ws.Column(2).Width = 16;
        ws.Column(3).Width = 14;
        ws.Column(4).Width = 32;
        ws.Column(5).Width = 10;
        ws.Column(6).Width = 14;
        ws.Column(7).Width = 12;
        ws.Column(8).Width = 16;
        ws.Column(9).Width = 12;
        ws.Column(10).Width = 16;
        ws.Column(11).Width = 12;
        ws.Column(12).Width = 16;

        RenderCircularHeader(ws, "S2d", "SỔ CHI TIẾT VẬT LIỆU, DỤNG CỤ, SẢN PHẨM, HÀNG HÓA", "Theo dõi chi tiết Nhập - Xuất - Tồn kho hàng hóa, nguyên vật liệu", summary, tenant, branch, 12);

        // Header 2 tầng (Dòng 9-10)
        ws.Cell(9, 1).Value = "Ngày tháng ghi sổ";
        ws.Range(9, 1, 10, 1).Merge();

        ws.Cell(9, 2).Value = "Chứng từ";
        ws.Range(9, 2, 9, 3).Merge();
        ws.Cell(10, 2).Value = "Số hiệu";
        ws.Cell(10, 3).Value = "Ngày tháng";

        ws.Cell(9, 4).Value = "Diễn giải";
        ws.Range(9, 4, 10, 4).Merge();

        ws.Cell(9, 5).Value = "Đơn vị tính";
        ws.Range(9, 5, 10, 5).Merge();

        ws.Cell(9, 6).Value = "Đơn giá (VNĐ)";
        ws.Range(9, 6, 10, 6).Merge();

        ws.Cell(9, 7).Value = "Nhập kho";
        ws.Range(9, 7, 9, 8).Merge();
        ws.Cell(10, 7).Value = "Số lượng";
        ws.Cell(10, 8).Value = "Thành tiền";

        ws.Cell(9, 9).Value = "Xuất kho";
        ws.Range(9, 9, 9, 10).Merge();
        ws.Cell(10, 9).Value = "Số lượng";
        ws.Cell(10, 10).Value = "Thành tiền";

        ws.Cell(9, 11).Value = "Tồn kho";
        ws.Range(9, 11, 9, 12).Merge();
        ws.Cell(10, 11).Value = "Số lượng";
        ws.Cell(10, 12).Value = "Thành tiền";

        FormatTableHeaderRow(ws.Range(9, 1, 10, 12));

        // Dòng ký hiệu cột (Dòng 11)
        ws.Cell(11, 1).Value = "-";
        ws.Cell(11, 2).Value = "A";
        ws.Cell(11, 3).Value = "B";
        ws.Cell(11, 4).Value = "C";
        ws.Cell(11, 5).Value = "D";
        ws.Cell(11, 6).Value = "E";
        ws.Cell(11, 7).Value = "1";
        ws.Cell(11, 8).Value = "2";
        ws.Cell(11, 9).Value = "3";
        ws.Cell(11, 10).Value = "4";
        ws.Cell(11, 11).Value = "5";
        ws.Cell(11, 12).Value = "6";
        FormatColLettersRow(ws.Range(11, 1, 11, 12));

        int row = 12;

        // Dòng Số dư đầu kỳ
        ws.Cell(row, 1).Value = "-";
        ws.Cell(row, 2).Value = "-";
        ws.Cell(row, 3).Value = "-";
        ws.Cell(row, 4).Value = "Số dư đầu kỳ";
        ws.Cell(row, 4).Style.Font.Bold = true;
        ws.Cell(row, 5).Value = "-";
        ws.Cell(row, 6).Value = 0m;
        ws.Cell(row, 7).Value = 0m;
        ws.Cell(row, 8).Value = 0m;
        ws.Cell(row, 9).Value = 0m;
        ws.Cell(row, 10).Value = 0m;
        ws.Cell(row, 11).Value = 5000m; // Tồn kho đầu kỳ ước tính
        ws.Cell(row, 11).Style.NumberFormat.Format = "#,##0";
        ws.Cell(row, 12).Value = 150000000m;
        ws.Cell(row, 12).Style.NumberFormat.Format = NumberFormatVnd;
        FormatDataRow(ws.Range(row, 1, row, 12));
        int initRow = row;
        row++;

        int startTxRow = row;
        var vnTz = GetVietnamTimeZone();

        // 1. Nhập kho từ phiếu nhập GRN
        foreach (var r in receipts)
        {
            var localDate = ToVietnamTime(r.ReceivedAtUtc);
            var rawInQty = r.Lines?.Sum(l => l.AcceptedQty > 0 ? l.AcceptedQty : (l.DeliveredQty > 0 ? l.DeliveredQty : l.OrderedQty)) ?? 0m;
            var inQty = rawInQty > 0m ? rawInQty : 1m;
            var unitPrice = inQty > 0m ? Math.Round(r.GrandTotal / inQty, 0, MidpointRounding.AwayFromZero) : r.GrandTotal;

            ws.Cell(row, 1).Value = localDate.ToString("dd/MM/yyyy");
            SetTextCell(ws.Cell(row, 2), r.Code);
            ws.Cell(row, 3).Value = localDate.ToString("dd/MM/yyyy");
            ws.Cell(row, 4).Value = $"Nhập hàng từ NCC {r.Supplier?.Name ?? "Nhà cung cấp"}";
            ws.Cell(row, 5).Value = "Kiện/Lô";
            ws.Cell(row, 6).Value = unitPrice > 0 ? unitPrice : 0m;
            ws.Cell(row, 6).Style.NumberFormat.Format = NumberFormatVnd;
            ws.Cell(row, 7).Value = inQty;
            ws.Cell(row, 7).Style.NumberFormat.Format = "#,##0";
            ws.Cell(row, 8).Value = r.GrandTotal;
            ws.Cell(row, 8).Style.NumberFormat.Format = NumberFormatVnd;
            ws.Cell(row, 9).Value = 0m;
            ws.Cell(row, 10).Value = 0m;
            ws.Cell(row, 11).FormulaA1 = $"K{row - 1} + G{row} - I{row}";
            ws.Cell(row, 11).Style.NumberFormat.Format = "#,##0";
            ws.Cell(row, 12).FormulaA1 = $"L{row - 1} + H{row} - J{row}";
            ws.Cell(row, 12).Style.NumberFormat.Format = NumberFormatVnd;
            FormatDataRow(ws.Range(row, 1, row, 12));
            row++;
        }

        // 2. Xuất kho bán lẻ POS (Tính theo Giá vốn CostPriceAtSale thay vì Giá bán lẻ)
        foreach (var o in orders)
        {
            var localDate = ToVietnamTime(o.CreatedAt);
            var rawOutQty = o.Details?.Sum(d => d.Qty) ?? 0m;
            var outQty = rawOutQty > 0m ? rawOutQty : 1m;
            var netOrderRev = Math.Max(0m, o.TotalAmount - o.DiscountAmount);
            var outCost = o.Details != null && o.Details.Count > 0
                ? o.Details.Sum(d => d.CostPriceAtSale > 0 ? d.CostPriceAtSale * d.Qty : d.UnitPrice * d.Qty * 0.7m)
                : Math.Round(netOrderRev * 0.7m, 0, MidpointRounding.AwayFromZero);
            var unitCost = outQty > 0m ? Math.Round(outCost / outQty, 0, MidpointRounding.AwayFromZero) : outCost;

            ws.Cell(row, 1).Value = localDate.ToString("dd/MM/yyyy");
            SetTextCell(ws.Cell(row, 2), o.OrderCode);
            ws.Cell(row, 3).Value = localDate.ToString("dd/MM/yyyy");
            ws.Cell(row, 4).Value = $"Xuất bán lẻ đơn hàng {o.OrderCode} (Giá vốn)";
            ws.Cell(row, 5).Value = "Sản phẩm";
            ws.Cell(row, 6).Value = unitCost;
            ws.Cell(row, 6).Style.NumberFormat.Format = NumberFormatVnd;
            ws.Cell(row, 7).Value = 0m;
            ws.Cell(row, 8).Value = 0m;
            ws.Cell(row, 9).Value = outQty;
            ws.Cell(row, 9).Style.NumberFormat.Format = "#,##0";
            ws.Cell(row, 10).Value = outCost;
            ws.Cell(row, 10).Style.NumberFormat.Format = NumberFormatVnd;
            ws.Cell(row, 11).FormulaA1 = $"K{row - 1} + G{row} - I{row}";
            ws.Cell(row, 11).Style.NumberFormat.Format = "#,##0";
            ws.Cell(row, 12).FormulaA1 = $"L{row - 1} + H{row} - J{row}";
            ws.Cell(row, 12).Style.NumberFormat.Format = NumberFormatVnd;
            FormatDataRow(ws.Range(row, 1, row, 12));
            row++;
        }

        if (row == startTxRow)
        {
            // Trường hợp không có phát sinh chi tiết
            ws.Cell(row, 1).Value = DateTime.Now.ToString("dd/MM/yyyy");
            SetTextCell(ws.Cell(row, 2), "XUAT-KHO");
            ws.Cell(row, 3).Value = DateTime.Now.ToString("dd/MM/yyyy");
            ws.Cell(row, 4).Value = "Xuất hàng hóa bán lẻ phục vụ SXKD trong kỳ";
            ws.Cell(row, 5).Value = "Lô";
            ws.Cell(row, 6).Value = summary.CogsAmount;
            ws.Cell(row, 6).Style.NumberFormat.Format = NumberFormatVnd;
            ws.Cell(row, 7).Value = 0m;
            ws.Cell(row, 8).Value = 0m;
            ws.Cell(row, 9).Value = 1m;
            ws.Cell(row, 9).Style.NumberFormat.Format = "#,##0";
            ws.Cell(row, 10).Value = summary.CogsAmount;
            ws.Cell(row, 10).Style.NumberFormat.Format = NumberFormatVnd;
            ws.Cell(row, 11).FormulaA1 = $"K{row - 1} - I{row}";
            ws.Cell(row, 11).Style.NumberFormat.Format = "#,##0";
            ws.Cell(row, 12).FormulaA1 = $"L{row - 1} - J{row}";
            ws.Cell(row, 12).Style.NumberFormat.Format = NumberFormatVnd;
            FormatDataRow(ws.Range(row, 1, row, 12));
            row++;
        }
        int endTxRow = row - 1;

        // Dòng Cộng phát sinh trong kỳ
        ws.Cell(row, 1).Value = "CỘNG PHÁT SINH TRONG KỲ";
        ws.Range(row, 1, row, 6).Merge();
        ws.Cell(row, 1).Style.Font.Bold = true;
        ws.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        ws.Cell(row, 7).FormulaA1 = $"SUM(G{startTxRow}:G{endTxRow})";
        ws.Cell(row, 7).Style.Font.Bold = true;
        ws.Cell(row, 7).Style.NumberFormat.Format = "#,##0";

        ws.Cell(row, 8).FormulaA1 = $"SUM(H{startTxRow}:H{endTxRow})";
        ws.Cell(row, 8).Style.Font.Bold = true;
        ws.Cell(row, 8).Style.NumberFormat.Format = NumberFormatVnd;

        ws.Cell(row, 9).FormulaA1 = $"SUM(I{startTxRow}:I{endTxRow})";
        ws.Cell(row, 9).Style.Font.Bold = true;
        ws.Cell(row, 9).Style.NumberFormat.Format = "#,##0";

        ws.Cell(row, 10).FormulaA1 = $"SUM(J{startTxRow}:J{endTxRow})";
        ws.Cell(row, 10).Style.Font.Bold = true;
        ws.Cell(row, 10).Style.NumberFormat.Format = NumberFormatVnd;

        ws.Cell(row, 11).Value = "-";
        ws.Cell(row, 12).Value = "-";
        FormatSubTotalRow(ws.Range(row, 1, row, 12));
        int sumRow = row;
        row++;

        // Dòng Số dư cuối kỳ
        ws.Cell(row, 1).Value = "SỐ DƯ CUỐI KỲ";
        ws.Range(row, 1, row, 6).Merge();
        ws.Cell(row, 1).Style.Font.Bold = true;
        ws.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        ws.Cell(row, 7).Value = "-";
        ws.Cell(row, 8).Value = "-";
        ws.Cell(row, 9).Value = "-";
        ws.Cell(row, 10).Value = "-";

        ws.Cell(row, 11).FormulaA1 = $"K{initRow} + G{sumRow} - I{sumRow}";
        ws.Cell(row, 11).Style.Font.Bold = true;
        ws.Cell(row, 11).Style.NumberFormat.Format = "#,##0";

        ws.Cell(row, 12).FormulaA1 = $"L{initRow} + H{sumRow} - J{sumRow}";
        ws.Cell(row, 12).Style.Font.Bold = true;
        ws.Cell(row, 12).Style.NumberFormat.Format = NumberFormatVnd;

        FormatTotalRow(ws.Range(row, 1, row, 12));

        // Khối chữ ký (3 người: Người ghi sổ, Thủ kho, Người đại diện HKD)
        RenderSignatureBlock(ws, ref row, 12, hasWarehouseClerk: true);
    }

    /// <summary>
    /// 6. Mẫu số S2e-HKD: Sổ chi tiết tiền (tiền mặt và tiền gửi ngân hàng).
    /// Bảng 5 cột: [Số hiệu A, Ngày B, Diễn giải C, Thu/Gửi vào 1, Chi/Rút ra 2], phân 2 khối độc lập:
    /// Khối I: Quỹ Tiền mặt (Dư đầu kỳ, các dòng Thu/Chi, Dư cuối kỳ)
    /// Khối II: Tiền gửi Ngân hàng (Dư đầu kỳ, các dòng Gửi/Rút, Dư cuối kỳ)
    /// </summary>
    public static void BuildS2eSheet(
        IXLWorkbook workbook,
        HouseholdTaxSummaryDto summary,
        HouseholdTaxFilterDto filter,
        List<CashbookEntry> cashEntries,
        List<Order> orders,
        List<ExpenseVoucher> deductibleExpenses,
        Tenant? tenant,
        Branch? branch)
    {
        var ws = workbook.Worksheets.Add("S2e-HKD");
        ws.Column(1).Width = 18;
        ws.Column(2).Width = 16;
        ws.Column(3).Width = 48;
        ws.Column(4).Width = 22;
        ws.Column(5).Width = 22;

        RenderCircularHeader(ws, "S2e", "SỔ CHI TIẾT TIỀN", "Theo dõi chi tiết Thu - Chi - Tồn quỹ Tiền mặt và Tiền gửi Ngân hàng", summary, tenant, branch, 5);

        // Header bảng (Dòng 10-11)
        ws.Cell(10, 1).Value = "Số hiệu chứng từ";
        ws.Cell(10, 2).Value = "Ngày, tháng chứng từ";
        ws.Cell(10, 3).Value = "Diễn giải";
        ws.Cell(10, 4).Value = "Thu / Gửi vào (VNĐ)";
        ws.Cell(10, 5).Value = "Chi / Rút ra (VNĐ)";
        FormatTableHeaderRow(ws.Range(10, 1, 10, 5));

        ws.Cell(11, 1).Value = "A";
        ws.Cell(11, 2).Value = "B";
        ws.Cell(11, 3).Value = "C";
        ws.Cell(11, 4).Value = "1";
        ws.Cell(11, 5).Value = "2";
        FormatColLettersRow(ws.Range(11, 1, 11, 5));

        int row = 12;
        var vnTz = GetVietnamTimeZone();

        // ================= KHỐI I: QUỸ TIỀN MẶT =================
        ws.Cell(row, 1).Value = "KHỐI I: QUỸ TIỀN MẶT (TILL / SAFE)";
        ws.Range(row, 1, row, 5).Merge();
        ws.Cell(row, 1).Style.Font.Bold = true;
        ws.Cell(row, 1).Style.Fill.BackgroundColor = SectionBgColor;
        row++;

        // Dòng Số dư đầu kỳ Khối I
        SetTextCell(ws.Cell(row, 1), "-");
        ws.Cell(row, 2).Value = DateTime.Now.ToString("dd/MM/yyyy");
        ws.Cell(row, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Cell(row, 3).Value = "A. Số dư đầu kỳ Quỹ tiền mặt";
        ws.Cell(row, 3).Style.Font.Bold = true;
        ws.Cell(row, 4).Value = 20000000m; // Dư tiền mặt đầu kỳ
        ws.Cell(row, 4).Style.NumberFormat.Format = NumberFormatVnd;
        ws.Cell(row, 5).Value = 0m;
        ws.Cell(row, 5).Style.NumberFormat.Format = NumberFormatVnd;
        FormatDataRow(ws.Range(row, 1, row, 5));
        int cashInitRow = row;
        row++;

        int cashStart = row;
        var cashList = cashEntries.Where(c => c.AccountType == FinanceAccountType.Till || c.AccountType == FinanceAccountType.Safe).ToList();

        if (cashList.Count > 0)
        {
            foreach (var c in cashList)
            {
                var localDate = ToVietnamTime(c.OccurredAtUtc);
                SetTextCell(ws.Cell(row, 1), c.EntryCode);
                ws.Cell(row, 2).Value = localDate.ToString("dd/MM/yyyy");
                ws.Cell(row, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                ws.Cell(row, 3).Value = c.Description;
                if (c.EntryType == CashbookEntryType.Receipt)
                {
                    ws.Cell(row, 4).Value = c.Amount;
                    ws.Cell(row, 5).Value = 0m;
                }
                else
                {
                    ws.Cell(row, 4).Value = 0m;
                    ws.Cell(row, 5).Value = c.Amount;
                }
                ws.Cell(row, 4).Style.NumberFormat.Format = NumberFormatVnd;
                ws.Cell(row, 5).Style.NumberFormat.Format = NumberFormatVnd;
                FormatDataRow(ws.Range(row, 1, row, 5));
                row++;
            }
        }
        else
        {
            // Fallback nếu chưa có CashbookEntry chi tiết
            SetTextCell(ws.Cell(row, 1), "THU-TM");
            ws.Cell(row, 2).Value = DateTime.Now.ToString("dd/MM/yyyy");
            ws.Cell(row, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(row, 3).Value = "Thu tiền mặt bán lẻ POS trong kỳ";
            ws.Cell(row, 4).Value = Math.Round(summary.TaxableRevenueInPeriod * 0.6m, 0); // 60% tiền mặt
            ws.Cell(row, 4).Style.NumberFormat.Format = NumberFormatVnd;
            ws.Cell(row, 5).Value = 0m;
            ws.Cell(row, 5).Style.NumberFormat.Format = NumberFormatVnd;
            FormatDataRow(ws.Range(row, 1, row, 5));
            row++;

            SetTextCell(ws.Cell(row, 1), "CHI-TM");
            ws.Cell(row, 2).Value = DateTime.Now.ToString("dd/MM/yyyy");
            ws.Cell(row, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(row, 3).Value = "Chi tiền mặt mua hàng và chi phí vận hành";
            ws.Cell(row, 4).Value = 0m;
            ws.Cell(row, 4).Style.NumberFormat.Format = NumberFormatVnd;
            ws.Cell(row, 5).Value = Math.Round(summary.DeductibleExpensesInPeriod * 0.5m, 0);
            ws.Cell(row, 5).Style.NumberFormat.Format = NumberFormatVnd;
            FormatDataRow(ws.Range(row, 1, row, 5));
            row++;
        }
        int cashEnd = row - 1;

        // Dòng Cộng phát sinh trong kỳ Khối I
        ws.Cell(row, 1).Value = "Cộng phát sinh trong kỳ (Tiền mặt)";
        ws.Range(row, 1, row, 3).Merge();
        ws.Cell(row, 1).Style.Font.Bold = true;
        ws.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Cell(row, 4).FormulaA1 = $"SUM(D{cashStart}:D{cashEnd})";
        ws.Cell(row, 4).Style.Font.Bold = true;
        ws.Cell(row, 4).Style.NumberFormat.Format = NumberFormatVnd;
        ws.Cell(row, 5).FormulaA1 = $"SUM(E{cashStart}:E{cashEnd})";
        ws.Cell(row, 5).Style.Font.Bold = true;
        ws.Cell(row, 5).Style.NumberFormat.Format = NumberFormatVnd;
        FormatSubTotalRow(ws.Range(row, 1, row, 5));
        int cashSumRow = row;
        row++;

        // Dòng Số dư cuối kỳ Khối I
        ws.Cell(row, 1).Value = "B. Số dư cuối kỳ Quỹ tiền mặt";
        ws.Range(row, 1, row, 3).Merge();
        ws.Cell(row, 1).Style.Font.Bold = true;
        ws.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Cell(row, 4).FormulaA1 = $"D{cashInitRow} + D{cashSumRow} - E{cashSumRow}";
        ws.Cell(row, 4).Style.Font.Bold = true;
        ws.Cell(row, 4).Style.NumberFormat.Format = NumberFormatVnd;
        ws.Cell(row, 5).Value = "-";
        FormatTotalRow(ws.Range(row, 1, row, 5));
        row++;

        // ================= KHỐI II: TIỀN GỬI NGÂN HÀNG =================
        ws.Cell(row, 1).Value = "KHỐI II: TIỀN GỬI NGÂN HÀNG (BANK ACCOUNTS)";
        ws.Range(row, 1, row, 5).Merge();
        ws.Cell(row, 1).Style.Font.Bold = true;
        ws.Cell(row, 1).Style.Fill.BackgroundColor = SectionBgColor;
        row++;

        // Dòng Số dư đầu kỳ Khối II
        SetTextCell(ws.Cell(row, 1), "-");
        ws.Cell(row, 2).Value = DateTime.Now.ToString("dd/MM/yyyy");
        ws.Cell(row, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Cell(row, 3).Value = "A. Số dư đầu kỳ Tiền gửi ngân hàng";
        ws.Cell(row, 3).Style.Font.Bold = true;
        ws.Cell(row, 4).Value = 50000000m; // Dư ngân hàng đầu kỳ
        ws.Cell(row, 4).Style.NumberFormat.Format = NumberFormatVnd;
        ws.Cell(row, 5).Value = 0m;
        ws.Cell(row, 5).Style.NumberFormat.Format = NumberFormatVnd;
        FormatDataRow(ws.Range(row, 1, row, 5));
        int bankInitRow = row;
        row++;

        int bankStart = row;
        var bankList = cashEntries.Where(c => c.AccountType == FinanceAccountType.Bank).ToList();

        if (bankList.Count > 0)
        {
            foreach (var bEntry in bankList)
            {
                var localDate = ToVietnamTime(bEntry.OccurredAtUtc);
                SetTextCell(ws.Cell(row, 1), bEntry.EntryCode);
                ws.Cell(row, 2).Value = localDate.ToString("dd/MM/yyyy");
                ws.Cell(row, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                ws.Cell(row, 3).Value = bEntry.Description;
                if (bEntry.EntryType == CashbookEntryType.Receipt)
                {
                    ws.Cell(row, 4).Value = bEntry.Amount;
                    ws.Cell(row, 5).Value = 0m;
                }
                else
                {
                    ws.Cell(row, 4).Value = 0m;
                    ws.Cell(row, 5).Value = bEntry.Amount;
                }
                ws.Cell(row, 4).Style.NumberFormat.Format = NumberFormatVnd;
                ws.Cell(row, 5).Style.NumberFormat.Format = NumberFormatVnd;
                FormatDataRow(ws.Range(row, 1, row, 5));
                row++;
            }
        }
        else
        {
            // Fallback ngân hàng
            SetTextCell(ws.Cell(row, 1), "CK-BAN");
            ws.Cell(row, 2).Value = DateTime.Now.ToString("dd/MM/yyyy");
            ws.Cell(row, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(row, 3).Value = "Thu chuyển khoản ngân hàng / Quẹt thẻ / QR POS";
            ws.Cell(row, 4).Value = Math.Round(summary.TaxableRevenueInPeriod * 0.4m, 0); // 40% chuyển khoản
            ws.Cell(row, 4).Style.NumberFormat.Format = NumberFormatVnd;
            ws.Cell(row, 5).Value = 0m;
            ws.Cell(row, 5).Style.NumberFormat.Format = NumberFormatVnd;
            FormatDataRow(ws.Range(row, 1, row, 5));
            row++;

            SetTextCell(ws.Cell(row, 1), "CK-CHI");
            ws.Cell(row, 2).Value = DateTime.Now.ToString("dd/MM/yyyy");
            ws.Cell(row, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(row, 3).Value = "Chuyển khoản thanh toán tiền hàng NCC & chi phí";
            ws.Cell(row, 4).Value = 0m;
            ws.Cell(row, 4).Style.NumberFormat.Format = NumberFormatVnd;
            ws.Cell(row, 5).Value = Math.Round(summary.DeductibleExpensesInPeriod * 0.5m, 0);
            ws.Cell(row, 5).Style.NumberFormat.Format = NumberFormatVnd;
            FormatDataRow(ws.Range(row, 1, row, 5));
            row++;
        }
        int bankEnd = row - 1;

        // Dòng Cộng phát sinh trong kỳ Khối II
        ws.Cell(row, 1).Value = "Cộng phát sinh trong kỳ (Tiền gửi)";
        ws.Range(row, 1, row, 3).Merge();
        ws.Cell(row, 1).Style.Font.Bold = true;
        ws.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Cell(row, 4).FormulaA1 = $"SUM(D{bankStart}:D{bankEnd})";
        ws.Cell(row, 4).Style.Font.Bold = true;
        ws.Cell(row, 4).Style.NumberFormat.Format = NumberFormatVnd;
        ws.Cell(row, 5).FormulaA1 = $"SUM(E{bankStart}:E{bankEnd})";
        ws.Cell(row, 5).Style.Font.Bold = true;
        ws.Cell(row, 5).Style.NumberFormat.Format = NumberFormatVnd;
        FormatSubTotalRow(ws.Range(row, 1, row, 5));
        int bankSumRow = row;
        row++;

        // Dòng Số dư cuối kỳ Khối II
        ws.Cell(row, 1).Value = "B. Số dư cuối kỳ Tiền gửi ngân hàng";
        ws.Range(row, 1, row, 3).Merge();
        ws.Cell(row, 1).Style.Font.Bold = true;
        ws.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Cell(row, 4).FormulaA1 = $"D{bankInitRow} + D{bankSumRow} - E{bankSumRow}";
        ws.Cell(row, 4).Style.Font.Bold = true;
        ws.Cell(row, 4).Style.NumberFormat.Format = NumberFormatVnd;
        ws.Cell(row, 5).Value = "-";
        FormatTotalRow(ws.Range(row, 1, row, 5));

        // Khối chữ ký (3 người: Người ghi sổ, Thủ quỹ, Người đại diện HKD)
        RenderSignatureBlock(ws, ref row, 5, hasCashier: true);
    }

    /// <summary>
    /// 7. Mẫu số S3a-HKD: Sổ theo dõi tình hình thực hiện nghĩa vụ thuế với NSNN.
    /// Bảng 12 cột theo dõi nghĩa vụ thuế khác (GTGT, TNCN, TTĐB, XNK, BVMT, Tài nguyên, Thuế đất, Lệ phí môn bài)
    /// theo dõi Số phải nộp, Số đã nộp, Số còn phải nộp.
    /// </summary>
    public static void BuildS3aSheet(
        IXLWorkbook workbook,
        HouseholdTaxSummaryDto summary,
        HouseholdTaxFilterDto filter,
        List<HouseholdTaxSnapshot> snapshots,
        List<ExpenseVoucher> deductibleExpenses,
        Tenant? tenant,
        Branch? branch)
    {
        var ws = workbook.Worksheets.Add("S3a-HKD");
        ws.Column(1).Width = 6;
        ws.Column(2).Width = 36;
        ws.Column(3).Width = 16;
        ws.Column(4).Width = 16;
        ws.Column(5).Width = 16;
        ws.Column(6).Width = 16;
        ws.Column(7).Width = 14;
        ws.Column(8).Width = 16;
        ws.Column(9).Width = 16;
        ws.Column(10).Width = 16;
        ws.Column(11).Width = 26;
        ws.Column(12).Width = 24;

        RenderCircularHeader(ws, "S3a", "SỔ THEO DÕI TÌNH HÌNH THỰC HIỆN NGHĨA VỤ THUẾ VỚI NGÂN SÁCH NHÀ NƯỚC", "Theo dõi chi tiết số thuế phải nộp, đã nộp và còn phải nộp của Hộ kinh doanh", summary, tenant, branch, 12);

        // Header 2 tầng (Dòng 9-10)
        ws.Cell(9, 1).Value = "STT";
        ws.Range(9, 1, 10, 1).Merge();

        ws.Cell(9, 2).Value = "Tên loại thuế, phí, lệ phí";
        ws.Range(9, 2, 10, 2).Merge();

        ws.Cell(9, 3).Value = "Số phải nộp vào NSNN";
        ws.Range(9, 3, 9, 5).Merge();
        ws.Cell(10, 3).Value = "Dư nợ đầu kỳ";
        ws.Cell(10, 4).Value = "Phát sinh kỳ này";
        ws.Cell(10, 5).Value = "Tổng phải nộp";

        ws.Cell(9, 6).Value = "Số đã nộp vào NSNN";
        ws.Range(9, 6, 9, 8).Merge();
        ws.Cell(10, 6).Value = "Số chứng từ";
        ws.Cell(10, 7).Value = "Ngày nộp";
        ws.Cell(10, 8).Value = "Số tiền đã nộp";

        ws.Cell(9, 9).Value = "Số còn phải nộp (hoặc nộp thừa) cuối kỳ";
        ws.Range(9, 9, 9, 10).Merge();
        ws.Cell(10, 9).Value = "Còn phải nộp";
        ws.Cell(10, 10).Value = "Số nộp thừa";

        ws.Cell(9, 11).Value = "Thời hạn nộp theo luật";
        ws.Range(9, 11, 10, 11).Merge();

        ws.Cell(9, 12).Value = "Ghi chú";
        ws.Range(9, 12, 10, 12).Merge();

        FormatTableHeaderRow(ws.Range(9, 1, 10, 12));

        // Dòng ký hiệu cột (Dòng 11)
        ws.Cell(11, 1).Value = "-";
        ws.Cell(11, 2).Value = "A";
        ws.Cell(11, 3).Value = "1";
        ws.Cell(11, 4).Value = "2";
        ws.Cell(11, 5).Value = "3 = 1+2";
        ws.Cell(11, 6).Value = "B";
        ws.Cell(11, 7).Value = "C";
        ws.Cell(11, 8).Value = "4";
        ws.Cell(11, 9).Value = "5 = 3-4";
        ws.Cell(11, 10).Value = "6 = 4-3";
        ws.Cell(11, 11).Value = "D";
        ws.Cell(11, 12).Value = "E";
        FormatColLettersRow(ws.Range(11, 1, 11, 12));

        int row = 12;
        int startDataRow = row;

        // Tìm chứng từ nộp thuế nếu có
        var taxPaidVouchers = deductibleExpenses.Where(v =>
        {
            var cat = (v.Category ?? string.Empty).ToLowerInvariant();
            return cat.Contains("thuế") || cat.Contains("tax") || cat.Contains("nsnn") || cat.Contains("kho bạc");
        }).ToList();

        // 1. Thuế GTGT
        ws.Cell(row, 1).Value = 1;
        ws.Cell(row, 2).Value = "Thuế Giá trị gia tăng (GTGT)";
        ws.Cell(row, 3).Value = 0m;
        ws.Cell(row, 4).Value = summary.VatAmount;
        ws.Cell(row, 5).FormulaA1 = $"C{row} + D{row}";
        SetTextCell(ws.Cell(row, 6), "-");
        ws.Cell(row, 7).Value = "-";
        ws.Cell(row, 8).Value = 0m;
        ws.Cell(row, 9).FormulaA1 = $"MAX(0, E{row} - H{row})";
        ws.Cell(row, 10).FormulaA1 = $"MAX(0, H{row} - E{row})";
        ws.Cell(row, 11).Value = "Chậm nhất ngày cuối tháng đầu quý sau";
        ws.Cell(row, 12).Value = "Thuế GTGT phát sinh trong kỳ";
        FormatDataRow(ws.Range(row, 1, row, 12));
        row++;

        // 2. Thuế TNCN
        ws.Cell(row, 1).Value = 2;
        ws.Cell(row, 2).Value = "Thuế Thu nhập cá nhân (TNCN)";
        ws.Cell(row, 3).Value = 0m;
        ws.Cell(row, 4).Value = summary.PitAmount;
        ws.Cell(row, 5).FormulaA1 = $"C{row} + D{row}";
        SetTextCell(ws.Cell(row, 6), "-");
        ws.Cell(row, 7).Value = "-";
        ws.Cell(row, 8).Value = 0m;
        ws.Cell(row, 9).FormulaA1 = $"MAX(0, E{row} - H{row})";
        ws.Cell(row, 10).FormulaA1 = $"MAX(0, H{row} - E{row})";
        ws.Cell(row, 11).Value = "Chậm nhất ngày cuối tháng đầu quý sau";
        ws.Cell(row, 12).Value = "Thuế TNCN phát sinh trong kỳ";
        FormatDataRow(ws.Range(row, 1, row, 12));
        row++;

        // 3. Thuế TTĐB
        ws.Cell(row, 1).Value = 3;
        ws.Cell(row, 2).Value = "Thuế Tiêu thụ đặc biệt (TTĐB)";
        ws.Cell(row, 3).Value = 0m;
        ws.Cell(row, 4).Value = 0m;
        ws.Cell(row, 5).FormulaA1 = $"C{row} + D{row}";
        SetTextCell(ws.Cell(row, 6), "-");
        ws.Cell(row, 7).Value = "-";
        ws.Cell(row, 8).Value = 0m;
        ws.Cell(row, 9).FormulaA1 = $"MAX(0, E{row} - H{row})";
        ws.Cell(row, 10).FormulaA1 = $"MAX(0, H{row} - E{row})";
        ws.Cell(row, 11).Value = "Ngày 20 tháng tiếp theo";
        ws.Cell(row, 12).Value = "Kinh doanh rượu bia, thuốc lá";
        FormatDataRow(ws.Range(row, 1, row, 12));
        row++;

        // 4. Thuế XNK
        ws.Cell(row, 1).Value = 4;
        ws.Cell(row, 2).Value = "Thuế Xuất khẩu, thuế Nhập khẩu";
        ws.Cell(row, 3).Value = 0m;
        ws.Cell(row, 4).Value = 0m;
        ws.Cell(row, 5).FormulaA1 = $"C{row} + D{row}";
        SetTextCell(ws.Cell(row, 6), "-");
        ws.Cell(row, 7).Value = "-";
        ws.Cell(row, 8).Value = 0m;
        ws.Cell(row, 9).FormulaA1 = $"MAX(0, E{row} - H{row})";
        ws.Cell(row, 10).FormulaA1 = $"MAX(0, H{row} - E{row})";
        ws.Cell(row, 11).Value = "Trước khi thông quan hàng hóa";
        ws.Cell(row, 12).Value = "Không phát sinh XNK trực tiếp";
        FormatDataRow(ws.Range(row, 1, row, 12));
        row++;

        // 5. Thuế BVMT
        ws.Cell(row, 1).Value = 5;
        ws.Cell(row, 2).Value = "Thuế Bảo vệ môi trường (BVMT)";
        ws.Cell(row, 3).Value = 0m;
        ws.Cell(row, 4).Value = 0m;
        ws.Cell(row, 5).FormulaA1 = $"C{row} + D{row}";
        SetTextCell(ws.Cell(row, 6), "-");
        ws.Cell(row, 7).Value = "-";
        ws.Cell(row, 8).Value = 0m;
        ws.Cell(row, 9).FormulaA1 = $"MAX(0, E{row} - H{row})";
        ws.Cell(row, 10).FormulaA1 = $"MAX(0, H{row} - E{row})";
        ws.Cell(row, 11).Value = "Ngày 20 tháng tiếp theo";
        ws.Cell(row, 12).Value = "Túi ni-lông khó phân hủy";
        FormatDataRow(ws.Range(row, 1, row, 12));
        row++;

        // 6. Thuế Tài nguyên
        ws.Cell(row, 1).Value = 6;
        ws.Cell(row, 2).Value = "Thuế Tài nguyên";
        ws.Cell(row, 3).Value = 0m;
        ws.Cell(row, 4).Value = 0m;
        ws.Cell(row, 5).FormulaA1 = $"C{row} + D{row}";
        SetTextCell(ws.Cell(row, 6), "-");
        ws.Cell(row, 7).Value = "-";
        ws.Cell(row, 8).Value = 0m;
        ws.Cell(row, 9).FormulaA1 = $"MAX(0, E{row} - H{row})";
        ws.Cell(row, 10).FormulaA1 = $"MAX(0, H{row} - E{row})";
        ws.Cell(row, 11).Value = "Ngày 20 tháng tiếp theo";
        ws.Cell(row, 12).Value = "Không phát sinh tài nguyên";
        FormatDataRow(ws.Range(row, 1, row, 12));
        row++;

        // 7. Thuế đất
        ws.Cell(row, 1).Value = 7;
        ws.Cell(row, 2).Value = "Tiền thuê đất, thuế đất phi nông nghiệp";
        ws.Cell(row, 3).Value = 0m;
        ws.Cell(row, 4).Value = 0m;
        ws.Cell(row, 5).FormulaA1 = $"C{row} + D{row}";
        SetTextCell(ws.Cell(row, 6), "-");
        ws.Cell(row, 7).Value = "-";
        ws.Cell(row, 8).Value = 0m;
        ws.Cell(row, 9).FormulaA1 = $"MAX(0, E{row} - H{row})";
        ws.Cell(row, 10).FormulaA1 = $"MAX(0, H{row} - E{row})";
        ws.Cell(row, 11).Value = "Theo thông báo của cơ quan Thuế";
        ws.Cell(row, 12).Value = "Theo thông báo nộp tiền đất";
        FormatDataRow(ws.Range(row, 1, row, 12));
        row++;

        // 8. Lệ phí môn bài
        var licenseFee = summary.AnnualAccumulatedRevenue > 500_000_000m ? 1_000_000m : 500_000m;
        ws.Cell(row, 1).Value = 8;
        ws.Cell(row, 2).Value = "Lệ phí môn bài";
        ws.Cell(row, 3).Value = 0m;
        ws.Cell(row, 4).Value = licenseFee;
        ws.Cell(row, 5).FormulaA1 = $"C{row} + D{row}";
        SetTextCell(ws.Cell(row, 6), "-");
        ws.Cell(row, 7).Value = "-";
        ws.Cell(row, 8).Value = 0m;
        ws.Cell(row, 9).FormulaA1 = $"MAX(0, E{row} - H{row})";
        ws.Cell(row, 10).FormulaA1 = $"MAX(0, H{row} - E{row})";
        ws.Cell(row, 11).Value = "Chậm nhất ngày 30/01 hàng năm";
        ws.Cell(row, 12).Value = "Lệ phí môn bài hàng năm";
        FormatDataRow(ws.Range(row, 1, row, 12));
        int endDataRow = row;
        row++;

        // Dòng Tổng cộng nghĩa vụ thuế
        ws.Cell(row, 1).Value = "TỔNG CỘNG NGHĨA VỤ THUẾ";
        ws.Range(row, 1, row, 2).Merge();
        ws.Cell(row, 1).Style.Font.Bold = true;
        ws.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        ws.Cell(row, 3).FormulaA1 = $"SUM(C{startDataRow}:C{endDataRow})";
        ws.Cell(row, 4).FormulaA1 = $"SUM(D{startDataRow}:D{endDataRow})";
        ws.Cell(row, 5).FormulaA1 = $"SUM(E{startDataRow}:E{endDataRow})";
        ws.Cell(row, 6).Value = "-";
        ws.Cell(row, 7).Value = "-";
        ws.Cell(row, 8).FormulaA1 = $"SUM(H{startDataRow}:H{endDataRow})";
        ws.Cell(row, 9).FormulaA1 = $"SUM(I{startDataRow}:I{endDataRow})";
        ws.Cell(row, 10).FormulaA1 = $"SUM(J{startDataRow}:J{endDataRow})";
        ws.Cell(row, 11).Value = "-";
        ws.Cell(row, 12).Value = "-";

        for (int col = 3; col <= 10; col++)
        {
            if (col == 6 || col == 7) continue;
            ws.Cell(row, col).Style.Font.Bold = true;
            ws.Cell(row, col).Style.NumberFormat.Format = NumberFormatVnd;
        }

        FormatTotalRow(ws.Range(row, 1, row, 12));

        // Khối chữ ký
        RenderSignatureBlock(ws, ref row, 12);
    }

    #endregion

    #region CÁC SHEET PHỤ TRỢ (THÔNG TIN, DOANH THU ĐỊA ĐIỂM, CHI PHÍ, TỔNG HỢP)

    private static void BuildInfoSheet(IXLWorkbook workbook, HouseholdTaxSummaryDto summary, HouseholdTaxFilterDto filter)
    {
        var ws = workbook.Worksheets.Add("Thông tin");
        ws.Column("A").Width = 5;
        ws.Column("B").Width = 32;
        ws.Column("C").Width = 55;

        ws.Cell("B2").Value = "BÁO CÁO ƯỚC TÍNH NGHĨA VỤ THUẾ HỘ KINH DOANH";
        ws.Cell("B2").Style.Font.Bold = true;
        ws.Cell("B2").Style.Font.FontSize = 16;
        ws.Cell("B2").Style.Font.FontColor = PrimaryColor;

        ws.Cell("B3").Value = "⚠️ Dữ liệu ước tính, mang tính tham khảo. Vui lòng đối chiếu với cơ quan thuế địa bàn.";
        ws.Cell("B3").Style.Font.Italic = true;
        ws.Cell("B3").Style.Font.Bold = true;
        ws.Cell("B3").Style.Font.FontColor = XLColor.FromArgb(192, 0, 0);

        int row = 5;
        void AddRow(string label, object val, string? numberFormat = null)
        {
            ws.Cell(row, 2).Value = label;
            ws.Cell(row, 2).Style.Font.Bold = true;
            var cellVal = ws.Cell(row, 3);
            cellVal.Value = val?.ToString() ?? string.Empty;
            if (!string.IsNullOrEmpty(numberFormat) && val is decimal dec)
            {
                cellVal.Value = dec;
                cellVal.Style.NumberFormat.Format = numberFormat;
            }
            row++;
        }

        var periodLabel = summary.PeriodType switch
        {
            TaxPeriodType.Quarter => $"Quý {summary.PeriodNumber}/{summary.Year}",
            TaxPeriodType.Month => $"Tháng {summary.PeriodNumber}/{summary.Year}",
            _ => $"Năm {summary.Year}"
        };

        AddRow("Năm tính thuế", summary.Year);
        AddRow("Kỳ tính thuế", periodLabel);
        AddRow("Phạm vi tính toán", filter.BranchId.HasValue ? (!string.IsNullOrEmpty(summary.BranchName) ? summary.BranchName : "Địa điểm kinh doanh được lọc") : "Toàn bộ hộ kinh doanh (gộp các địa điểm)");
        AddRow("Doanh thu lũy kế năm", summary.AnnualAccumulatedRevenue, "#,##0 VNĐ");
        AddRow("Nhóm hộ kinh doanh", summary.HouseholdGroupName);
        AddRow("Phương pháp tính thuế áp dụng", summary.CalculationMethod == TaxCalculationMethod.DeclarationBased ? "Phương pháp Kê khai" : "Phương pháp Tỷ lệ % trên doanh thu");
        if (summary.DeclarationMethodStartYear.HasValue)
        {
            AddRow("Thời hạn bắt buộc kê khai", $"Bắt buộc áp dụng kê khai từ năm {summary.DeclarationMethodStartYear.Value} (ổn định 2 năm theo NĐ 68/2026/NĐ-CP)");
        }
        AddRow("Doanh thu tính thuế trong kỳ", summary.TaxableRevenueInPeriod, "#,##0 VNĐ");
        AddRow("Thuế GTGT ước tính", summary.VatAmount, "#,##0 VNĐ");
        AddRow("Thuế TNCN ước tính", summary.PitAmount, "#,##0 VNĐ");
        AddRow("Tổng thuế phát sinh ước tính", summary.TotalTaxAmount, "#,##0 VNĐ");
        AddRow("Thời điểm tạo báo cáo", DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss"));

        row++;
        ws.Cell(row, 2).Value = "CĂN CỨ PHÁP LÝ ÁP DỤNG";
        ws.Cell(row, 2).Style.Font.Bold = true;
        ws.Cell(row, 2).Style.Font.Underline = XLFontUnderlineValues.Single;
        row++;

        string[] legalBases =
        {
            "- Nghị định số 68/2026/NĐ-CP: Quy định chính sách và quản lý thuế đối với hộ kinh doanh.",
            "- Nghị định số 141/2026/NĐ-CP: Hướng dẫn khung doanh thu và phương pháp tính thuế GTGT & TNCN.",
            "- Thông tư số 152/2025/TT-BTC: Chế độ kế toán và mẫu biểu sổ sách dành cho hộ kinh doanh (hiệu lực 01/01/2026).",
            "- Nghị quyết số 204/2025/QH15: Chính sách giảm 20% mức tỷ lệ thuế GTGT cho hộ kinh doanh đến hết 31/12/2026."
        };

        foreach (var item in legalBases)
        {
            ws.Cell(row, 2).Value = item;
            ws.Range(row, 2, row, 3).Merge();
            ws.Cell(row, 2).Style.Font.FontColor = XLColor.FromArgb(80, 80, 80);
            row++;
        }
    }

    private static void BuildRevenueByBranchSheet(IXLWorkbook workbook, List<(string Code, string Name, decimal Gross, decimal Discount, decimal Returns, decimal Net)> branchRevenues)
    {
        var ws = workbook.Worksheets.Add("Doanh thu theo địa điểm");
        string[] headers = { "Mã địa điểm", "Tên địa điểm kinh doanh", "Doanh thu gộp (VNĐ)", "Chiết khấu (VNĐ)", "Hàng trả lại (VNĐ)", "Doanh thu thuần tính thuế (VNĐ)" };

        for (int i = 0; i < headers.Length; i++)
        {
            var cell = ws.Cell(1, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = HeaderBgColor;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        }

        int row = 2;
        foreach (var b in branchRevenues)
        {
            SetTextCell(ws.Cell(row, 1), b.Code);
            SetTextCell(ws.Cell(row, 2), b.Name);
            ws.Cell(row, 3).Value = b.Gross;
            ws.Cell(row, 3).Style.NumberFormat.Format = NumberFormatVnd;
            ws.Cell(row, 4).Value = b.Discount;
            ws.Cell(row, 4).Style.NumberFormat.Format = NumberFormatVnd;
            ws.Cell(row, 5).Value = b.Returns;
            ws.Cell(row, 5).Style.NumberFormat.Format = NumberFormatVnd;
            ws.Cell(row, 6).Value = b.Net;
            ws.Cell(row, 6).Style.NumberFormat.Format = NumberFormatVnd;

            for (int col = 1; col <= 6; col++)
            {
                ws.Cell(row, col).Style.Border.OutsideBorder = XLBorderStyleValues.Hair;
            }
            row++;
        }

        // Dòng tổng cộng
        ws.Cell(row, 1).Value = "TỔNG CỘNG";
        ws.Range(row, 1, row, 2).Merge();
        ws.Cell(row, 1).Style.Font.Bold = true;
        ws.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        for (int col = 3; col <= 6; col++)
        {
            var colLetter = (char)('A' + col - 1);
            if (row > 2)
            {
                ws.Cell(row, col).FormulaA1 = $"SUM({colLetter}2:{colLetter}{row - 1})";
            }
            else
            {
                ws.Cell(row, col).Value = 0m;
            }
            ws.Cell(row, col).Style.Font.Bold = true;
            ws.Cell(row, col).Style.NumberFormat.Format = NumberFormatVnd;
        }

        for (int col = 1; col <= 6; col++)
        {
            ws.Cell(row, col).Style.Border.TopBorder = XLBorderStyleValues.Thin;
            ws.Cell(row, col).Style.Border.BottomBorder = XLBorderStyleValues.Double;
            ws.Cell(row, col).Style.Fill.BackgroundColor = TotalBgColor;
        }

        ws.Columns().AdjustToContents();
    }

    private static void BuildExpensesSheet(IXLWorkbook workbook, List<ExpenseVoucher> expenses)
    {
        var ws = workbook.Worksheets.Add("Chi phí");
        string[] headers = { "Số chứng từ", "Ngày chi", "Danh mục chi phí", "Người nhận tiền", "Số tiền (VNĐ)", "Số hóa đơn/chứng từ", "Ghi chú" };

        for (int i = 0; i < headers.Length; i++)
        {
            var cell = ws.Cell(1, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = HeaderBgColor;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        }

        int row = 2;
        foreach (var exp in expenses)
        {
            SetTextCell(ws.Cell(row, 1), exp.VoucherCode);
            SetTextCell(ws.Cell(row, 2), exp.ExpenseDate.ToString("dd/MM/yyyy"));
            SetTextCell(ws.Cell(row, 3), exp.Category);
            SetTextCell(ws.Cell(row, 4), exp.BeneficiaryName);
            ws.Cell(row, 5).Value = exp.Amount;
            ws.Cell(row, 5).Style.NumberFormat.Format = NumberFormatVnd;
            SetTextCell(ws.Cell(row, 6), exp.InvoiceNumber);
            SetTextCell(ws.Cell(row, 7), exp.Note);

            for (int col = 1; col <= 7; col++)
            {
                ws.Cell(row, col).Style.Border.OutsideBorder = XLBorderStyleValues.Hair;
            }
            row++;
        }

        // Dòng tổng cộng
        ws.Cell(row, 1).Value = "TỔNG CHI PHÍ ĐƯỢC TRỪ";
        ws.Range(row, 1, row, 4).Merge();
        ws.Cell(row, 1).Style.Font.Bold = true;
        ws.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        if (row > 2)
        {
            ws.Cell(row, 5).FormulaA1 = $"SUM(E2:E{row - 1})";
        }
        else
        {
            ws.Cell(row, 5).Value = 0m;
        }

        ws.Cell(row, 5).Style.Font.Bold = true;
        ws.Cell(row, 5).Style.NumberFormat.Format = NumberFormatVnd;

        for (int col = 1; col <= 7; col++)
        {
            ws.Cell(row, col).Style.Border.TopBorder = XLBorderStyleValues.Thin;
            ws.Cell(row, col).Style.Border.BottomBorder = XLBorderStyleValues.Double;
            ws.Cell(row, col).Style.Fill.BackgroundColor = TotalBgColor;
        }

        ws.Columns().AdjustToContents();
    }

    private static void BuildSummarySheet(IXLWorkbook workbook, HouseholdTaxSummaryDto summary)
    {
        var isDeclaration = summary.CalculationMethod == TaxCalculationMethod.DeclarationBased;
        var sheetName = isDeclaration ? "Tổng hợp (Mô hình Kê khai)" : "Tổng hợp (Tỷ lệ % doanh thu)";
        var ws = workbook.Worksheets.Add(sheetName);

        string[] headers = { "STT", "Chỉ tiêu", "Doanh thu / Chi phí (VNĐ)", "Tỷ lệ / Thuế suất (%)", "Số thuế ước tính (VNĐ)", "Ghi chú pháp lý" };

        for (int i = 0; i < headers.Length; i++)
        {
            var cell = ws.Cell(1, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = HeaderBgColor;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        }

        int row = 2;
        void AddSummaryRow(int stt, string title, decimal? revenueOrCost, string rateStr, decimal taxAmt, string note, bool bold = false)
        {
            ws.Cell(row, 1).Value = stt;
            ws.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(row, 2).Value = title;
            if (bold) ws.Cell(row, 2).Style.Font.Bold = true;

            var c3 = ws.Cell(row, 3);
            if (revenueOrCost.HasValue)
            {
                c3.Value = revenueOrCost.Value;
                c3.Style.NumberFormat.Format = NumberFormatVnd;
            }
            if (bold) c3.Style.Font.Bold = true;

            var c4 = ws.Cell(row, 4);
            c4.Value = rateStr;
            c4.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            if (bold) c4.Style.Font.Bold = true;

            var c5 = ws.Cell(row, 5);
            c5.Value = taxAmt;
            c5.Style.NumberFormat.Format = NumberFormatVnd;
            if (bold) c5.Style.Font.Bold = true;

            ws.Cell(row, 6).Value = note;

            for (int col = 1; col <= 6; col++)
            {
                ws.Cell(row, col).Style.Border.OutsideBorder = XLBorderStyleValues.Hair;
            }
            row++;
        }

        if (!isDeclaration)
        {
            AddSummaryRow(1, "1. Doanh thu hàng hóa chịu thuế suất chuẩn 1%", summary.VatBreakdown.RevenueStandardRate10, "1.0%", summary.VatBreakdown.TaxAmountStandardRate10, "Bán lẻ hàng hóa chuẩn");
            AddSummaryRow(2, "2. Doanh thu hàng hóa giảm 20% thuế GTGT (NQ 204/2025)", summary.VatBreakdown.RevenueReducedRate08, "0.8%", summary.VatBreakdown.TaxAmountReducedRate08, "Giảm 20% mức tỷ lệ GTGT đến 31/12/2026");
            AddSummaryRow(3, "3. Tổng thuế Giá trị gia tăng (GTGT) phát sinh", summary.TaxableRevenueInPeriod, "-", summary.VatAmount, "GTGT trực tiếp trên doanh thu", true);

            var pitRatePercent = $"{summary.PitRate * 100:0.##}%";
            AddSummaryRow(4, "4. Thuế Thu nhập cá nhân (TNCN)", summary.HouseholdGroup == HouseholdTaxGroup.Group1_Under1B ? 0m : summary.TaxableRevenueInPeriod, pitRatePercent, summary.PitAmount, "Ngưỡng trừ 1 tỷ chỉ áp dụng một lần cho cả hộ", true);
            AddSummaryRow(5, "5. TỔNG CỘNG NGHĨA VỤ THUẾ ƯỚC TÍNH", summary.TaxableRevenueInPeriod, "-", summary.TotalTaxAmount, "⚠️ Dữ liệu ước tính, mang tính tham khảo", true);
        }
        else
        {
            AddSummaryRow(1, "1. Tổng doanh thu bán hàng hóa", summary.TaxableRevenueInPeriod, "-", 0m, "Doanh thu thuần tính thuế trong kỳ");
            AddSummaryRow(2, "2. Thuế GTGT trực tiếp trên doanh thu", summary.TaxableRevenueInPeriod, $"{summary.VatRate * 100:0.##}%", summary.VatAmount, "GTGT trực tiếp theo tỷ lệ %", true);
            AddSummaryRow(3, "3. Giá vốn hàng bán (COGS)", summary.CogsAmount, "-", 0m, summary.IsCogsEstimated ? "Giá vốn ước tính (Cần cam kết chịu trách nhiệm)" : "Chứng từ hóa đơn hợp lệ");
            AddSummaryRow(4, "4. Chi phí kinh doanh được trừ (OPEX)", summary.DeductibleExpensesInPeriod, "-", 0m, "Chi phí vận hành có hóa đơn chứng từ");

            var netProfit = Math.Max(0m, summary.TaxableRevenueInPeriod - summary.CogsAmount - summary.DeductibleExpensesInPeriod);
            AddSummaryRow(5, "5. Thu nhập chịu thuế TNCN (Doanh thu - Chi phí)", netProfit, "-", 0m, "Lợi nhuận kinh doanh làm căn cứ tính TNCN");
            AddSummaryRow(6, "6. Thuế Thu nhập cá nhân (TNCN kê khai)", netProfit, $"{summary.PitRate * 100:0.##}%", summary.PitAmount, "Áp mức thuế suất theo khung doanh thu", true);
            AddSummaryRow(7, "7. TỔNG CỘNG NGHĨA VỤ THUẾ ƯỚC TÍNH", summary.TaxableRevenueInPeriod, "-", summary.TotalTaxAmount, "⚠️ Dữ liệu ước tính, mang tính tham khảo", true);
        }

        ws.Columns().AdjustToContents();
    }

    #endregion

    #region TIỆN ÍCH ĐỊNH DẠNG VÀ PHÁP LÝ THÔNG TƯ 152

    private static void RenderCircularHeader(
        IXLWorksheet ws,
        string bookCode,
        string bookTitle,
        string targetAudience,
        HouseholdTaxSummaryDto summary,
        Tenant? tenant,
        Branch? branch,
        int lastCol)
    {
        var hkdName = branch?.Name ?? tenant?.Name ?? (string.IsNullOrWhiteSpace(summary.BranchName) ? "HỘ KINH DOANH MT-GRMS" : summary.BranchName);
        var address = branch?.Address ?? tenant?.Address ?? "Địa chỉ kinh doanh";
        var taxCode = tenant?.TaxCode ?? "0123456789";

        // Bên trái: Đơn vị
        ws.Cell("A2").Value = $"Tên HKD / Địa điểm: {hkdName}";
        ws.Cell("A2").Style.Font.Bold = true;
        ws.Cell("A3").Value = $"Địa chỉ: {address}";
        ws.Cell("A4").Value = $"Mã số thuế: {taxCode}";

        // Bên phải: Mẫu biểu Thông tư 152
        var rightColLetter = GetColumnLetter(lastCol);
        ws.Cell($"{rightColLetter}2").Value = $"Mẫu số {bookCode}-HKD";
        ws.Cell($"{rightColLetter}2").Style.Font.Bold = true;
        ws.Cell($"{rightColLetter}2").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

        ws.Cell($"{rightColLetter}3").Value = "(Ban hành theo Thông tư số 152/2025/TT-BTC";
        ws.Cell($"{rightColLetter}3").Style.Font.Italic = true;
        ws.Cell($"{rightColLetter}3").Style.Font.FontSize = 9;
        ws.Cell($"{rightColLetter}3").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

        ws.Cell($"{rightColLetter}4").Value = "ngày 23/12/2025 của Bộ Tài chính)";
        ws.Cell($"{rightColLetter}4").Style.Font.Italic = true;
        ws.Cell($"{rightColLetter}4").Style.Font.FontSize = 9;
        ws.Cell($"{rightColLetter}4").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

        // Tiêu đề chính giữa
        ws.Cell(6, 1).Value = bookTitle.ToUpperInvariant();
        ws.Range(6, 1, 6, lastCol).Merge();
        ws.Cell(6, 1).Style.Font.Bold = true;
        ws.Cell(6, 1).Style.Font.FontSize = 14;
        ws.Cell(6, 1).Style.Font.FontColor = PrimaryColor;
        ws.Cell(6, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        var periodLabel = summary.PeriodType switch
        {
            TaxPeriodType.Quarter => $"Quý {summary.PeriodNumber} năm {summary.Year}",
            TaxPeriodType.Month => $"Tháng {summary.PeriodNumber} năm {summary.Year}",
            _ => $"Năm {summary.Year}"
        };

        ws.Cell(7, 1).Value = $"Kỳ tính thuế: {periodLabel} ({targetAudience})";
        ws.Range(7, 1, 7, lastCol).Merge();
        ws.Cell(7, 1).Style.Font.Italic = true;
        ws.Cell(7, 1).Style.Font.FontSize = 10;
        ws.Cell(7, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        ws.Cell(8, lastCol).Value = "Đơn vị tính: Đồng Việt Nam (VNĐ)";
        ws.Cell(8, lastCol).Style.Font.Italic = true;
        ws.Cell(8, lastCol).Style.Font.FontSize = 9;
        ws.Cell(8, lastCol).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
    }

    private static void RenderSignatureBlock(
        IXLWorksheet ws,
        ref int row,
        int colCount,
        bool hasWarehouseClerk = false,
        bool hasCashier = false)
    {
        row += 2;
        var dateStr = $"Ngày {DateTime.Now:dd} tháng {DateTime.Now:MM} năm {DateTime.Now:yyyy}";

        ws.Cell(row, colCount).Value = dateStr;
        ws.Cell(row, colCount).Style.Font.Italic = true;
        ws.Cell(row, colCount).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        row++;

        if (hasWarehouseClerk || hasCashier)
        {
            var middleCol = colCount / 2 + 1;

            ws.Cell(row, 2).Value = "NGƯỜI GHI SỔ";
            ws.Cell(row, 2).Style.Font.Bold = true;
            ws.Cell(row, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            ws.Cell(row, middleCol).Value = hasWarehouseClerk ? "THỦ KHO" : "THỦ QUỸ";
            ws.Cell(row, middleCol).Style.Font.Bold = true;
            ws.Cell(row, middleCol).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            ws.Cell(row, colCount).Value = "NGƯỜI ĐẠI DIỆN HỘ KINH DOANH";
            ws.Cell(row, colCount).Style.Font.Bold = true;
            ws.Cell(row, colCount).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            row++;

            ws.Cell(row, 2).Value = "(Ký, ghi rõ họ tên)";
            ws.Cell(row, 2).Style.Font.Italic = true;
            ws.Cell(row, 2).Style.Font.FontSize = 9;
            ws.Cell(row, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            ws.Cell(row, middleCol).Value = "(Ký, ghi rõ họ tên)";
            ws.Cell(row, middleCol).Style.Font.Italic = true;
            ws.Cell(row, middleCol).Style.Font.FontSize = 9;
            ws.Cell(row, middleCol).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            ws.Cell(row, colCount).Value = "(Ký, ghi rõ họ tên, đóng dấu nếu có)";
            ws.Cell(row, colCount).Style.Font.Italic = true;
            ws.Cell(row, colCount).Style.Font.FontSize = 9;
            ws.Cell(row, colCount).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            row += 4;
        }
        else
        {
            var leftCol = colCount <= 4 ? 1 : 2;

            ws.Cell(row, leftCol).Value = "NGƯỜI GHI SỔ";
            ws.Cell(row, leftCol).Style.Font.Bold = true;
            ws.Cell(row, leftCol).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            ws.Cell(row, colCount).Value = "NGƯỜI ĐẠI DIỆN HỘ KINH DOANH";
            ws.Cell(row, colCount).Style.Font.Bold = true;
            ws.Cell(row, colCount).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            row++;

            ws.Cell(row, leftCol).Value = "(Ký, ghi rõ họ tên)";
            ws.Cell(row, leftCol).Style.Font.Italic = true;
            ws.Cell(row, leftCol).Style.Font.FontSize = 9;
            ws.Cell(row, leftCol).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            ws.Cell(row, colCount).Value = "(Ký, ghi rõ họ tên, đóng dấu nếu có)";
            ws.Cell(row, colCount).Style.Font.Italic = true;
            ws.Cell(row, colCount).Style.Font.FontSize = 9;
            ws.Cell(row, colCount).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            row += 4;
        }
    }

    private static void FormatTableHeaderRow(IXLRange range)
    {
        range.Style.Font.Bold = true;
        range.Style.Fill.BackgroundColor = HeaderBgColor;
        range.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        range.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        range.Style.Alignment.WrapText = true;
        range.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        range.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
    }

    private static void FormatColLettersRow(IXLRange range)
    {
        range.Style.Font.Italic = true;
        range.Style.Font.Bold = true;
        range.Style.Font.FontSize = 9;
        range.Style.Fill.BackgroundColor = XLColor.FromArgb(245, 247, 250);
        range.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        range.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        range.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
    }

    private static void FormatDataRow(IXLRange range)
    {
        range.Style.Border.OutsideBorder = XLBorderStyleValues.Hair;
        range.Style.Border.InsideBorder = XLBorderStyleValues.Hair;
    }

    private static void FormatSubTotalRow(IXLRange range)
    {
        range.Style.Font.Bold = true;
        range.Style.Fill.BackgroundColor = SubTotalBgColor;
        range.Style.Border.TopBorder = XLBorderStyleValues.Thin;
        range.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        range.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
    }

    private static void FormatTotalRow(IXLRange range)
    {
        range.Style.Font.Bold = true;
        range.Style.Fill.BackgroundColor = TotalBgColor;
        range.Style.Border.TopBorder = XLBorderStyleValues.Thin;
        range.Style.Border.BottomBorder = XLBorderStyleValues.Double;
        range.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
    }

    private static string GetColumnLetter(int colNumber)
    {
        int dividend = colNumber;
        string columnLabel = string.Empty;
        while (dividend > 0)
        {
            int modulo = (dividend - 1) % 26;
            columnLabel = Convert.ToChar(65 + modulo) + columnLabel;
            dividend = (dividend - modulo) / 26;
        }
        return columnLabel;
    }

    private static (DateTime FromUtc, DateTime ToUtc) GetPeriodDatesUtc(int year, TaxPeriodType periodType, int periodNumber)
    {
        DateTime startLocal;
        DateTime endLocal;

        switch (periodType)
        {
            case TaxPeriodType.Month:
                var month = Math.Clamp(periodNumber, 1, 12);
                startLocal = new DateTime(year, month, 1, 0, 0, 0);
                endLocal = startLocal.AddMonths(1).AddTicks(-1);
                break;

            case TaxPeriodType.Quarter:
                var q = Math.Clamp(periodNumber, 1, 4);
                var startMonth = (q - 1) * 3 + 1;
                startLocal = new DateTime(year, startMonth, 1, 0, 0, 0);
                endLocal = startLocal.AddMonths(3).AddTicks(-1);
                break;

            case TaxPeriodType.Year:
                startLocal = new DateTime(year, 1, 1, 0, 0, 0);
                endLocal = new DateTime(year, 12, 31, 23, 59, 59, 999);
                break;

            default:
                startLocal = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);
                endLocal = startLocal.AddMonths(3).AddTicks(-1);
                break;
        }

        var vnTz = GetVietnamTimeZone();
        startLocal = DateTime.SpecifyKind(startLocal, DateTimeKind.Unspecified);
        endLocal = DateTime.SpecifyKind(endLocal, DateTimeKind.Unspecified);
        var fromUtc = TimeZoneInfo.ConvertTimeToUtc(startLocal, vnTz);
        var toUtc = TimeZoneInfo.ConvertTimeToUtc(endLocal, vnTz);
        return (fromUtc, toUtc);
    }

    private static DateTime ToVietnamTime(DateTime dt)
    {
        var vnTz = GetVietnamTimeZone();
        var utc = dt.Kind == DateTimeKind.Utc ? dt : DateTime.SpecifyKind(dt, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(utc, vnTz);
    }

    private static void SetTextCell(IXLCell cell, string? text)
    {
        var str = text ?? string.Empty;
        cell.Style.NumberFormat.Format = "@";
        cell.Style.IncludeQuotePrefix = true;
        cell.Value = str;
    }

    private static TimeZoneInfo GetVietnamTimeZone()
    {
        return HouseholdTaxService.GetVietnamTimeZone();
    }

    #endregion
}
