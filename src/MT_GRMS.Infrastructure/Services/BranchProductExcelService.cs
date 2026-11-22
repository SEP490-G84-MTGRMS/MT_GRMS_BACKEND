using System.Globalization;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Branches;
using MT_GRMS.Application.Branches.Dtos;
using MT_GRMS.Domain.Exceptions;
using MT_GRMS.Infrastructure.Persistence;

namespace MT_GRMS.Infrastructure.Services;

public class BranchProductExcelService : IBranchProductExcelService
{
    /// <summary>Sheet name — cùng phong cách SP_Theo_NCC ở màn /suppliers/products.</summary>
    public const string SheetName = "SP_Theo_CH";
    /// <summary>Legacy sheet (file cũ). Export mới không tạo sheet này.</summary>
    private const string UnitSheetName = "Gia_Theo_Don_Vi";
    private const string WorkbookSubject = "MT-GRMS Branch Product Prices v2";
    private static readonly string[] UnitHeaders =
        ["BranchId", "ProductId", "Mã hàng", "Tên hàng", "UnitId", "Đơn vị", "Giá hiện tại", "Giá mới"];

    /// <summary>
    /// Một sheet duy nhất: mỗi dòng = 1 sản phẩm × 1 đơn vị, sửa cột Giá bán.
    /// ProductId/UnitId ẩn để import khớp đúng ĐVT.
    /// </summary>
    private static readonly string[] Headers =
    [
        "Mã hàng",
        "Tên hàng",
        "Mã CH",
        "Tên CH",
        "Đơn vị",
        "Giá bán",
        "MOQ",
        "ProductId",
        "UnitId",
        "Mã NCC",
        "Tên nhà cung cấp",
        "Giá vốn",
        "DVN",
        "Trạng thái"
    ];

    private static readonly XLColor HeaderBg = XLColor.FromHtml("#0F766E");
    private static readonly XLColor BorderColor = XLColor.FromHtml("#CBD5E1");
    private static readonly XLColor EvenRowBg = XLColor.FromHtml("#F0FDFA");
    private static readonly XLColor OddRowBg = XLColor.FromHtml("#FFFFFF");
    private static readonly XLColor TextColor = XLColor.FromHtml("#0F172A");

    private readonly IBranchProductService _branchProductService;
    private readonly IBranchProductRepository _repository;
    private readonly AppDbContext? _db;

    public BranchProductExcelService(
        IBranchProductService branchProductService,
        IBranchProductRepository repository,
        AppDbContext? db = null)
    {
        _branchProductService = branchProductService;
        _repository = repository;
        _db = db;
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

        var products = await GetAllBranchProductsAsync(branchId, cancellationToken);
        var ordered = ApplyScreenFilter(products, filter)
            .OrderBy(x => x.Sku, StringComparer.OrdinalIgnoreCase)
            .ToList();

        using var workbook = new XLWorkbook();
        workbook.Properties.Title = "Sản phẩm theo cửa hàng";
        workbook.Properties.Subject = WorkbookSubject;
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
            foreach (var unit in GetEditableUnits(p))
            {
                var isEven = row % 2 == 0;
                var rowBg = isEven ? EvenRowBg : OddRowBg;
                var unitName = string.IsNullOrWhiteSpace(unit.UnitName)
                    ? (string.IsNullOrWhiteSpace(p.Unit) ? "Cái" : p.Unit.Trim())
                    : unit.UnitName.Trim();
                var sellPrice = unit.Price > 0 ? unit.Price : (p.RetailPrice > 0 ? p.RetailPrice : 0m);

                ws.Cell(row, 1).Value = p.Sku ?? string.Empty;
                ws.Cell(row, 2).Value = p.Name ?? string.Empty;
                ws.Cell(row, 3).Value = string.IsNullOrWhiteSpace(p.BranchCode) ? branch.BranchCode : p.BranchCode;
                ws.Cell(row, 4).Value = string.IsNullOrWhiteSpace(p.BranchName) ? branch.Name : p.BranchName;
                ws.Cell(row, 5).Value = unitName;
                ws.Cell(row, 6).Value = sellPrice;
                ws.Cell(row, 7).Value = p.MinOrderQuantity > 0 ? p.MinOrderQuantity : 1;
                ws.Cell(row, 8).Value = p.ProductId.ToString();
                ws.Cell(row, 9).Value = unit.UnitId?.ToString() ?? string.Empty;
                ws.Cell(row, 10).Value = p.LostSupplierSource ? string.Empty : p.PrimarySupplierCode;
                ws.Cell(row, 11).Value = p.LostSupplierSource ? string.Empty : p.PrimarySupplierName;
                ws.Cell(row, 12).Value = p.CostPrice;
                ws.Cell(row, 13).Value = string.IsNullOrWhiteSpace(p.Unit) ? "Cái" : p.Unit;
                ws.Cell(row, 14).Value = string.Equals(p.Status, "Active", StringComparison.OrdinalIgnoreCase)
                    ? "Hoạt động"
                    : "Tạm ngưng";

                for (var c = 1; c <= Headers.Length; c++)
                {
                    var cell = ws.Cell(row, c);
                    cell.Style.Font.FontName = "Aptos";
                    cell.Style.Font.FontSize = 10;
                    cell.Style.Font.FontColor = TextColor;
                    cell.Style.Fill.BackgroundColor = rowBg;
                    cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                    ApplyThinBorder(cell);
                    // Chỉ cho sửa cột Giá bán
                    cell.Style.Protection.Locked = c != 6;

                    if (c is 6 or 7 or 12)
                    {
                        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                    }
                    else if (c is 2 or 4 or 11)
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
                ws.Cell(row, 12).Style.NumberFormat.Format = "#,##0.## [$₫-vi-VN]";
                ws.Row(row).Height = 24;
                row++;
            }
        }

        ws.Column(1).Width = 18;
        ws.Column(2).Width = 32;
        ws.Column(3).Width = 16;
        ws.Column(4).Width = 28;
        ws.Column(5).Width = 14;
        ws.Column(6).Width = 16;
        ws.Column(7).Width = 10;
        ws.Column(8).Hide();
        ws.Column(9).Hide();
        ws.Column(10).Width = 16;
        ws.Column(11).Width = 28;
        ws.Column(12).Width = 16;
        ws.Column(13).Width = 14;
        ws.Column(14).Width = 16;
        if (row > 2)
        {
            ws.Rows(2, row - 1).AdjustToContents(2, Headers.Length, 24, double.MaxValue);
        }

        ws.SheetView.FreezeRows(1);
        if (ordered.Count > 0)
        {
            ws.RangeUsed()?.SetAutoFilter();
        }

        ws.Protect();
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
            if (workbook.Worksheets.TryGetWorksheet(UnitSheetName, out var unitSheet))
            {
                return await ImportUnitsAsync(unitSheet, branch.Id, branch.BranchCode, branch.Name,
                    fileName ?? "import.xlsx", filter, cancellationToken);
            }

            var ws = workbook.Worksheets.FirstOrDefault(w =>
                         string.Equals(w.Name, SheetName, StringComparison.OrdinalIgnoreCase)
                         || string.Equals(w.Name, "Gia_Ban_DVT", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(w.Name, "SP_Theo_NCC", StringComparison.OrdinalIgnoreCase))
                     ?? workbook.Worksheets.FirstOrDefault()
                     ?? throw new BusinessException("INVALID_SHEET", "File Excel không có sheet dữ liệu.");

            var headerMap = BuildHeaderMap(ws);
            RequireHeaders(headerMap, "SKU", "GiaBan");

            var assignedAll = await GetAllBranchProductsAsync(branchId, cancellationToken);
            var scopedIds = ApplyScreenFilter(assignedAll, filter).Select(x => x.ProductId).ToHashSet();
            var byProductId = assignedAll.DistinctBy(x => x.ProductId).ToDictionary(x => x.ProductId);
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
                IsPreview = filter?.PreviewOnly == true,
            };

            var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
            var startRow = DetectDataStartRow(ws, headerMap);
            var pending = new List<(BranchProductExcelImportRowResultDto Row, BranchProductDto Product, BranchProductUnitPriceDto Unit, decimal Price)>();
            var seenUnits = new HashSet<(Guid ProductId, Guid? UnitId, string UnitName)>();

            for (var r = startRow; r <= lastRow; r++)
            {
                var skuRaw = GetCellString(ws, r, headerMap, "SKU");
                var productIdRaw = GetCellString(ws, r, headerMap, "ProductId");
                var nameRaw = GetCellString(ws, r, headerMap, "TenSanPham");
                var unitNameRaw = GetCellString(ws, r, headerMap, "DonVi");
                var unitIdRaw = GetCellString(ws, r, headerMap, "UnitId");
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

                    var units = GetEditableUnits(assigned);
                    BranchProductUnitPriceDto? unit = null;
                    if (Guid.TryParse(unitIdRaw, out var unitId) && unitId != Guid.Empty)
                    {
                        unit = units.FirstOrDefault(u => u.UnitId == unitId);
                    }

                    if (unit == null && !string.IsNullOrWhiteSpace(unitNameRaw))
                    {
                        unit = units.FirstOrDefault(u =>
                            string.Equals(u.UnitName?.Trim(), unitNameRaw.Trim(), StringComparison.OrdinalIgnoreCase));
                    }

                    // File cũ / 1 dòng 1 SP: không có ĐVT → cập nhật đơn vị cơ sở
                    if (unit == null && string.IsNullOrWhiteSpace(unitNameRaw) && string.IsNullOrWhiteSpace(unitIdRaw))
                    {
                        unit = units.FirstOrDefault(u => u.IsBaseUnit) ?? units.FirstOrDefault();
                    }

                    if (unit == null)
                    {
                        throw new BusinessException(
                            "UNIT_NOT_FOUND",
                            $"Không tìm thấy đơn vị \"{unitNameRaw}\" của sản phẩm. Xuất lại Excel mẫu.");
                    }

                    var unitKey = (assigned.ProductId, unit.UnitId, unit.UnitName?.Trim() ?? string.Empty);
                    if (!seenUnits.Add(unitKey))
                    {
                        throw new BusinessException("DUPLICATE_UNIT", "Đơn vị bị trùng trong file.");
                    }

                    var previous = unit.Price > 0 ? unit.Price : assigned.RetailPrice;
                    rowResult.UnitPricesSummary = $"{unit.UnitName}: {previous:N0} → {price:N0} đ";
                    rowResult.Status = "OK";
                    rowResult.Message = result.IsPreview
                        ? (previous == price ? "Giá giữ nguyên." : "Sẵn sàng cập nhật.")
                        : (previous == price ? "Giá giữ nguyên." : $"Cập nhật giá {unit.UnitName}.");
                    pending.Add((rowResult, assigned, unit, price));
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

            if (!result.IsPreview)
            {
                foreach (var group in pending.GroupBy(x => x.Product.ProductId))
                {
                    var product = group.First().Product;
                    var existingUnits = GetEditableUnits(product).ToList();
                    var changes = existingUnits.Select(u =>
                    {
                        var match = group.FirstOrDefault(g =>
                            (u.UnitId.HasValue && g.Unit.UnitId == u.UnitId)
                            || string.Equals(u.UnitName, g.Unit.UnitName, StringComparison.OrdinalIgnoreCase));
                        return new BranchProductUnitPriceDto
                        {
                            UnitId = u.UnitId,
                            UnitName = u.UnitName,
                            ConversionRate = u.ConversionRate,
                            IsBaseUnit = u.IsBaseUnit,
                            Price = match.Product != null ? match.Price : u.Price,
                        };
                    }).ToList();

                    // Đơn vị mới trong file nhưng không có trong existing (fallback 1 ĐVT ảo)
                    foreach (var g in group)
                    {
                        var exists = changes.Any(c =>
                            (c.UnitId.HasValue && c.UnitId == g.Unit.UnitId)
                            || string.Equals(c.UnitName, g.Unit.UnitName, StringComparison.OrdinalIgnoreCase));
                        if (!exists)
                        {
                            changes.Add(new BranchProductUnitPriceDto
                            {
                                UnitId = g.Unit.UnitId,
                                UnitName = g.Unit.UnitName,
                                ConversionRate = g.Unit.ConversionRate,
                                IsBaseUnit = g.Unit.IsBaseUnit,
                                Price = g.Price,
                            });
                        }
                    }

                    var basePrice = changes.FirstOrDefault(c => c.IsBaseUnit)?.Price
                        ?? group.First().Price;

                    await _branchProductService.UpdateProductAsync(
                        branchId,
                        product.ProductId,
                        new UpdateBranchProductCommand
                        {
                            RetailPrice = basePrice,
                            Status = product.Status,
                            UnitPrices = changes,
                        },
                        cancellationToken);
                }
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

    private static IReadOnlyList<BranchProductUnitPriceDto> GetEditableUnits(BranchProductDto product) =>
        product.UnitPrices.Count > 0
            ? product.UnitPrices
            : [new BranchProductUnitPriceDto
            {
                UnitName = string.IsNullOrWhiteSpace(product.Unit) ? "Cái" : product.Unit,
                Price = product.RetailPrice,
                IsBaseUnit = true,
            }];

    private async Task<BranchProductExcelImportResultDto> ImportUnitsAsync(
        IXLWorksheet sheet, Guid branchId, string branchCode, string branchName,
        string fileName, BranchProductQueryFilter? filter, CancellationToken cancellationToken)
    {
        for (var i = 0; i < UnitHeaders.Length; i++)
        {
            if (sheet.Cell(1, i + 1).GetString() != UnitHeaders[i])
                throw new BusinessException("INVALID_UNIT_SHEET", $"Tiêu đề cột {i + 1} của sheet {UnitSheetName} không hợp lệ.");
        }

        var preview = filter?.PreviewOnly == true;
        await using var transaction = !preview && _db != null
            ? await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken)
            : null;
        var products = ApplyScreenFilter(await GetAllBranchProductsAsync(branchId, cancellationToken), filter)
            .DistinctBy(p => p.ProductId)
            .ToDictionary(p => p.ProductId);
        var result = new BranchProductExcelImportResultDto
        {
            BranchId = branchId, BranchCode = branchCode, BranchName = branchName,
            FileName = fileName, IsPreview = preview,
        };
        var seen = new HashSet<(Guid, Guid?)>();
        var updates = new List<(BranchProductDto Product, BranchProductUnitPriceDto Unit, decimal Price)>();
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 1;
        for (var row = 2; row <= lastRow; row++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var newPrice = sheet.Cell(row, 8);
            if (newPrice.IsEmpty() && !newPrice.HasFormula) continue;

            var entry = new BranchProductExcelImportRowResultDto
            {
                Row = row, Sku = sheet.Cell(row, 3).GetString(), ProductName = sheet.Cell(row, 4).GetString(),
                Status = "ERROR",
            };
            result.Rows.Add(entry);
            var unitName = sheet.Cell(row, 6).GetString();
            entry.UnitPricesSummary = unitName;
            if (!Guid.TryParse(sheet.Cell(row, 1).GetString(), out var fileBranch) || fileBranch != branchId
                || !Guid.TryParse(sheet.Cell(row, 2).GetString(), out var productId)
                || !products.TryGetValue(productId, out var product) || product.Sku != entry.Sku)
            {
                entry.Message = "Sản phẩm không khớp với cửa hàng hoặc bộ lọc hiện tại.";
                continue;
            }
            entry.ProductId = productId.ToString();
            var unitIdText = sheet.Cell(row, 5).GetString();
            var unit = GetEditableUnits(product).FirstOrDefault(u =>
                string.Equals(u.UnitId?.ToString() ?? string.Empty, unitIdText, StringComparison.OrdinalIgnoreCase));
            if (unit == null || unit.UnitName != unitName)
            {
                entry.Message = "Thông tin đơn vị đã thay đổi. Hãy xuất mẫu mới.";
                continue;
            }
            if (!seen.Add((productId, unit.UnitId)))
            {
                entry.Message = "Đơn vị bị trùng trong file.";
                continue;
            }
            var previousPrice = sheet.Cell(row, 7);
            if (previousPrice.HasFormula || previousPrice.DataType != XLDataType.Number
                || !previousPrice.TryGetValue<decimal>(out var original) || original != unit.Price)
            {
                entry.Message = "Giá hiện tại đã thay đổi. Hãy xuất mẫu mới.";
                continue;
            }
            if (newPrice.HasFormula || newPrice.DataType != XLDataType.Number
                || !newPrice.TryGetValue<decimal>(out var price) || price <= 0m
                || price > 999_999_999_999.99m || decimal.Round(price, 2) != price)
            {
                entry.Message = "Giá mới phải là ô số dương, tối đa 12 chữ số phần nguyên và 2 chữ số thập phân.";
                continue;
            }
            entry.Status = "OK";
            entry.UnitPricesSummary = $"{unitName}: {original:N2} → {price:N2} đ";
            entry.Message = original == price ? "Giá giữ nguyên." : preview ? "Sẵn sàng cập nhật." : "Đã cập nhật.";
            if (original != price) updates.Add((product, unit, price));
        }

        if (result.Rows.Count == 0)
            throw new BusinessException("EMPTY_DATA", "Hãy nhập ít nhất một ô Giá mới trong sheet giá theo đơn vị.");

        if (result.Rows.Any(r => r.Status == "ERROR"))
        {
            foreach (var row in result.Rows.Where(r => r.Status == "OK"))
            {
                row.Status = "ERROR";
                row.Message = "Chưa áp dụng vì file còn dòng lỗi.";
            }
        }
        else if (!preview && updates.Count > 0)
        {
            if (transaction == null)
                throw new InvalidOperationException("Cần giao dịch cơ sở dữ liệu để nhập giá theo đơn vị.");
            foreach (var group in updates.GroupBy(update => update.Product.ProductId))
            {
                var product = group.First().Product;
                var units = GetEditableUnits(product);
                var baseUnit = units.FirstOrDefault(u => u.IsBaseUnit) ?? units[0];
                var changes = group.Select(change => new BranchProductUnitPriceDto
                {
                    UnitId = change.Unit.UnitId,
                    UnitName = change.Unit.UnitName,
                    ConversionRate = change.Unit.ConversionRate,
                    IsBaseUnit = change.Unit == baseUnit,
                    Price = change.Price,
                }).ToList();
                var basePrice = changes.FirstOrDefault(u => u.IsBaseUnit)?.Price ?? product.RetailPrice;
                if (!changes.Any(u => u.IsBaseUnit))
                    changes.Add(new BranchProductUnitPriceDto { IsBaseUnit = true, Price = basePrice });
                await _branchProductService.UpdateProductAsync(branchId, product.ProductId,
                    new UpdateBranchProductCommand { RetailPrice = basePrice, UnitPrices = changes }, cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
        }

        result.Summary = new BranchProductExcelImportSummaryDto
        {
            TotalRows = result.Rows.Count,
            SuccessCount = result.Rows.Count(r => r.Status == "OK"),
            FailedCount = result.Rows.Count(r => r.Status != "OK"),
        };
        return result;
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

    private async Task<List<BranchProductDto>> GetAllBranchProductsAsync(
        Guid branchId,
        CancellationToken cancellationToken)
    {
        const int pageSize = 1000;
        var products = new List<BranchProductDto>();
        for (var pageIndex = 1; ; pageIndex++)
        {
            var page = await _branchProductService.GetBranchProductsAsync(
                branchId,
                new BranchProductQueryFilter { PageIndex = pageIndex, PageSize = pageSize },
                cancellationToken);
            products.AddRange(page.Items);
            if (!page.Items.Any() || (long)pageIndex * pageSize >= page.TotalItems)
            {
                return products;
            }
        }
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
            "Unit ID" or "UnitId" or "Mã đơn vị" or "Ma don vi" => "UnitId",
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
