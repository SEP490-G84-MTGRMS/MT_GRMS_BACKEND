using MT_GRMS.Application.Catalog.Dtos;

namespace MT_GRMS.Application.Catalog;

/// <summary>
/// KHUNG MẪU INTERFACE TẦNG APPLICATION:
/// - Toàn bộ Input parameters: là DTO (ProductCreateRequest) hoặc primitive types (Guid).
/// - Toàn bộ Output return types: là DTO (ProductResponse).
/// - TUYỆT ĐỐI không để lộ Domain Entity ra interface này.
/// </summary>
public interface ICatalogService
{
    Task<ProductResponse> CreateProductAsync(ProductCreateRequest request, CancellationToken cancellationToken = default);
    Task<ProductResponse?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProductResponse>> GetAllAsync(CancellationToken cancellationToken = default);
}
