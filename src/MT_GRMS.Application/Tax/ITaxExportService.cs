using MT_GRMS.Application.Tax.Dtos;

namespace MT_GRMS.Application.Tax;

/// <summary>
/// Service xuất báo cáo thuế hộ kinh doanh dạng bảng tính Excel.
/// </summary>
public interface ITaxExportService
{
    /// <summary>
    /// Xuất báo cáo thuế 4 sheet phẳng (Thông tin, Doanh thu theo địa điểm, Chi phí, Tổng hợp gần mẫu S2a/S2b-S2c).
    /// </summary>
    Task<byte[]> ExportTaxSummaryToExcelAsync(HouseholdTaxFilterDto filter, CancellationToken cancellationToken = default);
}

