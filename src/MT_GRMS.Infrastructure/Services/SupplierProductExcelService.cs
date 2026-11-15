using System.Globalization;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Suppliers;
using MT_GRMS.Application.Suppliers.Dtos;
using MT_GRMS.Domain.Entities.Supplier;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Domain.Exceptions;

namespace MT_GRMS.Infrastructure.Services;

public class SupplierProductExcelService : ISupplierProductExcelService
{
    public const string SheetName = "Gia_Nhap_MOQ";

    private static readonly string[] Headers =
    [
        "ProductId",
        "MaNCC",
        "TenNCC",
        "SKU",
        "TenSanPham",
        "DVTNhap",
        "GiaNhapSi",
        "MOQ"
    ];

    private static readonly XLColor HeaderBg = XLColor.FromHtml("#0F766E");
    private static readonly XLColor ReadonlyBg = XLColor.FromHtml("#F1F5F9");
    private static readonly XLColor EditableBg = XLColor.FromHtml("#FEF3C7");

    private readonly ISupplierRepository _supplierRepository;
    private readonly IUnitOfWork _unitOfWork;

    public SupplierProductExcelService(ISupplierRepository supplierRepository, IUnitOfWork unitOfWork)
    {
        _supplierRepository = supplierRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<SupplierProductExcelFileDto> ExportAsync(
        Guid supplierId,
        CancellationToken cancellationToken = default)
    {
        var supplier = await _supplierRepository.GetByIdAsync(supplierId, cancellationToken)
            ?? throw new NotFoundException($"Không tìm thấy nhà cung cấp với ID: {supplierId}");

        var items = await _supplierRepository.GetActiveSupplierProductsForSupplierAsync(supplierId, cancellationToken);
        var ordered = items
            .OrderBy(x => x.Product?.ProductCode ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToList();

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add(SheetName);

        for (var i = 0; i < Headers.Length; i++)
        {
            var cell = ws.Cell(1, i + 1);
            cell.Value = Headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = HeaderBg;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        ws.Cell(2, 1).Value = "KHÔNG SỬA các cột ProductId → DVTNhap. Chỉ điền/sửa GiaNhapSi và MOQ rồi gửi lại để import.";
        ws.Range(2, 1, 2, Headers.Length).Merge();
        ws.Cell(2, 1).Style.Font.Italic = true;
        ws.Cell(2, 1).Style.Font.FontColor = XLColor.FromHtml("#B45309");
        ws.Cell(2, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#FFFBEB");

        var row = 3;
        foreach (var sp in ordered)
        {
            var productId = sp.ProductId.ToString();
            var sku = sp.Product?.ProductCode ?? string.Empty;
            var name = sp.Product?.Name ?? string.Empty;
            var uom = string.IsNullOrWhiteSpace(sp.DeliveryUoM)
                ? (sp.Product?.BaseUnit ?? "Cái")
                : sp.DeliveryUoM;

            ws.Cell(row, 1).Value = productId;
            ws.Cell(row, 2).Value = supplier.Code;
            ws.Cell(row, 3).Value = supplier.Name;
            ws.Cell(row, 4).Value = sku;
            ws.Cell(row, 5).Value = name;
            ws.Cell(row, 6).Value = uom;
            if (sp.AgreedWholesalePrice.HasValue && sp.AgreedWholesalePrice.Value > 0)
            {
                ws.Cell(row, 7).Value = sp.AgreedWholesalePrice.Value;
            }
            else
            {
                ws.Cell(row, 7).Value = string.Empty;
            }

            ws.Cell(row, 8).Value = sp.MinimumOrderQuantity > 0 ? sp.MinimumOrderQuantity : 1;

            for (var c = 1; c <= 6; c++)
            {
                ws.Cell(row, c).Style.Fill.BackgroundColor = ReadonlyBg;
            }

            ws.Cell(row, 7).Style.Fill.BackgroundColor = EditableBg;
            ws.Cell(row, 8).Style.Fill.BackgroundColor = EditableBg;
            ws.Cell(row, 7).Style.NumberFormat.Format = "#,##0.###";
            ws.Cell(row, 8).Style.NumberFormat.Format = "0";
            row++;
        }

        ws.Column(1).Width = 38;
        ws.Column(2).Width = 18;
        ws.Column(3).Width = 28;
        ws.Column(4).Width = 14;
        ws.Column(5).Width = 36;
        ws.Column(6).Width = 12;
        ws.Column(7).Width = 14;
        ws.Column(8).Width = 10;
        ws.SheetView.FreezeRows(2);

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        var safeCode = Regex.Replace(supplier.Code ?? "NCC", @"[^\w\-]+", "_");
        var fileName = $"NCC_GiaNhap_MOQ_{safeCode}_{DateTime.UtcNow:yyyyMMdd}.xlsx";

        return new SupplierProductExcelFileDto
        {
            Content = ms.ToArray(),
            FileName = fileName,
        };
    }

    public async Task<SupplierProductExcelImportResultDto> ImportAsync(
        Guid supplierId,
        Stream fileStream,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        var supplier = await _supplierRepository.GetByIdAsync(supplierId, cancellationToken)
            ?? throw new NotFoundException($"Không tìm thấy nhà cung cấp với ID: {supplierId}");

        if (fileStream == null || !fileStream.CanRead)
        {
            throw new BusinessException("INVALID_FILE", "File Excel không hợp lệ hoặc rỗng.");
        }

        var ext = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
        if (ext is not (".xlsx" or ".xlsm" or ".xls"))
        {
            throw new BusinessException("INVALID_FORMAT", "Chỉ chấp nhận file Excel (.xlsx).");
        }

        XLWorkbook workbook;
        try
        {
            workbook = new XLWorkbook(fileStream);
        }
        catch (Exception)
        {
            throw new BusinessException("INVALID_FILE", "Không đọc được file Excel. Vui lòng dùng file xuất từ hệ thống.");
        }

        using (workbook)
        {
            var ws = workbook.Worksheets.FirstOrDefault(w =>
                         string.Equals(w.Name, SheetName, StringComparison.OrdinalIgnoreCase))
                     ?? workbook.Worksheets.FirstOrDefault()
                     ?? throw new BusinessException("INVALID_SHEET", "File Excel không có sheet dữ liệu.");

            var headerMap = BuildHeaderMap(ws);
            RequireHeaders(headerMap, "SKU", "GiaNhapSi", "MOQ");

            var assigned = await _supplierRepository.GetActiveSupplierProductsForSupplierAsync(supplierId, cancellationToken);
            var byProductId = assigned.ToDictionary(x => x.ProductId);
            var bySku = assigned
                .Where(x => !string.IsNullOrWhiteSpace(x.Product?.ProductCode))
                .GroupBy(x => x.Product!.ProductCode.Trim().ToUpperInvariant())
                .ToDictionary(g => g.Key, g => g.First());

            var result = new SupplierProductExcelImportResultDto
            {
                FileName = string.IsNullOrWhiteSpace(fileName) ? "import.xlsx" : fileName,
                SupplierId = supplier.Id,
                SupplierCode = supplier.Code,
                SupplierName = supplier.Name,
            };

            var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
            var startRow = DetectDataStartRow(ws, headerMap);

            for (var r = startRow; r <= lastRow; r++)
            {
                var skuRaw = GetCellString(ws, r, headerMap, "SKU");
                var productIdRaw = GetCellString(ws, r, headerMap, "ProductId");
                var nameRaw = GetCellString(ws, r, headerMap, "TenSanPham");
                var priceRaw = GetCellString(ws, r, headerMap, "GiaNhapSi");
                var moqRaw = GetCellString(ws, r, headerMap, "MOQ");

                // Skip blank trailing rows
                if (string.IsNullOrWhiteSpace(skuRaw)
                    && string.IsNullOrWhiteSpace(productIdRaw)
                    && string.IsNullOrWhiteSpace(priceRaw)
                    && string.IsNullOrWhiteSpace(moqRaw))
                {
                    continue;
                }

                var rowResult = new SupplierProductExcelImportRowResultDto
                {
                    Row = r,
                    ProductId = productIdRaw,
                    Sku = skuRaw,
                    ProductName = nameRaw,
                };

                try
                {
                    SupplierProduct? entity = null;
                    if (Guid.TryParse(productIdRaw, out var productId)
                        && byProductId.TryGetValue(productId, out var byId))
                    {
                        entity = byId;
                    }
                    else if (!string.IsNullOrWhiteSpace(skuRaw)
                             && bySku.TryGetValue(skuRaw.Trim().ToUpperInvariant(), out var byCode))
                    {
                        entity = byCode;
                    }

                    if (entity == null)
                    {
                        throw new BusinessException(
                            "PRODUCT_NOT_ASSIGNED",
                            "SKU/ProductId không thuộc danh sách sản phẩm đã gán cho nhà cung cấp này.");
                    }

                    rowResult.Sku = entity.Product?.ProductCode ?? skuRaw;
                    rowResult.ProductName = entity.Product?.Name ?? nameRaw;
                    rowResult.ProductId = entity.ProductId.ToString();

                    if (!TryParseDecimal(priceRaw, out var price))
                    {
                        throw new BusinessException("INVALID_PRICE", "Giá nhập sỉ không hợp lệ (để trống hoặc số ≥ 0).");
                    }

                    if (price < 0m)
                    {
                        throw new BusinessException("INVALID_PRICE", "Giá nhập sỉ không được âm.");
                    }

                    if (!TryParseInt(moqRaw, out var moq) || moq < 1)
                    {
                        throw new BusinessException("INVALID_MOQ", "MOQ phải là số nguyên ≥ 1.");
                    }

                    var normalizedPrice = SupplierProductPricing.NormalizeAgreedPriceToPersist(price);
                    entity.AgreedWholesalePrice = normalizedPrice;
                    entity.MinimumOrderQuantity = moq;

                    rowResult.AgreedWholesalePrice = normalizedPrice;
                    rowResult.Moq = moq;
                    rowResult.Status = "OK";
                    rowResult.Message = normalizedPrice.HasValue
                        ? $"Cập nhật giá {normalizedPrice:N0} đ, MOQ {moq}."
                        : $"Xóa giá thỏa thuận (chưa thỏa thuận), MOQ {moq}.";
                }
                catch (BusinessException bex)
                {
                    rowResult.Status = "ERROR";
                    rowResult.Message = bex.Message;
                }
                catch (Exception ex)
                {
                    rowResult.Status = "ERROR";
                    rowResult.Message = ex.Message;
                }

                result.Rows.Add(rowResult);
            }

            if (result.Rows.Count == 0)
            {
                throw new BusinessException("EMPTY_DATA", "File không có dòng dữ liệu để import.");
            }

            if (result.Rows.Any(x => x.Status == "OK"))
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            result.Summary = new SupplierProductExcelImportSummaryDto
            {
                TotalRows = result.Rows.Count,
                SuccessCount = result.Rows.Count(x => x.Status == "OK"),
                FailedCount = result.Rows.Count(x => x.Status != "OK"),
            };

            return result;
        }
    }

    private static Dictionary<string, int> BuildHeaderMap(IXLWorksheet ws)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        // Header thường ở row 1
        var headerRow = 1;
        var lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? Headers.Length;
        for (var c = 1; c <= lastCol; c++)
        {
            var raw = ws.Cell(headerRow, c).GetString()?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var key = NormalizeHeader(raw);
            if (!map.ContainsKey(key))
            {
                map[key] = c;
            }
        }

        return map;
    }

    private static string NormalizeHeader(string raw)
    {
        var s = raw.Trim();
        // alias tiếng Việt / tiếng Anh
        return s switch
        {
            "Mã NCC" or "Ma NCC" or "SupplierCode" => "MaNCC",
            "Tên NCC" or "Ten NCC" or "SupplierName" => "TenNCC",
            "Tên sản phẩm" or "Ten san pham" or "ProductName" => "TenSanPham",
            "ĐVT nhập" or "DVT nhập" or "DVT" or "ĐVT" or "Unit" => "DVTNhap",
            "Giá nhập sỉ" or "Gia nhap si" or "AgreedPrice" or "Price" => "GiaNhapSi",
            "MinimumOrderQuantity" or "MinOrder" => "MOQ",
            "Product ID" or "Id" => "ProductId",
            "Mã SKU" or "Ma SKU" or "ProductCode" => "SKU",
            _ => Regex.Replace(s, @"\s+", string.Empty)
        };
    }

    private static void RequireHeaders(Dictionary<string, int> map, params string[] required)
    {
        var missing = required.Where(r => !map.ContainsKey(r)).ToList();
        if (missing.Count > 0)
        {
            throw new BusinessException(
                "MISSING_HEADERS",
                $"Thiếu cột bắt buộc trong Excel: {string.Join(", ", missing)}. Hãy dùng đúng file mẫu xuất từ hệ thống.");
        }
    }

    private static int DetectDataStartRow(IXLWorksheet ws, Dictionary<string, int> headerMap)
    {
        // Row 1 = header; row 2 có thể là note; data từ row 3 nếu cột ProductId ở row 3 là GUID
        for (var r = 2; r <= 5; r++)
        {
            var sku = GetCellString(ws, r, headerMap, "SKU");
            var pid = GetCellString(ws, r, headerMap, "ProductId");
            if (Guid.TryParse(pid, out _) || (!string.IsNullOrWhiteSpace(sku) && !sku.Contains(' ', StringComparison.Ordinal)))
            {
                // tránh nhận dòng hướng dẫn
                if (sku.Contains("KHÔNG", StringComparison.OrdinalIgnoreCase)
                    || pid.Contains("KHÔNG", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return r;
            }
        }

        return 3;
    }

    private static string GetCellString(IXLWorksheet ws, int row, Dictionary<string, int> map, string key)
    {
        if (!map.TryGetValue(key, out var col)) return string.Empty;
        var cell = ws.Cell(row, col);
        if (cell.DataType == XLDataType.Number)
        {
            return cell.GetDouble().ToString(CultureInfo.InvariantCulture);
        }

        return cell.GetFormattedString()?.Trim() ?? cell.GetString()?.Trim() ?? string.Empty;
    }

    private static bool TryParseDecimal(string? raw, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrWhiteSpace(raw))
        {
            // trống = chưa thỏa thuận → 0 → normalize null
            value = 0m;
            return true;
        }

        var cleaned = raw.Trim()
            .Replace("đ", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("d", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(" ", string.Empty)
            .Replace(",", string.Empty);

        return decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out value)
               || decimal.TryParse(raw.Trim(), NumberStyles.Number, new CultureInfo("vi-VN"), out value);
    }

    private static bool TryParseInt(string? raw, out int value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var cleaned = raw.Trim().Replace(" ", string.Empty).Replace(",", string.Empty);
        if (decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out var d)
            || decimal.TryParse(raw.Trim(), NumberStyles.Number, new CultureInfo("vi-VN"), out d))
        {
            if (d != Math.Floor(d)) return false;
            value = (int)d;
            return true;
        }

        return false;
    }
}
