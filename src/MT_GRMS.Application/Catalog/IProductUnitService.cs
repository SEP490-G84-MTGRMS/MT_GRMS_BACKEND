using MT_GRMS.Application.Catalog.Dtos;

namespace MT_GRMS.Application.Catalog;

public interface IProductUnitService
{
    Task<IReadOnlyList<ProductUnitResponse>> GetUnitsByProductIdAsync(Guid productId, CancellationToken cancellationToken = default);
    Task<ProductUnitResponse?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ProductUnitResponse> CreateAsync(Guid productId, ProductUnitCreateRequest request, CancellationToken cancellationToken = default);
    Task<ProductUnitResponse> UpdateAsync(Guid id, ProductUnitUpdateRequest request, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

