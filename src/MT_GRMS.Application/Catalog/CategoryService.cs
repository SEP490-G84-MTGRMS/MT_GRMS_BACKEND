using System.Text.RegularExpressions;
using MT_GRMS.Application.Catalog.Dtos;
using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Exceptions;

namespace MT_GRMS.Application.Catalog;

public class CategoryService : ICategoryService
{
    private static readonly Regex CodeRegex = new(@"^[A-Z0-9_-]{1,30}$", RegexOptions.Compiled);
    private readonly ICategoryRepository _categoryRepository;
    private readonly IUnitOfWork _unitOfWork;

    public CategoryService(ICategoryRepository categoryRepository, IUnitOfWork unitOfWork)
    {
        _categoryRepository = categoryRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<IReadOnlyList<CategoryResponse>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _categoryRepository.GetAllWithProductCountAsync(cancellationToken);
    }

    public async Task<CategoryResponse?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _categoryRepository.GetByIdWithProductCountAsync(id, cancellationToken);
    }

    public async Task<CategoryResponse> CreateAsync(CategoryCreateRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ArgumentException("Tên nhóm hàng không được để trống.");
        }

        var trimmedName = request.Name.Trim();
        if (trimmedName.Length > 100)
        {
            throw new ArgumentException("Tên nhóm hàng không được vượt quá 100 ký tự.");
        }

        var trimmedDesc = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        if (trimmedDesc != null && trimmedDesc.Length > 500)
        {
            throw new ArgumentException("Mô tả nhóm hàng không được vượt quá 500 ký tự.");
        }

        if (await _categoryRepository.ExistsByNameAsync(trimmedName, null, cancellationToken))
        {
            throw new BusinessException("CATEGORY_NAME_EXISTS", $"Nhóm hàng với tên '{trimmedName}' đã tồn tại trong hệ thống.");
        }

        string code;
        if (string.IsNullOrWhiteSpace(request.Code))
        {
            var maxCode = await _categoryRepository.GetMaxSequentialCodeAsync("CAT", 9, cancellationToken);
            var nextNumber = 1;
            if (!string.IsNullOrWhiteSpace(maxCode) && maxCode.Length == 9 && int.TryParse(maxCode[3..], out var currentMax))
            {
                nextNumber = currentMax + 1;
            }

            code = $"CAT{nextNumber:D6}";
            while (await _categoryRepository.ExistsByCodeAsync(code, null, cancellationToken))
            {
                nextNumber++;
                code = $"CAT{nextNumber:D6}";
            }
        }
        else
        {
            code = request.Code.Trim().ToUpper();
            if (code.Length > 30 || !CodeRegex.IsMatch(code))
            {
                throw new ArgumentException("Mã nhóm hàng chỉ gồm chữ cái không dấu, số, gạch ngang/dưới và tối đa 30 ký tự.");
            }
            if (await _categoryRepository.ExistsByCodeAsync(code, null, cancellationToken))
            {
                throw new BusinessException("CATEGORY_CODE_EXISTS", $"Mã nhóm hàng '{code}' đã tồn tại trong hệ thống.");
            }
        }

        var category = new Category
        {
            Id = Guid.NewGuid(),
            CategoryCode = code,
            Name = trimmedName,
            Description = trimmedDesc
        };

        await _categoryRepository.AddAsync(category, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CategoryResponse(
            category.Id,
            category.CategoryCode,
            category.Name,
            category.Description,
            0
        );
    }

    public async Task<CategoryResponse> UpdateAsync(Guid id, CategoryUpdateRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ArgumentException("Tên nhóm hàng không được để trống.");
        }

        var trimmedName = request.Name.Trim();
        if (trimmedName.Length > 100)
        {
            throw new ArgumentException("Tên nhóm hàng không được vượt quá 100 ký tự.");
        }

        var trimmedDesc = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        if (trimmedDesc != null && trimmedDesc.Length > 500)
        {
            throw new ArgumentException("Mô tả nhóm hàng không được vượt quá 500 ký tự.");
        }

        var category = await _categoryRepository.GetByIdAsync(id, cancellationToken);
        if (category == null)
        {
            throw new NotFoundException("Nhóm hàng", id);
        }

        if (await _categoryRepository.ExistsByNameAsync(trimmedName, id, cancellationToken))
        {
            throw new BusinessException("CATEGORY_NAME_EXISTS", $"Nhóm hàng với tên '{trimmedName}' đã tồn tại trong hệ thống.");
        }

        category.Name = trimmedName;
        category.Description = trimmedDesc;
        category.UpdatedAt = DateTime.UtcNow;

        _categoryRepository.Update(category);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var productCount = await _categoryRepository.GetProductCountAsync(id, cancellationToken);

        return new CategoryResponse(
            category.Id,
            category.CategoryCode,
            category.Name,
            category.Description,
            productCount
        );
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var category = await _categoryRepository.GetByIdAsync(id, cancellationToken);
        if (category == null)
        {
            throw new NotFoundException("Nhóm hàng", id);
        }

        var productCount = await _categoryRepository.GetProductCountAsync(id, cancellationToken);
        if (productCount > 0)
        {
            throw new BusinessException("CATEGORY_HAS_PRODUCTS", $"Không thể xóa nhóm hàng '{category.Name}' vì đang chứa {productCount} sản phẩm liên kết.");
        }

        _categoryRepository.Delete(category);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

