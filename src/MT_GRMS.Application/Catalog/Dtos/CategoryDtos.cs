namespace MT_GRMS.Application.Catalog.Dtos;

/// <summary>
/// DTO trả về thông tin danh mục (danh mục phẳng, không phân cấp cha con)
/// tương thích với interface Category của Frontend
/// </summary>
public record CategoryResponse(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    int ProductCount
);

public record CategoryCreateRequest(
    string Name,
    string? Code = null,
    string? Description = null
);

public record CategoryUpdateRequest(
    string Name,
    string? Description = null
);

