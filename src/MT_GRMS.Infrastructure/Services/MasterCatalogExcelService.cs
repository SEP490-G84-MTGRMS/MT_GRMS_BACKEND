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

        using var workbook = new XLWorkbook(fileStream);
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
            if (string.IsNullOrWhiteSpace(barcode) && string.IsNullOrWhiteSpace(name))
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
            if (!string.IsNullOrWhiteSpace(priceStr) && decimal.TryParse(priceStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var p))
            {
                suggestedPrice = p;
            }
            else if (!string.IsNullOrWhiteSpace(priceStr) && decimal.TryParse(priceStr, NumberStyles.Any, ViCulture, out var pVi))
            {
                suggestedPrice = pVi;
            }

            decimal vatRate = 8.00m;
            if (!string.IsNullOrWhiteSpace(vatStr) && decimal.TryParse(vatStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var v))
            {
                vatRate = v;
            }

            try
            {
                var existing = await _masterRepo.GetByBarcodeWithUnitsAsync(barcode, cancellationToken);
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
                    ParseAndApplyUnits(existing, packagingStr);
                    _masterRepo.Update(existing);
                }
                else
                {
                    var newProduct = new MasterProduct
                    {
                        Barcode = barcode,
                        Name = name,
                        Brand = string.IsNullOrWhiteSpace(brand) ? "Khác" : brand,
                        CategoryName = string.IsNullOrWhiteSpace(category) ? "Bách hóa" : category,
                        BaseUnit = string.IsNullOrWhiteSpace(baseUnit) ? "Cái" : baseUnit,
                        SuggestedRetailPrice = suggestedPrice,
                        VatRate = vatRate,
                        TaxCategory = "STANDARD_1PCT",
                        ImageUrl = imageUrl,
                        Description = desc,
                        IsActive = true,
                        UsageCount = 0
                    };

                    ParseAndApplyUnits(newProduct, packagingStr);
                    await _masterRepo.AddAsync(newProduct, cancellationToken);
                }

                successCount++;
                resultRows.Add(new MasterProductExcelImportRow(rowIdx, barcode, name, brand, category, baseUnit, suggestedPrice, vatRate, imageUrl, packagingStr, true, null));
            }
            catch (Exception ex)
            {
                failedCount++;
                var err = $"Dòng {rowIdx} ({barcode}): Lỗi xử lý CSDL - {ex.Message}";
                errorMessages.Add(err);
                resultRows.Add(new MasterProductExcelImportRow(rowIdx, barcode, name, brand, category, baseUnit, suggestedPrice, vatRate, imageUrl, packagingStr, false, err));
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new MasterProductExcelImportResult(totalRows, successCount, failedCount, errorMessages, resultRows);
    }

    private static void ParseAndApplyUnits(MasterProduct product, string? packagingStr)
    {
        if (string.IsNullOrWhiteSpace(packagingStr)) return;

        // Cú pháp: "Thùng:30:8934563138172; Lốc:6:8935049500551"
        var segments = packagingStr.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var seg in segments)
        {
            var parts = seg.Split(':', StringSplitOptions.TrimEntries);
            if (parts.Length < 2) continue;

            var unitName = parts[0];
            if (!decimal.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var rate) || rate <= 1)
            {
                continue;
            }

            var unitBarcode = parts.Length >= 3 ? parts[2] : null;

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
