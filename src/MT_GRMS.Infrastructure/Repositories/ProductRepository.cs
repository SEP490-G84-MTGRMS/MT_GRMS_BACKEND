using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Catalog;
using MT_GRMS.Application.Catalog.Dtos;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Enums;
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
            .Include(p => p.Units)
            .Where(p => !p.IsDeleted);

        if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
        {
            var term = filter.SearchTerm.Trim().ToLower();
            query = query.Where(p =>
                p.Name.ToLower().Contains(term) ||
                p.ProductCode.ToLower().Contains(term) ||
                (p.Barcode != null && p.Barcode.ToLower().Contains(term)));
        }

        var skuTerms = CleanTerms(filter.Skus);
        if (skuTerms.Count > 0)
        {
            query = WhereAnyContains(query, p => p.ProductCode, skuTerms);
        }

        var nameTerms = CleanTerms(filter.Names);
        if (nameTerms.Count > 0)
        {
            query = WhereAnyContains(query, p => p.Name, nameTerms);
        }

        var barcodeTerms = CleanTerms(filter.Barcodes);
        if (barcodeTerms.Count > 0)
        {
            query = WhereAnyBarcodeContains(query, barcodeTerms);
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
            if (Enum.TryParse<ProductStatus>(filter.Status.Trim(), true, out var prodStatus))
            {
                query = query.Where(p => p.Status == prodStatus);
            }
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var pageIndex = Math.Max(1, filter.PageIndex);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);

        var items = await query
            .OrderBy(p => p.ProductCode)
            .ThenBy(p => p.Name)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<Product?> GetByIdWithCategoryAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Include(p => p.Category)
            .Include(p => p.Units)
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

    private static List<string> CleanTerms(IReadOnlyList<string>? values)
    {
        if (values == null || values.Count == 0) return [];
        return values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim().ToLower())
            .Distinct()
            .ToList();
    }

    private static IQueryable<Product> WhereAnyContains(
        IQueryable<Product> query,
        Expression<Func<Product, string>> selector,
        IReadOnlyList<string> terms)
    {
        if (terms.Count == 0) return query;
        var parameter = selector.Parameters[0];
        var source = Expression.Call(selector.Body, nameof(string.ToLower), Type.EmptyTypes);
        Expression? body = null;
        foreach (var term in terms)
        {
            var contains = Expression.Call(source, nameof(string.Contains), Type.EmptyTypes, Expression.Constant(term));
            body = body is null ? contains : Expression.OrElse(body, contains);
        }
        return query.Where(Expression.Lambda<Func<Product, bool>>(body!, parameter));
    }

    private static IQueryable<Product> WhereAnyBarcodeContains(
        IQueryable<Product> query,
        IReadOnlyList<string> terms)
    {
        if (terms.Count == 0) return query;
        var p = Expression.Parameter(typeof(Product), "p");
        var barcodeProp = Expression.Property(p, nameof(Product.Barcode));
        var notNull = Expression.NotEqual(barcodeProp, Expression.Constant(null, typeof(string)));
        var lowerBarcode = Expression.Call(barcodeProp, nameof(string.ToLower), Type.EmptyTypes);
        Expression? body = null;
        foreach (var term in terms)
        {
            var mainMatch = Expression.AndAlso(
                notNull,
                Expression.Call(lowerBarcode, nameof(string.Contains), Type.EmptyTypes, Expression.Constant(term)));

            // p.Units.Any(u => !u.IsDeleted && u.Barcode != null && u.Barcode.ToLower().Contains(term))
            Expression<Func<Product, bool>> unitExpr = prod =>
                prod.Units.Any(u => !u.IsDeleted && u.Barcode != null && u.Barcode.ToLower().Contains(term));
            var invoked = new ParameterReplacer(unitExpr.Parameters[0], p).Visit(unitExpr.Body)!;
            var either = Expression.OrElse(mainMatch, invoked);
            body = body is null ? either : Expression.OrElse(body, either);
        }
        return query.Where(Expression.Lambda<Func<Product, bool>>(body!, p));
    }

    private sealed class ParameterReplacer(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : base.VisitParameter(node);
    }
}
