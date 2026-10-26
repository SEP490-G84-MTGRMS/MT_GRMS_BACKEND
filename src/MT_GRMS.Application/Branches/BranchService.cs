using MT_GRMS.Application.Branches.Dtos;
using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Domain.Exceptions;

namespace MT_GRMS.Application.Branches;

public class BranchService : IBranchService
{
    private readonly IBranchRepository _branchRepo;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITenantContext _tenantContext;

    public BranchService(
        IBranchRepository branchRepo,
        IUnitOfWork unitOfWork,
        ITenantContext tenantContext)
    {
        _branchRepo = branchRepo;
        _unitOfWork = unitOfWork;
        _tenantContext = tenantContext;
    }

    public async Task<PagedResult<BranchDto>> GetPagedBranchesAsync(
        BranchFilterQuery query,
        CancellationToken cancellationToken = default)
    {
        var pageIndex = Math.Max(1, query.PageIndex);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);

        var (items, totalCount) = await _branchRepo.GetPagedListAsync(
            query.Keyword,
            query.BranchCode,
            query.Status,
            pageIndex,
            pageSize,
            cancellationToken);

        var dtos = items.Select(MapToDto).ToList();

        return new PagedResult<BranchDto>
        {
            Items = dtos,
            Page = pageIndex,
            PageSize = pageSize,
            TotalItems = totalCount,
        };
    }

    public async Task<BranchDto> GetBranchByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var branch = await _branchRepo.GetByIdAsync(id, cancellationToken);
        if (branch == null || branch.IsDeleted)
        {
            throw new NotFoundException("Chi nhánh", id);
        }

        return MapToDto(branch);
    }

    private static BranchDto MapToDto(Branch branch)
    {
        return new BranchDto
        {
            Id = branch.Id,
            Code = branch.Code,
            Name = branch.Name,
            Address = branch.Address,
            Phone = branch.Phone,
            ManagerName = branch.ManagerName,
            Status = branch.Status,
            PendingOrdersCount = branch.PendingOrdersCount,
            LastOrderDate = branch.LastOrderDate,
            CreatedAt = branch.CreatedAt,
        };
    }
}
