using MT_GRMS.Application.Admin.Dtos;
using MT_GRMS.Application.Common;

namespace MT_GRMS.Application.Admin.Services;

public interface ITenantService
{
    Task<PagedResult<TenantSummaryResponse>> GetTenantsAsync(TenantFilterRequest filter, CancellationToken cancellationToken = default);
    Task<TenantDetailResponse> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<TenantDetailResponse> UpdateTenantAsync(string id, UpdateTenantRequest request, CancellationToken cancellationToken = default);
    Task LockTenantAsync(string id, LockTenantRequest request, string lockedBy, CancellationToken cancellationToken = default);
    Task UnlockTenantAsync(string id, CancellationToken cancellationToken = default);
    Task ExtendGracePeriodAsync(string id, ExtendGraceRequest request, CancellationToken cancellationToken = default);
}
