using MT_GRMS.Application.Branches.Dtos;

namespace MT_GRMS.Application.Branches;

public interface IBranchProductExcelService
{
    /// <summary>Xuất SP đã gán chi nhánh để điền giá bán theo từng ĐVT.</summary>
    Task<BranchProductExcelFileDto> ExportAsync(Guid branchId, CancellationToken cancellationToken = default);

    /// <summary>Import Excel: validate + override giá bán theo ĐVT (partial success theo dòng).</summary>
    Task<BranchProductExcelImportResultDto> ImportAsync(
        Guid branchId,
        Stream fileStream,
        string fileName,
        CancellationToken cancellationToken = default);
}
