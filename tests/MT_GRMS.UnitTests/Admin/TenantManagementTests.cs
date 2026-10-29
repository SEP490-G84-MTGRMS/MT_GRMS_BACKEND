using MT_GRMS.Application.Admin.Dtos;
using MT_GRMS.Application.Admin.Repositories;
using MT_GRMS.Application.Admin.Services;
using MT_GRMS.Application.Admin.Validators;
using MT_GRMS.Domain.Entities.Identity;
using MT_GRMS.Domain.Entities.SaaS;
using Xunit;

namespace MT_GRMS.UnitTests.Admin;

public class FakeTenantRepository : ITenantRepository
{
    public List<Tenant> Tenants { get; } = new();

    public Task<Tenant?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Tenants.FirstOrDefault(t => t.Id == id && !t.IsDeleted));
    }

    public Task<Tenant?> GetByIdWithDetailsAsync(string id, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Tenants.FirstOrDefault(t => t.Id == id && !t.IsDeleted));
    }

    public Task<Tenant?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Tenants.FirstOrDefault(t => t.Code.Equals(code, StringComparison.OrdinalIgnoreCase) && !t.IsDeleted));
    }

    public Task<Tenant?> GetBySubdomainAsync(string subdomain, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Tenants.FirstOrDefault(t => t.Subdomain.Equals(subdomain, StringComparison.OrdinalIgnoreCase) && !t.IsDeleted));
    }

    public Task<(IReadOnlyList<Tenant> Items, int TotalCount)> GetPagedAsync(
        string? keyword = null,
        string? status = null,
        string? planCode = null,
        int page = 1,
        int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        IEnumerable<Tenant> query = Tenants.Where(t => !t.IsDeleted);

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.ToLower();
            query = query.Where(t => t.Name.ToLower().Contains(kw) || t.Subdomain.ToLower().Contains(kw));
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            var st = status.ToUpperInvariant();
            if (st == "ACTIVE") query = query.Where(t => t.Status == "ACTIVE" && t.LockType == null && (t.SubscriptionExpiresAt == null || t.SubscriptionExpiresAt > now));
            else if (st == "LOCKED") query = query.Where(t => t.Status == "LOCKED" || t.LockType != null);
            else if (st == "GRACE") query = query.Where(t => t.Status == "GRACE_PERIOD" || (t.SubscriptionExpiresAt != null && t.SubscriptionExpiresAt <= now && (t.GracePeriodExpiresAt == null || t.GracePeriodExpiresAt >= now)));
            else if (st == "EXPIRING") query = query.Where(t => t.SubscriptionExpiresAt != null && t.SubscriptionExpiresAt > now && t.SubscriptionExpiresAt <= now.AddDays(7));
        }

        var list = query.ToList();
        var paged = list.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return Task.FromResult(((IReadOnlyList<Tenant>)paged, list.Count));
    }

    public Task<int> GetTotalCountAsync(CancellationToken cancellationToken = default) => Task.FromResult(Tenants.Count(t => !t.IsDeleted));
    public Task<int> GetActiveCountAsync(CancellationToken cancellationToken = default) => Task.FromResult(Tenants.Count(t => !t.IsDeleted && t.Status == "ACTIVE" && t.LockType == null));
    public Task<int> GetExpiringSoonCountAsync(int daysThreshold = 7, CancellationToken cancellationToken = default) => Task.FromResult(0);
    public Task<int> GetGracePeriodCountAsync(CancellationToken cancellationToken = default) => Task.FromResult(Tenants.Count(t => !t.IsDeleted && t.Status == "GRACE_PERIOD"));
    public Task<int> GetLockedCountAsync(CancellationToken cancellationToken = default) => Task.FromResult(Tenants.Count(t => !t.IsDeleted && (t.Status == "LOCKED" || t.LockType != null)));
    public Task<int> GetTotalBranchesCountAsync(CancellationToken cancellationToken = default) => Task.FromResult(Tenants.Sum(t => t.Branches.Count));
    public Task<IReadOnlyList<Tenant>> GetUrgentAlertTenantsAsync(int limit = 10, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Tenant>>(Tenants.Take(limit).ToList());
    public Task<IReadOnlyList<Tenant>> GetAllActiveWithPlanAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Tenant>>(Tenants.ToList());

    public void Update(Tenant tenant)
    {
    }
}

public class TenantManagementTests
{
    private readonly FakeTenantRepository _repo;
    private readonly FakeUnitOfWork _uow;
    private readonly TenantService _service;

    public TenantManagementTests()
    {
        _repo = new FakeTenantRepository();
        _uow = new FakeUnitOfWork();
        _service = new TenantService(_repo, _uow);

        var planPro = new SubscriptionPlan
        {
            Id = "plan-pro",
            PlanCode = "PRO",
            PlanName = "Gói Chuyên Nghiệp",
            MaxBranches = 5,
            MaxStaff = 20
        };

        var tenantActive = new Tenant
        {
            Id = "tenant-01",
            Subdomain = "mart-hanoi",
            Name = "Siêu thị Hà Nội Mart",
            Code = "HN-MART",
            Status = "ACTIVE",
            CurrentPlan = "PRO",
            SubscriptionPlan = planPro,
            SubscriptionExpiresAt = DateTime.UtcNow.AddMonths(3)
        };
        tenantActive.TenantUsers.Add(new TenantUser
        {
            Role = "STORE_OWNER",
            User = new User { FullName = "Trần Văn Chủ", Phone = "0988111222", Email = "owner@hnmart.vn" }
        });

        var tenantLocked = new Tenant
        {
            Id = "tenant-02",
            Subdomain = "bachhoa-saigon",
            Name = "Bách Hóa Sài Gòn",
            Code = "SG-BH",
            Status = "LOCKED",
            LockType = "SOFT",
            LockReason = "Chậm thanh toán kỳ cước T10",
            CurrentPlan = "PRO",
            SubscriptionPlan = planPro
        };

        _repo.Tenants.AddRange(new[] { tenantActive, tenantLocked });
    }

    [Fact]
    public async Task REQ_ADMIN_BE02_GetTenants_FilterActive_ReturnsOnlyActive()
    {
        // Act
        var result = await _service.GetTenantsAsync(new TenantFilterRequest(Status: "ACTIVE"));

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.Items);
        Assert.Equal("mart-hanoi", result.Items[0].Subdomain);
        Assert.Equal("Trần Văn Chủ", result.Items[0].OwnerName);
    }

    [Fact]
    public async Task REQ_ADMIN_BE02_LockTenant_AppliesSoftLockWithReason()
    {
        // Act
        await _service.LockTenantAsync("tenant-01", new LockTenantRequest("SOFT", "Chậm thanh toán 15 ngày"), "admin_hung");

        // Assert
        var tenant = _repo.Tenants.First(t => t.Id == "tenant-01");
        Assert.Equal("LOCKED", tenant.Status);
        Assert.Equal("SOFT", tenant.LockType);
        Assert.Equal("Chậm thanh toán 15 ngày", tenant.LockReason);
        Assert.Equal("admin_hung", tenant.LockedBy);
        Assert.NotNull(tenant.LockedAt);
        Assert.Equal(1, _uow.SaveChangesCalls);
    }

    [Fact]
    public async Task REQ_ADMIN_BE02_LockTenant_InvalidLockType_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.LockTenantAsync("tenant-01", new LockTenantRequest("INVALID_TYPE", "Lý do"), "admin_hung"));
    }

    [Fact]
    public async Task REQ_ADMIN_BE02_UnlockTenant_ClearsLockFieldsAndRestoresStatus()
    {
        // Act
        await _service.UnlockTenantAsync("tenant-02");

        // Assert
        var tenant = _repo.Tenants.First(t => t.Id == "tenant-02");
        Assert.Null(tenant.LockType);
        Assert.Null(tenant.LockReason);
        Assert.Null(tenant.LockedAt);
        Assert.Null(tenant.LockedBy);
        Assert.Equal("ACTIVE", tenant.Status);
        Assert.Equal(1, _uow.SaveChangesCalls);
    }

    [Fact]
    public async Task REQ_ADMIN_BE02_ExtendGracePeriod_AddsDaysAndUpdatesStatus()
    {
        // Act
        await _service.ExtendGracePeriodAsync("tenant-01", new ExtendGraceRequest(AdditionalDays: 7, Reason: "Chờ kế toán đối soát"));

        // Assert
        var tenant = _repo.Tenants.First(t => t.Id == "tenant-01");
        Assert.NotNull(tenant.GracePeriodExpiresAt);
        Assert.True(tenant.GracePeriodExpiresAt > DateTime.UtcNow.AddDays(6));
        Assert.Equal("GRACE_PERIOD", tenant.Status);
        Assert.Equal(1, _uow.SaveChangesCalls);
    }

    [Fact]
    public void REQ_ADMIN_BE02_Validators_RejectInvalidRequests()
    {
        // Arrange
        var lockValidator = new LockTenantRequestValidator();
        var graceValidator = new ExtendGraceRequestValidator();

        // Act & Assert
        var badLock = lockValidator.Validate(new LockTenantRequest("UNKNOWN", ""));
        Assert.False(badLock.IsValid);

        var badGrace = graceValidator.Validate(new ExtendGraceRequest(0, ""));
        Assert.False(badGrace.IsValid);
    }
}

