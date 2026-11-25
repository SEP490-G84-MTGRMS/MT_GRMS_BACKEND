using MT_GRMS.Application.Admin.Validators;
using System.Globalization;
using ClosedXML.Excel;
using MT_GRMS.Application.Admin.Dtos;
using MT_GRMS.Application.Admin.Repositories;
using MT_GRMS.Application.Admin.Services;
using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Entities.Catalog;

namespace MT_GRMS.Infrastructure.Services;

public class MasterCatalogExcelService : IMasterCatalogExcelService
{
    private const string SheetName = "HangHoaMau_FMCG";
    private static readonly XLColor BrandBlue = XLColor.FromHtml("#0066FF");
    private static readonly XLColor HeaderBg = XLColor.FromHtml("#0F766E");
    private static readonly XLColor ExampleRowBg = XLColor.FromHtml("#F8FAFC");
    private static readonly CultureInfo ViCulture = new("vi-VN");

    private readonly IMasterProductRepository _masterRepo;
    private readonly IUnitOfWork _unitOfWork;

    public MasterCatalogExcelService(IMasterProductRepository masterRepo, IUnitOfWork unitOfWork)
    {
        _masterRepo = masterRepo;
        _unitOfWork = unitOfWork;
    }

    public async Task<byte[]> GenerateTemplateAsync(CancellationToken cancellationToken = default)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add(SheetName);

        // Header Columns
        var headers = new[]
        {
            "MaVach_EAN13 (*)",
            "TenSanPham (*)",
            "ThuongHieu (*)",
            "NganhHang (*)",
            "DonViCoBan (*)",
            "GiaThamKhao",
            "ThueGTGT (%)",
            "LinkAnh",
            "QuyCachDongGoi (Ten:HeSo:MaVach; ...)",
            "MoTa"
        };

        for (var i = 0; i < headers.Length; i++)
        {
            var cell = worksheet.Cell(1, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = HeaderBg;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            cell.Style.Border.OutsideBorderColor = XLColor.LightGray;
        }

        worksheet.Row(1).Height = 28;

        // Dữ liệu mẫu (3 dòng FMCG phổ biến nhất)
        var sampleRows = new[]
        {
            new[]
            {
                "8934563138165",
                "Mì Hảo Hảo Tôm Chua Cay 75g",
                "Acecook",
                "Mì ăn liền",
                "Gói",
                "4500",
                "8",
                "https://images.unsplash.com/photo-1569718212165-3a8278d5f624?w=500",
                "Thùng:30:8934563138172",
                "Mì tôm chua cay hương vị quốc dân"
            },
            new[]
            {
                "8935049500544",
                "Nước tăng lực Sting Dâu Chai 330ml",
                "Suntory PepsiCo",
                "Nước giải khát",
                "Chai",
                "10000",
                "8",
                "https://images.unsplash.com/photo-1622483767028-3f66f32aef97?w=500",
                "Lốc:6:8935049500551; Thùng:24:8935049500568",
                "Nước ngọt có gas vị dâu tây đỏ"
            },
            new[]
            {
                "8934822201017",
                "Bột giặt OMO Đỏ Sạch Nhanh 800g",
                "Unilever",
                "Hóa mỹ phẩm & Tẩy rửa",
                "Túi",
                "36000",
                "10",
                "https://images.unsplash.com/photo-1583947215259-38e31be8751f?w=500",
                "Thùng:12:8934822201024",
                "Xoáy bay vết bẩn cứng đầu"
            }
        };

        for (var r = 0; r < sampleRows.Length; r++)
        {
            var rowNum = r + 2;
            for (var c = 0; c < sampleRows[r].Length; c++)
            {
                var cell = worksheet.Cell(rowNum, c + 1);
                cell.Value = sampleRows[r][c];
                cell.Style.Fill.BackgroundColor = ExampleRowBg;
                cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                cell.Style.Border.OutsideBorderColor = XLColor.LightGray;
                if (c == 0) cell.Style.NumberFormat.Format = "@"; // Text format cho Barcode
            }
            worksheet.Row(rowNum).Height = 22;
        }

        // Tự động căn chỉnh độ rộng cột
        worksheet.Columns().AdjustToContents(15.0, 50.0);

        using var memoryStream = new MemoryStream();
        workbook.SaveAs(memoryStream);
        return await Task.FromResult(memoryStream.ToArray());
    }

    public async Task<MasterProductExcelImportResult> ImportExcelAsync(
        Stream fileStream,
        bool upsert = true,
        CancellationToken cancellationToken = default)
    {
        var resultRows = new List<MasterProductExcelImportRow>();
        var errorMessages = new List<string>();
        var successCount = 0;
        var failedCount = 0;

        using var workbook = OpenWorkbook(fileStream);
        var worksheet = workbook.Worksheets.FirstOrDefault(w => w.Name == SheetName)
            ?? workbook.Worksheets.FirstOrDefault();

        if (worksheet == null)
        {
            return new MasterProductExcelImportResult(0, 0, 1, ["Tệp Excel không chứa trang tính (sheet) nào hợp lệ."], []);
        }

        var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 0;
        if (lastRow < 2)
        {
            return new MasterProductExcelImportResult(0, 0, 0, ["Tệp Excel không có dòng dữ liệu nào."], []);
        }

        var totalRows = lastRow - 1;
        var acceptedBarcodes = new HashSet<string>(StringComparer.Ordinal);

        for (var rowIdx = 2; rowIdx <= lastRow; rowIdx++)
        {
            var row = worksheet.Row(rowIdx);
            var barcode = row.Cell(1).GetString()?.Trim() ?? string.Empty;
            var name = row.Cell(2).GetString()?.Trim() ?? string.Empty;
            var brand = row.Cell(3).GetString()?.Trim() ?? string.Empty;
            var category = row.Cell(4).GetString()?.Trim() ?? string.Empty;
            var baseUnit = row.Cell(5).GetString()?.Trim() ?? string.Empty;
            var priceStr = row.Cell(6).GetString()?.Trim();
            var vatStr = row.Cell(7).GetString()?.Trim();
            var imageUrl = row.Cell(8).GetString()?.Trim();
            var packagingStr = row.Cell(9).GetString()?.Trim();
            var desc = row.Cell(10).GetString()?.Trim();

            // Bỏ qua dòng hoàn toàn trống
            if (row.Cells(1, 10).All(cell => cell.IsEmpty()))
            {
                continue;
            }

            // Validation cơ bản
            if (string.IsNullOrWhiteSpace(barcode))
            {
                failedCount++;
                var err = $"Dòng {rowIdx}: Thiếu mã vạch (Barcode EAN-13).";
                errorMessages.Add(err);
                resultRows.Add(new MasterProductExcelImportRow(rowIdx, barcode, name, brand, category, baseUnit, null, 8, imageUrl, packagingStr, false, err));
                continue;
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                failedCount++;
                var err = $"Dòng {rowIdx} ({barcode}): Thiếu tên sản phẩm.";
                errorMessages.Add(err);
                resultRows.Add(new MasterProductExcelImportRow(rowIdx, barcode, name, brand, category, baseUnit, null, 8, imageUrl, packagingStr, false, err));
                continue;
            }

            decimal? suggestedPrice = null;
            decimal vatRate = 8m;
            try
            {
                if (!string.IsNullOrWhiteSpace(priceStr))
                    suggestedPrice = ReadDecimal(priceStr, "Giá bán lẻ");
                if (!string.IsNullOrWhiteSpace(vatStr))
                    vatRate = ReadDecimal(vatStr, "Thuế suất");
                if (acceptedBarcodes.Contains(barcode))
                    throw new FormatException("Mã vạch trùng với một dòng đã nhập trong tệp.");
                var units = ParseUnits(packagingStr);
                var existing = await _masterRepo.GetByBarcodeWithUnitsAsync(barcode, cancellationToken);
                var candidate = new CreateMasterProductRequest(barcode, name,
                    brand.Length > 0 ? brand : existing?.Brand ?? string.Empty,
                    category.Length > 0 ? category : existing?.CategoryName ?? string.Empty,
                    baseUnit.Length > 0 ? baseUnit : existing?.BaseUnit ?? string.Empty,
                    suggestedPrice, vatRate, ImageUrl: imageUrl, Description: desc, Units: units);
                MasterProductValidator.ValidateCreate(candidate);
                if (existing != null)
                {
                    if (!upsert)
                    {
                        failedCount++;
                        var err = $"Dòng {rowIdx} ({barcode}): Mã vạch đã tồn tại (Chế độ bỏ qua trùng).";
                        errorMessages.Add(err);
                        resultRows.Add(new MasterProductExcelImportRow(rowIdx, barcode, name, brand, category, baseUnit, suggestedPrice, vatRate, imageUrl, packagingStr, false, err));
                        continue;
                    }

                    // Cập nhật thông tin
                    existing.Name = name;
                    if (!string.IsNullOrWhiteSpace(brand)) existing.Brand = brand;
                    if (!string.IsNullOrWhiteSpace(category)) existing.CategoryName = category;
                    if (!string.IsNullOrWhiteSpace(baseUnit)) existing.BaseUnit = baseUnit;
                    if (suggestedPrice.HasValue) existing.SuggestedRetailPrice = suggestedPrice.Value;
                    existing.VatRate = vatRate;
                    if (!string.IsNullOrWhiteSpace(imageUrl)) existing.ImageUrl = imageUrl;
                    if (!string.IsNullOrWhiteSpace(desc)) existing.Description = desc;

                    // Parse lại quy cách
                    ApplyUnits(existing, units);
                    _masterRepo.Update(existing);
                }
                else
                {
                    var newProduct = new MasterProduct
                    {
                        Barcode = barcode,
                        Name = name,
                        Brand = candidate.Brand,
                        CategoryName = candidate.CategoryName,
                        BaseUnit = candidate.BaseUnit,
                        SuggestedRetailPrice = suggestedPrice,
                        VatRate = vatRate,
                        TaxCategory = "STANDARD_1PCT",
                        ImageUrl = imageUrl,
                        Description = desc,
                        IsActive = true,
                        UsageCount = 0
                    };

                    ApplyUnits(newProduct, units);
                    await _masterRepo.AddAsync(newProduct, cancellationToken);
                }

                acceptedBarcodes.Add(barcode);
                successCount++;
                resultRows.Add(new MasterProductExcelImportRow(rowIdx, barcode, name, brand, category, baseUnit, suggestedPrice, vatRate, imageUrl, packagingStr, true, null));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failedCount++;
                var err = $"Dòng {rowIdx} ({barcode}): {ex.Message}";
                errorMessages.Add(err);
                resultRows.Add(new MasterProductExcelImportRow(rowIdx, barcode, name, brand, category, baseUnit, suggestedPrice, vatRate, imageUrl, packagingStr, false, err));
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new MasterProductExcelImportResult(totalRows, successCount, failedCount, errorMessages, resultRows);
    }

    private static XLWorkbook OpenWorkbook(Stream stream)
    {
        try
        {
            return new XLWorkbook(stream);
        }
        catch (Exception ex) when (ex is InvalidDataException or FileFormatException or DocumentFormat.OpenXml.Packaging.OpenXmlPackageException or System.Xml.XmlException)
        {
            throw new MT_GRMS.Domain.Exceptions.BusinessException("INVALID_FILE_TYPE", "Không đọc được tệp Excel. Vui lòng chọn tệp .xlsx hợp lệ.");
        }
    }

    private static decimal ReadDecimal(string text, string field)
    {
        const NumberStyles style = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;
        if (decimal.TryParse(text, style, CultureInfo.InvariantCulture, out var value) ||
            decimal.TryParse(text, style, ViCulture, out value))
            return value;
        throw new FormatException($"{field} phải là số hợp lệ.");
    }

    private static List<CreateMasterProductUnitRequest> ParseUnits(string? specs)
    {
        var units = new List<CreateMasterProductUnitRequest>();
        if (string.IsNullOrWhiteSpace(specs)) return units;
        foreach (var segment in specs.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = segment.Split(':', StringSplitOptions.TrimEntries);
            if (parts.Length is < 2 or > 3)
                throw new FormatException("Quy cách đóng gói cần có tên đơn vị và hệ số, có thể thêm mã vạch.");
            units.Add(new(parts[0], ReadDecimal(parts[1], "Hệ số quy đổi"), parts.Length == 3 ? parts[2] : null));
        }
        if (units.Count == 0) throw new FormatException("Thiếu đơn vị quy đổi.");
        return units;
    }

    private static void ApplyUnits(MasterProduct product, List<CreateMasterProductUnitRequest> units)
    {
        foreach (var (unitName, rate, unitBarcode, _) in units)
        {
            // Kiểm tra xem unit đã có chưa
            var existingUnit = product.Units.FirstOrDefault(u => u.UnitName.Equals(unitName, StringComparison.OrdinalIgnoreCase));
            if (existingUnit != null)
            {
                existingUnit.ConversionRate = rate;
                if (!string.IsNullOrWhiteSpace(unitBarcode)) existingUnit.Barcode = unitBarcode;
            }
            else
            {
                product.Units.Add(new MasterProductUnit
                {
                    MasterProductId = product.Id,
                    UnitName = unitName,
                    ConversionRate = rate,
                    Barcode = unitBarcode,
                    IsPurchaseUnit = true
                });
            }
        }
    }
}
