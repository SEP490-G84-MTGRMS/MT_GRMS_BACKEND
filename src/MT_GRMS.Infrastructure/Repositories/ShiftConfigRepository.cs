using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.HR.ShiftConfigs;
using MT_GRMS.Application.HR.ShiftConfigs.Dtos;
using MT_GRMS.Domain.Entities.HR;
using MT_GRMS.Infrastructure.Persistence;

namespace MT_GRMS.Infrastructure.Repositories;

public class ShiftConfigRepository : IShiftConfigRepository
{
    private readonly AppDbContext _context;
    private readonly DbSet<ShiftConfig> _dbSet;

    public ShiftConfigRepository(AppDbContext context)
    {
        _context = context;
        _dbSet = context.Set<ShiftConfig>();
    }

    public async Task<ShiftConfig?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Include(s => s.Branch)
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<ShiftConfig>> GetByBranchIdAsync(
        Guid? branchId,
        bool? isActive = null,
        CancellationToken cancellationToken = default)
    {
        var query = _dbSet.Include(s => s.Branch).AsQueryable();

        if (branchId.HasValue && branchId.Value != Guid.Empty)
        {
            var branchStr = branchId.Value.ToString();
            query = query.Where(s => s.BranchId == branchId.Value || (s.BranchIds != null && s.BranchIds.Contains(branchStr)));
        }

        if (isActive.HasValue)
        {
            query = query.Where(s => s.IsActive == isActive.Value);
        }

        return await query
            .OrderBy(s => s.StartTime)
            .ToListAsync(cancellationToken);
    }

    public async Task<(IReadOnlyList<ShiftConfig> Items, int TotalCount)> GetPagedListAsync(
        ShiftConfigQueryFilter filter,
        CancellationToken cancellationToken = default)
    {
        var query = _dbSet
            .AsNoTracking()
            .Include(s => s.Branch)
            .Where(s => !s.IsDeleted);

        if (!string.IsNullOrWhiteSpace(filter.Keyword))
        {
            var kw = filter.Keyword.Trim().ToLower();
            query = query.Where(s => s.ShiftCode.ToLower().Contains(kw) || s.ShiftName.ToLower().Contains(kw));
        }

        if (filter.BranchIds != null && filter.BranchIds.Count > 0)
        {
            var validBranchIds = filter.BranchIds.Where(id => id != Guid.Empty).ToList();
            if (validBranchIds.Count == 1)
            {
                var bId = validBranchIds[0];
                var bStr = bId.ToString();
                query = query.Where(s => s.BranchId == bId || (s.BranchIds != null && s.BranchIds.Contains(bStr)));
            }
            else if (validBranchIds.Count > 1)
            {
                var branchFilterExpr = BuildBranchFilter(validBranchIds);
                query = query.Where(branchFilterExpr);
            }
        }
        else if (filter.BranchId.HasValue && filter.BranchId.Value != Guid.Empty)
        {
            var bId = filter.BranchId.Value;
            var bStr = bId.ToString();
            query = query.Where(s => s.BranchId == bId || (s.BranchIds != null && s.BranchIds.Contains(bStr)));
        }

        if (filter.IsActive.HasValue)
        {
            query = query.Where(s => s.IsActive == filter.IsActive.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var page = filter.Page > 0 ? filter.Page : 1;
        var pageSize = filter.PageSize > 0 ? filter.PageSize : 20;

        var items = await query
            .OrderBy(s => s.StartTime)
            .ThenBy(s => s.ShiftCode)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    private static Expression<Func<ShiftConfig, bool>> BuildBranchFilter(List<Guid> branchIds)
    {
        var param = Expression.Parameter(typeof(ShiftConfig), "s");
        var branchIdProp = Expression.Property(param, nameof(ShiftConfig.BranchId));
        var containsBranchIdMethod = typeof(List<Guid>).GetMethod("Contains", new[] { typeof(Guid) })!;
        var containsBranchIdExpr = Expression.Call(Expression.Constant(branchIds), containsBranchIdMethod, branchIdProp);

        Expression combined = containsBranchIdExpr;

        var branchIdsProp = Expression.Property(param, nameof(ShiftConfig.BranchIds));
        var notNullExpr = Expression.NotEqual(branchIdsProp, Expression.Constant(null, typeof(string)));
        var containsStrMethod = typeof(string).GetMethod("Contains", new[] { typeof(string) })!;

        foreach (var bId in branchIds)
        {
            var bStr = bId.ToString();
            var callContains = Expression.Call(branchIdsProp, containsStrMethod, Expression.Constant(bStr));
            var branchCheck = Expression.AndAlso(notNullExpr, callContains);
            combined = Expression.OrElse(combined, branchCheck);
        }

        return Expression.Lambda<Func<ShiftConfig, bool>>(combined, param);
    }

    public async Task<ShiftConfig?> GetByCodeAsync(
        Guid branchId,
        string shiftCode,
        CancellationToken cancellationToken = default)
    {
        var normalizedCode = shiftCode.Trim().ToUpper();
        return await _dbSet
            .FirstOrDefaultAsync(s => s.BranchId == branchId && s.ShiftCode.ToUpper() == normalizedCode, cancellationToken);
    }

    public async Task<bool> ExistsAsync(
        Expression<Func<ShiftConfig, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        return await _dbSet.AnyAsync(predicate, cancellationToken);
    }

    public async Task AddAsync(ShiftConfig shiftConfig, CancellationToken cancellationToken = default)
    {
        await _dbSet.AddAsync(shiftConfig, cancellationToken);
    }

    public void Update(ShiftConfig shiftConfig)
    {
        shiftConfig.UpdatedAt = DateTime.UtcNow;
        _dbSet.Update(shiftConfig);
    }

    public void Delete(ShiftConfig shiftConfig)
    {
        shiftConfig.IsDeleted = true;
        shiftConfig.UpdatedAt = DateTime.UtcNow;
        _dbSet.Update(shiftConfig);
    }
}

