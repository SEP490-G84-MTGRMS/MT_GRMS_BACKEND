using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.HR.AttendancePolicies;
using MT_GRMS.Domain.Entities.HR;
using MT_GRMS.Infrastructure.Persistence;

namespace MT_GRMS.Infrastructure.Repositories;

public class BranchAttendancePolicyRepository : IBranchAttendancePolicyRepository
{
    private readonly AppDbContext _context;
    private readonly DbSet<BranchAttendancePolicy> _dbSet;

    public BranchAttendancePolicyRepository(AppDbContext context)
    {
        _context = context;
        _dbSet = context.Set<BranchAttendancePolicy>();
    }

    public async Task<BranchAttendancePolicy?> GetByBranchIdAsync(Guid branchId, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Include(x => x.Branch)
            .FirstOrDefaultAsync(x => x.BranchId == branchId, cancellationToken);
    }

    public async Task<IReadOnlyList<BranchAttendancePolicy>> GetByBranchIdsAsync(IEnumerable<Guid> branchIds, CancellationToken cancellationToken = default)
    {
        var idList = branchIds.ToList();
        return await _dbSet
            .Include(x => x.Branch)
            .Where(x => idList.Contains(x.BranchId))
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(BranchAttendancePolicy policy, CancellationToken cancellationToken = default)
    {
        await _dbSet.AddAsync(policy, cancellationToken);
    }

    public void Update(BranchAttendancePolicy policy)
    {
        _dbSet.Update(policy);
    }
}

