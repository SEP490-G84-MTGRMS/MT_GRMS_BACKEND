using System.Data;
using System.Text.RegularExpressions;
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
        if (dtos.Count == 1 && !dtos[0].IsHeadquarter)
        {
            dtos[0].IsHeadquarter = await IsOnlyBranchAsync(cancellationToken);
        }

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

        var dto = MapToDto(branch);
        dto.IsHeadquarter = dto.IsHeadquarter || await IsOnlyBranchAsync(cancellationToken);
        return dto;
    }

    public async Task<BranchDetailDto> GetBranchDetailAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var detail = await _branchRepo.GetBranchDetailAsync(id, cancellationToken);
        if (detail == null)
        {
            throw new NotFoundException("Chi nhánh", id);
        }

        detail.IsHeadquarter = detail.IsHeadquarter || await IsOnlyBranchAsync(cancellationToken);
        return detail;
    }

    public async Task<BranchQuotaDto> GetBranchQuotaAsync(CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.CurrentTenantId;
        var tenant = !string.IsNullOrWhiteSpace(tenantId) && tenantId != "DEFAULT"
            ? await _tenantRepo.GetByIdAsync(tenantId, cancellationToken)
            : (await _tenantRepo.GetPagedAsync(pageSize: 1, cancellationToken: cancellationToken)).Items.FirstOrDefault();

        var usedBranches = await _branchRepo.CountTotalAsync(cancellationToken);
        var maxBranches = 1;
        var planCode = tenant?.CurrentPlan ?? "NO_PLAN";
        var planName = "Gói Thường (Cửa Hàng Độc Lập)";

        if (tenant != null && !string.IsNullOrWhiteSpace(tenant.CurrentPlan))
        {
            var plan = await _planRepo.GetByCodeAsync(tenant.CurrentPlan, cancellationToken);
            if (plan != null)
            {
                maxBranches = plan.MaxBranches + tenant.ExtraBranches;
                planName = plan.PlanName;
            }
            else
            {
                if (tenant.CurrentPlan.Equals("PRO", StringComparison.OrdinalIgnoreCase))
                {
                    maxBranches = 2 + tenant.ExtraBranches;
                    planName = "Gói Pro (Chuỗi Cửa Hàng)";
                }
                else if (tenant.CurrentPlan.Equals("TRIAL", StringComparison.OrdinalIgnoreCase))
                {
                    maxBranches = 1 + tenant.ExtraBranches;
                    planName = "Gói Dùng Thử (Trải Nghiệm)";
                }
                else
                {
                    maxBranches = 1 + tenant.ExtraBranches;
                    planName = "Gói Thường (Cửa Hàng Độc Lập)";
                }
            }
        }
        else if (tenant != null)
        {
            maxBranches = 1 + tenant.ExtraBranches;
        }

        if (maxBranches <= 0) maxBranches = 1;

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

        var name = request.Name?.Trim();
        if (name is null || name.Length < 2)
            throw new BusinessException("INVALID_INPUT", "Tên chi nhánh phải có ít nhất 2 ký tự.");

        var code = request.BranchCode.Trim().ToUpper();
        var address = request.Address?.Trim();
        var phone = request.Phone?.Trim();
        var email = request.Email?.Trim();
        var managerName = request.ManagerName?.Trim();

        if (code.Length > 30)
            throw new BusinessException("INVALID_INPUT", "Mã chi nhánh không được vượt quá 30 ký tự.");
        if (name.Length > 150)
            throw new BusinessException("INVALID_INPUT", "Tên chi nhánh không được vượt quá 150 ký tự.");
        if (string.IsNullOrEmpty(address))
            throw new BusinessException("INVALID_INPUT", "Vui lòng nhập địa chỉ chi nhánh.");
        if (address.Length > 255)
            throw new BusinessException("INVALID_INPUT", "Địa chỉ chi nhánh không được vượt quá 255 ký tự.");
        if (!string.IsNullOrEmpty(phone) && (phone.Length > 20
            || !Regex.IsMatch(phone, @"^\+?[0-9][0-9 .-]*[0-9]$")
            || phone.Count(c => c is >= '0' and <= '9') is < 8 or > 15))
            throw new BusinessException("INVALID_INPUT", "Hotline phải có 8–15 chữ số và không vượt quá 20 ký tự. Có thể dùng dấu + ở đầu, dấu cách, dấu chấm hoặc dấu gạch ngang.");
        if (!string.IsNullOrEmpty(email) && (email.Length > 150
            || !Regex.IsMatch(email, @"^[^\s@.]+(?:\.[^\s@.]+)*@(?:[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?\.)+[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?$")))
            throw new BusinessException("INVALID_INPUT", "Email phải đúng định dạng và không vượt quá 150 ký tự.");
        if (managerName?.Length > 150)
            throw new BusinessException("INVALID_INPUT", "Tên quản lý không được vượt quá 150 ký tự.");

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
            Name = name,
            Address = address,
            Phone = string.IsNullOrEmpty(phone) ? null : phone,
            Email = string.IsNullOrEmpty(email) ? null : email,
            ManagerName = string.IsNullOrEmpty(managerName) ? null : managerName,
            IsHeadquarter = quota.UsedBranches == 0 || request.IsHeadquarter,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        await _branchRepo.AddAsync(branch, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return MapToDto(branch);
    }

    public async Task<BranchDto> UpdateBranchAsync(Guid id, UpdateBranchRequest request, CancellationToken cancellationToken = default)
    {
        var name = request.Name?.Trim();
        if (name is null || name.Length < 2)
            throw new BusinessException("INVALID_INPUT", "Tên chi nhánh phải có ít nhất 2 ký tự.");

        var branch = await _branchRepo.GetByIdAsync(id, cancellationToken);
        if (branch == null || branch.IsDeleted)
        {
            throw new NotFoundException("Chi nhánh", id);
        }

        var isOnlyBranch = await IsOnlyBranchAsync(cancellationToken);
        // Nếu đặt chi nhánh này làm Trụ sở chính, tự động gỡ cờ Trụ sở chính ở các chi nhánh khác
        if (!isOnlyBranch && request.IsHeadquarter && !branch.IsHeadquarter)
        {
            var paged = await _branchRepo.GetPagedListAsync(null, null, null, null, null, null, null, 1, 100, cancellationToken);
            foreach (var other in paged.Items.Where(b => b.Id != id && b.IsHeadquarter))
            {
                other.IsHeadquarter = false;
                other.UpdatedAt = DateTime.UtcNow;
                _branchRepo.Update(other);
            }
        }

        branch.Name = name;
        branch.Address = request.Address?.Trim() ?? string.Empty;
        branch.Phone = request.Phone?.Trim();
        branch.Email = request.Email?.Trim();
        branch.ManagerName = request.ManagerName?.Trim();
        branch.IsHeadquarter = isOnlyBranch || request.IsHeadquarter;
        branch.UpdatedAt = DateTime.UtcNow;

        _branchRepo.Update(branch);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return MapToDto(branch);
    }

    public Task<BranchDto> ToggleBranchStatusAsync(Guid id, UpdateBranchStatusRequest request, CancellationToken cancellationToken = default)
        => _unitOfWork.ExecuteInTransactionAsync(
            token => ChangeBranchStatusAsync(id, request, token), cancellationToken, IsolationLevel.Serializable);

    private async Task<BranchDto> ChangeBranchStatusAsync(Guid id, UpdateBranchStatusRequest request, CancellationToken cancellationToken)
    {
        var branch = await _branchRepo.GetByIdAsync(id, cancellationToken);
        if (branch == null || branch.IsDeleted)
        {
            throw new NotFoundException("Chi nhánh", id);
        }

        var isOnlyBranch = await IsOnlyBranchAsync(cancellationToken);
        if (!request.IsActive && isOnlyBranch)
        {
            throw new BusinessException("CANNOT_DISABLE_ONLY_BRANCH", "Không thể ngừng hoạt động chi nhánh duy nhất của gian hàng.");
        }

        // Kiểm tra hạn mức nếu kích hoạt lại chi nhánh
        if (request.IsActive && !branch.IsActive)
        {
            var quota = await GetBranchQuotaAsync(cancellationToken);
            if (quota.UsedBranches > quota.MaxBranches)
            {
                throw new BusinessException("QUOTA_EXCEEDED",
                    $"Không thể kích hoạt chi nhánh '{branch.Name}'. Chuỗi cửa hàng của bạn hiện có {quota.UsedBranches}/{quota.MaxBranches} chi nhánh (đã vượt quá hạn mức gói {quota.CurrentPlanName}). Vui lòng nâng cấp gói cước.");
            }
        }

        branch.IsActive = request.IsActive;
        branch.IsHeadquarter = isOnlyBranch || branch.IsHeadquarter;
        branch.UpdatedAt = DateTime.UtcNow;

        _branchRepo.Update(branch);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return MapToDto(branch);
    }

    public Task DeleteBranchAsync(Guid id, CancellationToken cancellationToken = default)
        => _unitOfWork.ExecuteInTransactionAsync(
            token => RemoveBranchAsync(id, token), cancellationToken, IsolationLevel.Serializable);

    private async Task RemoveBranchAsync(Guid id, CancellationToken cancellationToken)
    {
        var branch = await _branchRepo.GetByIdAsync(id, cancellationToken);
        if (branch == null || branch.IsDeleted)
        {
            throw new NotFoundException("Chi nhánh", id);
        }

        if (branch.IsHeadquarter)
        {
            throw new BusinessException("CANNOT_DELETE_HEADQUARTER", "Không thể xóa chi nhánh chính (Trụ sở chính) của chuỗi cửa hàng. Vui lòng chỉ định chi nhánh khác làm trụ sở chính trước khi xóa.");
        }

        if (await IsOnlyBranchAsync(cancellationToken))
        {
            throw new BusinessException("CANNOT_DELETE_ONLY_BRANCH", "Không thể xóa chi nhánh duy nhất của gian hàng.");
        }

        branch.IsDeleted = true;
        branch.IsActive = false;
        branch.UpdatedAt = DateTime.UtcNow;

        _branchRepo.Update(branch);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task<bool> IsOnlyBranchAsync(CancellationToken cancellationToken)
        => await _branchRepo.CountTotalAsync(cancellationToken) == 1;

    private static BranchDto MapToDto(Branch branch)
    {
        return new BranchDto
        {
            Id = branch.Id,
            Code = branch.Code,
            Name = branch.Name,
            Address = branch.Address ?? string.Empty,
            Phone = branch.Phone,
            Email = branch.Email,
            ManagerName = branch.ManagerName,
            IsHeadquarter = branch.IsHeadquarter,
            Status = branch.Status,
            PendingOrdersCount = branch.PendingOrdersCount,
            LastOrderDate = branch.LastOrderDate,
            CreatedAt = branch.CreatedAt,
        };
    }
}
