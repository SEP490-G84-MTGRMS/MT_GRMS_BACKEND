using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Catalog;
using MT_GRMS.Application.Catalog.Dtos;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Infrastructure.Persistence;

namespace MT_GRMS.Infrastructure.Repositories;

public class ProductRepository : GenericRepository<Product>, IProductRepository
{
    public ProductRepository(AppDbContext context) : base(context)
    {
    }

    public async Task<(IReadOnlyList<Product> Items, int TotalCount)> GetPagedListAsync(
        ProductQueryFilter filter,
        CancellationToken cancellationToken = default)
    {
        var query = _dbSet
            .AsNoTracking()
            .Include(p => p.Category)
            .Where(p => !p.IsDeleted);

        if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
        {
            var term = filter.SearchTerm.Trim().ToLower();
            query = query.Where(p =>
                p.Name.ToLower().Contains(term) ||
                p.ProductCode.ToLower().Contains(term) ||
                p.Barcode.ToLower().Contains(term));
        }

        if (filter.CategoryIds != null && filter.CategoryIds.Any())
        {
            var validIds = filter.CategoryIds.Where(id => id != Guid.Empty).ToList();
            if (validIds.Any())
            {
                query = query.Where(p => validIds.Contains(p.CategoryId));
            }
        }
        else if (filter.CategoryId.HasValue)
        {
            query = query.Where(p => p.CategoryId == filter.CategoryId.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            query = query.Where(p => p.Status == filter.Status);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var pageIndex = Math.Max(1, filter.PageIndex);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);

        var items = await query
            .OrderByDescending(p => p.CreatedAt)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<Product?> GetByIdWithCategoryAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .AsNoTracking()
            .Include(p => p.Category)
            .FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted, cancellationToken);
    }

    public async Task<bool> ExistsByCodeAsync(string code, Guid? excludeId = null, CancellationToken cancellationToken = default)
    {
        var normalizedCode = code.Trim().ToUpper();
        return await _dbSet.AnyAsync(p =>
            !p.IsDeleted &&
            p.ProductCode.ToUpper() == normalizedCode &&
            (!excludeId.HasValue || p.Id != excludeId.Value),
            cancellationToken);
    }

    public async Task<bool> ExistsByBarcodeAsync(string barcode, Guid? excludeId = null, CancellationToken cancellationToken = default)
    {
        var normalizedBarcode = barcode.Trim();
        return await _dbSet.AnyAsync(p =>
            !p.IsDeleted &&
            p.Barcode == normalizedBarcode &&
            (!excludeId.HasValue || p.Id != excludeId.Value),
            cancellationToken);
    }
}
