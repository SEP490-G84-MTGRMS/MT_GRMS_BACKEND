using MT_GRMS.Application.Tax.Dtos;
using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Application.Tax;

/// <summary>
/// Service xuất báo cáo thuế và sổ kế toán hộ kinh doanh dạng bảng tính Excel chuẩn 100% Thông tư 152/2025/TT-BTC.
/// </summary>
public interface ITaxExportService
{
    /// <summary>
    /// Xuất báo cáo thuế và sổ kế toán hộ kinh doanh (nếu có bookType trong filter thì xuất sổ đó, ngược lại xuất trọn bộ).
    /// </summary>
    Task<byte[]> ExportTaxSummaryToExcelAsync(HouseholdTaxFilterDto filter, CancellationToken cancellationToken = default);

    /// <summary>
    /// Xuất báo cáo thuế và sổ kế toán hộ kinh doanh với bookType cụ thể.
    /// Nếu bookType != null: xuất đúng sổ đó kèm sheet Thông tin.
    /// Nếu bookType == null: xuất trọn bộ Workbook cả 7 sổ kế toán Thông tư 152.
    /// </summary>
    Task<byte[]> ExportTaxSummaryToExcelAsync(HouseholdTaxFilterDto filter, AccountingBookType? bookType, CancellationToken cancellationToken = default);
}

