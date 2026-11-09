using MT_GRMS.Application.Branches.Dtos;
using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Entities.SaaS;

namespace MT_GRMS.Application.Branches;

public interface IBranchRepository : IRepository<Branch>
{
    Task<(IReadOnlyList<Branch> Items, int TotalCount)> GetPagedListAsync(
        string? keyword,
        string? branchCode,
        string? status,
        IReadOnlyList<string>? branchCodes,
        IReadOnlyList<string>? branchNames,
        IReadOnlyList<string>? addresses,
        IReadOnlyList<string>? phones,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsByCodeAsync(string code, CancellationToken cancellationToken = default);

    Task<int> CountActiveAsync(CancellationToken cancellationToken = default);

    Task<int> CountTotalAsync(CancellationToken cancellationToken = default);

    Task<BranchDetailDto?> GetBranchDetailAsync(Guid id, CancellationToken cancellationToken = default);
}
