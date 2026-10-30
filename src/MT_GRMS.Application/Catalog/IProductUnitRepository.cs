using MT_GRMS.Domain.Entities.Catalog;

namespace MT_GRMS.Application.Catalog;

public interface IProductUnitRepository
{
    Task<ProductUnit?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProductUnit>> GetByProductIdAsync(Guid productId, CancellationToken cancellationToken = default);
    Task<bool> ExistsByNameAsync(Guid productId, string unitName, Guid? excludeId = null, CancellationToken cancellationToken = default);
    Task<bool> ExistsByBarcodeAsync(string barcode, Guid? excludeId = null, CancellationToken cancellationToken = default);
    Task<ProductUnit?> GetBaseUnitByProductIdAsync(Guid productId, CancellationToken cancellationToken = default);
    Task<int> CountByProductIdAsync(Guid productId, CancellationToken cancellationToken = default);
    Task AddAsync(ProductUnit entity, CancellationToken cancellationToken = default);
    void Update(ProductUnit entity);
    void Delete(ProductUnit entity);
}
