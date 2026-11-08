using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Branches;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Infrastructure.Persistence;

namespace MT_GRMS.Infrastructure.Repositories;

public class BranchRepository : GenericRepository<Branch>, IBranchRepository
{
    public BranchRepository(AppDbContext context) : base(context)
    {
    }

    public async Task<bool> ExistsByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        return await _context.Branches.AnyAsync(b => b.BranchCode == code, cancellationToken);
    }

    public async Task<int> CountActiveAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Branches.CountAsync(b => b.IsActive && !b.IsDeleted, cancellationToken);
    }

    public async Task<(IReadOnlyList<Branch> Items, int TotalCount)> GetPagedListAsync(
        string? keyword,
        string? branchCode,
        string? status,
        IReadOnlyList<string>? branchCodes,
        IReadOnlyList<string>? branchNames,
        IReadOnlyList<string>? addresses,
        IReadOnlyList<string>? phones,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = _context.Branches.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.Trim().ToLower();
            query = query.Where(b =>
                b.BranchCode.ToLower().Contains(kw) ||
                b.Name.ToLower().Contains(kw) ||
                (b.Address != null && b.Address.ToLower().Contains(kw)) ||
                (b.Phone != null && b.Phone.ToLower().Contains(kw)));
        }

        var codeTerms = CleanTerms(branchCodes);
        if (codeTerms.Count > 0)
        {
            query = WhereAnyContains(query, b => b.BranchCode, codeTerms, ignoreCase: true);
        }
        else if (!string.IsNullOrWhiteSpace(branchCode) && !string.Equals(branchCode, "all", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(b => b.BranchCode == branchCode);
        }

        var nameTerms = CleanTerms(branchNames);
        if (nameTerms.Count > 0)
        {
            query = WhereAnyContains(query, b => b.Name, nameTerms, ignoreCase: true);
        }

        var addressTerms = CleanTerms(addresses);
        if (addressTerms.Count > 0)
        {
            query = WhereAnyContains(query, b => b.Address ?? string.Empty, addressTerms, ignoreCase: true);
        }

        var phoneTerms = CleanTerms(phones);
        if (phoneTerms.Count > 0)
        {
            query = WhereAnyContains(query, b => b.Phone ?? string.Empty, phoneTerms, ignoreCase: true);
        }

        if (!string.IsNullOrWhiteSpace(status) && !string.Equals(status, "all", StringComparison.OrdinalIgnoreCase))
        {
            var normalizedStatus = status.Trim().ToLower();
            if (normalizedStatus == "active")
            {
                query = query.Where(b => b.IsActive);
            }
            else if (normalizedStatus == "inactive")
            {
                query = query.Where(b => !b.IsActive);
            }
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var safePageIndex = Math.Max(1, pageIndex);
        var safePageSize = Math.Clamp(pageSize, 1, 100);

        var items = await query
            .OrderBy(b => b.BranchCode)
            .Skip((safePageIndex - 1) * safePageSize)
            .Take(safePageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
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

    private static IQueryable<T> WhereAnyContains<T>(
        IQueryable<T> query,
        Expression<Func<T, string>> selector,
        IReadOnlyList<string> terms,
        bool ignoreCase)
    {
        if (terms.Count == 0) return query;

        var parameter = selector.Parameters[0];
        Expression source = selector.Body;
        if (ignoreCase)
        {
            source = Expression.Call(source, nameof(string.ToLower), Type.EmptyTypes);
        }

        Expression? body = null;
        foreach (var term in terms)
        {
            var contains = Expression.Call(
                source,
                nameof(string.Contains),
                Type.EmptyTypes,
                Expression.Constant(term));
            body = body is null ? contains : Expression.OrElse(body, contains);
        }

        var predicate = Expression.Lambda<Func<T, bool>>(body!, parameter);
        return query.Where(predicate);
    }
}
