using ClosedXML.Excel;
using MT_GRMS.Application.PurchaseOrders.Dispatch;
using MT_GRMS.Domain.Entities.Supplier;

namespace MT_GRMS.Infrastructure.Services;

public class SupplierOrderExcelGenerator : ISupplierOrderExcelGenerator
{
    private static readonly XLColor HeaderBg = XLColor.FromHtml("#0F766E");
    private static readonly XLColor SubtotalBg = XLColor.FromHtml("#CCFBF1");
    private static readonly XLColor TotalBg = XLColor.FromHtml("#FEF3C7");
    private static readonly XLColor SectionBg = XLColor.FromHtml("#F0FDFA");

    public Task<byte[]> GenerateAsync(
        SupplierDispatchPackage package,
        IReadOnlyList<PurchaseOrder> purchaseOrders,
        CancellationToken cancellationToken = default)
    {
        using var workbook = new XLWorkbook();
        BuildSummarySheet(workbook, purchaseOrders);
        foreach (var storeGroup in purchaseOrders
            .GroupBy(p => p.StoreCodeSnapshot ?? p.BranchId.ToString())
            .OrderBy(g => g.Key))
        {
            BuildStoreSheet(workbook, package, storeGroup.ToList());
        }

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return Task.FromResult(ms.ToArray());
    }

    private static void BuildSummarySheet(IXLWorkbook workbook, IReadOnlyList<PurchaseOrder> purchaseOrders)
    {
        var ws = workbook.Worksheets.Add("TONG_HOP");
        var headers = new[]
        {
            "STT", "Mã PO", "Revision", "Mã cửa hàng", "Tên cửa hàng", "Ngày giao",
            "Mã sản phẩm", "Tên sản phẩm", "Đơn vị đặt hàng", "Số lượng đặt", "Đơn giá", "Thành tiền"
        };

        for (var i = 0; i < headers.Length; i++)
        {
            var cell = ws.Cell(1, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = HeaderBg;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        var rows = purchaseOrders
            .SelectMany(po => (po.Details ?? Array.Empty<PurchaseOrderDetail>())
                .Where(d => !d.IsDeleted)
                .Select(d => new
            {
                PoCode = po.Code,
                Revision = po.RevisionNumber <= 0 ? 1 : po.RevisionNumber,
                StoreCode = po.StoreCodeSnapshot ?? string.Empty,
                StoreName = po.StoreNameSnapshot ?? string.Empty,
                DeliveryDate = d.DeliveryDate ?? po.DeliveryDate,
                ProductCode = d.ProductCodeSnapshot ?? string.Empty,
                ProductName = d.ProductNameSnapshot ?? string.Empty,
                Unit = d.PurchaseUnitNameSnapshot ?? d.DeliveryUoM,
                Qty = d.OrderedQty ?? d.FinalQty ?? 0m,
                UnitPrice = d.UnitPrice,
                LineTotal = d.LineAmount
            }))
            .OrderBy(r => r.StoreCode)
            .ThenBy(r => r.DeliveryDate)
            .ThenBy(r => r.ProductCode)
            .ToList();

        var row = 2;
        var stt = 1;
        string? currentStore = null;
        decimal storeSubtotal = 0m;
        decimal grandTotal = 0m;

        void WriteStoreSubtotal()
        {
            if (currentStore == null) return;
            ws.Cell(row, 1).Value = "";
            ws.Range(row, 1, row, 11).Merge();
            ws.Cell(row, 1).Value = $"Cộng cửa hàng {currentStore}";
            ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Cell(row, 12).Value = storeSubtotal;
            ws.Cell(row, 12).Style.NumberFormat.Format = "#,##0";
            ws.Cell(row, 12).Style.Font.Bold = true;
            ws.Range(row, 1, row, 12).Style.Fill.BackgroundColor = SubtotalBg;
            row++;
            storeSubtotal = 0m;
        }

        foreach (var r in rows)
        {
            if (currentStore != null && !string.Equals(currentStore, r.StoreCode, StringComparison.Ordinal))
            {
                WriteStoreSubtotal();
            }

            currentStore = r.StoreCode;
            ws.Cell(row, 1).Value = stt++;
            ws.Cell(row, 2).Value = r.PoCode;
            ws.Cell(row, 3).Value = r.Revision;
            ws.Cell(row, 4).Value = r.StoreCode;
            ws.Cell(row, 5).Value = r.StoreName;
            if (r.DeliveryDate.HasValue)
            {
                ws.Cell(row, 6).Value = r.DeliveryDate.Value;
                ws.Cell(row, 6).Style.DateFormat.Format = "dd/MM/yyyy";
            }

            ws.Cell(row, 7).Value = r.ProductCode;
            ws.Cell(row, 8).Value = r.ProductName;
            ws.Cell(row, 9).Value = r.Unit;
            ws.Cell(row, 10).Value = r.Qty;
            ws.Cell(row, 10).Style.NumberFormat.Format = "#,##0.###";
            ws.Cell(row, 10).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            ws.Cell(row, 11).Value = r.UnitPrice;
            ws.Cell(row, 11).Style.NumberFormat.Format = "#,##0";
            ws.Cell(row, 11).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            ws.Cell(row, 12).Value = r.LineTotal;
            ws.Cell(row, 12).Style.NumberFormat.Format = "#,##0";
            ws.Cell(row, 12).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

            storeSubtotal += r.LineTotal;
            grandTotal += r.LineTotal;
            row++;
        }

        WriteStoreSubtotal();

        ws.Range(row, 1, row, 11).Merge();
        ws.Cell(row, 1).Value = "TỔNG GIÁ TRỊ TOÀN BỘ ĐƠN HÀNG";
        ws.Cell(row, 1).Style.Font.Bold = true;
        ws.Cell(row, 12).Value = grandTotal;
        ws.Cell(row, 12).Style.NumberFormat.Format = "#,##0 \"đ\"";
        ws.Cell(row, 12).Style.Font.Bold = true;
        ws.Range(row, 1, row, 12).Style.Fill.BackgroundColor = TotalBg;

        if (rows.Count > 0)
        {
            var filterEnd = Math.Max(1, rows.Count + 1);
            ws.Range(1, 1, filterEnd, 12).SetAutoFilter();
        }

        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents(1, 40);
        ws.RangeUsed()?.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
        ws.Range(1, 1, Math.Max(1, rows.Count + 1), 12).Style.Border.SetInsideBorder(XLBorderStyleValues.Thin);
    }

    private static void BuildStoreSheet(
        IXLWorkbook workbook,
        SupplierDispatchPackage package,
        IReadOnlyList<PurchaseOrder> storePos)
    {
        var first = storePos[0];
        var sheetName = MakeUniqueSheetName(
            workbook,
            first.StoreCodeSnapshot ?? first.StoreNameSnapshot ?? "CH");

        var ws = workbook.Worksheets.Add(sheetName);
        var r = 1;
        ws.Cell(r, 1).Value = "Thông tin đơn đặt hàng NCC";
        ws.Cell(r, 1).Style.Font.Bold = true;
        ws.Cell(r, 1).Style.Font.FontSize = 14;
        r += 2;

        void Info(string label, string? value)
        {
            ws.Cell(r, 1).Value = label;
            ws.Cell(r, 1).Style.Font.Bold = true;
            ws.Cell(r, 2).Value = value ?? string.Empty;
            r++;
        }

        Info("Mã NCC", package.SupplierCodeSnapshot);
        Info("Tên NCC", package.SupplierNameSnapshot);
        Info("Mã cửa hàng", first.StoreCodeSnapshot);
        Info("Tên cửa hàng", first.StoreNameSnapshot);
        Info("Địa chỉ", first.StoreAddressSnapshot);
        Info("Điện thoại", first.StorePhoneSnapshot);
        Info("Ngày tạo đơn", DateTime.UtcNow.AddHours(7).ToString("dd/MM/yyyy HH:mm"));
        Info("Tổng số PO", storePos.Count.ToString());
        r++;

        foreach (var po in storePos.OrderBy(p => p.DeliveryDate).ThenBy(p => p.Code))
        {
            ws.Range(r, 1, r, 7).Merge();
            ws.Cell(r, 1).Value = $"PO: {po.Code}    Ngày giao: {(po.DeliveryDate?.ToString("dd/MM/yyyy") ?? "—")}";
            ws.Cell(r, 1).Style.Font.Bold = true;
            ws.Cell(r, 1).Style.Fill.BackgroundColor = SectionBg;
            r++;

            var headers = new[] { "STT", "Mã SP", "Tên sản phẩm", "ĐVT đặt", "SL đặt", "Đơn giá", "Thành tiền" };
            for (var i = 0; i < headers.Length; i++)
            {
                var cell = ws.Cell(r, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Fill.BackgroundColor = HeaderBg;
            }

            r++;
            var stt = 1;
            decimal poTotal = 0m;
            foreach (var d in (po.Details ?? Array.Empty<PurchaseOrderDetail>())
                .OrderBy(x => x.ProductCodeSnapshot))
            {
                var qty = d.OrderedQty ?? d.FinalQty ?? 0m;
                ws.Cell(r, 1).Value = stt++;
                ws.Cell(r, 2).Value = d.ProductCodeSnapshot ?? string.Empty;
                ws.Cell(r, 3).Value = d.ProductNameSnapshot ?? string.Empty;
                ws.Cell(r, 4).Value = d.PurchaseUnitNameSnapshot ?? d.DeliveryUoM;
                ws.Cell(r, 5).Value = qty;
                ws.Cell(r, 5).Style.NumberFormat.Format = "#,##0.###";
                ws.Cell(r, 5).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                ws.Cell(r, 6).Value = d.UnitPrice;
                ws.Cell(r, 6).Style.NumberFormat.Format = "#,##0";
                ws.Cell(r, 6).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                ws.Cell(r, 7).Value = d.LineAmount;
                ws.Cell(r, 7).Style.NumberFormat.Format = "#,##0";
                ws.Cell(r, 7).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                poTotal += d.LineAmount;
                r++;
            }

            ws.Range(r, 1, r, 6).Merge();
            ws.Cell(r, 1).Value = "Tổng tiền PO";
            ws.Cell(r, 1).Style.Font.Bold = true;
            ws.Cell(r, 7).Value = poTotal;
            ws.Cell(r, 7).Style.NumberFormat.Format = "#,##0 \"đ\"";
            ws.Cell(r, 7).Style.Font.Bold = true;
            ws.Range(r, 1, r, 7).Style.Fill.BackgroundColor = TotalBg;
            r += 2;
        }

        ws.Columns().AdjustToContents(1, 45);
        ws.SheetView.FreezeRows(1);
    }

    private static string MakeUniqueSheetName(IXLWorkbook workbook, string raw)
    {
        var invalid = new HashSet<char>(Path.GetInvalidFileNameChars().Concat(new[] { ':', '\\', '/', '?', '*', '[', ']' }));
        var cleaned = new string(raw.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        if (string.IsNullOrWhiteSpace(cleaned)) cleaned = "CH";
        if (cleaned.Length > 28) cleaned = cleaned[..28];

        var name = cleaned;
        var i = 1;
        while (workbook.Worksheets.Any(w => string.Equals(w.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            var suffix = $"_{i++}";
            name = cleaned.Length + suffix.Length > 31
                ? cleaned[..(31 - suffix.Length)] + suffix
                : cleaned + suffix;
        }

        return name;
    }
}
