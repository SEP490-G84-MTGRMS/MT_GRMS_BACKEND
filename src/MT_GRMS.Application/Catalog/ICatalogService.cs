using MT_GRMS.Application.Catalog.Dtos;

namespace MT_GRMS.Application.Catalog;

/// <summary>
/// Service quản lý danh mục sản phẩm (Catalog)
/// </summary>
public interface ICatalogService
{
    Task<ProductResponse> CreateProductAsync(ProductCreateRequest request, CancellationToken cancellationToken = default);
    Task<ProductResponse?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ProductResponse?> GetByBarcodeAsync(string barcode, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProductResponse>> GetAllAsync(CancellationToken cancellationToken = default);
}
