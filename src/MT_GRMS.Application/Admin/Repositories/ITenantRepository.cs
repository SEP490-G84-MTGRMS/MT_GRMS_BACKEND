using MT_GRMS.Domain.Entities.SaaS;

namespace MT_GRMS.Application.Admin.Repositories;

public interface ITenantRepository
{
    Task<Tenant?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<Tenant?> GetByIdWithDetailsAsync(string id, CancellationToken cancellationToken = default);
    Task<Tenant?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);
    Task<Tenant?> GetBySubdomainAsync(string subdomain, CancellationToken cancellationToken = default);
    Task<(IReadOnlyList<Tenant> Items, int TotalCount)> GetPagedAsync(
        string? keyword = null,
        string? status = null,
        string? planCode = null,
        int page = 1,
        int pageSize = 10,
        CancellationToken cancellationToken = default);
    Task<int> GetTotalCountAsync(CancellationToken cancellationToken = default);
    Task<int> GetActiveCountAsync(CancellationToken cancellationToken = default);
    Task<int> GetExpiringSoonCountAsync(int daysThreshold = 7, CancellationToken cancellationToken = default);
    Task<int> GetGracePeriodCountAsync(CancellationToken cancellationToken = default);
    Task<int> GetLockedCountAsync(CancellationToken cancellationToken = default);
    Task<int> GetTotalBranchesCountAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Tenant>> GetUrgentAlertTenantsAsync(int limit = 10, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Tenant>> GetAllActiveWithPlanAsync(CancellationToken cancellationToken = default);
    void Update(Tenant tenant);
}

