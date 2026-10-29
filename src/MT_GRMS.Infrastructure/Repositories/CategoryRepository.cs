using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Catalog;
using MT_GRMS.Application.Catalog.Dtos;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Infrastructure.Persistence;

namespace MT_GRMS.Infrastructure.Repositories;

public class CategoryRepository : GenericRepository<Category>, ICategoryRepository
{
    public CategoryRepository(AppDbContext context) : base(context)
    {
    }

    public async Task<IReadOnlyList<CategoryResponse>> GetAllWithProductCountAsync(CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .AsNoTracking()
            .OrderBy(c => c.DisplayOrder)
            .ThenBy(c => c.Name)
            .Select(c => new CategoryResponse(
                c.Id,
                c.CategoryCode,
                c.Name,
                c.Description,
                c.DisplayOrder,
                c.Products.Count(p => !p.IsDeleted)
            ))
            .ToListAsync(cancellationToken);
    }

    public async Task<CategoryResponse?> GetByIdWithProductCountAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new CategoryResponse(
                c.Id,
                c.CategoryCode,
                c.Name,
                c.Description,
                c.DisplayOrder,
                c.Products.Count(p => !p.IsDeleted)
            ))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> ExistsByNameAsync(string name, Guid? excludeId = null, CancellationToken cancellationToken = default)
    {
        var normalizedName = name.Trim().ToLower();
        return await _dbSet.AnyAsync(c =>
            c.Name.ToLower() == normalizedName &&
            (!excludeId.HasValue || c.Id != excludeId.Value),
            cancellationToken);
    }

    public async Task<bool> ExistsByCodeAsync(string code, Guid? excludeId = null, CancellationToken cancellationToken = default)
    {
        var normalizedCode = code.Trim().ToUpper();
        return await _dbSet.AnyAsync(c =>
            c.CategoryCode.ToUpper() == normalizedCode &&
            (!excludeId.HasValue || c.Id != excludeId.Value),
            cancellationToken);
    }

    public async Task<int> GetProductCountAsync(Guid categoryId, CancellationToken cancellationToken = default)
    {
        return await _context.Products
            .CountAsync(p => p.CategoryId == categoryId && !p.IsDeleted, cancellationToken);
    }
}

