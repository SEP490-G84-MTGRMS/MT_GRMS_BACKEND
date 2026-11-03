using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Catalog;
using MT_GRMS.Application.Catalog.Dtos;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Infrastructure.Persistence;

namespace MT_GRMS.Infrastructure.Repositories;

public class ProductRepository : GenericRepository<Product>, IProductRepository
{
    private static string RemoveDiacritics(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var normalizedString = text.Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder();
        foreach (var c in normalizedString)
        {
            var uc = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
            if (uc != System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c);
            }
        }
        return sb.ToString().Normalize(System.Text.NormalizationForm.FormC).Replace('đ', 'd').Replace('Đ', 'D').ToLower();
    }

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
            var termNoTone = RemoveDiacritics(term);

            if (_context.Database.IsRelational())
            {
                query = query.Where(p =>
                    p.Name.ToLower().Contains(term) ||
                    p.ProductCode.ToLower().Contains(term) ||
                    (p.Barcode != null && p.Barcode.ToLower().Contains(term)));
            }
            else
            {
                query = query.Where(p =>
                    p.Name.ToLower().Contains(term) ||
                    RemoveDiacritics(p.Name).Contains(termNoTone) ||
                    p.ProductCode.ToLower().Contains(term) ||
                    (p.Barcode != null && p.Barcode.ToLower().Contains(term)));
            }
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
            .ThenByDescending(p => p.Id)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<Product?> GetByIdWithCategoryAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbSet
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
        if (string.IsNullOrWhiteSpace(barcode))
        {
            return false;
        }

        var normalizedBarcode = barcode.Trim();
        return await _dbSet.AnyAsync(p =>
            !p.IsDeleted &&
            p.Barcode == normalizedBarcode &&
            (!excludeId.HasValue || p.Id != excludeId.Value),
            cancellationToken);
    }

    public async Task<string?> GetMaxSequentialCodeAsync(string prefix, int totalLength, CancellationToken cancellationToken = default)
    {
        var normalizedPrefix = prefix.Trim().ToUpper();
        return await _dbSet
            .IgnoreQueryFilters()
            .Where(p => p.ProductCode.StartsWith(normalizedPrefix) &&
                        p.ProductCode.Length == totalLength)
            .OrderByDescending(p => p.ProductCode)
            .Select(p => p.ProductCode)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> HasOrdersAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        return await _context.OrderDetails.AnyAsync(od => od.ProductId == productId, cancellationToken);
    }

    public async Task<HashSet<Guid>> GetProductIdsWithOrdersAsync(IEnumerable<Guid> productIds, CancellationToken cancellationToken = default)
    {
        var idList = productIds.Distinct().ToList();
        if (!idList.Any()) return new HashSet<Guid>();

        var orderedIds = await _context.OrderDetails
            .Where(od => idList.Contains(od.ProductId))
            .Select(od => od.ProductId)
            .Distinct()
            .ToListAsync(cancellationToken);

        return orderedIds.ToHashSet();
    }

    public async Task DeleteWithUnitsAsync(Product product, CancellationToken cancellationToken = default)
    {
        product.IsDeleted = true;
        product.UpdatedAt = DateTime.UtcNow;
        _dbSet.Update(product);

        var units = await _context.ProductUnits
            .Where(u => u.ProductId == product.Id && !u.IsDeleted)
            .ToListAsync(cancellationToken);

        foreach (var unit in units)
        {
            unit.IsDeleted = true;
            unit.UpdatedAt = DateTime.UtcNow;
            _context.ProductUnits.Update(unit);
        }
    }
}
