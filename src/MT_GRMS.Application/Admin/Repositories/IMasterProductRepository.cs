using MT_GRMS.Application.Admin.Dtos;
using MT_GRMS.Domain.Entities.Catalog;

namespace MT_GRMS.Application.Admin.Repositories;

public interface IMasterProductRepository
{
    Task<(IReadOnlyList<MasterProduct> Items, int TotalCount)> GetPagedListAsync(
        MasterProductFilterRequest filter,
        CancellationToken cancellationToken = default);

    Task<MasterProduct?> GetByIdWithUnitsAsync(Guid id, CancellationToken cancellationToken = default);

    Task<MasterProduct?> GetByBarcodeWithUnitsAsync(string barcode, CancellationToken cancellationToken = default);

    Task<bool> ExistsByBarcodeAsync(string barcode, Guid? excludeId = null, CancellationToken cancellationToken = default);

    Task<MasterCatalogKpiDto> GetKpisAsync(CancellationToken cancellationToken = default);

    Task AddAsync(MasterProduct product, CancellationToken cancellationToken = default);

    Task AddRangeAsync(IEnumerable<MasterProduct> products, CancellationToken cancellationToken = default);

    void Update(MasterProduct product);

    Task IncrementUsageCountAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetAllBrandsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetAllCategoriesAsync(CancellationToken cancellationToken = default);
}
