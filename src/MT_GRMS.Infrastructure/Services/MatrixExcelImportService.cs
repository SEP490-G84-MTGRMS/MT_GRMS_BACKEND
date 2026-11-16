using System.Globalization;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.PurchaseOrders;
using MT_GRMS.Application.PurchaseOrders.Dtos;

namespace MT_GRMS.Infrastructure.Services;

public class MatrixExcelImportService : IMatrixExcelImportService
{
    private static readonly Regex DateKeyRegex = new(@"\[(\d{4}-\d{2}-\d{2})\]", RegexOptions.Compiled);

    private readonly IPurchaseOrderService _poService;
    private readonly ITenantContext _tenantContext;

    public MatrixExcelImportService(IPurchaseOrderService poService, ITenantContext tenantContext)
    {
        _poService = poService;
        _tenantContext = tenantContext;
    }

    public async Task<MatrixExcelValidationResultDto> PreviewAsync(
        Stream fileStream,
        MatrixExcelPreviewQuery query,
        CancellationToken cancellationToken = default)
    {
        var result = new MatrixExcelValidationResultDto
        {
            FileName = query.FileName ?? "unknown.xlsx",
            FileSize = query.FileSize,
            ScopeStatus = new MatrixExcelScopeStatusDto
            {
                ProductsInFilterCount = query.AllowedProducts?.Count ?? 0,
                PeriodExpected =
                    $"{query.ViewFrom:dd/MM/yyyy} - {query.ViewFrom.AddDays(Math.Max(1, query.DurationDays) - 1):dd/MM/yyyy}"
            }
        };

        var matrix = await _poService.GetOrderMatrixAsync(new OrderMatrixQuery
        {
            BranchId = query.BranchId,
            SupplierId = query.SupplierId,
            ViewFrom = query.ViewFrom,
            DurationDays = Math.Max(1, query.DurationDays)
        }, cancellationToken);

        result.ScopeStatus.BranchExpected = $"{matrix.BranchName} ({query.BranchId})";
        result.ScopeStatus.SupplierExpected = query.SupplierId.HasValue && query.SupplierId.Value != Guid.Empty
            ? query.SupplierId.Value.ToString()
            : "ALL";

        var allowedSet = BuildAllowedSet(query.AllowedProducts, matrix.Products, result);
        if (result.Summary.ErrorCount > 0 && allowedSet.Count == 0 && (query.AllowedProducts?.Count ?? 0) > 0)
        {
            result.Valid = false;
            return result;
        }

        ParsedSheet? parsed;
        try
        {
            parsed = ParseWorkbook(fileStream);
        }
        catch (Exception ex)
        {
            AddError(result, 0, "-", "-", "-", "-", "-", "FORMAT_INVALID",
                $"Không thể đọc file Excel: {ex.Message}");
            result.Valid = false;
            return result;
        }

        if (parsed == null || parsed.Rows.Count == 0)
        {
            AddError(result, 0, "-", "-", "-", "-", "-", "SHEET_NOT_FOUND",
                "Không tìm thấy sheet PO_Matrix hoặc không có dữ liệu.");
            result.Valid = false;
            return result;
        }

        ValidateScopeBanners(parsed, query, matrix, result);
        if (!result.ScopeStatus.BranchMatched || !result.ScopeStatus.SupplierMatched)
        {
            result.Valid = false;
            return result;
        }

        if (parsed.SkuCol < 0)
        {
            AddError(result, parsed.HeaderRow + 1, "-", "-", "-", "-", "-", "HEADER_MISSING",
                "Không tìm thấy cột \"Mã sản phẩm\" trong file Excel.");
            result.Valid = false;
            return result;
        }

        if (parsed.DayColumns.Count == 0)
        {
            AddError(result, parsed.HeaderRow + 1, "-", "-", "-", "-", "-", "NO_DELIVERY_DATE",
                "Không tìm thấy cột ngày giao hàng dạng [yyyy-MM-dd] trong file.");
            result.Valid = false;
            return result;
        }

        var viewFrom = query.ViewFrom.Date;
        var viewTo = viewFrom.AddDays(Math.Max(1, query.DurationDays) - 1);
        var periodOk = parsed.DayColumns.Any(d => d.Date >= viewFrom && d.Date <= viewTo);
        result.ScopeStatus.PeriodMatched = periodOk;
        if (!periodOk)
        {
            AddError(result, parsed.HeaderRow + 1, "-", "-", "-", "-", "-", "INVALID_PERIOD",
                $"Không có cột ngày nào thuộc kỳ đang mở ({result.ScopeStatus.PeriodExpected}).");
        }

        var productsBySku = matrix.Products
            .GroupBy(p => (p.Sku ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var codeToSupplierId = matrix.SupplierConfigs
            .Where(c => !string.IsNullOrWhiteSpace(c.SupplierCode))
            .GroupBy(c => c.SupplierCode.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().SupplierId, StringComparer.OrdinalIgnoreCase);

        foreach (var cfg in matrix.SupplierConfigs)
        {
            codeToSupplierId.TryAdd(cfg.SupplierId.ToString(), cfg.SupplierId);
        }

        var cellLookup = matrix.Cells.ToDictionary(
            c => (c.ProductId, c.SupplierId, c.DeliveryDate.Date),
            c => c);

        var dayStatusLookup = matrix.DayStatuses.ToDictionary(
            d => (d.SupplierId, d.DeliveryDate.Date),
            d => d);

        var isStoreOwner = IsStoreOwnerRole();
        var seenSkuSupplier = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in parsed.Rows)
        {
            var excelRow = row.ExcelRowNumber;
            var rawSku = row.Sku.Trim();
            if (string.IsNullOrWhiteSpace(rawSku))
            {
                continue;
            }

            if (!productsBySku.TryGetValue(rawSku, out var candidates) || candidates.Count == 0)
            {
                AddError(result, excelRow, rawSku, row.Name, "-", "-", rawSku, "PRODUCT_NOT_IN_MATRIX",
                    $"Mã sản phẩm \"{rawSku}\" không thuộc ma trận chi nhánh đang mở.");
                continue;
            }

            Guid supplierId;
            if (!string.IsNullOrWhiteSpace(row.SupplierCode))
            {
                if (Guid.TryParse(row.SupplierCode.Trim(), out var sidFromGuid))
                {
                    supplierId = sidFromGuid;
                }
                else if (!codeToSupplierId.TryGetValue(row.SupplierCode.Trim(), out supplierId))
                {
                    AddError(result, excelRow, rawSku, row.Name, "-", "-", row.SupplierCode, "SUPPLIER_MISMATCH",
                        $"Mã NCC \"{row.SupplierCode}\" không khớp nhà cung cấp trên ma trận.");
                    continue;
                }
            }
            else if (query.SupplierId.HasValue && query.SupplierId.Value != Guid.Empty)
            {
                supplierId = query.SupplierId.Value;
            }
            else if (candidates.Count == 1)
            {
                supplierId = candidates[0].SupplierId;
            }
            else
            {
                AddError(result, excelRow, rawSku, row.Name, "-", "-", "-", "PRODUCT_AMBIGUOUS",
                    $"SKU \"{rawSku}\" thuộc nhiều NCC. Cần cột Mã NCC khi đang lọc Tất cả NCC.");
                continue;
            }

            var product = candidates.FirstOrDefault(c => c.SupplierId == supplierId);
            if (product == null)
            {
                AddError(result, excelRow, rawSku, row.Name, "-", "-", row.SupplierCode, "PRODUCT_NOT_IN_MATRIX",
                    $"SKU \"{rawSku}\" không gắn với NCC đã chọn trên ma trận.");
                continue;
            }

            var dupKey = $"{rawSku}__{supplierId}";
            if (!seenSkuSupplier.Add(dupKey))
            {
                AddError(result, excelRow, rawSku, product.Name, "-", "-", rawSku, "DUPLICATE_PRODUCT",
                    $"Mã sản phẩm \"{rawSku}\" (NCC) bị trùng trong file.");
                continue;
            }

            if (!allowedSet.Contains((product.Id, supplierId)))
            {
                result.Summary.OutOfFilterCount++;
                AddError(result, excelRow, rawSku, product.Name, "-", "-", rawSku, "PRODUCT_OUT_OF_FILTER",
                    $"Sản phẩm \"{rawSku}\" không nằm trong bộ lọc đang hiển thị trên màn hình.");
                continue;
            }

            if (product.IsRowDisabled)
            {
                AddError(result, excelRow, rawSku, product.Name, "-", "-", "-", "PRODUCT_INACTIVE",
                    product.RowDisabledReason ?? "Dòng sản phẩm đang bị khóa trên ma trận.");
                continue;
            }

            foreach (var dayCol in parsed.DayColumns)
            {
                if (dayCol.Date < viewFrom || dayCol.Date > viewTo)
                {
                    var rawOut = row.Values.GetValueOrDefault(dayCol.ColIndex);
                    if (IsBlank(rawOut))
                    {
                        result.Summary.IgnoredCells++;
                        continue;
                    }

                    AddError(result, excelRow, rawSku, product.Name, dayCol.DateKey, dayCol.Label,
                        rawOut ?? "-", "DATE_OUT_OF_PERIOD",
                        $"Ngày {dayCol.DateKey} nằm ngoài kỳ đang mở trên màn hình.");
                    continue;
                }

                var raw = row.Values.GetValueOrDefault(dayCol.ColIndex);
                if (IsBlank(raw))
                {
                    result.Summary.IgnoredCells++;
                    continue;
                }

                result.Summary.TotalCells++;

                if (!TryParseQty(raw, out var qty, out var qtyError))
                {
                    AddError(result, excelRow, rawSku, product.Name, dayCol.DateKey, dayCol.Label,
                        raw ?? "-", qtyError, qtyError == "NEGATIVE_QUANTITY"
                            ? "Số lượng không được âm."
                            : qtyError == "DECIMAL_QUANTITY_NOT_ALLOWED"
                                ? "Số lượng phải là số nguyên."
                                : "Số lượng không hợp lệ.");
                    continue;
                }

                cellLookup.TryGetValue((product.Id, supplierId, dayCol.Date), out var existing);
                var currentFinal = existing != null
                    ? OrderMatrixRules.ComputeFinalQty(existing.EmployeeQty, existing.StoreQty, existing.IsOwnerAdjusted)
                    : 0m;
                var ownerLocked = existing != null && (existing.IsOwnerAdjusted || existing.StoreQty.HasValue);

                dayStatusLookup.TryGetValue((supplierId, dayCol.Date), out var daySt);
                var status = daySt?.DeliveryDateStatus ?? OrderMatrixRules.StatusOpen;
                var editable = OrderMatrixRules.IsEditableStatus(status)
                               && dayCol.Date >= OrderMatrixRules.GetEarliestAllowedDeliveryDate(OrderMatrixRules.GetVietnamNow());

                if (!editable)
                {
                    if (qty == currentFinal)
                    {
                        result.Summary.IgnoredCells++;
                        continue;
                    }

                    var code = status == OrderMatrixRules.StatusFinalized
                        ? "DATE_ALREADY_FINALIZED"
                        : status is OrderMatrixRules.StatusLocked or OrderMatrixRules.StatusSupplierOff
                            ? "CUTOFF_PASSED"
                            : "BLOCKED_CELL_MODIFIED";
                    AddError(result, excelRow, rawSku, product.Name, dayCol.DateKey, dayCol.Label,
                        qty.ToString(CultureInfo.InvariantCulture), code,
                        $"Ô ngày {dayCol.DateKey} đang khóa ({status}), không cho đổi số lượng.");
                    continue;
                }

                if (!isStoreOwner && ownerLocked)
                {
                    result.Summary.SkippedOwnerLocked++;
                    result.Summary.WarningCount++;
                    result.Warnings.Add(new MatrixExcelIssueDto
                    {
                        Type = "WARNING",
                        Row = excelRow,
                        ProductCode = rawSku,
                        ProductName = product.Name,
                        DeliveryDate = dayCol.DateKey,
                        DateLabel = dayCol.Label,
                        InputValue = qty.ToString(CultureInfo.InvariantCulture),
                        Code = "OWNER_LOCKED_SKIPPED",
                        Message = "Ô đã được Store Owner chốt — bỏ qua khi nhân viên nạp Excel."
                    });
                    continue;
                }

                if (qty > 0 && OrderMatrixRules.ViolatesMoq(qty, product.Moq))
                {
                    result.Summary.ErrorCount++;
                    result.Errors.Add(new MatrixExcelIssueDto
                    {
                        Type = "ERROR",
                        Row = excelRow,
                        ProductCode = rawSku,
                        ProductName = product.Name,
                        DeliveryDate = dayCol.DateKey,
                        DateLabel = dayCol.Label,
                        InputValue = qty.ToString(CultureInfo.InvariantCulture),
                        Code = "MOQ_NOT_MET",
                        Message = $"Số lượng {qty} nhỏ hơn MOQ {product.Moq}. Không được lưu bản thảo."
                    });
                    continue;
                }

                if (qty > 0 && OrderMatrixRules.ViolatesOrderMultiple(qty, product.OrderMultiple))
                {
                    result.Summary.WarningCount++;
                    result.Warnings.Add(new MatrixExcelIssueDto
                    {
                        Type = "WARNING",
                        Row = excelRow,
                        ProductCode = rawSku,
                        ProductName = product.Name,
                        DeliveryDate = dayCol.DateKey,
                        DateLabel = dayCol.Label,
                        InputValue = qty.ToString(CultureInfo.InvariantCulture),
                        Code = "ORDER_MULTIPLE_INVALID",
                        Message = $"Số lượng {qty} không chia hết cho bội số {product.OrderMultiple}."
                    });
                }

                result.Summary.ValidCells++;
                result.CellsToApply.Add(new MatrixExcelCellApplyDto
                {
                    ExcelRow = excelRow,
                    ProductId = product.Id,
                    SupplierId = supplierId,
                    DeliveryDate = dayCol.Date,
                    DateLabel = dayCol.Label,
                    ProductCode = rawSku,
                    ProductName = product.Name,
                    CurrentQty = currentFinal,
                    Quantity = qty
                });
            }
        }

        result.Valid = result.Summary.ErrorCount == 0;
        return result;
    }

    public async Task<MatrixExcelImportResultDto> ImportAsync(
        ImportMatrixExcelCommand command,
        CancellationToken cancellationToken = default)
    {
        var result = new MatrixExcelImportResultDto
        {
            FileName = command.FileName ?? "matrix.xlsx"
        };

        var durationDays = Math.Max(1, (command.ViewTo.Date - command.ViewFrom.Date).Days + 1);
        var matrix = await _poService.GetOrderMatrixAsync(new OrderMatrixQuery
        {
            BranchId = command.BranchId,
            SupplierId = command.SupplierId,
            ViewFrom = command.ViewFrom,
            DurationDays = durationDays
        }, cancellationToken);

        var allowedSet = new HashSet<(Guid ProductId, Guid SupplierId)>();
        foreach (var a in command.AllowedProducts ?? new List<MatrixExcelAllowedProductDto>())
        {
            if (matrix.Products.Any(p => p.Id == a.ProductId && p.SupplierId == a.SupplierId))
            {
                allowedSet.Add((a.ProductId, a.SupplierId));
            }
        }

        var cellLookup = matrix.Cells.ToDictionary(
            c => (c.ProductId, c.SupplierId, c.DeliveryDate.Date),
            c => c);
        var dayStatusLookup = matrix.DayStatuses.ToDictionary(
            d => (d.SupplierId, d.DeliveryDate.Date),
            d => d);
        var productLookup = matrix.Products.ToDictionary(p => (p.Id, p.SupplierId));

        var isStoreOwner = IsStoreOwnerRole();
        var draftCells = new List<OrderMatrixCellDto>();
        var applied = new List<(DateTime Date, string Label, Guid ProductId)>();

        foreach (var cell in command.Cells ?? new List<ImportMatrixExcelCellDto>())
        {
            var date = cell.DeliveryDate.Date;
            var dateKey = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var label = string.IsNullOrWhiteSpace(cell.DateLabel) ? dateKey : cell.DateLabel!;
            var sku = cell.ProductCode ?? "-";
            var name = cell.ProductName ?? "-";
            var row = cell.ExcelRow ?? 0;

            if (!allowedSet.Contains((cell.ProductId, cell.SupplierId)))
            {
                result.Errors.Add(Issue("ERROR", row, sku, name, dateKey, label, cell.Quantity,
                    "PRODUCT_OUT_OF_FILTER", "Sản phẩm không thuộc bộ lọc đang mở trên màn hình."));
                continue;
            }

            if (!productLookup.TryGetValue((cell.ProductId, cell.SupplierId), out var product))
            {
                result.Errors.Add(Issue("ERROR", row, sku, name, dateKey, label, cell.Quantity,
                    "PRODUCT_NOT_IN_MATRIX", "Sản phẩm không thuộc ma trận chi nhánh."));
                continue;
            }

            cellLookup.TryGetValue((cell.ProductId, cell.SupplierId, date), out var existing);
            var ownerLocked = existing != null && (existing.IsOwnerAdjusted || existing.StoreQty.HasValue);

            dayStatusLookup.TryGetValue((cell.SupplierId, date), out var daySt);
            var status = daySt?.DeliveryDateStatus ?? OrderMatrixRules.StatusOpen;
            var editable = OrderMatrixRules.IsEditableStatus(status)
                           && date >= OrderMatrixRules.GetEarliestAllowedDeliveryDate(OrderMatrixRules.GetVietnamNow());

            if (!editable)
            {
                result.Errors.Add(Issue("ERROR", row, sku, product.Name, dateKey, label, cell.Quantity,
                    "BLOCKED_CELL_MODIFIED", $"Ô ngày {dateKey} đang khóa ({status})."));
                continue;
            }

            if (!isStoreOwner && ownerLocked)
            {
                result.Summary.SkippedOwnerLocked++;
                result.Warnings.Add(Issue("WARNING", row, sku, product.Name, dateKey, label, cell.Quantity,
                    "OWNER_LOCKED_SKIPPED", "Ô đã được Store Owner chốt — bỏ qua."));
                continue;
            }

            if (cell.Quantity > 0 && OrderMatrixRules.ViolatesMoq(cell.Quantity, product.Moq))
            {
                result.Errors.Add(Issue("ERROR", row, sku, product.Name, dateKey, label, cell.Quantity,
                    "MOQ_NOT_MET", $"Số lượng {cell.Quantity} nhỏ hơn MOQ {product.Moq}. Không được lưu bản thảo."));
                continue;
            }

            var employeeQty = existing?.EmployeeQty ?? 0m;
            decimal? storeQty = existing?.StoreQty;
            var isOwnerAdjusted = existing?.IsOwnerAdjusted ?? false;
            var finalQty = cell.Quantity;

            if (isStoreOwner)
            {
                storeQty = cell.Quantity;
                isOwnerAdjusted = true;
            }
            else
            {
                employeeQty = cell.Quantity;
                finalQty = cell.Quantity;
            }

            draftCells.Add(new OrderMatrixCellDto
            {
                ProductId = cell.ProductId,
                SupplierId = cell.SupplierId,
                DeliveryDate = date,
                EmployeeQty = employeeQty,
                StoreQty = storeQty,
                IsOwnerAdjusted = isOwnerAdjusted,
                FinalQty = OrderMatrixRules.ComputeFinalQty(employeeQty, storeQty, isOwnerAdjusted)
            });

            applied.Add((date, label, cell.ProductId));
        }

        result.Summary.ErrorCount = result.Errors.Count;
        result.Summary.WarningCount = result.Warnings.Count;

        if (draftCells.Count == 0)
        {
            result.Success = false;
            result.Summary.AppliedCells = 0;
            result.Summary.SuccessDayCount = 0;
            return result;
        }

        // Merge with existing editable cells not in import payload so Save không mất ô khác
        var importedKeys = draftCells
            .Select(c => (c.ProductId, c.SupplierId, c.DeliveryDate.Date))
            .ToHashSet();

        foreach (var existing in matrix.Cells)
        {
            var key = (existing.ProductId, existing.SupplierId, existing.DeliveryDate.Date);
            if (importedKeys.Contains(key)) continue;
            draftCells.Add(existing);
        }

        var saved = await _poService.SaveMatrixDraftAsync(new SaveMatrixDraftCommand
        {
            BranchId = command.BranchId,
            SupplierId = command.SupplierId,
            ViewFrom = command.ViewFrom,
            ViewTo = command.ViewTo,
            Note = $"Import Excel: {result.FileName}",
            Cells = draftCells
        }, cancellationToken);

        result.DraftPoId = saved.Id;
        result.Summary.AppliedCells = applied.Count;
        result.SuccessDays = applied
            .GroupBy(a => a.Date)
            .OrderBy(g => g.Key)
            .Select(g => new MatrixExcelSuccessDayDto
            {
                DeliveryDate = g.Key,
                DateLabel = g.First().Label,
                AppliedCells = g.Count(),
                ProductCount = g.Select(x => x.ProductId).Distinct().Count()
            })
            .ToList();
        result.Summary.SuccessDayCount = result.SuccessDays.Count;
        result.Success = result.Summary.ErrorCount == 0 || result.Summary.AppliedCells > 0;
        return result;
    }

    private HashSet<(Guid ProductId, Guid SupplierId)> BuildAllowedSet(
        List<MatrixExcelAllowedProductDto>? allowed,
        IReadOnlyList<OrderMatrixProductDto> matrixProducts,
        MatrixExcelValidationResultDto result)
    {
        var set = new HashSet<(Guid, Guid)>();
        var matrixSet = matrixProducts.Select(p => (p.Id, p.SupplierId)).ToHashSet();
        foreach (var a in allowed ?? new List<MatrixExcelAllowedProductDto>())
        {
            if (!matrixSet.Contains((a.ProductId, a.SupplierId)))
            {
                AddError(result, 0, a.ProductId.ToString(), "-", "-", "-", "-", "PRODUCT_NOT_IN_MATRIX",
                    "allowedProducts chứa mục không thuộc ma trận hiện tại.");
                continue;
            }
            set.Add((a.ProductId, a.SupplierId));
        }

        // Nếu FE không gửi allow-list → mặc định toàn bộ matrix (tránh chặn cứng khi empty)
        if (set.Count == 0 && (allowed == null || allowed.Count == 0))
        {
            foreach (var p in matrixProducts)
            {
                set.Add((p.Id, p.SupplierId));
            }
        }

        return set;
    }

    private static void ValidateScopeBanners(
        ParsedSheet parsed,
        MatrixExcelPreviewQuery query,
        OrderMatrixResponse matrix,
        MatrixExcelValidationResultDto result)
    {
        var branchToken = ExtractAfter(parsed.BranchBanner, "Mã hệ thống:") 
                          ?? ExtractAfter(parsed.BranchBanner, "Mã CN:");
        result.ScopeStatus.BranchActual = branchToken ?? parsed.BranchBanner;

        var branchOk = false;
        if (!string.IsNullOrWhiteSpace(branchToken))
        {
            if (Guid.TryParse(branchToken.Trim(), out var bid))
            {
                branchOk = bid == query.BranchId;
            }
            else
            {
                branchOk = string.Equals(branchToken.Trim(), matrix.BranchName, StringComparison.OrdinalIgnoreCase);
            }
        }

        result.ScopeStatus.BranchMatched = branchOk;
        if (!branchOk)
        {
            AddError(result, 2, "-", "-", "-", "-", branchToken ?? "-", "BRANCH_MISMATCH",
                $"File Excel thuộc chi nhánh \"{branchToken}\", không khớp chi nhánh đang mở ({query.BranchId}).");
        }

        var supplierToken = ExtractAfter(parsed.SupplierBanner, "Mã NCC:");
        result.ScopeStatus.SupplierActual = supplierToken ?? parsed.SupplierBanner;

        var expectAll = !query.SupplierId.HasValue || query.SupplierId.Value == Guid.Empty;
        bool supplierOk;
        if (expectAll)
        {
            supplierOk = string.IsNullOrWhiteSpace(supplierToken)
                         || supplierToken.Contains("ALL", StringComparison.OrdinalIgnoreCase)
                         || Guid.TryParse(supplierToken.Trim(), out _)
                         || matrix.SupplierConfigs.Any(c =>
                             string.Equals(c.SupplierCode, supplierToken?.Trim(), StringComparison.OrdinalIgnoreCase)
                             || c.SupplierId.ToString().Equals(supplierToken?.Trim(), StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            supplierOk = false;
            if (!string.IsNullOrWhiteSpace(supplierToken))
            {
                if (Guid.TryParse(supplierToken.Trim(), out var sid))
                {
                    supplierOk = sid == query.SupplierId!.Value;
                }
                else
                {
                    var cfg = matrix.SupplierConfigs.FirstOrDefault(c => c.SupplierId == query.SupplierId);
                    supplierOk = cfg != null &&
                                 string.Equals(cfg.SupplierCode, supplierToken.Trim(), StringComparison.OrdinalIgnoreCase);
                }
            }
        }

        result.ScopeStatus.SupplierMatched = supplierOk;
        if (!supplierOk)
        {
            AddError(result, 3, "-", "-", "-", "-", supplierToken ?? "-", "SUPPLIER_MISMATCH",
                $"File Excel thuộc NCC \"{supplierToken}\", không khớp NCC đang chọn trên màn hình.");
        }
    }

    private static string? ExtractAfter(string? banner, string marker)
    {
        if (string.IsNullOrWhiteSpace(banner)) return null;
        var idx = banner.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        var rest = banner[(idx + marker.Length)..].Trim();
        var cut = rest.IndexOfAny(new[] { '|', '\n', '\r' });
        if (cut >= 0) rest = rest[..cut].Trim();
        return rest.Trim();
    }

    private bool IsStoreOwnerRole()
    {
        var role = (_tenantContext.CurrentRole ?? string.Empty).Trim().ToUpperInvariant();
        return string.IsNullOrEmpty(role) || role is "STORE_OWNER" or "ADMIN";
    }

    private static bool IsBlank(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return true;
        var t = raw.Trim();
        return t is "-" or "—" or "–" or "N/A" or "n/a";
    }

    private static bool TryParseQty(string? raw, out decimal qty, out string errorCode)
    {
        qty = 0;
        errorCode = "INVALID_QUANTITY";
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var t = raw.Trim().Replace(",", "");
        if (!decimal.TryParse(t, NumberStyles.Number, CultureInfo.InvariantCulture, out qty)
            && !decimal.TryParse(t, NumberStyles.Number, CultureInfo.GetCultureInfo("vi-VN"), out qty))
        {
            return false;
        }

        if (qty < 0)
        {
            errorCode = "NEGATIVE_QUANTITY";
            return false;
        }

        if (qty != Math.Truncate(qty))
        {
            errorCode = "DECIMAL_QUANTITY_NOT_ALLOWED";
            return false;
        }

        if (qty > 1_000_000m)
        {
            errorCode = "QUANTITY_TOO_LARGE";
            return false;
        }

        return true;
    }

    private static void AddError(
        MatrixExcelValidationResultDto result,
        int row,
        string productCode,
        string productName,
        string deliveryDate,
        string dateLabel,
        string inputValue,
        string code,
        string message)
    {
        result.Summary.ErrorCount++;
        result.Errors.Add(new MatrixExcelIssueDto
        {
            Type = "ERROR",
            Row = row,
            ProductCode = productCode,
            ProductName = productName,
            DeliveryDate = deliveryDate,
            DateLabel = dateLabel,
            InputValue = inputValue,
            Code = code,
            Message = message
        });
    }

    private static MatrixExcelIssueDto Issue(
        string type, int row, string sku, string name, string date, string label, decimal qty, string code, string message)
        => new()
        {
            Type = type,
            Row = row,
            ProductCode = sku,
            ProductName = name,
            DeliveryDate = date,
            DateLabel = label,
            InputValue = qty.ToString(CultureInfo.InvariantCulture),
            Code = code,
            Message = message
        };

    private static ParsedSheet ParseWorkbook(Stream stream)
    {
        using var book = new XLWorkbook(stream);
        var ws = book.Worksheets.FirstOrDefault(s =>
                     string.Equals(s.Name, "PO_Matrix", StringComparison.OrdinalIgnoreCase))
                 ?? book.Worksheets.First();

        var used = ws.RangeUsed();
        if (used == null) return new ParsedSheet();

        var rowCount = used.RowCount();
        var colCount = used.ColumnCount();
        var grid = new string[rowCount + 1, colCount + 1];
        for (var r = 1; r <= rowCount; r++)
        {
            for (var c = 1; c <= colCount; c++)
            {
                grid[r, c] = ws.Cell(r, c).GetFormattedString()?.Trim() ?? string.Empty;
            }
        }

        var parsed = new ParsedSheet
        {
            BranchBanner = grid[Math.Min(2, rowCount), 1],
            SupplierBanner = grid[Math.Min(3, rowCount), 1]
        };

        var headerRow = -1;
        for (var r = 1; r <= Math.Min(15, rowCount); r++)
        {
            for (var c = 1; c <= colCount; c++)
            {
                var val = NormalizeHeader(grid[r, c]);
                if (IsSkuHeader(val))
                {
                    headerRow = r;
                    parsed.SkuCol = c;
                }
                if (val.Contains("mã ncc") || val is "mã nhà cung cấp" or "supplier code" or "ncc")
                {
                    parsed.SupplierCol = c;
                }
                if (val.Contains("tên sản phẩm") || val is "product name")
                {
                    parsed.NameCol = c;
                }
            }
            if (headerRow > 0 && parsed.SkuCol > 0) break;
        }

        if (headerRow < 0) return parsed;
        parsed.HeaderRow = headerRow;

        for (var c = 1; c <= colCount; c++)
        {
            var header = grid[headerRow, c];
            var m = DateKeyRegex.Match(header.Replace('\n', ' '));
            if (!m.Success) continue;
            if (!DateTime.TryParseExact(m.Groups[1].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var dt))
            {
                continue;
            }

            parsed.DayColumns.Add(new DayCol
            {
                ColIndex = c,
                Date = dt.Date,
                DateKey = m.Groups[1].Value,
                Label = header.Split('\n')[0].Trim()
            });
        }

        for (var r = headerRow + 1; r <= rowCount; r++)
        {
            var sku = parsed.SkuCol > 0 ? grid[r, parsed.SkuCol] : string.Empty;
            if (string.IsNullOrWhiteSpace(sku)) continue;
            if (sku.Contains("TỔNG CỘNG", StringComparison.OrdinalIgnoreCase)) break;

            var row = new DataRow
            {
                ExcelRowNumber = r,
                Sku = sku,
                Name = parsed.NameCol > 0 ? grid[r, parsed.NameCol] : string.Empty,
                SupplierCode = parsed.SupplierCol > 0 ? grid[r, parsed.SupplierCol] : string.Empty
            };
            foreach (var d in parsed.DayColumns)
            {
                row.Values[d.ColIndex] = grid[r, d.ColIndex];
            }
            parsed.Rows.Add(row);
        }

        return parsed;
    }

    private static string NormalizeHeader(string? s)
        => (s ?? string.Empty).Trim().ToLowerInvariant().Replace('\n', ' ');

    private static bool IsSkuHeader(string val)
        => val is "mã sản phẩm" or "mã sku" or "sku" or "product code" or "msコード"
           || val.Contains("mã sản phẩm");

    private sealed class ParsedSheet
    {
        public string? BranchBanner { get; set; }
        public string? SupplierBanner { get; set; }
        public int HeaderRow { get; set; }
        public int SkuCol { get; set; } = -1;
        public int SupplierCol { get; set; } = -1;
        public int NameCol { get; set; } = -1;
        public List<DayCol> DayColumns { get; } = new();
        public List<DataRow> Rows { get; } = new();
    }

    private sealed class DayCol
    {
        public int ColIndex { get; set; }
        public DateTime Date { get; set; }
        public string DateKey { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
    }

    private sealed class DataRow
    {
        public int ExcelRowNumber { get; set; }
        public string Sku { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string SupplierCode { get; set; } = string.Empty;
        public Dictionary<int, string> Values { get; } = new();
    }
}
