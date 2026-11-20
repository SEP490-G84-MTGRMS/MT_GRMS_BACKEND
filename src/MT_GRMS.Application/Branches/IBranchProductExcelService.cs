using MT_GRMS.Application.Branches.Dtos;

namespace MT_GRMS.Application.Branches;

public interface IBranchProductExcelService
{
    /// <summary>Xuất SP đã gán chi nhánh theo bộ lọc màn hình (giá bán theo từng ĐVT).</summary>
    Task<BranchProductExcelFileDto> ExportAsync(
        Guid branchId,
        BranchProductQueryFilter? filter = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Import Excel: validate theo bộ lọc màn hình + override giá bán theo ĐVT (partial success theo dòng).
    /// </summary>
    Task<BranchProductExcelImportResultDto> ImportAsync(
        Guid branchId,
        Stream fileStream,
        string fileName,
        BranchProductQueryFilter? filter = null,
        CancellationToken cancellationToken = default);
}
