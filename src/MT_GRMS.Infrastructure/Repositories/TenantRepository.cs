using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Admin.Repositories;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Infrastructure.Persistence;

namespace MT_GRMS.Infrastructure.Repositories;

public class TenantRepository : ITenantRepository
{
    private readonly AppDbContext _context;

    public TenantRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<Tenant?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        return await _context.Tenants
            .FirstOrDefaultAsync(t => t.Id == id && !t.IsDeleted, cancellationToken);
    }

    public async Task<Tenant?> GetByIdWithDetailsAsync(string id, CancellationToken cancellationToken = default)
    {
        return await _context.Tenants
            .Include(t => t.SubscriptionPlan)
            .Include(t => t.Branches.Where(b => !b.IsDeleted))
            .Include(t => t.TenantUsers.Where(tu => tu.IsActive))
                .ThenInclude(tu => tu.User)
            .FirstOrDefaultAsync(t => t.Id == id && !t.IsDeleted, cancellationToken);
    }

    public async Task<Tenant?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        return await _context.Tenants
            .FirstOrDefaultAsync(t => t.Code.ToLower() == code.ToLower() && !t.IsDeleted, cancellationToken);
    }

    public async Task<Tenant?> GetBySubdomainAsync(string subdomain, CancellationToken cancellationToken = default)
    {
        return await _context.Tenants
            .FirstOrDefaultAsync(t => t.Subdomain.ToLower() == subdomain.ToLower() && !t.IsDeleted, cancellationToken);
    }

    public async Task<(IReadOnlyList<Tenant> Items, int TotalCount)> GetPagedAsync(
        string? keyword = null,
        string? status = null,
        string? planCode = null,
        int page = 1,
        int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var query = _context.Tenants
            .Include(t => t.SubscriptionPlan)
            .Include(t => t.Branches.Where(b => !b.IsDeleted))
            .Include(t => t.TenantUsers.Where(tu => tu.IsActive))
                .ThenInclude(tu => tu.User)
            .Where(t => !t.IsDeleted);

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.Trim().ToLower();
            query = query.Where(t =>
                t.Name.ToLower().Contains(kw) ||
                t.Subdomain.ToLower().Contains(kw) ||
                t.Code.ToLower().Contains(kw) ||
                (t.TaxCode != null && t.TaxCode.ToLower().Contains(kw)) ||
                (t.Hotline != null && t.Hotline.ToLower().Contains(kw)));
        }

        if (!string.IsNullOrWhiteSpace(planCode))
        {
            var pc = planCode.Trim().ToLower();
            query = query.Where(t => t.CurrentPlan != null && t.CurrentPlan.ToLower() == pc);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            var st = status.Trim().ToUpperInvariant();
            switch (st)
            {
                case "ACTIVE":
                    query = query.Where(t => t.Status == "ACTIVE" && t.LockType == null && (t.SubscriptionExpiresAt == null || t.SubscriptionExpiresAt > now));
                    break;
                case "EXPIRING":
                    var weekAhead = now.AddDays(7);
                    query = query.Where(t => t.SubscriptionExpiresAt != null && t.SubscriptionExpiresAt > now && t.SubscriptionExpiresAt <= weekAhead);
                    break;
                case "GRACE":
                case "GRACE_PERIOD":
                    query = query.Where(t => t.Status == "GRACE_PERIOD" || (t.SubscriptionExpiresAt != null && t.SubscriptionExpiresAt <= now && (t.GracePeriodExpiresAt == null || t.GracePeriodExpiresAt >= now)));
                    break;
                case "LOCKED":
                    query = query.Where(t => t.Status == "LOCKED" || t.LockType != null);
                    break;
                case "INACTIVE":
                    query = query.Where(t => t.Status == "INACTIVE");
                    break;
            }
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(t => t.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<int> GetTotalCountAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Tenants.CountAsync(t => !t.IsDeleted, cancellationToken);
    }

    public async Task<int> GetActiveCountAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        return await _context.Tenants
            .CountAsync(t => !t.IsDeleted && t.Status == "ACTIVE" && t.LockType == null && (t.SubscriptionExpiresAt == null || t.SubscriptionExpiresAt > now), cancellationToken);
    }

    public async Task<int> GetExpiringSoonCountAsync(int daysThreshold = 7, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var threshold = now.AddDays(daysThreshold);
        return await _context.Tenants
            .CountAsync(t => !t.IsDeleted && t.SubscriptionExpiresAt != null && t.SubscriptionExpiresAt > now && t.SubscriptionExpiresAt <= threshold, cancellationToken);
    }

    public async Task<int> GetGracePeriodCountAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        return await _context.Tenants
            .CountAsync(t => !t.IsDeleted && (t.Status == "GRACE_PERIOD" || (t.SubscriptionExpiresAt != null && t.SubscriptionExpiresAt <= now && (t.GracePeriodExpiresAt == null || t.GracePeriodExpiresAt >= now))), cancellationToken);
    }

    public async Task<int> GetLockedCountAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Tenants
            .CountAsync(t => !t.IsDeleted && (t.Status == "LOCKED" || t.LockType != null), cancellationToken);
    }

    public async Task<int> GetTotalBranchesCountAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Branches.IgnoreQueryFilters()
            .CountAsync(b => !b.IsDeleted, cancellationToken);
    }

    public async Task<IReadOnlyList<Tenant>> GetUrgentAlertTenantsAsync(int limit = 10, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var weekAhead = now.AddDays(7);

        return await _context.Tenants
            .Include(t => t.SubscriptionPlan)
            .Include(t => t.TenantUsers.Where(tu => tu.IsActive))
                .ThenInclude(tu => tu.User)
            .Where(t => !t.IsDeleted && (
                t.LockType != null ||
                t.Status == "LOCKED" ||
                t.Status == "GRACE_PERIOD" ||
                (t.SubscriptionExpiresAt != null && t.SubscriptionExpiresAt <= now) ||
                (t.SubscriptionExpiresAt != null && t.SubscriptionExpiresAt <= weekAhead)
            ))
            .OrderBy(t => t.GracePeriodExpiresAt ?? t.SubscriptionExpiresAt ?? DateTime.MaxValue)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Tenant>> GetAllActiveWithPlanAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Tenants
            .Include(t => t.SubscriptionPlan)
            .Where(t => !t.IsDeleted)
            .ToListAsync(cancellationToken);
    }

    public void Update(Tenant tenant)
    {
        tenant.UpdatedAt = DateTime.UtcNow;
        _context.Tenants.Update(tenant);
    }
}

