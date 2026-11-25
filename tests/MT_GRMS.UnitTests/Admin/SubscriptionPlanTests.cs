using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Admin.Dtos;
using MT_GRMS.Application.Admin.Repositories;
using MT_GRMS.Application.Admin.Services;
using MT_GRMS.Application.Admin.Validators;
using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Entities.SaaS;
using Xunit;

namespace MT_GRMS.UnitTests.Admin;

public class FakeSubscriptionPlanRepository : ISubscriptionPlanRepository
{
    public List<SubscriptionPlan> Plans { get; } = new();
    public Dictionary<string, int> TenantCounts { get; } = new();

    public Task<SubscriptionPlan?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        var plan = Plans.FirstOrDefault(p => p.Id == id && !p.IsDeleted);
        return Task.FromResult(plan);
    }

    public Task<SubscriptionPlan?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        var plan = Plans.FirstOrDefault(p => p.PlanCode.Equals(code, StringComparison.OrdinalIgnoreCase) && !p.IsDeleted);
        return Task.FromResult(plan);
    }

    public Task<IReadOnlyList<SubscriptionPlan>> GetAllAsync(string? keyword = null, string? status = null, CancellationToken cancellationToken = default)
    {
        IEnumerable<SubscriptionPlan> query = Plans.Where(p => !p.IsDeleted);

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.ToLower();
            query = query.Where(p => p.PlanName.ToLower().Contains(kw) || p.PlanCode.ToLower().Contains(kw));
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            var st = status.ToLower();
            if (st == "active") query = query.Where(p => p.IsActive);
            else if (st == "inactive") query = query.Where(p => !p.IsActive);
        }

        return Task.FromResult<IReadOnlyList<SubscriptionPlan>>(query.ToList());
    }

    public Task<int> CountActiveTenantsByPlanIdAsync(string planId, CancellationToken cancellationToken = default)
    {
        TenantCounts.TryGetValue(planId, out var count);
        return Task.FromResult(count);
    }

    public Task<Dictionary<string, int>> GetActiveTenantCountsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new Dictionary<string, int>(TenantCounts));
    }

    public Task AddAsync(SubscriptionPlan plan, CancellationToken cancellationToken = default)
    {
        Plans.Add(plan);
        return Task.CompletedTask;
    }

    public void Update(SubscriptionPlan plan)
    {
    }

    public void Delete(SubscriptionPlan plan)
    {
        plan.IsDeleted = true;
    }
}

public class FakeUnitOfWork : IUnitOfWork
{
    public int SaveChangesCalls { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveChangesCalls++;
        return Task.FromResult(1);
    }
}

public class SubscriptionPlanTests
{
    private readonly FakeSubscriptionPlanRepository _repo;
    private readonly FakeUnitOfWork _uow;
    private readonly SubscriptionPlanService _service;

    public SubscriptionPlanTests()
    {
        _repo = new FakeSubscriptionPlanRepository();
        _uow = new FakeUnitOfWork();
        _service = new SubscriptionPlanService(_repo, _uow);

        _repo.Plans.AddRange(new[]
        {
            new SubscriptionPlan
            {
                Id = "plan-starter",
                PlanCode = "STARTER",
                PlanName = "Gói Cơ Bản",
                PriceMonthly = 490000,
                PriceYearly = 4900000,
                MaxBranches = 1,
                MaxStaff = 3,
                IsActive = true
            },
            new SubscriptionPlan
            {
                Id = "plan-growth",
                PlanCode = "GROWTH",
                PlanName = "Gói Tăng Trưởng",
                PriceMonthly = 990000,
                PriceYearly = 9900000,
                MaxBranches = 5,
                MaxStaff = 15,
                IsActive = true
            }
        });

        _repo.TenantCounts["plan-starter"] = 0;
        _repo.TenantCounts["plan-growth"] = 4;
        _repo.TenantCounts["GROWTH"] = 4;
    }

    [Fact]
    public async Task REQ_ADMIN_BE01_GetPlans_ReturnsAllActivePlansWithCorrectCounts()
    {
        // Act
        var plans = await _service.GetPlansAsync(new PlanFilterRequest());

        // Assert
        Assert.NotNull(plans);
        Assert.Equal(2, plans.Count);
        var growth = plans.First(p => p.Code == "GROWTH");
        Assert.Equal(4, growth.ActiveTenantCount);
        Assert.Equal("ACTIVE", growth.Status);
    }

    [Fact]
    public async Task REQ_ADMIN_BE01_CreatePlan_Success_AddsPlanAndSaves()
    {
        // Arrange
        var request = new CreatePlanRequest(
            Code: "ENTERPRISE",
            Name: "Gói Doanh Nghiệp Toàn Diện",
            Description: "Không giới hạn chi nhánh",
            MonthlyPrice: 1990000,
            YearlyPrice: 19900000,
            Quotas: new PlanQuotaDto(MaxBranches: 20, MaxEmployees: 100, MaxStorageGb: 50, BackupFrequency: "Daily"),
            Features: new PlanFeatureDto()
        );

        // Act
        var result = await _service.CreatePlanAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("ENTERPRISE", result.Code);
        Assert.Equal(1990000, result.MonthlyPrice);
        Assert.Equal(1, _uow.SaveChangesCalls);
        Assert.Equal(3, _repo.Plans.Count);
    }

    [Fact]
    public async Task CreateInactivePlan_PreservesSelectedStatus()
    {
        var request = new CreatePlanRequest("DRAFT", "Chờ mở bán", null, 0m, 0m, null, null, IsActive: false);
        var result = await _service.CreatePlanAsync(request);
        Assert.Equal("INACTIVE", result.Status);
    }

    [Fact]
    public async Task REQ_ADMIN_BE01_CreatePlan_DuplicateCode_ThrowsInvalidOperationException()
    {
        // Arrange
        var request = new CreatePlanRequest(
            Code: "STARTER", // trùng mã
            Name: "Gói Trùng Mã",
            Description: null,
            MonthlyPrice: 500000,
            YearlyPrice: 5000000,
            Quotas: null,
            Features: null
        );

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.CreatePlanAsync(request));
        Assert.Contains("đã được sử dụng", ex.Message);
    }

    [Fact]
    public async Task REQ_ADMIN_BE01_DeletePlan_PlanInUse_ThrowsInvalidOperationException()
    {
        // Act & Assert (plan-growth đang có 4 tenants dùng)
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.DeletePlanAsync("plan-growth"));
        Assert.Contains("Không thể xóa gói dịch vụ đang có 4 cửa hàng sử dụng", ex.Message);
    }

    [Fact]
    public async Task REQ_ADMIN_BE01_DeletePlan_NoTenants_SuccessfullyDeletes()
    {
        // Act (plan-starter có 0 tenant dùng)
        await _service.DeletePlanAsync("plan-starter");

        // Assert
        var deletedPlan = _repo.Plans.First(p => p.Id == "plan-starter");
        Assert.True(deletedPlan.IsDeleted);
        Assert.Equal(1, _uow.SaveChangesCalls);
    }

    [Fact]
    public async Task REQ_ADMIN_BE01_ToggleStatus_FlipsActiveState()
    {
        // Act
        var newStatus = await _service.ToggleStatusAsync("plan-starter");

        // Assert
        Assert.False(newStatus);
        var plan = _repo.Plans.First(p => p.Id == "plan-starter");
        Assert.False(plan.IsActive);
    }

    [Fact]
    public void REQ_ADMIN_BE01_CreatePlanValidator_InvalidCodeOrNegativePrice_Fails()
    {
        // Arrange
        var validator = new CreatePlanRequestValidator();
        var badRequest = new CreatePlanRequest(
            Code: "lowercase-with-dash", // phải là UPPER_CASE
            Name: "",                     // rỗng
            Description: null,
            MonthlyPrice: -100,           // âm
            YearlyPrice: -200,            // âm
            Quotas: null,
            Features: null
        );

        // Act
        var result = validator.Validate(badRequest);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Code");
        Assert.Contains(result.Errors, e => e.PropertyName == "Name");
        Assert.Contains(result.Errors, e => e.PropertyName == "MonthlyPrice");
        Assert.Contains(result.Errors, e => e.PropertyName == "YearlyPrice");
    }

    [Fact]
    public async Task GetKpiBannerAsync_CalculatesMetricsCorrectly()
    {
        var banner = await _service.GetKpisAsync();
        Assert.NotNull(banner);
        Assert.Equal(2, banner.TotalPlans);
        Assert.Equal(2, banner.ActivePlans);
        Assert.Equal(4, banner.TotalActiveTenants);
        Assert.True(banner.EstimatedMrr > 0);
    }

    [Fact]
    public async Task GetByIdAsync_NotFound_And_ProPlan_Quotas()
    {
        // Not found
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.GetByIdAsync("non-existent"));

        // PRO plan
        _repo.Plans.Add(new SubscriptionPlan
        {
            Id = "plan-pro",
            PlanCode = "PRO",
            PlanName = "Pro Plan",
            PriceMonthly = 1500000,
            PriceYearly = 15000000,
            MaxBranches = 10,
            MaxStaff = 30,
            EnabledModulesJson = "{ malformed json",
            IsActive = true
        });

        var proDetail = await _service.GetByIdAsync("plan-pro");
        Assert.NotNull(proDetail);
        Assert.Equal(2, proDetail.Quotas?.MaxStorageGb);
    }

    [Fact]
    public async Task UpdatePlanAsync_NotFound_And_Success()
    {
        var req = new UpdatePlanRequest(
            Name: "Gói Cơ Bản Mới",
            Description: "Mô tả mới",
            MonthlyPrice: 550000,
            YearlyPrice: 5500000,
            Quotas: new PlanQuotaDto(2, 6, 4, "Daily"),
            Features: new PlanFeatureDto()
        );

        // Not found
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.UpdatePlanAsync("non-existent", req));

        // Success
        var updated = await _service.UpdatePlanAsync("plan-starter", req);
        Assert.NotNull(updated);
        Assert.Equal("Gói Cơ Bản Mới", updated.Name);
        Assert.Equal(550000, updated.MonthlyPrice);
    }

    [Fact]
    public async Task ToggleStatus_And_DeletePlan_NotFound_ThrowsKeyNotFoundException()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.ToggleStatusAsync("non-existent"));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.DeletePlanAsync("non-existent"));
    }

    [Fact]
    public async Task SubscriptionPlanRepository_GetTenantsByPlanIdAsync_ReturnsMatchingTenantsWithStatus()
    {
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<MT_GRMS.Infrastructure.Persistence.AppDbContext>()
            .UseInMemoryDatabase($"PlanTenants_{Guid.NewGuid()}")
            .Options;
        await using var db = new MT_GRMS.Infrastructure.Persistence.AppDbContext(options);

        db.SubscriptionPlans.Add(new SubscriptionPlan
        {
            Id = "plan-pro",
            PlanCode = "PRO",
            PlanName = "Gói Pro",
            PriceMonthly = 249000m,
            PriceYearly = 2490000m,
            MaxBranches = 5,
            MaxStaff = 20,
            IsActive = true
        });
        db.Tenants.AddRange(
            new Tenant
            {
                Id = "t-pro-active",
                Name = "Siêu Thị An Phú",
                Subdomain = "an-phu",
                Code = "AP-01",
                CurrentPlan = "PRO",
                Status = "ACTIVE",
                SubscriptionExpiresAt = DateTime.UtcNow.AddDays(30)
            },
            new Tenant
            {
                Id = "t-pro-locked",
                Name = "Tạp Hóa Bị Khóa",
                Subdomain = "locked-shop",
                Code = "LK-01",
                CurrentPlan = "PRO",
                Status = "LOCKED",
                LockType = "SOFT"
            }
        );
        await db.SaveChangesAsync();

        var realRepo = new MT_GRMS.Infrastructure.Repositories.SubscriptionPlanRepository(db);
        var realService = new SubscriptionPlanService(realRepo, _uow);

        var detail = await realService.GetByIdAsync("plan-pro");
        Assert.Equal(2, detail.ActiveTenantCount);
        Assert.Equal(2, detail.Tenants.Count);
        Assert.Contains(detail.Tenants, t => t.Id == "t-pro-active" && t.Status == "ACTIVE");
        Assert.Contains(detail.Tenants, t => t.Id == "t-pro-locked" && t.Status == "LOCKED");
    }
}

