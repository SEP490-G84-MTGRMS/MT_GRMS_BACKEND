using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Entities.SaaS;

namespace MT_GRMS.Application.Branches;

public interface IBranchRepository : IRepository<Branch>
{
    Task<(IReadOnlyList<Branch> Items, int TotalCount)> GetPagedListAsync(
        string? keyword,
        string? branchCode,
        string? status,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsByCodeAsync(string code, CancellationToken cancellationToken = default);
}
