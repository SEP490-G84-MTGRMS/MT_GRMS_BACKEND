using MT_GRMS.Application.Branches.Dtos;
using MT_GRMS.Application.Common;

namespace MT_GRMS.Application.Branches;

public interface IBranchService
{
    Task<PagedResult<BranchDto>> GetPagedBranchesAsync(BranchFilterQuery query, CancellationToken cancellationToken = default);
    Task<BranchDto> GetBranchByIdAsync(Guid id, CancellationToken cancellationToken = default);
}
