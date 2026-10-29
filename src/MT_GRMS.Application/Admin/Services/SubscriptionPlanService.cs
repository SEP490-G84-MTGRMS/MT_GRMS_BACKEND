using System.Text.Json;
using MT_GRMS.Application.Admin.Dtos;
using MT_GRMS.Application.Admin.Repositories;
using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Entities.SaaS;

namespace MT_GRMS.Application.Admin.Services;

public class SubscriptionPlanService : ISubscriptionPlanService
{
    private readonly ISubscriptionPlanRepository _planRepository;
    private readonly IUnitOfWork _unitOfWork;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public SubscriptionPlanService(
        ISubscriptionPlanRepository planRepository,
        IUnitOfWork unitOfWork)
    {
        _planRepository = planRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<IReadOnlyList<PlanSummaryResponse>> GetPlansAsync(PlanFilterRequest filter, CancellationToken cancellationToken = default)
    {
        var plans = await _planRepository.GetAllAsync(filter.Keyword, filter.Status, cancellationToken);
        var tenantCounts = await _planRepository.GetActiveTenantCountsAsync(cancellationToken);

        var result = new List<PlanSummaryResponse>();
        foreach (var plan in plans)
        {
            var count = 0;
            if (tenantCounts.TryGetValue(plan.PlanCode, out var c1)) count = c1;
            else if (tenantCounts.TryGetValue(plan.Id, out var c2)) count = c2;

            var (description, quotas, features) = ParseModulesJson(plan.EnabledModulesJson, plan.MaxBranches, plan.MaxStaff);

            result.Add(new PlanSummaryResponse(
                Id: plan.Id,
                Code: plan.PlanCode,
                Name: plan.PlanName,
                Description: description,
                MonthlyPrice: plan.PriceMonthly,
                YearlyPrice: plan.PriceYearly,
                Status: plan.IsActive ? "ACTIVE" : "INACTIVE",
                ActiveTenantCount: count,
                Quotas: quotas,
                Features: features,
                CreatedAt: plan.CreatedAt,
                UpdatedAt: plan.UpdatedAt
            ));
        }

        return result;
    }

    public async Task<PlanKpiBannerResponse> GetKpisAsync(CancellationToken cancellationToken = default)
    {
        var plans = await _planRepository.GetAllAsync(cancellationToken: cancellationToken);
        var tenantCounts = await _planRepository.GetActiveTenantCountsAsync(cancellationToken);

        var totalPlans = plans.Count;
        var activePlans = plans.Count(p => p.IsActive);
        var totalActiveTenants = 0;
        decimal totalRevenueMonthly = 0;

        foreach (var plan in plans)
        {
            var count = 0;
            if (tenantCounts.TryGetValue(plan.PlanCode, out var c1)) count = c1;
            else if (tenantCounts.TryGetValue(plan.Id, out var c2)) count = c2;

            totalActiveTenants += count;
            totalRevenueMonthly += plan.PriceMonthly * count;
        }

        return new PlanKpiBannerResponse(
            TotalPlans: totalPlans,
            ActivePlans: activePlans,
            TotalActiveTenants: totalActiveTenants,
            EstimatedMrr: totalRevenueMonthly
        );
    }

    public async Task<PlanDetailResponse> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        var plan = await _planRepository.GetByIdAsync(id, cancellationToken);
        if (plan == null)
        {
            throw new KeyNotFoundException($"Không tìm thấy gói dịch vụ với mã định danh '{id}'.");
        }

        var tenantCount = await _planRepository.CountActiveTenantsByPlanIdAsync(id, cancellationToken);
        var (description, quotas, features) = ParseModulesJson(plan.EnabledModulesJson, plan.MaxBranches, plan.MaxStaff);

        return new PlanDetailResponse(
            Id: plan.Id,
            Code: plan.PlanCode,
            Name: plan.PlanName,
            Description: description,
            MonthlyPrice: plan.PriceMonthly,
            YearlyPrice: plan.PriceYearly,
            Status: plan.IsActive ? "ACTIVE" : "INACTIVE",
            ActiveTenantCount: tenantCount,
            Quotas: quotas,
            Features: features,
            Tenants: new List<PlanTenantSummaryDto>(),
            CreatedAt: plan.CreatedAt,
            UpdatedAt: plan.UpdatedAt
        );
    }

    public async Task<PlanDetailResponse> CreatePlanAsync(CreatePlanRequest request, CancellationToken cancellationToken = default)
    {
        var existing = await _planRepository.GetByCodeAsync(request.Code, cancellationToken);
        if (existing != null)
        {
            throw new InvalidOperationException($"Mã gói cước '{request.Code}' đã được sử dụng.");
        }

        var modulesJson = BuildModulesJson(request.Description, request.Quotas, request.Features);

        var plan = new SubscriptionPlan
        {
            PlanCode = request.Code.Trim().ToUpperInvariant(),
            PlanName = request.Name.Trim(),
            PriceMonthly = request.MonthlyPrice,
            PriceYearly = request.YearlyPrice,
            MaxBranches = request.Quotas?.MaxBranches ?? 1,
            MaxStaff = request.Quotas?.MaxEmployees ?? 5,
            EnabledModulesJson = modulesJson,
            IsActive = true
        };

        await _planRepository.AddAsync(plan, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(plan.Id, cancellationToken);
    }

    public async Task<PlanDetailResponse> UpdatePlanAsync(string id, UpdatePlanRequest request, CancellationToken cancellationToken = default)
    {
        var plan = await _planRepository.GetByIdAsync(id, cancellationToken);
        if (plan == null)
        {
            throw new KeyNotFoundException($"Không tìm thấy gói dịch vụ với mã định danh '{id}'.");
        }

        var modulesJson = BuildModulesJson(request.Description, request.Quotas, request.Features);

        plan.PlanName = request.Name.Trim();
        plan.PriceMonthly = request.MonthlyPrice;
        plan.PriceYearly = request.YearlyPrice;
        plan.MaxBranches = request.Quotas?.MaxBranches ?? plan.MaxBranches;
        plan.MaxStaff = request.Quotas?.MaxEmployees ?? plan.MaxStaff;
        plan.EnabledModulesJson = modulesJson;

        _planRepository.Update(plan);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(plan.Id, cancellationToken);
    }

    public async Task<bool> ToggleStatusAsync(string id, CancellationToken cancellationToken = default)
    {
        var plan = await _planRepository.GetByIdAsync(id, cancellationToken);
        if (plan == null)
        {
            throw new KeyNotFoundException($"Không tìm thấy gói dịch vụ với mã định danh '{id}'.");
        }

        plan.IsActive = !plan.IsActive;
        _planRepository.Update(plan);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return plan.IsActive;
    }

    public async Task DeletePlanAsync(string id, CancellationToken cancellationToken = default)
    {
        var plan = await _planRepository.GetByIdAsync(id, cancellationToken);
        if (plan == null)
        {
            throw new KeyNotFoundException($"Không tìm thấy gói dịch vụ với mã định danh '{id}'.");
        }

        var activeTenants = await _planRepository.CountActiveTenantsByPlanIdAsync(id, cancellationToken);
        if (activeTenants > 0)
        {
            throw new InvalidOperationException($"Không thể xóa gói dịch vụ đang có {activeTenants} cửa hàng sử dụng.");
        }

        _planRepository.Delete(plan);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private static string BuildModulesJson(string? description, PlanQuotaDto? quotas, PlanFeatureDto? features)
    {
        var payload = new
        {
            description = description ?? string.Empty,
            quotas = quotas ?? new PlanQuotaDto(),
            features = features ?? new PlanFeatureDto()
        };

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private static (string Description, PlanQuotaDto Quotas, PlanFeatureDto Features) ParseModulesJson(string? json, int maxBranches, int maxStaff)
    {
        var defaultQuotas = new PlanQuotaDto(
            MaxBranches: maxBranches > 0 ? maxBranches : 1,
            MaxEmployees: maxStaff > 0 ? maxStaff : 5,
            MaxStorageGb: 5,
            BackupFrequency: "Weekly"
        );

        var defaultFeatures = new PlanFeatureDto();

        if (string.IsNullOrWhiteSpace(json))
        {
            return (string.Empty, defaultQuotas, defaultFeatures);
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Object)
            {
                var desc = root.TryGetProperty("description", out var descProp) ? descProp.GetString() ?? string.Empty : string.Empty;
                PlanQuotaDto? q = null;
                PlanFeatureDto? f = null;

                if (root.TryGetProperty("quotas", out var quotasProp) && quotasProp.ValueKind == JsonValueKind.Object)
                {
                    q = JsonSerializer.Deserialize<PlanQuotaDto>(quotasProp.GetRawText(), JsonOptions);
                }

                if (root.TryGetProperty("features", out var featuresProp) && featuresProp.ValueKind == JsonValueKind.Object)
                {
                    f = JsonSerializer.Deserialize<PlanFeatureDto>(featuresProp.GetRawText(), JsonOptions);
                }

                return (desc, q ?? defaultQuotas, f ?? defaultFeatures);
            }
        }
        catch
        {
            // Fallback
        }

        return (string.Empty, defaultQuotas, defaultFeatures);
    }
}
