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
    /// <summary>Sheet name — cùng phong cách DanhMucHangHoa ở màn /store-owner/products.</summary>
    public const string SheetName = "SP_Theo_NCC";

    /// <summary>Header hiển thị tiếng Việt (giống Danh mục hàng hóa).</summary>
    private static readonly string[] Headers =
    [
        "Mã hàng",
        "Tên hàng",
        "Mã NCC",
        "Tên NCC",
        "Đơn vị nhập",
        "Giá nhập sỉ",
        "MOQ",
        "ProductId"
    ];

    private static readonly XLColor HeaderBg = XLColor.FromHtml("#0F766E");
    private static readonly XLColor BorderColor = XLColor.FromHtml("#CBD5E1");
    private static readonly XLColor EvenRowBg = XLColor.FromHtml("#F0FDFA");
    private static readonly XLColor OddRowBg = XLColor.FromHtml("#FFFFFF");
    private static readonly XLColor TextColor = XLColor.FromHtml("#0F172A");

    private readonly ISupplierRepository _supplierRepository;
    private readonly IUnitOfWork _unitOfWork;

    public SupplierProductExcelService(ISupplierRepository supplierRepository, IUnitOfWork unitOfWork)
    {
        _supplierRepository = supplierRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<SupplierProductExcelFileDto> ExportAsync(
        Guid supplierId,
        SupplierProductQueryFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        var supplier = await _supplierRepository.GetByIdAsync(supplierId, cancellationToken)
            ?? throw new NotFoundException($"Không tìm thấy nhà cung cấp với ID: {supplierId}");

        var all = await _supplierRepository.GetSupplierProductsForSupplierAsync(supplierId, cancellationToken);
        var ordered = ApplyScreenFilter(all, filter)
            .OrderBy(x => x.Product?.ProductCode ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToList();

        using var workbook = new XLWorkbook();
        workbook.Properties.Title = "Sản phẩm theo nhà cung cấp";
        workbook.Properties.Subject = "Giá nhập sỉ & MOQ — MT-GRMS";
        workbook.Properties.Author = "MT-GRMS";

        var ws = workbook.Worksheets.Add(SheetName);

        for (var i = 0; i < Headers.Length; i++)
        {
            var cell = ws.Cell(1, i + 1);
            cell.Value = Headers[i];
            cell.Style.Font.FontName = "Aptos";
            cell.Style.Font.FontSize = 11;
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = HeaderBg;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            cell.Style.Alignment.WrapText = true;
            ApplyThinBorder(cell);
        }

        ws.Row(1).Height = 34;

        var row = 2;
        foreach (var sp in ordered)
        {
            var sku = sp.Product?.ProductCode ?? string.Empty;
            var name = sp.Product?.Name ?? string.Empty;
            var purchaseUnitEntity = sp.Product?.Units?
                .FirstOrDefault(u => !u.IsDeleted && u.IsPurchaseUnit);
            var uom = !string.IsNullOrWhiteSpace(purchaseUnitEntity?.UnitName)
                ? purchaseUnitEntity.UnitName.Trim()
                : (!string.IsNullOrWhiteSpace(sp.DeliveryUoM)
                    ? sp.DeliveryUoM.Trim()
                    : (sp.Product?.BaseUnit ?? "Cái"));
            var conversionRate = purchaseUnitEntity != null && purchaseUnitEntity.ConversionRate > 0
                ? purchaseUnitEntity.ConversionRate
                : (sp.ConversionRate > 0 ? sp.ConversionRate : 1m);
            var isEven = row % 2 == 0;
            var rowBg = isEven ? EvenRowBg : OddRowBg;

            ws.Cell(row, 1).Value = sku;
            ws.Cell(row, 2).Value = name;
            ws.Cell(row, 3).Value = supplier.Code;
            ws.Cell(row, 4).Value = supplier.Name;
            ws.Cell(row, 5).Value = uom;
            // Keep export consistent with the UI and purchase-order pricing:
            // prefer the negotiated supplier price, then fall back to Master cost price scaled by PurchaseUnit conversion rate.
            var scaledMasterCost = sp.Product?.CostPrice.HasValue == true
                ? Math.Round(sp.Product.CostPrice.Value * conversionRate, 2)
                : 0m;
            var effectivePurchasePrice = sp.AgreedWholesalePrice.HasValue
                                         && sp.AgreedWholesalePrice.Value > 0
                ? sp.AgreedWholesalePrice.Value
                : scaledMasterCost;
            ws.Cell(row, 6).Value = effectivePurchasePrice;

            ws.Cell(row, 7).Value = sp.MinimumOrderQuantity > 0 ? sp.MinimumOrderQuantity : 1;
            ws.Cell(row, 8).Value = sp.ProductId.ToString();

            for (var c = 1; c <= Headers.Length; c++)
            {
                var cell = ws.Cell(row, c);
                cell.Style.Font.FontName = "Aptos";
                cell.Style.Font.FontSize = 10;
                cell.Style.Font.FontColor = TextColor;
                cell.Style.Fill.BackgroundColor = rowBg;
                cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                ApplyThinBorder(cell);

                if (c is 6 or 7)
                {
                    cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                }
                else if (c == 2 || c == 4)
                {
                    cell.Style.Alignment.WrapText = true;
                }
                else
                {
                    cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                }
            }

            ws.Cell(row, 6).Style.NumberFormat.Format = "#,##0 [$₫-vi-VN]";
            ws.Cell(row, 7).Style.NumberFormat.Format = "#,##0";
            ws.Row(row).Height = 24;
            row++;
        }

        ws.Column(1).Width = 18;
        ws.Column(2).Width = 32;
        ws.Column(3).Width = 16;
        ws.Column(4).Width = 28;
        ws.Column(5).Width = 14;
        ws.Column(6).Width = 16;
        ws.Column(7).Width = 10;
        ws.Column(8).Width = 38;

        ws.SheetView.FreezeRows(1);
        if (ordered.Count > 0)
        {
            ws.RangeUsed()?.SetAutoFilter();
        }

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        var safeCode = Regex.Replace(supplier.Code ?? "NCC", @"[^\w\-]+", "_");
        var fileName = $"SP_Theo_NCC_{safeCode}_{DateTime.UtcNow:yyyyMMdd}.xlsx";

        return new SupplierProductExcelFileDto
        {
            Content = ms.ToArray(),
            FileName = fileName,
        };
    }

    private static void ApplyThinBorder(IXLCell cell)
    {
        cell.Style.Border.TopBorder = XLBorderStyleValues.Thin;
        cell.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        cell.Style.Border.LeftBorder = XLBorderStyleValues.Thin;
        cell.Style.Border.RightBorder = XLBorderStyleValues.Thin;
        cell.Style.Border.TopBorderColor = BorderColor;
        cell.Style.Border.BottomBorderColor = BorderColor;
        cell.Style.Border.LeftBorderColor = BorderColor;
        cell.Style.Border.RightBorderColor = BorderColor;
    }

    public async Task<SupplierProductExcelImportResultDto> ImportAsync(
        Guid supplierId,
        Stream fileStream,
        string fileName,
        SupplierProductQueryFilter? filter = null,
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

            var assignedAll = await _supplierRepository.GetSupplierProductsForSupplierAsync(supplierId, cancellationToken);
            var scoped = ApplyScreenFilter(assignedAll, filter).ToList();
            var scopedIds = scoped.Select(x => x.ProductId).ToHashSet();
            var byProductId = assignedAll.ToDictionary(x => x.ProductId);
            var bySku = assignedAll
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

                    if (!scopedIds.Contains(entity.ProductId))
                    {
                        throw new BusinessException(
                            "PRODUCT_OUT_OF_FILTER",
                            "Sản phẩm không thuộc bộ lọc màn hình hiện tại. Xuất lại Excel theo bộ lọc rồi import.");
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

    /// <summary>
    /// Mirror filter logic of GetPagedSupplierProductsAsync (không phân trang) —
    /// dùng chung cho export và validate import theo bộ lọc màn hình.
    /// </summary>
    internal static IEnumerable<SupplierProduct> ApplyScreenFilter(
        IEnumerable<SupplierProduct> source,
        SupplierProductQueryFilter? filter)
    {
        if (filter == null) return source;

        IEnumerable<SupplierProduct> q = source;

        if (filter.CategoryId.HasValue && filter.CategoryId != Guid.Empty)
        {
            q = q.Where(sp => sp.Product?.CategoryId == filter.CategoryId.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.CategoryName)
            && !string.Equals(filter.CategoryName, "ALL", StringComparison.OrdinalIgnoreCase))
        {
            var cat = filter.CategoryName.Trim().ToLowerInvariant();
            q = q.Where(sp =>
                sp.Product?.Category != null
                && sp.Product.Category.Name.ToLowerInvariant().Contains(cat));
        }

        if (filter.Status.HasValue)
        {
            q = q.Where(sp => sp.Status == filter.Status.Value);
        }

        var skuTerms = CleanTerms(filter.Skus);
        if (skuTerms.Count > 0)
        {
            q = q.Where(sp =>
            {
                var code = sp.Product?.ProductCode ?? string.Empty;
                return skuTerms.Any(t => code.Contains(t, StringComparison.OrdinalIgnoreCase));
            });
        }

        var nameTerms = CleanTerms(filter.Names);
        if (nameTerms.Count > 0)
        {
            q = q.Where(sp =>
            {
                var name = sp.Product?.Name ?? string.Empty;
                return nameTerms.Any(t => name.Contains(t, StringComparison.OrdinalIgnoreCase));
            });
        }
        else if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
        {
            var term = filter.SearchTerm.Trim();
            q = q.Where(sp =>
            {
                var p = sp.Product;
                if (p == null) return false;
                return (p.Name?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
                       || (p.Barcode?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
                       || (p.ProductCode?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
                       || (sp.SupplierProductCode?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false);
            });
        }

        var supplierCodeTerms = CleanTerms(filter.SupplierCodes);
        if (supplierCodeTerms.Count > 0)
        {
            q = q.Where(sp =>
            {
                var code = sp.Supplier?.Code ?? string.Empty;
                return supplierCodeTerms.Any(t => code.Contains(t, StringComparison.OrdinalIgnoreCase));
            });
        }

        var supplierNameTerms = CleanTerms(filter.SupplierNames);
        if (supplierNameTerms.Count > 0)
        {
            q = q.Where(sp =>
            {
                var name = sp.Supplier?.Name ?? string.Empty;
                return supplierNameTerms.Any(t => name.Contains(t, StringComparison.OrdinalIgnoreCase));
            });
        }
        else if (!string.IsNullOrWhiteSpace(filter.SupplierSearch) && supplierCodeTerms.Count == 0)
        {
            var supplierTerm = filter.SupplierSearch.Trim();
            q = q.Where(sp =>
                sp.Supplier != null
                && ((sp.Supplier.Code?.Contains(supplierTerm, StringComparison.OrdinalIgnoreCase) ?? false)
                    || (sp.Supplier.Name?.Contains(supplierTerm, StringComparison.OrdinalIgnoreCase) ?? false)));
        }

        return q;
    }

    private static List<string> CleanTerms(IReadOnlyList<string>? values)
    {
        if (values == null || values.Count == 0) return [];
        return values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
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
        // alias tiếng Việt / tiếng Anh — khớp header Danh mục hàng hóa + file cũ
        return s switch
        {
            "Mã NCC" or "Ma NCC" or "MaNCC" or "SupplierCode" => "MaNCC",
            "Tên NCC" or "Ten NCC" or "TenNCC" or "SupplierName" => "TenNCC",
            "Tên hàng" or "Tên hàng (*)" or "Ten hang" or "Tên sản phẩm" or "Ten san pham"
                or "TenSanPham" or "ProductName" => "TenSanPham",
            "Đơn vị nhập" or "Don vi nhap" or "ĐVT nhập" or "DVT nhập" or "DVTNhap"
                or "DVT" or "ĐVT" or "Unit" => "DVTNhap",
            "Giá nhập sỉ" or "Gia nhap si" or "GiaNhapSi" or "AgreedPrice" or "Price" => "GiaNhapSi",
            "MinimumOrderQuantity" or "MinOrder" => "MOQ",
            "Product ID" or "Id" or "Mã hệ thống" => "ProductId",
            "Mã hàng" or "Ma hang" or "Mã SKU" or "Ma SKU" or "ProductCode" or "SKU" => "SKU",
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
