using System.Globalization;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using MT_GRMS.Application.Branches;
using MT_GRMS.Application.Branches.Dtos;
using MT_GRMS.Domain.Exceptions;

namespace MT_GRMS.Infrastructure.Services;

public class BranchProductExcelService : IBranchProductExcelService
{
    /// <summary>Sheet name — cùng phong cách SP_Theo_NCC ở màn /suppliers/products.</summary>
    public const string SheetName = "SP_Theo_CH";

    /// <summary>Header cố định 8 cột — mirror SP theo NCC (CH thay NCC, Giá bán thay Giá nhập sỉ).</summary>
    private static readonly string[] Headers =
    [
        "Mã hàng",
        "Tên hàng",
        "Mã CH",
        "Tên CH",
        "Đơn vị",
        "Giá bán",
        "MOQ",
        "ProductId"
    ];

    private static readonly XLColor HeaderBg = XLColor.FromHtml("#0F766E");
    private static readonly XLColor BorderColor = XLColor.FromHtml("#CBD5E1");
    private static readonly XLColor EvenRowBg = XLColor.FromHtml("#F0FDFA");
    private static readonly XLColor OddRowBg = XLColor.FromHtml("#FFFFFF");
    private static readonly XLColor TextColor = XLColor.FromHtml("#0F172A");

    private readonly IBranchProductService _branchProductService;
    private readonly IBranchProductRepository _repository;

    public BranchProductExcelService(
        IBranchProductService branchProductService,
        IBranchProductRepository repository)
    {
        _branchProductService = branchProductService;
        _repository = repository;
    }

    public async Task<BranchProductExcelFileDto> ExportAsync(
        Guid branchId,
        BranchProductQueryFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        var branch = await _repository.GetBranchByIdAsync(branchId, cancellationToken)
            ?? throw new NotFoundException($"Không tìm thấy chi nhánh với ID: {branchId}");

        if (branch.IsDeleted)
        {
            throw new NotFoundException($"Không tìm thấy chi nhánh với ID: {branchId}");
        }

        var paged = await _branchProductService.GetBranchProductsAsync(
            branchId,
            new BranchProductQueryFilter { PageIndex = 1, PageSize = 10_000 },
            cancellationToken);

        var ordered = ApplyScreenFilter(paged.Items, filter)
            .OrderBy(x => x.Sku, StringComparer.OrdinalIgnoreCase)
            .ToList();

        using var workbook = new XLWorkbook();
        workbook.Properties.Title = "Sản phẩm theo cửa hàng";
        workbook.Properties.Subject = "Giá bán & MOQ — MT-GRMS";
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
        foreach (var p in ordered)
        {
            var (unitName, sellPrice) = ResolveUnitAndSellPrice(p);
            var isEven = row % 2 == 0;
            var rowBg = isEven ? EvenRowBg : OddRowBg;

            ws.Cell(row, 1).Value = p.Sku ?? string.Empty;
            ws.Cell(row, 2).Value = p.Name ?? string.Empty;
            ws.Cell(row, 3).Value = string.IsNullOrWhiteSpace(p.BranchCode) ? branch.BranchCode : p.BranchCode;
            ws.Cell(row, 4).Value = string.IsNullOrWhiteSpace(p.BranchName) ? branch.Name : p.BranchName;
            ws.Cell(row, 5).Value = unitName;
            ws.Cell(row, 6).Value = sellPrice;
            ws.Cell(row, 7).Value = p.MinOrderQuantity > 0 ? p.MinOrderQuantity : 1;
            ws.Cell(row, 8).Value = p.ProductId.ToString();

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
                else if (c is 2 or 4)
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
        var safeCode = Regex.Replace(branch.BranchCode ?? "CH", @"[^\w\-]+", "_");
        var fileName = $"SP_Theo_CH_{safeCode}_{DateTime.UtcNow:yyyyMMdd}.xlsx";

        return new BranchProductExcelFileDto
        {
            Content = ms.ToArray(),
            FileName = fileName,
        };
    }

    public async Task<BranchProductExcelImportResultDto> ImportAsync(
        Guid branchId,
        Stream fileStream,
        string fileName,
        BranchProductQueryFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        var branch = await _repository.GetBranchByIdAsync(branchId, cancellationToken)
            ?? throw new NotFoundException($"Không tìm thấy chi nhánh với ID: {branchId}");

        if (branch.IsDeleted)
        {
            throw new NotFoundException($"Không tìm thấy chi nhánh với ID: {branchId}");
        }

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
                         string.Equals(w.Name, SheetName, StringComparison.OrdinalIgnoreCase)
                         || string.Equals(w.Name, "Gia_Ban_DVT", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(w.Name, "SP_Theo_NCC", StringComparison.OrdinalIgnoreCase))
                     ?? workbook.Worksheets.FirstOrDefault()
                     ?? throw new BusinessException("INVALID_SHEET", "File Excel không có sheet dữ liệu.");

            var headerMap = BuildHeaderMap(ws);
            RequireHeaders(headerMap, "SKU", "GiaBan");

            var paged = await _branchProductService.GetBranchProductsAsync(
                branchId,
                new BranchProductQueryFilter { PageIndex = 1, PageSize = 10_000 },
                cancellationToken);

            var assignedAll = paged.Items.ToList();
            var scopedIds = ApplyScreenFilter(assignedAll, filter).Select(x => x.ProductId).ToHashSet();
            var byProductId = assignedAll.ToDictionary(x => x.ProductId);
            var bySku = assignedAll
                .Where(x => !string.IsNullOrWhiteSpace(x.Sku))
                .GroupBy(x => x.Sku.Trim().ToUpperInvariant())
                .ToDictionary(g => g.Key, g => g.First());

            var result = new BranchProductExcelImportResultDto
            {
                FileName = string.IsNullOrWhiteSpace(fileName) ? "import.xlsx" : fileName,
                BranchId = branch.Id,
                BranchCode = branch.BranchCode,
                BranchName = branch.Name,
            };

            var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
            var startRow = DetectDataStartRow(ws, headerMap);

            for (var r = startRow; r <= lastRow; r++)
            {
                var skuRaw = GetCellString(ws, r, headerMap, "SKU");
                var productIdRaw = GetCellString(ws, r, headerMap, "ProductId");
                var nameRaw = GetCellString(ws, r, headerMap, "TenSanPham");
                var priceRaw = GetCellString(ws, r, headerMap, "GiaBan");
                var moqRaw = GetCellString(ws, r, headerMap, "MOQ");

                if (string.IsNullOrWhiteSpace(skuRaw)
                    && string.IsNullOrWhiteSpace(productIdRaw)
                    && string.IsNullOrWhiteSpace(priceRaw)
                    && string.IsNullOrWhiteSpace(moqRaw))
                {
                    continue;
                }

                var rowResult = new BranchProductExcelImportRowResultDto
                {
                    Row = r,
                    ProductId = productIdRaw,
                    Sku = skuRaw,
                    ProductName = nameRaw,
                };

                try
                {
                    BranchProductDto? assigned = null;
                    if (Guid.TryParse(productIdRaw, out var productId)
                        && byProductId.TryGetValue(productId, out var byId))
                    {
                        assigned = byId;
                    }
                    else if (!string.IsNullOrWhiteSpace(skuRaw)
                             && bySku.TryGetValue(skuRaw.Trim().ToUpperInvariant(), out var byCode))
                    {
                        assigned = byCode;
                    }

                    if (assigned == null)
                    {
                        throw new BusinessException(
                            "PRODUCT_NOT_ASSIGNED",
                            "SKU/ProductId không thuộc danh sách sản phẩm đã gán cho chi nhánh này.");
                    }

                    if (!scopedIds.Contains(assigned.ProductId))
                    {
                        throw new BusinessException(
                            "PRODUCT_OUT_OF_FILTER",
                            "Sản phẩm không thuộc bộ lọc màn hình hiện tại. Xuất lại Excel theo bộ lọc rồi import.");
                    }

                    rowResult.Sku = assigned.Sku;
                    rowResult.ProductName = assigned.Name;
                    rowResult.ProductId = assigned.ProductId.ToString();

                    if (!TryParseDecimal(priceRaw, out var price) || price <= 0m)
                    {
                        throw new BusinessException(
                            "INVALID_PRICE",
                            "Giá bán không hợp lệ (phải là số > 0).");
                    }

                    var unitUpdates = BuildUnitPriceUpdates(assigned, price);

                    await _branchProductService.UpdateProductAsync(
                        branchId,
                        assigned.ProductId,
                        new UpdateBranchProductCommand
                        {
                            RetailPrice = price,
                            Status = assigned.Status,
                            UnitPrices = unitUpdates,
                        },
                        cancellationToken);

                    rowResult.UnitPricesSummary = $"Giá bán:{price:N0}";
                    rowResult.Status = "OK";
                    rowResult.Message = $"Cập nhật giá bán {price:N0} đ.";
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

            result.Summary = new BranchProductExcelImportSummaryDto
            {
                TotalRows = result.Rows.Count,
                SuccessCount = result.Rows.Count(x => x.Status == "OK"),
                FailedCount = result.Rows.Count(x => x.Status != "OK"),
            };

            return result;
        }
    }

    /// <summary>
    /// Mirror filter logic của danh sách SP theo CH (không phân trang) —
    /// dùng chung cho export và validate import theo bộ lọc màn hình.
    /// </summary>
    internal static IEnumerable<BranchProductDto> ApplyScreenFilter(
        IEnumerable<BranchProductDto> source,
        BranchProductQueryFilter? filter)
    {
        if (filter == null) return source;

        IEnumerable<BranchProductDto> q = source;

        if (!string.IsNullOrWhiteSpace(filter.Status)
            && !string.Equals(filter.Status, "ALL", StringComparison.OrdinalIgnoreCase))
        {
            q = q.Where(x => string.Equals(x.Status, filter.Status, StringComparison.OrdinalIgnoreCase));
        }

        var skuTerms = CleanTerms(filter.Skus);
        if (skuTerms.Count > 0)
        {
            q = q.Where(x =>
                skuTerms.Any(t => (x.Sku ?? string.Empty).Contains(t, StringComparison.OrdinalIgnoreCase)));
        }

        var nameTerms = CleanTerms(filter.Names);
        if (nameTerms.Count > 0)
        {
            q = q.Where(x =>
                nameTerms.Any(t => (x.Name ?? string.Empty).Contains(t, StringComparison.OrdinalIgnoreCase)));
        }
        else if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
        {
            var term = filter.SearchTerm.Trim();
            q = q.Where(x =>
                (x.Name?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
                || (x.Sku?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
                || (x.Barcode?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        var branchCodeTerms = CleanTerms(filter.BranchCodes);
        if (branchCodeTerms.Count > 0)
        {
            q = q.Where(x =>
                branchCodeTerms.Any(t =>
                    (x.BranchCode ?? string.Empty).Contains(t, StringComparison.OrdinalIgnoreCase)));
        }
        else if (!string.IsNullOrWhiteSpace(filter.BranchCode))
        {
            var code = filter.BranchCode.Trim();
            q = q.Where(x =>
                (x.BranchCode ?? string.Empty).Contains(code, StringComparison.OrdinalIgnoreCase));
        }

        var branchNameTerms = CleanTerms(filter.BranchNames);
        if (branchNameTerms.Count > 0)
        {
            q = q.Where(x =>
                branchNameTerms.Any(t =>
                    (x.BranchName ?? string.Empty).Contains(t, StringComparison.OrdinalIgnoreCase)));
        }

        var supplierCodeTerms = CleanTerms(filter.SupplierCodes);
        if (supplierCodeTerms.Count > 0)
        {
            q = q.Where(x =>
                supplierCodeTerms.Any(t =>
                    (x.PrimarySupplierCode ?? string.Empty).Contains(t, StringComparison.OrdinalIgnoreCase)));
        }

        var supplierNameTerms = CleanTerms(filter.SupplierNames);
        if (supplierNameTerms.Count > 0)
        {
            q = q.Where(x =>
                supplierNameTerms.Any(t =>
                    (x.PrimarySupplierName ?? string.Empty).Contains(t, StringComparison.OrdinalIgnoreCase)));
        }

        if (!string.IsNullOrWhiteSpace(filter.Category)
            && !string.Equals(filter.Category, "ALL", StringComparison.OrdinalIgnoreCase))
        {
            var cat = filter.Category.Trim();
            q = q.Where(x =>
                (x.Category ?? string.Empty).Contains(cat, StringComparison.OrdinalIgnoreCase));
        }

        return q;
    }

    private static (string UnitName, decimal SellPrice) ResolveUnitAndSellPrice(BranchProductDto p)
    {
        var baseUnit = (p.UnitPrices ?? [])
            .Where(u => !string.IsNullOrWhiteSpace(u.UnitName))
            .OrderByDescending(u => u.IsBaseUnit)
            .ThenBy(u => u.ConversionRate <= 0 ? 1m : u.ConversionRate)
            .FirstOrDefault();

        var unitName = !string.IsNullOrWhiteSpace(baseUnit?.UnitName)
            ? baseUnit!.UnitName.Trim()
            : (!string.IsNullOrWhiteSpace(p.Unit) ? p.Unit.Trim() : "Cái");

        var sellPrice = baseUnit != null && baseUnit.Price > 0
            ? baseUnit.Price
            : (p.RetailPrice > 0 ? p.RetailPrice : 0m);

        return (unitName, sellPrice);
    }

    private static List<BranchProductUnitPriceDto>? BuildUnitPriceUpdates(BranchProductDto assigned, decimal price)
    {
        var units = assigned.UnitPrices ?? [];
        if (units.Count == 0)
        {
            return null;
        }

        var baseUnit = units
            .OrderByDescending(u => u.IsBaseUnit)
            .ThenBy(u => u.ConversionRate <= 0 ? 1m : u.ConversionRate)
            .FirstOrDefault();

        if (baseUnit == null || !baseUnit.UnitId.HasValue || baseUnit.UnitId.Value == Guid.Empty)
        {
            return null;
        }

        return
        [
            new BranchProductUnitPriceDto
            {
                UnitId = baseUnit.UnitId,
                UnitName = baseUnit.UnitName,
                ConversionRate = baseUnit.ConversionRate,
                Price = price,
                IsBaseUnit = baseUnit.IsBaseUnit,
            }
        ];
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
        var lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? Headers.Length;
        for (var c = 1; c <= lastCol; c++)
        {
            var raw = ws.Cell(1, c).GetString()?.Trim() ?? string.Empty;
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
        return s switch
        {
            "Mã CH" or "Ma CH" or "Mã chi nhánh" or "Ma chi nhanh" or "BranchCode" or "MaCH" => "MaCH",
            "Tên CH" or "Ten CH" or "Tên chi nhánh" or "Ten chi nhanh" or "BranchName" or "TenCH" => "TenCH",
            "Tên hàng" or "Tên hàng (*)" or "Ten hang" or "Tên sản phẩm" or "Ten san pham"
                or "TenSanPham" or "ProductName" => "TenSanPham",
            "Đơn vị" or "Don vi" or "Đơn vị bán" or "ĐVT" or "DVT" or "Unit"
                or "Đơn vị nhập" or "Don vi nhap" => "DonVi",
            "Giá bán" or "Gia ban" or "GiaBan" or "Giá bán lẻ" or "RetailPrice"
                or "Giá nhập sỉ" or "Gia nhap si" or "GiaNhapSi" or "Price" => "GiaBan",
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
        for (var r = 2; r <= 5; r++)
        {
            var sku = GetCellString(ws, r, headerMap, "SKU");
            var pid = GetCellString(ws, r, headerMap, "ProductId");
            if (Guid.TryParse(pid, out _)
                || (!string.IsNullOrWhiteSpace(sku) && !sku.Contains(' ', StringComparison.Ordinal)))
            {
                if (sku.Contains("KHÔNG", StringComparison.OrdinalIgnoreCase)
                    || pid.Contains("KHÔNG", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return r;
            }
        }

        return 2;
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
        if (string.IsNullOrWhiteSpace(raw)) return false;

        var cleaned = raw.Trim()
            .Replace("đ", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(" ", string.Empty)
            .Replace(",", string.Empty);

        return decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out value)
               || decimal.TryParse(raw.Trim(), NumberStyles.Number, new CultureInfo("vi-VN"), out value);
    }
}
