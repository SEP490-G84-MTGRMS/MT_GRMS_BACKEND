using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Admin.Dtos;
using MT_GRMS.Application.Admin.Repositories;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Infrastructure.Persistence;

namespace MT_GRMS.Infrastructure.Repositories;

public class MasterProductRepository : IMasterProductRepository
{
    private readonly AppDbContext _context;

    public MasterProductRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<(IReadOnlyList<MasterProduct> Items, int TotalCount)> GetPagedListAsync(
        MasterProductFilterRequest filter,
        CancellationToken cancellationToken = default)
    {
        var query = _context.MasterProducts
            .Include(p => p.Units.Where(u => !u.IsDeleted))
            .Where(p => !p.IsDeleted)
            .AsNoTracking();

        if (!string.IsNullOrWhiteSpace(filter.Keyword))
        {
            var kw = filter.Keyword.Trim().ToLower();
            query = query.Where(p =>
                p.Name.ToLower().Contains(kw) ||
                p.Barcode.ToLower().Contains(kw) ||
                p.Brand.ToLower().Contains(kw) ||
                p.CategoryName.ToLower().Contains(kw));
        }

        if (!string.IsNullOrWhiteSpace(filter.Brand) && filter.Brand != "ALL")
        {
            var b = filter.Brand.Trim().ToLower();
            query = query.Where(p => p.Brand.ToLower() == b);
        }

        if (!string.IsNullOrWhiteSpace(filter.CategoryName) && filter.CategoryName != "ALL")
        {
            var c = filter.CategoryName.Trim().ToLower();
            query = query.Where(p => p.CategoryName.ToLower() == c);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var pageIndex = filter.PageIndex < 1 ? 1 : filter.PageIndex;
        var pageSize = filter.PageSize < 1 ? 15 : filter.PageSize;

        var items = await query
            .OrderByDescending(p => p.UsageCount)
            .ThenByDescending(p => p.CreatedAt)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<MasterProduct?> GetByIdWithUnitsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.MasterProducts
            .Include(p => p.Units.Where(u => !u.IsDeleted))
            .FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted, cancellationToken);
    }

    public async Task<MasterProduct?> GetByBarcodeWithUnitsAsync(string barcode, CancellationToken cancellationToken = default)
    {
        var trimmed = barcode.Trim();
        return await _context.MasterProducts
            .Include(p => p.Units.Where(u => !u.IsDeleted))
            .FirstOrDefaultAsync(p => p.Barcode == trimmed && !p.IsDeleted && p.IsActive, cancellationToken);
    }

    public async Task<bool> ExistsByBarcodeAsync(string barcode, Guid? excludeId = null, CancellationToken cancellationToken = default)
    {
        var trimmed = barcode.Trim();
        var query = _context.MasterProducts.Where(p => p.Barcode == trimmed && !p.IsDeleted);
        if (excludeId.HasValue)
        {
            query = query.Where(p => p.Id != excludeId.Value);
        }
        return await query.AnyAsync(cancellationToken);
    }

    public async Task<MasterCatalogKpiDto> GetKpisAsync(CancellationToken cancellationToken = default)
    {
        var activeQuery = _context.MasterProducts.Where(p => !p.IsDeleted);

        var totalProducts = await activeQuery.CountAsync(cancellationToken);
        var totalBrands = await activeQuery.Select(p => p.Brand).Distinct().CountAsync(cancellationToken);
        var totalUsage = await activeQuery.SumAsync(p => (int?)p.UsageCount, cancellationToken) ?? 0;
        var withImage = await activeQuery.CountAsync(p => !string.IsNullOrEmpty(p.ImageUrl), cancellationToken);

        decimal imageCoverage = totalProducts > 0
            ? Math.Round((decimal)withImage * 100m / totalProducts, 1)
            : 0.0m;

        return new MasterCatalogKpiDto(
            TotalProducts: totalProducts,
            TotalBrands: totalBrands,
            TotalUsage: totalUsage,
            ImageCoveragePercent: imageCoverage
        );
    }

    public async Task AddAsync(MasterProduct product, CancellationToken cancellationToken = default)
    {
        await _context.MasterProducts.AddAsync(product, cancellationToken);
    }

    public async Task AddRangeAsync(IEnumerable<MasterProduct> products, CancellationToken cancellationToken = default)
    {
        await _context.MasterProducts.AddRangeAsync(products, cancellationToken);
    }

    public void Update(MasterProduct product)
    {
        product.UpdatedAt = DateTime.UtcNow;
        _context.MasterProducts.Update(product);
    }

    public async Task IncrementUsageCountAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var product = await _context.MasterProducts.FindAsync([id], cancellationToken);
        if (product != null)
        {
            product.UsageCount++;
            product.UpdatedAt = DateTime.UtcNow;
        }
    }

    public async Task<IReadOnlyList<string>> GetAllBrandsAsync(CancellationToken cancellationToken = default)
    {
        return await _context.MasterProducts
            .Where(p => !p.IsDeleted && !string.IsNullOrEmpty(p.Brand))
            .Select(p => p.Brand)
            .Distinct()
            .OrderBy(b => b)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetAllCategoriesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.MasterProducts
            .Where(p => !p.IsDeleted && !string.IsNullOrEmpty(p.CategoryName))
            .Select(p => p.CategoryName)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync(cancellationToken);
    }
}
