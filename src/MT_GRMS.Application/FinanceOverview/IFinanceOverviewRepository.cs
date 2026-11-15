using MT_GRMS.Application.FinanceOverview.Dtos;

namespace MT_GRMS.Application.FinanceOverview;

public interface IFinanceOverviewRepository
{
    Task<FinanceOverviewResponse> GetOverviewAsync(
        Guid? branchId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken ct = default);
}
