using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.HR.ShiftConfigs;
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
        Guid branchId,
        bool? isActive = null,
        CancellationToken cancellationToken = default)
    {
        var query = _dbSet.Where(s => s.BranchId == branchId);

        if (isActive.HasValue)
        {
            query = query.Where(s => s.IsActive == isActive.Value);
        }

        return await query
            .OrderBy(s => s.StartTime)
            .ToListAsync(cancellationToken);
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

