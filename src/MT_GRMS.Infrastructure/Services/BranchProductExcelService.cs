using System.Globalization;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using MT_GRMS.Application.Branches;
using MT_GRMS.Application.Branches.Dtos;
using MT_GRMS.Domain.Exceptions;

namespace MT_GRMS.Infrastructure.Services;

public class BranchProductExcelService : IBranchProductExcelService
{
    public const string SheetName = "Gia_Ban_DVT";

    private static readonly string[] FixedHeaders =
    [
        "ProductId",
        "MaCH",
        "TenCH",
        "SKU",
        "TenSanPham"
    ];

    private static readonly HashSet<string> FixedHeaderKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "ProductId", "MaCH", "TenCH", "SKU", "TenSanPham",
        "MaChiNhanh", "TenChiNhanh", "BranchCode", "BranchName", "ProductName"
    };

    private static readonly XLColor HeaderBg = XLColor.FromHtml("#0F766E");
    private static readonly XLColor ReadonlyBg = XLColor.FromHtml("#F1F5F9");
    private static readonly XLColor EditableBg = XLColor.FromHtml("#FEF3C7");

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

        var items = paged.Items
            .OrderBy(x => x.Sku, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var unitColumns = items
            .SelectMany(p => p.UnitPrices ?? [])
            .Where(u => !string.IsNullOrWhiteSpace(u.UnitName))
            .GroupBy(u => u.UnitName.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var sample = g.OrderByDescending(x => x.IsBaseUnit).ThenBy(x => x.ConversionRate).First();
                return new UnitColumn(sample.UnitName.Trim(), sample.IsBaseUnit, sample.ConversionRate);
            })
            .OrderByDescending(c => c.IsBase)
            .ThenBy(c => c.ConversionRate)
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (unitColumns.Count == 0)
        {
            unitColumns.Add(new UnitColumn("Cái", true, 1m));
        }

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add(SheetName);

        var headers = FixedHeaders.Concat(unitColumns.Select(c => c.Name)).ToList();
        for (var i = 0; i < headers.Count; i++)
        {
            var cell = ws.Cell(1, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = HeaderBg;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        var unitNamesList = string.Join(", ", unitColumns.Select(c => c.Name));
        ws.Cell(2, 1).Value =
            $"KHÔNG SỬA ProductId → TenSanPham. Chỉ sửa các cột giá ĐVT ({unitNamesList}) rồi import lại. Ô trống = giữ nguyên giá hiện tại.";
        ws.Range(2, 1, 2, headers.Count).Merge();
        ws.Cell(2, 1).Style.Font.Italic = true;
        ws.Cell(2, 1).Style.Font.FontColor = XLColor.FromHtml("#B45309");
        ws.Cell(2, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#FFFBEB");

        var row = 3;
        foreach (var p in items)
        {
            ws.Cell(row, 1).Value = p.ProductId.ToString();
            ws.Cell(row, 2).Value = p.BranchCode;
            ws.Cell(row, 3).Value = p.BranchName;
            ws.Cell(row, 4).Value = p.Sku;
            ws.Cell(row, 5).Value = p.Name;

            for (var c = 1; c <= FixedHeaders.Length; c++)
            {
                ws.Cell(row, c).Style.Fill.BackgroundColor = ReadonlyBg;
            }

            var priceByUnit = (p.UnitPrices ?? [])
                .Where(u => !string.IsNullOrWhiteSpace(u.UnitName))
                .GroupBy(u => u.UnitName.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Price, StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < unitColumns.Count; i++)
            {
                var col = FixedHeaders.Length + 1 + i;
                var unitName = unitColumns[i].Name;
                var cell = ws.Cell(row, col);
                cell.Style.Fill.BackgroundColor = EditableBg;
                cell.Style.NumberFormat.Format = "#,##0.###";

                if (priceByUnit.TryGetValue(unitName, out var price) && price > 0)
                {
                    cell.Value = price;
                }
                else if (priceByUnit.ContainsKey(unitName))
                {
                    cell.Value = string.Empty;
                }
                else
                {
                    // SP không có ĐVT này — khóa ô
                    cell.Value = string.Empty;
                    cell.Style.Fill.BackgroundColor = ReadonlyBg;
                }
            }

            row++;
        }

        ws.Column(1).Width = 38;
        ws.Column(2).Width = 12;
        ws.Column(3).Width = 28;
        ws.Column(4).Width = 14;
        ws.Column(5).Width = 36;
        for (var i = 0; i < unitColumns.Count; i++)
        {
            ws.Column(FixedHeaders.Length + 1 + i).Width = 14;
        }

        ws.SheetView.FreezeRows(2);

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        var safeCode = Regex.Replace(branch.BranchCode ?? "CH", @"[^\w\-]+", "_");
        var fileName = $"CH_GiaBan_DVT_{safeCode}_{DateTime.UtcNow:yyyyMMdd}.xlsx";

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
                         string.Equals(w.Name, SheetName, StringComparison.OrdinalIgnoreCase))
                     ?? workbook.Worksheets.FirstOrDefault()
                     ?? throw new BusinessException("INVALID_SHEET", "File Excel không có sheet dữ liệu.");

            var headerMap = BuildHeaderMap(ws);
            RequireHeaders(headerMap, "SKU");

            var unitHeaderCols = RecoverUnitColumnsFromHeaderRow(ws, headerMap);

            if (unitHeaderCols.Count == 0)
            {
                throw new BusinessException(
                    "MISSING_UNIT_COLUMNS",
                    "File không có cột giá theo ĐVT. Hãy dùng đúng file mẫu xuất từ hệ thống.");
            }

            var paged = await _branchProductService.GetBranchProductsAsync(
                branchId,
                new BranchProductQueryFilter { PageIndex = 1, PageSize = 10_000 },
                cancellationToken);

            var byProductId = paged.Items.ToDictionary(x => x.ProductId);
            var bySku = paged.Items
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

                var anyPrice = unitHeaderCols.Any(u =>
                    !string.IsNullOrWhiteSpace(GetCellStringByCol(ws, r, u.Col)));

                if (string.IsNullOrWhiteSpace(skuRaw)
                    && string.IsNullOrWhiteSpace(productIdRaw)
                    && !anyPrice)
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

                    rowResult.Sku = assigned.Sku;
                    rowResult.ProductName = assigned.Name;
                    rowResult.ProductId = assigned.ProductId.ToString();

                    var masterUnits = assigned.UnitPrices ?? [];
                    var unitsByName = masterUnits
                        .Where(u => !string.IsNullOrWhiteSpace(u.UnitName))
                        .GroupBy(u => u.UnitName.Trim(), StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

                    var updates = new List<BranchProductUnitPriceDto>();
                    var summaryParts = new List<string>();

                    foreach (var (unitName, col) in unitHeaderCols)
                    {
                        var raw = GetCellStringByCol(ws, r, col);
                        if (string.IsNullOrWhiteSpace(raw))
                        {
                            continue;
                        }

                        if (!unitsByName.TryGetValue(unitName, out var unit))
                        {
                            // Cột ĐVT không thuộc SP này → bỏ qua
                            continue;
                        }

                        if (!TryParseDecimal(raw, out var price) || price < 0m)
                        {
                            throw new BusinessException(
                                "INVALID_PRICE",
                                $"Giá ĐVT \"{unitName}\" không hợp lệ (phải là số ≥ 0).");
                        }

                        if (price <= 0m)
                        {
                            throw new BusinessException(
                                "INVALID_PRICE",
                                $"Giá ĐVT \"{unitName}\" phải lớn hơn 0.");
                        }

                        if (!unit.UnitId.HasValue || unit.UnitId.Value == Guid.Empty)
                        {
                            throw new BusinessException(
                                "MISSING_UNIT_ID",
                                $"ĐVT \"{unitName}\" thiếu UnitId — không thể cập nhật. Kiểm tra cấu hình đơn vị hàng hóa.");
                        }

                        updates.Add(new BranchProductUnitPriceDto
                        {
                            UnitId = unit.UnitId,
                            UnitName = unit.UnitName,
                            ConversionRate = unit.ConversionRate,
                            Price = price,
                            IsBaseUnit = unit.IsBaseUnit,
                        });
                        summaryParts.Add($"{unit.UnitName}:{price:N0}");
                    }

                    if (updates.Count == 0)
                    {
                        throw new BusinessException(
                            "NO_PRICE_CHANGE",
                            "Không có giá ĐVT nào được điền để cập nhật trên dòng này.");
                    }

                    var basePrice = updates.FirstOrDefault(u => u.IsBaseUnit)?.Price
                        ?? updates.OrderBy(u => u.ConversionRate <= 0 ? 1m : u.ConversionRate).First().Price;

                    await _branchProductService.UpdateProductAsync(
                        branchId,
                        assigned.ProductId,
                        new UpdateBranchProductCommand
                        {
                            RetailPrice = basePrice,
                            Status = assigned.Status,
                            UnitPrices = updates,
                        },
                        cancellationToken);

                    rowResult.UnitPricesSummary = string.Join("; ", summaryParts);
                    rowResult.Status = "OK";
                    rowResult.Message = $"Đã cập nhật {updates.Count} ĐVT: {rowResult.UnitPricesSummary}.";
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

    private static List<(string UnitName, int Col)> RecoverUnitColumnsFromHeaderRow(
        IXLWorksheet ws,
        Dictionary<string, int> headerMap)
    {
        var fixedCols = new HashSet<int>();
        foreach (var key in new[] { "ProductId", "MaCH", "TenCH", "SKU", "TenSanPham" })
        {
            if (headerMap.TryGetValue(key, out var col))
            {
                fixedCols.Add(col);
            }
        }

        var list = new List<(string UnitName, int Col)>();
        var lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? FixedHeaders.Length;
        for (var c = 1; c <= lastCol; c++)
        {
            if (fixedCols.Contains(c)) continue;
            var raw = ws.Cell(1, c).GetString()?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(raw)) continue;
            if (IsFixedAlias(raw)) continue;
            list.Add((raw, c));
        }

        return list;
    }

    private static bool IsFixedAlias(string raw)
    {
        var n = NormalizeHeader(raw);
        return FixedHeaderKeys.Contains(n) || FixedHeaderKeys.Contains(raw);
    }

    private static Dictionary<string, int> BuildHeaderMap(IXLWorksheet ws)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? FixedHeaders.Length;
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
            "Mã CH" or "Ma CH" or "Mã chi nhánh" or "Ma chi nhanh" or "BranchCode" => "MaCH",
            "Tên CH" or "Ten CH" or "Tên chi nhánh" or "Ten chi nhanh" or "BranchName" => "TenCH",
            "Tên sản phẩm" or "Ten san pham" or "ProductName" => "TenSanPham",
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
        for (var r = 2; r <= 5; r++)
        {
            var sku = GetCellString(ws, r, headerMap, "SKU");
            var pid = GetCellString(ws, r, headerMap, "ProductId");
            if (Guid.TryParse(pid, out _) || (!string.IsNullOrWhiteSpace(sku) && !sku.Contains(' ', StringComparison.Ordinal)))
            {
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
        return GetCellStringByCol(ws, row, col);
    }

    private static string GetCellStringByCol(IXLWorksheet ws, int row, int col)
    {
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

    private sealed record UnitColumn(string Name, bool IsBase, decimal ConversionRate);
}
