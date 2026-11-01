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

    public async Task<(IReadOnlyList<Branch> Items, int TotalCount)> GetPagedListAsync(
        string? keyword,
        string? branchCode,
        string? status,
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

        if (!string.IsNullOrWhiteSpace(branchCode) && !string.Equals(branchCode, "all", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(b => b.BranchCode == branchCode);
        }

        if (!string.IsNullOrWhiteSpace(status) && !string.Equals(status, "all", StringComparison.OrdinalIgnoreCase))
        {
            var isActive = string.Equals(status.Trim(), "active", StringComparison.OrdinalIgnoreCase);
            query = query.Where(b => b.IsActive == isActive);
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
}