using MT_GRMS.Application.Admin.Dtos;

namespace MT_GRMS.Application.Admin.Services;

public interface IMasterCatalogExcelService
{
    Task<byte[]> GenerateTemplateAsync(CancellationToken cancellationToken = default);

    Task<MasterProductExcelImportResult> ImportExcelAsync(
        Stream fileStream,
        bool upsert = true,
        CancellationToken cancellationToken = default);
}
