using MT_GRMS.Application.FinanceOverview.Dtos;
using MT_GRMS.Domain.Exceptions;

namespace MT_GRMS.Application.FinanceOverview;

public sealed class FinanceOverviewService : IFinanceOverviewService
{
    private static readonly TimeSpan MaximumRange = TimeSpan.FromDays(366);
    private readonly IFinanceOverviewRepository _repository;

    public FinanceOverviewService(IFinanceOverviewRepository repository)
    {
        _repository = repository;
    }

    public Task<FinanceOverviewResponse> GetOverviewAsync(
        Guid? branchId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken ct = default)
    {
        if (fromUtc.Kind != DateTimeKind.Utc || toUtc.Kind != DateTimeKind.Utc)
        {
            throw new BusinessException("INVALID_PERIOD", "fromDate và toDate phải là thời gian UTC.");
        }

        if (fromUtc > toUtc)
        {
            throw new BusinessException("INVALID_PERIOD", "Ngày bắt đầu không được sau ngày kết thúc.");
        }

        if (toUtc - fromUtc > MaximumRange)
        {
            throw new BusinessException("PERIOD_TOO_LARGE", "Khoảng thời gian tổng quan không được vượt quá 366 ngày.");
        }

        return _repository.GetOverviewAsync(branchId, fromUtc, toUtc, ct);
    }
}
