using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Tax;
using MT_GRMS.Application.Tax.Dtos;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Infrastructure.Persistence;

namespace MT_GRMS.Infrastructure.Services;

/// <summary>
/// Service xuất báo cáo thuế hộ kinh doanh dạng bảng tính Excel 4 sheet phẳng.
/// </summary>
public class TaxExportService : ITaxExportService
{
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

    public async Task<byte[]> ExportTaxSummaryToExcelAsync(HouseholdTaxFilterDto filter, CancellationToken cancellationToken = default)
    {
        var summary = await _taxService.GetTaxSummaryAsync(filter, cancellationToken);
        var (fromDateUtc, toDateUtc) = GetPeriodDatesUtc(filter.Year, filter.PeriodType, filter.PeriodNumber);
        var deductibleExpenses = await _taxRepository.GetDeductibleExpenseVouchersAsync(fromDateUtc, toDateUtc, filter.BranchId, cancellationToken);

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

        // Nếu không có chi nhánh nào được lưu trong DB, dùng số liệu tổng hợp trong kỳ
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

        using var workbook = new XLWorkbook();

        // 1. Sheet Thông tin
        BuildInfoSheet(workbook, summary, filter);

        // 2. Sheet Doanh thu theo địa điểm
        BuildRevenueByBranchSheet(workbook, branchRevenues);

        // 3. Sheet Chi phí
        BuildExpensesSheet(workbook, deductibleExpenses);

        // 4. Sheet Tổng hợp (gần mẫu S2a hoặc S2b-S2c)
        BuildSummarySheet(workbook, summary);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void BuildInfoSheet(IXLWorkbook workbook, HouseholdTaxSummaryDto summary, HouseholdTaxFilterDto filter)
    {
        var ws = workbook.Worksheets.Add("Thông tin");
        ws.Column("A").Width = 5;
        ws.Column("B").Width = 32;
        ws.Column("C").Width = 55;

        ws.Cell("B2").Value = "BÁO CÁO ƯỚC TÍNH NGHĨA VỤ THUẾ HỘ KINH DOANH";
        ws.Cell("B2").Style.Font.Bold = true;
        ws.Cell("B2").Style.Font.FontSize = 16;
        ws.Cell("B2").Style.Font.FontColor = XLColor.FromArgb(24, 43, 73);

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
            "- Thông tư số 152/2025/TT-BTC: Chế độ kế toán và mẫu biểu sổ sách dành cho hộ kinh doanh.",
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
            cell.Style.Fill.BackgroundColor = XLColor.FromArgb(240, 240, 240);
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        }

        int row = 2;
        foreach (var b in branchRevenues)
        {
            SetTextCell(ws.Cell(row, 1), b.Code);
            SetTextCell(ws.Cell(row, 2), b.Name);
            ws.Cell(row, 3).Value = b.Gross;
            ws.Cell(row, 3).Style.NumberFormat.Format = "#,##0";
            ws.Cell(row, 4).Value = b.Discount;
            ws.Cell(row, 4).Style.NumberFormat.Format = "#,##0";
            ws.Cell(row, 5).Value = b.Returns;
            ws.Cell(row, 5).Style.NumberFormat.Format = "#,##0";
            ws.Cell(row, 6).Value = b.Net;
            ws.Cell(row, 6).Style.NumberFormat.Format = "#,##0";

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
            ws.Cell(row, col).Style.NumberFormat.Format = "#,##0";
        }

        for (int col = 1; col <= 6; col++)
        {
            ws.Cell(row, col).Style.Border.TopBorder = XLBorderStyleValues.Thin;
            ws.Cell(row, col).Style.Border.BottomBorder = XLBorderStyleValues.Double;
            ws.Cell(row, col).Style.Fill.BackgroundColor = XLColor.FromArgb(248, 248, 248);
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
            cell.Style.Fill.BackgroundColor = XLColor.FromArgb(240, 240, 240);
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
            ws.Cell(row, 5).Style.NumberFormat.Format = "#,##0";
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
        ws.Cell(row, 5).Style.NumberFormat.Format = "#,##0";

        for (int col = 1; col <= 7; col++)
        {
            ws.Cell(row, col).Style.Border.TopBorder = XLBorderStyleValues.Thin;
            ws.Cell(row, col).Style.Border.BottomBorder = XLBorderStyleValues.Double;
            ws.Cell(row, col).Style.Fill.BackgroundColor = XLColor.FromArgb(248, 248, 248);
        }

        ws.Columns().AdjustToContents();
    }

    private static void BuildSummarySheet(IXLWorkbook workbook, HouseholdTaxSummaryDto summary)
    {
        var isDeclaration = summary.CalculationMethod == TaxCalculationMethod.DeclarationBased;
        var sheetName = isDeclaration ? "Tổng hợp (gần mẫu S2b-S2c)" : "Tổng hợp (gần mẫu S2a)";
        var ws = workbook.Worksheets.Add(sheetName);

        string[] headers = { "STT", "Chỉ tiêu", "Doanh thu / Chi phí (VNĐ)", "Tỷ lệ / Thuế suất (%)", "Số thuế ước tính (VNĐ)", "Ghi chú pháp lý" };

        for (int i = 0; i < headers.Length; i++)
        {
            var cell = ws.Cell(1, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromArgb(240, 240, 240);
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
                c3.Style.NumberFormat.Format = "#,##0";
            }
            if (bold) c3.Style.Font.Bold = true;

            var c4 = ws.Cell(row, 4);
            c4.Value = rateStr;
            c4.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            if (bold) c4.Style.Font.Bold = true;

            var c5 = ws.Cell(row, 5);
            c5.Value = taxAmt;
            c5.Style.NumberFormat.Format = "#,##0";
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
            // Mẫu S2a: Tỷ lệ % trên doanh thu
            AddSummaryRow(1, "1. Doanh thu hàng hóa chịu thuế suất chuẩn 1%", summary.VatBreakdown.RevenueStandardRate10, "1.0%", summary.VatBreakdown.TaxAmountStandardRate10, "Bán lẻ hàng hóa chuẩn");
            AddSummaryRow(2, "2. Doanh thu hàng hóa giảm 20% thuế GTGT (NQ 204/2025)", summary.VatBreakdown.RevenueReducedRate08, "0.8%", summary.VatBreakdown.TaxAmountReducedRate08, "Giảm 20% mức tỷ lệ GTGT đến 31/12/2026");
            AddSummaryRow(3, "3. Tổng thuế Giá trị gia tăng (GTGT) phát sinh", summary.TaxableRevenueInPeriod, "-", summary.VatAmount, "GTGT trực tiếp trên doanh thu", true);

            var pitRatePercent = $"{summary.PitRate * 100:0.##}%";
            AddSummaryRow(4, "4. Thuế Thu nhập cá nhân (TNCN)", summary.HouseholdGroup == HouseholdTaxGroup.Group1_Under1B ? 0m : summary.TaxableRevenueInPeriod, pitRatePercent, summary.PitAmount, "Ngưỡng trừ 1 tỷ chỉ áp dụng một lần cho cả hộ", true);
            AddSummaryRow(5, "5. TỔNG CỘNG NGHĨA VỤ THUẾ ƯỚC TÍNH", summary.TaxableRevenueInPeriod, "-", summary.TotalTaxAmount, "⚠️ Dữ liệu ước tính, mang tính tham khảo", true);
        }
        else
        {
            // Mẫu S2b-S2c: Phương pháp Kê khai
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
                startLocal = new DateTime(year, 1, 1, 0, 0, 0);
                endLocal = startLocal.AddMonths(3).AddTicks(-1);
                break;
        }

        var vnTz = GetVietnamTimeZone();
        var fromUtc = TimeZoneInfo.ConvertTimeToUtc(startLocal, vnTz);
        var toUtc = TimeZoneInfo.ConvertTimeToUtc(endLocal, vnTz);
        return (fromUtc, toUtc);
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
}
