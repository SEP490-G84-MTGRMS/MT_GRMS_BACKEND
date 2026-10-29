using MT_GRMS.Application.Admin.Dtos;

namespace MT_GRMS.Application.Admin.Services;

public interface ISubscriptionPlanService
{
    Task<IReadOnlyList<PlanSummaryResponse>> GetPlansAsync(PlanFilterRequest filter, CancellationToken cancellationToken = default);
    Task<PlanKpiBannerResponse> GetKpisAsync(CancellationToken cancellationToken = default);
    Task<PlanDetailResponse> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<PlanDetailResponse> CreatePlanAsync(CreatePlanRequest request, CancellationToken cancellationToken = default);
    Task<PlanDetailResponse> UpdatePlanAsync(string id, UpdatePlanRequest request, CancellationToken cancellationToken = default);
    Task<bool> ToggleStatusAsync(string id, CancellationToken cancellationToken = default);
    Task DeletePlanAsync(string id, CancellationToken cancellationToken = default);
}

