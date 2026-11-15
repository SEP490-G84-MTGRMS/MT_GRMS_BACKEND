using MT_GRMS.Application.Suppliers.Dtos;

namespace MT_GRMS.Application.Suppliers;

public interface ISupplierProductExcelService
{
    /// <summary>Xuất danh sách SP đã gán NCC (Active) để điền giá nhập sỉ + MOQ.</summary>
    Task<SupplierProductExcelFileDto> ExportAsync(Guid supplierId, CancellationToken cancellationToken = default);

    /// <summary>Import Excel: validate + override AgreedWholesalePrice &amp; MOQ (partial success).</summary>
    Task<SupplierProductExcelImportResultDto> ImportAsync(
        Guid supplierId,
        Stream fileStream,
        string fileName,
        CancellationToken cancellationToken = default);
}
