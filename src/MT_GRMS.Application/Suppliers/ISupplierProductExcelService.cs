using MT_GRMS.Application.Suppliers.Dtos;

namespace MT_GRMS.Application.Suppliers;

public interface ISupplierProductExcelService
{
    /// <summary>Xuất SP đã gán NCC theo bộ lọc màn hình để điền giá nhập sỉ + MOQ.</summary>
    Task<SupplierProductExcelFileDto> ExportAsync(
        Guid supplierId,
        SupplierProductQueryFilter? filter = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Import Excel: validate theo bộ lọc màn hình + override AgreedWholesalePrice &amp; MOQ (partial success).
    /// </summary>
    Task<SupplierProductExcelImportResultDto> ImportAsync(
        Guid supplierId,
        Stream fileStream,
        string fileName,
        SupplierProductQueryFilter? filter = null,
        CancellationToken cancellationToken = default);
}
