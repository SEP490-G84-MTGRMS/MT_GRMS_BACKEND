using MT_GRMS.Application.Catalog.Dtos;
using MT_GRMS.Application.Common;

namespace MT_GRMS.Application.Catalog;

/// <summary>
/// Interface nghiệp vụ quản lý sản phẩm (Product Catalog)
/// </summary>
public interface ICatalogService
{
    Task<PagedResult<ProductResponse>> GetProductsPagedAsync(ProductQueryFilter filter, CancellationToken cancellationToken = default);
    Task<ProductResponse?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ProductResponse> CreateProductAsync(ProductCreateRequest request, CancellationToken cancellationToken = default);
    Task<ProductResponse> UpdateProductAsync(Guid id, ProductUpdateRequest request, CancellationToken cancellationToken = default);
    Task DeleteProductAsync(Guid id, CancellationToken cancellationToken = default);

    // Backward compatibility
    Task<IReadOnlyList<ProductResponse>> GetAllAsync(CancellationToken cancellationToken = default);
}
