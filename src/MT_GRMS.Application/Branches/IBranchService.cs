using MT_GRMS.Application.Branches.Dtos;
using MT_GRMS.Application.Common;

namespace MT_GRMS.Application.Branches;

public interface IBranchService
{
    Task<PagedResult<BranchDto>> GetPagedBranchesAsync(BranchFilterQuery query, CancellationToken cancellationToken = default);
    Task<BranchDto> GetBranchByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<BranchQuotaDto> GetBranchQuotaAsync(CancellationToken cancellationToken = default);
    Task<BranchDto> CreateBranchAsync(CreateBranchRequest request, CancellationToken cancellationToken = default);
    Task<BranchDto> UpdateBranchAsync(Guid id, UpdateBranchRequest request, CancellationToken cancellationToken = default);
    Task<BranchDto> ToggleBranchStatusAsync(Guid id, UpdateBranchStatusRequest request, CancellationToken cancellationToken = default);
    Task DeleteBranchAsync(Guid id, CancellationToken cancellationToken = default);
}
