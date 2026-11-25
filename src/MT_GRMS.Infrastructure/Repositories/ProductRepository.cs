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

    private static Expression<Func<Product, bool>> CombineOr(IEnumerable<Expression<Func<Product, bool>>> predicates)
    {
        var param = Expression.Parameter(typeof(Product), "p");
        Expression? body = null;
        foreach (var pred in predicates)
        {
            var replaced = new ParameterReplacer(pred.Parameters[0], param).Visit(pred.Body)!;
            body = body == null ? replaced : Expression.OrElse(body, replaced);
        }
        return Expression.Lambda<Func<Product, bool>>(body ?? Expression.Constant(true), param);
    }

    private sealed class ParameterReplacer(ParameterExpression source, ParameterExpression target) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == source ? target : base.VisitParameter(node);
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
                (p.Barcode != null && p.Barcode.ToLower().Contains(term)) ||
                p.Units.Any(u => !u.IsDeleted && u.Barcode != null && u.Barcode.ToLower().Contains(term)));
        }

        var skuTokens = filter.Skus?.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim().ToLower()).Distinct().ToList();
        if (skuTokens is { Count: > 0 })
        {
            query = query.Where(CombineOr(skuTokens.Select<string, Expression<Func<Product, bool>>>(t =>
                p => p.ProductCode.ToLower().Contains(t))));
        }

        var nameTokens = filter.Names?.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim().ToLower()).Distinct().ToList();
        if (nameTokens is { Count: > 0 })
        {
            query = query.Where(CombineOr(nameTokens.Select<string, Expression<Func<Product, bool>>>(t =>
                p => p.Name.ToLower().Contains(t))));
        }

        var barcodeTokens = filter.Barcodes?.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim().ToLower()).Distinct().ToList();
        if (barcodeTokens is { Count: > 0 })
        {
            query = query.Where(CombineOr(barcodeTokens.Select<string, Expression<Func<Product, bool>>>(t =>
                p => (p.Barcode != null && p.Barcode.ToLower().Contains(t)) ||
                     p.Units.Any(u => !u.IsDeleted && u.Barcode != null && u.Barcode.ToLower().Contains(t)))));
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
            var normalizedStatus = filter.Status.Trim();
            if (normalizedStatus.Equals("Discontinued", StringComparison.OrdinalIgnoreCase))
            {
                normalizedStatus = nameof(ProductStatus.Inactive);
            }
            if (Enum.TryParse<ProductStatus>(normalizedStatus, true, out var prodStatus))
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
}
