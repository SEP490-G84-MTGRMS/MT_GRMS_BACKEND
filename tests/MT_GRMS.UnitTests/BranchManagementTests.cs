using System.Linq.Expressions;
using MT_GRMS.Application.Admin.Repositories;
using MT_GRMS.Application.Branches;
using MT_GRMS.Application.Branches.Dtos;
using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Domain.Exceptions;
using Xunit;

namespace MT_GRMS.UnitTests;

public class BranchManagementTests
{
    private readonly FakeBranchRepository _repo;
    private readonly FakeUnitOfWork _uow;
    private readonly FakeTenantContext _tenantContext;
    private readonly FakeTenantRepository _tenantRepo;
    private readonly FakeSubscriptionPlanRepository _planRepo;
    private readonly BranchService _service;

    public BranchManagementTests()
    {
        _repo = new FakeBranchRepository();
        _uow = new FakeUnitOfWork();
        _tenantContext = new FakeTenantContext("tenant-test-1");
        _tenantRepo = new FakeTenantRepository();
        _planRepo = new FakeSubscriptionPlanRepository();
        _service = new BranchService(_repo, _uow, _tenantContext, _tenantRepo, _planRepo);

        // Seed sample branches for tenant-test-1
        _repo.Branches.AddRange(new[]
        {
            new Branch { Id = Guid.Parse("11111111-1111-1111-1111-111111111111"), TenantId = "tenant-test-1", Code = "BR-01", Name = "Chi nhánh Cầu Giấy", Address = "123 Đường Cầu Giấy, Hà Nội", Phone = "024.3768.9999", ManagerName = "Nguyễn Văn An", Status = "Active", PendingOrdersCount = 3, LastOrderDate = new DateTime(2026, 10, 18) },
            new Branch { Id = Guid.Parse("22222222-2222-2222-2222-222222222222"), TenantId = "tenant-test-1", Code = "BR-02", Name = "Chi nhánh Đống Đa", Address = "45 Phố Thái Hà, Hà Nội", Phone = "024.3857.2222", ManagerName = "Trần Thị Mai", Status = "Active", PendingOrdersCount = 1, LastOrderDate = new DateTime(2026, 10, 19) },
            new Branch { Id = Guid.Parse("33333333-3333-3333-3333-333333333333"), TenantId = "tenant-test-1", Code = "BR-03", Name = "Chi nhánh Hà Đông", Address = "78 Đường Quang Trung, Hà Nội", Phone = "024.3355.8888", ManagerName = "Lê Hoàng Long", Status = "Active", PendingOrdersCount = 5, LastOrderDate = new DateTime(2026, 10, 17) },
            new Branch { Id = Guid.Parse("44444444-4444-4444-4444-444444444444"), TenantId = "tenant-test-1", Code = "BR-04", Name = "Chi nhánh Ba Đình", Address = "12 Phố Đội Cấn, Hà Nội", Phone = "024.3845.6666", ManagerName = "Phạm Quỳnh Nga", Status = "Inactive", PendingOrdersCount = 0, LastOrderDate = new DateTime(2026, 10, 15) },
            new Branch { Id = Guid.Parse("55555555-5555-5555-5555-555555555555"), TenantId = "tenant-test-1", Code = "DC-01", Name = "Kho tổng DC1 (Long Biên)", Address = "Lô CN-05 KCN Sài Đồng B, Hà Nội", Phone = "024.3678.1111", ManagerName = "Vũ Đức Thịnh", Status = "Active", PendingOrdersCount = 8, LastOrderDate = new DateTime(2026, 10, 20) },
            // Other tenant branch
            new Branch { Id = Guid.Parse("66666666-6666-6666-6666-666666666666"), TenantId = "tenant-other", Code = "BR-OTHER", Name = "Chi nhánh Khác Tenant", Address = "TP. Hồ Chí Minh", Status = "Active" },
        });
    }

    [Fact]
    public async Task REQ_BR01_GetPagedBranches_DefaultPagination_ReturnsCorrectPagedResult()
    {
        // Act
        var result = await _service.GetPagedBranchesAsync(new BranchFilterQuery { PageIndex = 1, PageSize = 10 });

        // Assert
        Assert.NotNull(result);
        Assert.Equal(5, result.TotalItems);
        Assert.Equal(5, result.Items.Count);
        Assert.Equal(1, result.Page);
        Assert.Equal(10, result.PageSize);
    }

    [Theory]
    [InlineData("cầu giấy", "BR-01")]
    [InlineData("Thái Hà", "BR-02")]
    [InlineData("024.3355.8888", "BR-03")]
    [InlineData("Phạm Quỳnh Nga", "BR-04")]
    public async Task REQ_BR01_GetPagedBranches_FilterByKeyword_ReturnsMatchingBranches(string keyword, string expectedCode)
    {
        // Act
        var result = await _service.GetPagedBranchesAsync(new BranchFilterQuery { Keyword = keyword });

        // Assert
        Assert.Single(result.Items);
        Assert.Equal(expectedCode, result.Items.First().Code);
    }

    [Fact]
    public async Task REQ_BR01_GetPagedBranches_FilterByBranchCode_ReturnsExactBranch()
    {
        // Act
        var result = await _service.GetPagedBranchesAsync(new BranchFilterQuery { BranchCode = "DC-01" });

        // Assert
        Assert.Single(result.Items);
        Assert.Equal("DC-01", result.Items.First().Code);
        Assert.Equal("Kho tổng DC1 (Long Biên)", result.Items.First().Name);
    }

    [Fact]
    public async Task REQ_BR01_GetPagedBranches_FilterByStatus_ReturnsOnlyMatchingStatus()
    {
        // Act
        var activeBranches = await _service.GetPagedBranchesAsync(new BranchFilterQuery { Status = "Active" });
        var inactiveBranches = await _service.GetPagedBranchesAsync(new BranchFilterQuery { Status = "Inactive" });

        // Assert
        Assert.Equal(4, activeBranches.TotalItems);
        Assert.All(activeBranches.Items, b => Assert.Equal("Active", b.Status));

        Assert.Equal(1, inactiveBranches.TotalItems);
        Assert.Equal("BR-04", inactiveBranches.Items.First().Code);
    }

    [Fact]
    public async Task REQ_BR01_GetPagedBranches_InvalidPageParams_ClampsSafeValues()
    {
        // Act: negative page and negative size
        var resultNegative = await _service.GetPagedBranchesAsync(new BranchFilterQuery { PageIndex = -5, PageSize = 0 });

        // Assert
        Assert.Equal(1, resultNegative.Page);
        Assert.Equal(1, resultNegative.PageSize);

        // Act: overly large pageSize clamped to 100
        var resultLarge = await _service.GetPagedBranchesAsync(new BranchFilterQuery { PageIndex = 1, PageSize = 500 });
        Assert.Equal(100, resultLarge.PageSize);
    }

    [Fact]
    public async Task REQ_BR01_GetBranchQuota_ReturnsCorrectQuota()
    {
        // Act
        var quota = await _service.GetBranchQuotaAsync();

        // Assert
        Assert.NotNull(quota);
        Assert.Equal(4, quota.UsedBranches);
        Assert.Equal(5, quota.MaxBranches);
        Assert.True(quota.CanAddMore);
        Assert.Equal("PRO", quota.CurrentPlanCode);
    }

    [Fact]
    public async Task REQ_BR02_GetBranchById_ExistingValidId_ReturnsBranchDto()
    {
        // Arrange
        var id = Guid.Parse("11111111-1111-1111-1111-111111111111");

        // Act
        var result = await _service.GetBranchByIdAsync(id);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("BR-01", result.Code);
        Assert.Equal("Chi nhánh Cầu Giấy", result.Name);
        Assert.Equal(3, result.PendingOrdersCount);
    }

    [Fact]
    public async Task REQ_BR02_GetBranchById_NonExistentId_ThrowsNotFoundException()
    {
        // Arrange
        var nonExistentId = Guid.Parse("99999999-9999-9999-9999-999999999999");

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(() => _service.GetBranchByIdAsync(nonExistentId));
    }

    [Fact]
    public async Task REQ_BR02_CreateBranch_ValidRequest_Success()
    {
        // Arrange
        var req = new CreateBranchRequest("BR-NEW", "Chi nhánh Mới", "789 Phố Mới", "0912345678", "Trần Văn Mới");

        // Act
        var result = await _service.CreateBranchAsync(req);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("BR-NEW", result.Code);
        Assert.Equal("Chi nhánh Mới", result.Name);
        Assert.Equal("Active", result.Status);
    }

    [Fact]
    public async Task REQ_BR02_CreateBranch_DuplicateCode_ThrowsBusinessException()
    {
        // Arrange
        var req = new CreateBranchRequest("BR-01", "Chi nhánh Trùng", "Địa chỉ");

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BusinessException>(() => _service.CreateBranchAsync(req));
        Assert.Equal("DUPLICATE_CODE", ex.ErrorCode);
    }

    [Fact]
    public async Task REQ_BR02_CreateBranch_QuotaExceeded_ThrowsBusinessException()
    {
        // Arrange: limit quota to 4 (currently 4 active)
        _planRepo.Plan!.MaxBranches = 4;
        var req = new CreateBranchRequest("BR-OVER", "Chi nhánh Vượt Quota", "Địa chỉ");

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BusinessException>(() => _service.CreateBranchAsync(req));
        Assert.Equal("QUOTA_EXCEEDED", ex.ErrorCode);
    }

    [Fact]
    public async Task REQ_BR03_UpdateBranch_ValidRequest_Success()
    {
        // Arrange
        var id = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var req = new UpdateBranchRequest("Chi nhánh Cầu Giấy Updated", "125 Đường Cầu Giấy", "0999888777", "Nguyễn Văn Mới");

        // Act
        var result = await _service.UpdateBranchAsync(id, req);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Chi nhánh Cầu Giấy Updated", result.Name);
        Assert.Equal("125 Đường Cầu Giấy", result.Address);
    }

    [Fact]
    public async Task REQ_BR04_ToggleBranchStatus_Success()
    {
        // Arrange
        var id = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var req = new UpdateBranchStatusRequest(false);

        // Act
        var result = await _service.ToggleBranchStatusAsync(id, req);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Inactive", result.Status);
    }

    // Helper classes
    private class FakeBranchRepository : IBranchRepository
    {
        public List<Branch> Branches { get; } = new();

        public Task<bool> ExistsByCodeAsync(string code, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Branches.Any(b => b.Code == code && !b.IsDeleted));
        }

        public Task<int> CountActiveAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Branches.Count(b => b.TenantId == "tenant-test-1" && b.IsActive && !b.IsDeleted));
        }

        public Task<(IReadOnlyList<Branch> Items, int TotalCount)> GetPagedListAsync(
            string? keyword,
            string? branchCode,
            string? status,
            int pageIndex,
            int pageSize,
            CancellationToken cancellationToken = default)
        {
            // Simulate tenant filter (only tenant-test-1)
            var query = Branches.Where(b => b.TenantId == "tenant-test-1" && !b.IsDeleted);

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                var kw = keyword.Trim().ToLower();
                query = query.Where(b =>
                    b.Code.ToLower().Contains(kw) ||
                    b.Name.ToLower().Contains(kw) ||
                    b.Address.ToLower().Contains(kw) ||
                    (b.ManagerName != null && b.ManagerName.ToLower().Contains(kw)) ||
                    (b.Phone != null && b.Phone.Contains(kw)));
            }

            if (!string.IsNullOrWhiteSpace(branchCode) && !string.Equals(branchCode, "all", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(b => b.Code == branchCode);
            }

            if (!string.IsNullOrWhiteSpace(status) && !string.Equals(status, "all", StringComparison.OrdinalIgnoreCase))
            {
                var norm = status.Trim().ToLower();
                query = query.Where(b => b.Status.ToLower() == norm);
            }

            var list = query.OrderBy(b => b.Code).ToList();
            var totalCount = list.Count;

            var safeIndex = Math.Max(1, pageIndex);
            var safeSize = Math.Clamp(pageSize, 1, 100);

            var paged = list.Skip((safeIndex - 1) * safeSize).Take(safeSize).ToList();
            return Task.FromResult(((IReadOnlyList<Branch>)paged, totalCount));
        }

        public Task<Branch?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Branches.FirstOrDefault(b => b.Id == id && !b.IsDeleted));
        }

        public Task<IReadOnlyList<Branch>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult((IReadOnlyList<Branch>)Branches.Where(b => !b.IsDeleted).ToList());
        }

        public Task<IReadOnlyList<Branch>> FindAsync(Expression<Func<Branch, bool>> predicate, CancellationToken cancellationToken = default)
        {
            return Task.FromResult((IReadOnlyList<Branch>)Branches.Where(predicate.Compile()).ToList());
        }

        public Task AddAsync(Branch entity, CancellationToken cancellationToken = default)
        {
            Branches.Add(entity);
            return Task.CompletedTask;
        }

        public void Update(Branch entity) { }
        public void Delete(Branch entity) => entity.IsDeleted = true;
    }

    private class FakeUnitOfWork : IUnitOfWork
    {
        public bool HasSaved { get; private set; }
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            HasSaved = true;
            return Task.FromResult(1);
        }
    }

    private class FakeTenantContext : ITenantContext
    {
        public string CurrentTenantId { get; set; } = "tenant-test-1";
        public Guid CurrentUserId { get; set; } = Guid.Parse("11111111-1111-1111-1111-111111111111");
        public Guid? CurrentBranchId { get; set; } = null;
        public string CurrentRole { get; set; } = "STORE_OWNER";

        public void SetContext(string tenantId, Guid userId, Guid? branchId)
        {
            CurrentTenantId = tenantId;
            CurrentUserId = userId;
            CurrentBranchId = branchId;
        }

        public void SetRole(string? role) => CurrentRole = role?.Trim() ?? string.Empty;

        public FakeTenantContext(string tenantId) => CurrentTenantId = tenantId;
    }

    private class FakeTenantRepository : ITenantRepository
    {
        public Tenant? Tenant { get; set; } = new Tenant
        {
            Id = "tenant-test-1",
            Name = "Chuỗi Tạp Hóa Test",
            CurrentPlan = "PRO",
            Status = "ACTIVE"
        };

        public Task<Tenant?> GetByIdAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult(Tenant);
        public Task<Tenant?> GetByIdWithDetailsAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult(Tenant);
        public Task<Tenant?> GetByCodeAsync(string code, CancellationToken cancellationToken = default) => Task.FromResult(Tenant);
        public Task<Tenant?> GetBySubdomainAsync(string subdomain, CancellationToken cancellationToken = default) => Task.FromResult(Tenant);
        public Task<(IReadOnlyList<Tenant> Items, int TotalCount)> GetPagedAsync(string? keyword = null, string? status = null, string? planCode = null, int page = 1, int pageSize = 10, CancellationToken cancellationToken = default)
            => Task.FromResult(((IReadOnlyList<Tenant>)(Tenant != null ? new List<Tenant> { Tenant } : new List<Tenant>()), Tenant != null ? 1 : 0));
        public Task<int> GetTotalCountAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);
        public Task<int> GetActiveCountAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);
        public Task<int> GetExpiringSoonCountAsync(int daysThreshold = 7, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<int> GetGracePeriodCountAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<int> GetLockedCountAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<int> GetTotalBranchesCountAsync(CancellationToken cancellationToken = default) => Task.FromResult(4);
        public Task<IReadOnlyList<Tenant>> GetUrgentAlertTenantsAsync(int limit = 10, CancellationToken cancellationToken = default) => Task.FromResult((IReadOnlyList<Tenant>)new List<Tenant>());
        public Task<IReadOnlyList<Tenant>> GetAllActiveWithPlanAsync(CancellationToken cancellationToken = default) => Task.FromResult((IReadOnlyList<Tenant>)new List<Tenant>());
        public void Update(Tenant tenant) { }
    }

    private class FakeSubscriptionPlanRepository : ISubscriptionPlanRepository
    {
        public SubscriptionPlan? Plan { get; set; } = new SubscriptionPlan
        {
            Id = "plan_pro",
            PlanCode = "PRO",
            PlanName = "Gói Doanh Nghiệp (Toàn Năng)",
            MaxBranches = 5,
            MaxStaff = 50,
            PriceMonthly = 999000m
        };

        public Task<SubscriptionPlan?> GetByIdAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult(Plan);
        public Task<SubscriptionPlan?> GetByCodeAsync(string code, CancellationToken cancellationToken = default) => Task.FromResult(Plan);
        public Task<IReadOnlyList<SubscriptionPlan>> GetAllAsync(string? keyword = null, string? status = null, CancellationToken cancellationToken = default)
            => Task.FromResult((IReadOnlyList<SubscriptionPlan>)(Plan != null ? new List<SubscriptionPlan> { Plan } : new List<SubscriptionPlan>()));
        public Task<int> CountActiveTenantsByPlanIdAsync(string planId, CancellationToken cancellationToken = default) => Task.FromResult(1);
        public Task<Dictionary<string, int>> GetActiveTenantCountsAsync(CancellationToken cancellationToken = default) => Task.FromResult(new Dictionary<string, int>());
        public Task AddAsync(SubscriptionPlan plan, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Update(SubscriptionPlan plan) { }
        public void Delete(SubscriptionPlan plan) { }
    }
}
