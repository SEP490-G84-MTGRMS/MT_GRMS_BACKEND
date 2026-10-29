using MT_GRMS.Domain.Entities.SaaS;

namespace MT_GRMS.Application.Admin.Repositories;

public interface ISubscriptionPlanRepository
{
    Task<SubscriptionPlan?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<SubscriptionPlan?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SubscriptionPlan>> GetAllAsync(string? keyword = null, string? status = null, CancellationToken cancellationToken = default);
    Task<int> CountActiveTenantsByPlanIdAsync(string planId, CancellationToken cancellationToken = default);
    Task<Dictionary<string, int>> GetActiveTenantCountsAsync(CancellationToken cancellationToken = default);
    Task AddAsync(SubscriptionPlan plan, CancellationToken cancellationToken = default);
    void Update(SubscriptionPlan plan);
    void Delete(SubscriptionPlan plan);
}

