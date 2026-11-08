using MT_GRMS.Application.Admin.Repositories;
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
    private readonly ITenantRepository _tenantRepo;
    private readonly ISubscriptionPlanRepository _planRepo;

    public BranchService(
        IBranchRepository branchRepo,
        IUnitOfWork unitOfWork,
        ITenantContext tenantContext,
        ITenantRepository tenantRepo,
        ISubscriptionPlanRepository planRepo)
    {
        _branchRepo = branchRepo;
        _unitOfWork = unitOfWork;
        _tenantContext = tenantContext;
        _tenantRepo = tenantRepo;
        _planRepo = planRepo;
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
            query.BranchCodes,
            query.BranchNames,
            query.Addresses,
            query.Phones,
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

    public async Task<BranchQuotaDto> GetBranchQuotaAsync(CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.CurrentTenantId;
        var tenant = !string.IsNullOrWhiteSpace(tenantId) && tenantId != "DEFAULT"
            ? await _tenantRepo.GetByIdAsync(tenantId, cancellationToken)
            : (await _tenantRepo.GetPagedAsync(pageSize: 1, cancellationToken: cancellationToken)).Items.FirstOrDefault();

        var usedBranches = await _branchRepo.CountActiveAsync(cancellationToken);
        var maxBranches = 1;
        var planCode = tenant?.CurrentPlan ?? "FREE";
        var planName = "Gói Cơ Bản (Miễn Phí)";

        if (tenant != null && !string.IsNullOrWhiteSpace(tenant.CurrentPlan))
        {
            var plan = await _planRepo.GetByCodeAsync(tenant.CurrentPlan, cancellationToken);
            if (plan != null)
            {
                maxBranches = plan.MaxBranches;
                planName = plan.PlanName;
            }
        }

        if (maxBranches <= 0) maxBranches = 10;

        return new BranchQuotaDto(
            UsedBranches: usedBranches,
            MaxBranches: maxBranches,
            CanAddMore: usedBranches < maxBranches,
            CurrentPlanCode: planCode,
            CurrentPlanName: planName
        );
    }

    public async Task<BranchDto> CreateBranchAsync(CreateBranchRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.BranchCode))
            throw new BusinessException("INVALID_INPUT", "Mã chi nhánh không được để trống.");

        if (string.IsNullOrWhiteSpace(request.Name))
            throw new BusinessException("INVALID_INPUT", "Tên chi nhánh không được để trống.");

        var code = request.BranchCode.Trim().ToUpper();

        // 1. Kiểm tra trùng mã chi nhánh
        if (await _branchRepo.ExistsByCodeAsync(code, cancellationToken))
        {
            throw new BusinessException("DUPLICATE_CODE", $"Mã chi nhánh '{code}' đã tồn tại trong chuỗi cửa hàng.");
        }

        // 2. Kiểm tra hạn mức chi nhánh (Quota)
        var quota = await GetBranchQuotaAsync(cancellationToken);
        if (!quota.CanAddMore)
        {
            throw new BusinessException("QUOTA_EXCEEDED", $"Chuỗi cửa hàng của bạn đã đạt giới hạn tối đa {quota.MaxBranches} chi nhánh của gói {quota.CurrentPlanName}. Vui lòng nâng cấp gói cước để tiếp tục thêm chi nhánh.");
        }

        var tenantId = _tenantContext.CurrentTenantId;
        var branch = new Branch
        {
            Id = Guid.NewGuid(),
            TenantId = !string.IsNullOrWhiteSpace(tenantId) && tenantId != "DEFAULT" ? tenantId : "t-001-minhduc",
            BranchCode = code,
            Name = request.Name.Trim(),
            Address = request.Address?.Trim() ?? string.Empty,
            Phone = request.Phone?.Trim(),
            ManagerName = request.ManagerName?.Trim(),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        await _branchRepo.AddAsync(branch, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return MapToDto(branch);
    }

    public async Task<BranchDto> UpdateBranchAsync(Guid id, UpdateBranchRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new BusinessException("INVALID_INPUT", "Tên chi nhánh không được để trống.");

        var branch = await _branchRepo.GetByIdAsync(id, cancellationToken);
        if (branch == null || branch.IsDeleted)
        {
            throw new NotFoundException("Chi nhánh", id);
        }

        branch.Name = request.Name.Trim();
        branch.Address = request.Address?.Trim() ?? string.Empty;
        branch.Phone = request.Phone?.Trim();
        branch.ManagerName = request.ManagerName?.Trim();
        branch.UpdatedAt = DateTime.UtcNow;

        _branchRepo.Update(branch);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return MapToDto(branch);
    }

    public async Task<BranchDto> ToggleBranchStatusAsync(Guid id, UpdateBranchStatusRequest request, CancellationToken cancellationToken = default)
    {
        var branch = await _branchRepo.GetByIdAsync(id, cancellationToken);
        if (branch == null || branch.IsDeleted)
        {
            throw new NotFoundException("Chi nhánh", id);
        }

        branch.IsActive = request.IsActive;
        branch.UpdatedAt = DateTime.UtcNow;

        _branchRepo.Update(branch);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return MapToDto(branch);
    }

    private static BranchDto MapToDto(Branch branch)
    {
        return new BranchDto
        {
            Id = branch.Id,
            Code = branch.Code,
            Name = branch.Name,
            Address = branch.Address ?? string.Empty,
            Phone = branch.Phone,
            ManagerName = branch.ManagerName,
            Status = branch.Status,
            PendingOrdersCount = branch.PendingOrdersCount,
            LastOrderDate = branch.LastOrderDate,
            CreatedAt = branch.CreatedAt,
        };
    }
}
