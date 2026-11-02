using MT_GRMS.Application.Catalog.Dtos;

namespace MT_GRMS.Application.Catalog;

/// <summary>
/// Interface nghiệp vụ quản lý nhóm hàng (Category - mô hình phẳng)
/// </summary>
public interface ICategoryService
{
    Task<IReadOnlyList<CategoryResponse>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<CategoryResponse?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<CategoryResponse> CreateAsync(CategoryCreateRequest request, CancellationToken cancellationToken = default);
    Task<CategoryResponse> UpdateAsync(Guid id, CategoryUpdateRequest request, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

