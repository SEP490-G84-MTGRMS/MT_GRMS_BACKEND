using MT_GRMS.Application.Admin.Dtos;
using MT_GRMS.Application.Admin.Repositories;
using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Entities.SaaS;

namespace MT_GRMS.Application.Admin.Services;

public class TenantService : ITenantService
{
    private readonly ITenantRepository _tenantRepository;
    private readonly IUnitOfWork _unitOfWork;

    public TenantService(
        ITenantRepository tenantRepository,
        IUnitOfWork unitOfWork)
    {
        _tenantRepository = tenantRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<PagedResult<TenantSummaryResponse>> GetTenantsAsync(TenantFilterRequest filter, CancellationToken cancellationToken = default)
    {
        var page = Math.Max(1, filter.Page);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);

        var (items, totalCount) = await _tenantRepository.GetPagedAsync(
            keyword: filter.Keyword,
            status: filter.Status,
            planCode: filter.PlanCode,
            page: page,
            pageSize: pageSize,
            cancellationToken: cancellationToken
        );

        var now = DateTime.UtcNow;
        var dtos = items.Select(t =>
        {
            var owner = t.TenantUsers.FirstOrDefault(tu => tu.Role == "STORE_OWNER" || tu.Role == "TENANT_ADMIN")?.User
                        ?? t.TenantUsers.FirstOrDefault()?.User;

            var ownerName = !string.IsNullOrWhiteSpace(owner?.FullName) ? owner.FullName : (owner?.UserName ?? t.Name);
            var ownerEmail = owner?.Email ?? string.Empty;
            var ownerPhone = !string.IsNullOrWhiteSpace(owner?.Phone) ? owner.Phone : (t.Hotline ?? string.Empty);

            var (status, graceDaysRemaining, daysUntilExpiry) = ComputeStatus(t, now);

            return new TenantSummaryResponse(
                Id: t.Id,
                Subdomain: t.Subdomain,
                Name: t.Name,
                TaxCode: t.TaxCode,
                City: t.City,
                OwnerName: ownerName,
                OwnerEmail: ownerEmail,
                OwnerPhone: ownerPhone,
                Plan: t.SubscriptionPlan?.PlanName ?? t.CurrentPlan ?? "Chưa kích hoạt",
                BranchCount: t.Branches.Count,
                MaxBranches: t.SubscriptionPlan?.MaxBranches ?? 1,
                StaffCount: t.TenantUsers.Count,
                MaxStaff: t.SubscriptionPlan?.MaxStaff ?? 5,
                ExpiryDate: t.SubscriptionExpiresAt,
                Status: status,
                LockType: t.LockType,
                GraceDaysRemaining: graceDaysRemaining,
                DaysUntilExpiry: daysUntilExpiry
            );
        }).ToList();

        return new PagedResult<TenantSummaryResponse>
        {
            Items = dtos,
            TotalItems = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<TenantDetailResponse> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        var t = await _tenantRepository.GetByIdWithDetailsAsync(id, cancellationToken);
        if (t == null)
        {
            throw new KeyNotFoundException($"Không tìm thấy khách thuê với mã định danh '{id}'.");
        }

        var branches = t.Branches.Select(b => new TenantDetailBranchSummaryDto(
            Id: b.Id,
            BranchCode: b.BranchCode,
            Name: b.Name,
            Address: b.Address,
            Phone: b.Phone,
            IsActive: b.IsActive
        )).ToList();

        var now = DateTime.UtcNow;
        var (status, _, _) = ComputeStatus(t, now);

        return new TenantDetailResponse(
            Id: t.Id,
            Subdomain: t.Subdomain,
            Name: t.Name,
            Code: t.Code,
            TaxCode: t.TaxCode,
            Hotline: t.Hotline,
            Address: t.Address,
            City: t.City,
            Status: status,
            CurrentPlan: t.CurrentPlan,
            PlanName: t.SubscriptionPlan?.PlanName ?? t.CurrentPlan ?? "Chưa kích hoạt",
            SubscriptionExpiresAt: t.SubscriptionExpiresAt,
            GracePeriodExpiresAt: t.GracePeriodExpiresAt,
            LockType: t.LockType,
            LockReason: t.LockReason,
            LockedAt: t.LockedAt,
            LockedBy: t.LockedBy,
            BranchCount: t.Branches.Count,
            MaxBranches: t.SubscriptionPlan?.MaxBranches ?? 1,
            StaffCount: t.TenantUsers.Count,
            MaxStaff: t.SubscriptionPlan?.MaxStaff ?? 5,
            Branches: branches,
            CreatedAt: t.CreatedAt,
            UpdatedAt: t.UpdatedAt
        );
    }

    public async Task<TenantDetailResponse> UpdateTenantAsync(string id, UpdateTenantRequest request, CancellationToken cancellationToken = default)
    {
        var tenant = await _tenantRepository.GetByIdAsync(id, cancellationToken);
        if (tenant == null)
        {
            throw new KeyNotFoundException($"Không tìm thấy khách thuê với mã định danh '{id}'.");
        }

        tenant.Name = request.Name.Trim();
        tenant.TaxCode = request.TaxCode?.Trim();
        tenant.Hotline = request.Hotline?.Trim();
        tenant.Address = request.Address?.Trim();
        tenant.City = request.City?.Trim();

        _tenantRepository.Update(tenant);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(id, cancellationToken);
    }

    public async Task LockTenantAsync(string id, LockTenantRequest request, string lockedBy, CancellationToken cancellationToken = default)
    {
        var tenant = await _tenantRepository.GetByIdAsync(id, cancellationToken);
        if (tenant == null)
        {
            throw new KeyNotFoundException($"Không tìm thấy khách thuê với mã định danh '{id}'.");
        }

        var lockType = request.LockType.Trim().ToUpperInvariant();
        if (lockType != "SOFT" && lockType != "HARD")
        {
            throw new ArgumentException("Loại khóa chỉ chấp nhận SOFT (khóa tính năng ghi/chỉ cho đọc) hoặc HARD (khóa hoàn toàn).", nameof(request.LockType));
        }

        tenant.LockType = lockType;
        tenant.LockReason = request.Reason.Trim();
        tenant.LockedAt = DateTime.UtcNow;
        tenant.LockedBy = lockedBy;
        tenant.Status = "LOCKED";

        _tenantRepository.Update(tenant);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task UnlockTenantAsync(string id, CancellationToken cancellationToken = default)
    {
        var tenant = await _tenantRepository.GetByIdAsync(id, cancellationToken);
        if (tenant == null)
        {
            throw new KeyNotFoundException($"Không tìm thấy khách thuê với mã định danh '{id}'.");
        }

        tenant.LockType = null;
        tenant.LockReason = null;
        tenant.LockedAt = null;
        tenant.LockedBy = null;

        var now = DateTime.UtcNow;
        if (tenant.SubscriptionExpiresAt != null && tenant.SubscriptionExpiresAt <= now)
        {
            tenant.Status = (tenant.GracePeriodExpiresAt != null && tenant.GracePeriodExpiresAt >= now)
                ? "GRACE_PERIOD"
                : "EXPIRED";
        }
        else
        {
            tenant.Status = "ACTIVE";
        }

        _tenantRepository.Update(tenant);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task ExtendGracePeriodAsync(string id, ExtendGraceRequest request, CancellationToken cancellationToken = default)
    {
        if (request.AdditionalDays <= 0)
        {
            throw new ArgumentException("Số ngày gia hạn ân hạn phải lớn hơn 0.", nameof(request.AdditionalDays));
        }

        var tenant = await _tenantRepository.GetByIdAsync(id, cancellationToken);
        if (tenant == null)
        {
            throw new KeyNotFoundException($"Không tìm thấy khách thuê với mã định danh '{id}'.");
        }

        var now = DateTime.UtcNow;
        var baseDate = (tenant.GracePeriodExpiresAt != null && tenant.GracePeriodExpiresAt > now)
            ? tenant.GracePeriodExpiresAt.Value
            : now;

        tenant.GracePeriodExpiresAt = baseDate.AddDays(request.AdditionalDays);
        if (tenant.LockType == null)
        {
            tenant.Status = "GRACE_PERIOD";
        }

        _tenantRepository.Update(tenant);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private static (string Status, int? GraceDaysRemaining, int? DaysUntilExpiry) ComputeStatus(Tenant t, DateTime now)
    {
        int? graceDays = null;
        int? daysUntilExpiry = null;

        if (t.GracePeriodExpiresAt != null && t.GracePeriodExpiresAt > now)
        {
            graceDays = (int)Math.Ceiling((t.GracePeriodExpiresAt.Value - now).TotalDays);
        }

        if (t.SubscriptionExpiresAt != null && t.SubscriptionExpiresAt > now)
        {
            daysUntilExpiry = (int)Math.Ceiling((t.SubscriptionExpiresAt.Value - now).TotalDays);
        }

        if (t.LockType != null || t.Status == "LOCKED")
        {
            return ("LOCKED", graceDays, daysUntilExpiry);
        }

        if (t.Status == "GRACE_PERIOD" || (t.SubscriptionExpiresAt != null && t.SubscriptionExpiresAt <= now && (t.GracePeriodExpiresAt == null || t.GracePeriodExpiresAt >= now)))
        {
            return ("GRACE", graceDays, daysUntilExpiry);
        }

        if (daysUntilExpiry.HasValue && daysUntilExpiry <= 7)
        {
            return ("EXPIRING", graceDays, daysUntilExpiry);
        }

        return (t.Status, graceDays, daysUntilExpiry);
    }
}
