using MT_GRMS.Application.Admin.Dtos;
using MT_GRMS.Application.Common;

namespace MT_GRMS.Application.Admin.Services;

public interface IMasterCatalogService
{
    Task<PagedResult<MasterProductDto>> GetPagedListAsync(MasterProductFilterRequest filter, CancellationToken cancellationToken = default);

    Task<MasterProductDetailDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<MasterCatalogKpiDto> GetKpisAsync(CancellationToken cancellationToken = default);

    Task<MasterProductDetailDto> CreateAsync(CreateMasterProductRequest request, CancellationToken cancellationToken = default);

    Task<MasterProductDetailDto> UpdateAsync(Guid id, UpdateMasterProductRequest request, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    Task<MasterProductSuggestionDto?> LookupByBarcodeAsync(string barcode, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetBrandsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetCategoriesAsync(CancellationToken cancellationToken = default);
}
