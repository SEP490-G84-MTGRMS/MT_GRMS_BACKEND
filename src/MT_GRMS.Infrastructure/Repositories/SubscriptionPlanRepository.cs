using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Admin.Repositories;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Infrastructure.Persistence;

namespace MT_GRMS.Infrastructure.Repositories;

public class SubscriptionPlanRepository : ISubscriptionPlanRepository
{
    private readonly AppDbContext _context;

    public SubscriptionPlanRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<SubscriptionPlan?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        return await _context.SubscriptionPlans
            .FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted, cancellationToken);
    }

    public async Task<SubscriptionPlan?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        return await _context.SubscriptionPlans
            .FirstOrDefaultAsync(p => p.PlanCode.ToLower() == code.ToLower() && !p.IsDeleted, cancellationToken);
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetAllAsync(
        string? keyword = null,
        string? status = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.SubscriptionPlans
            .Where(p => !p.IsDeleted)
            .AsNoTracking();

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.Trim().ToLower();
            query = query.Where(p => p.PlanName.ToLower().Contains(kw) || p.PlanCode.ToLower().Contains(kw));
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            var st = status.Trim().ToLower();
            if (st == "active")
            {
                query = query.Where(p => p.IsActive);
            }
            else if (st == "inactive")
            {
                query = query.Where(p => !p.IsActive);
            }
        }

        return await query.OrderBy(p => p.PriceMonthly).ToListAsync(cancellationToken);
    }

    public async Task<int> CountActiveTenantsByPlanIdAsync(string planId, CancellationToken cancellationToken = default)
    {
        var plan = await _context.SubscriptionPlans
            .FirstOrDefaultAsync(p => p.Id == planId, cancellationToken);

        if (plan == null) return 0;

        return await _context.Tenants
            .Where(t => !t.IsDeleted && (t.CurrentPlan == plan.PlanCode || t.CurrentPlan == plan.Id))
            .CountAsync(cancellationToken);
    }

    public async Task<Dictionary<string, int>> GetActiveTenantCountsAsync(CancellationToken cancellationToken = default)
    {
        var counts = await _context.Tenants
            .Where(t => !t.IsDeleted && !string.IsNullOrEmpty(t.CurrentPlan))
            .GroupBy(t => t.CurrentPlan!)
            .Select(g => new { PlanCode = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.PlanCode, x => x.Count, cancellationToken);

        return counts;
    }

    public async Task AddAsync(SubscriptionPlan plan, CancellationToken cancellationToken = default)
    {
        await _context.SubscriptionPlans.AddAsync(plan, cancellationToken);
    }

    public void Update(SubscriptionPlan plan)
    {
        _context.SubscriptionPlans.Update(plan);
    }

    public void Delete(SubscriptionPlan plan)
    {
        plan.IsDeleted = true;
        plan.UpdatedAt = DateTime.UtcNow;
        _context.SubscriptionPlans.Update(plan);
    }
}

