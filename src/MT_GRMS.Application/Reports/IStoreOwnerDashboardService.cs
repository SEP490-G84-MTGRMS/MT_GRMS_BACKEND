using MT_GRMS.Application.Reports.Dtos;

namespace MT_GRMS.Application.Reports;

public interface IStoreOwnerDashboardService
{
    Task<StoreOwnerDashboardResponse?> GetAsync(string period, Guid? branchId, CancellationToken ct = default);
}
