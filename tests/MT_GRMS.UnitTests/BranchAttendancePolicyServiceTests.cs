using MT_GRMS.Application.Branches;
using MT_GRMS.Application.Branches.Dtos;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.HR.AttendancePolicies;
using MT_GRMS.Application.HR.AttendancePolicies.Dtos;
using MT_GRMS.Domain.Entities.HR;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Domain.Exceptions;
using Xunit;

namespace MT_GRMS.UnitTests;

public class BranchAttendancePolicyServiceTests
{
    private class FakeUnitOfWork : IUnitOfWork
    {
        public bool SaveChangesCalled { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveChangesCalled = true;
            return Task.FromResult(1);
        }
    }

    private class FakeTenantContext : ITenantContext
    {
        public string CurrentTenantId { get; set; } = "tenant_test_01";
        public Guid CurrentUserId { get; set; } = Guid.NewGuid();
        public string CurrentRole { get; set; } = "STORE_OWNER";
        public Guid? CurrentBranchId { get; set; } = Guid.NewGuid();

        public void SetContext(string tenantId, Guid userId, Guid? branchId)
        {
            CurrentTenantId = tenantId;
            CurrentUserId = userId;
            CurrentBranchId = branchId;
        }

        public void SetRole(string? role)
        {
            CurrentRole = role ?? string.Empty;
        }
    }

    private class FakeBranchRepository : IBranchRepository
    {
        public List<Branch> Branches { get; } = new();

        public Task<Branch?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Branches.FirstOrDefault(b => b.Id == id && !b.IsDeleted));
        }

        public Task<IReadOnlyList<Branch>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult((IReadOnlyList<Branch>)Branches.Where(b => !b.IsDeleted).ToList());
        }

        public Task<IReadOnlyList<Branch>> FindAsync(System.Linq.Expressions.Expression<Func<Branch, bool>> predicate, CancellationToken cancellationToken = default)
        {
            var func = predicate.Compile();
            var res = Branches.Where(func).Where(b => !b.IsDeleted).ToList();
            return Task.FromResult((IReadOnlyList<Branch>)res);
        }

        public Task<(IReadOnlyList<Branch> Items, int TotalCount)> GetPagedListAsync(
            string? keyword,
            string? branchCode,
            string? status,
            IReadOnlyList<string>? branchCodes,
            IReadOnlyList<string>? branchNames,
            IReadOnlyList<string>? addresses,
            IReadOnlyList<string>? phones,
            int pageIndex,
            int pageSize,
            CancellationToken cancellationToken = default)
        {
            var items = Branches.Where(b => !b.IsDeleted).ToList();
            return Task.FromResult(((IReadOnlyList<Branch>)items, items.Count));
        }

        public Task<bool> ExistsByCodeAsync(string code, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Branches.Any(b => b.BranchCode == code && !b.IsDeleted));
        }

        public Task AddAsync(Branch entity, CancellationToken cancellationToken = default)
        {
            Branches.Add(entity);
            return Task.CompletedTask;
        }

        public void Update(Branch entity)
        {
            var idx = Branches.FindIndex(b => b.Id == entity.Id);
            if (idx >= 0) Branches[idx] = entity;
        }

        public void Delete(Branch entity)
        {
            entity.IsDeleted = true;
        }

        public Task<int> CountActiveAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Branches.Count(b => !b.IsDeleted));
        }

        public Task<int> CountTotalAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Branches.Count(b => !b.IsDeleted));
        }

        public Task<BranchDetailDto?> GetBranchDetailAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<BranchDetailDto?>(null);
    }

    private class FakeBranchAttendancePolicyRepository : IBranchAttendancePolicyRepository
    {
        public List<BranchAttendancePolicy> Policies { get; } = new();

        public Task<BranchAttendancePolicy?> GetByBranchIdAsync(Guid branchId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Policies.FirstOrDefault(p => p.BranchId == branchId && !p.IsDeleted));
        }

        public Task<IReadOnlyList<BranchAttendancePolicy>> GetByBranchIdsAsync(IEnumerable<Guid> branchIds, CancellationToken cancellationToken = default)
        {
            var idList = branchIds.ToList();
            var res = Policies.Where(p => idList.Contains(p.BranchId) && !p.IsDeleted).ToList();
            return Task.FromResult((IReadOnlyList<BranchAttendancePolicy>)res);
        }

        public Task AddAsync(BranchAttendancePolicy policy, CancellationToken cancellationToken = default)
        {
            Policies.Add(policy);
            return Task.CompletedTask;
        }

        public void Update(BranchAttendancePolicy policy)
        {
            var idx = Policies.FindIndex(p => p.Id == policy.Id);
            if (idx >= 0) Policies[idx] = policy;
        }
    }

    private readonly FakeBranchAttendancePolicyRepository _policyRepo;
    private readonly FakeBranchRepository _branchRepo;
    private readonly FakeUnitOfWork _uow;
    private readonly FakeTenantContext _tenantContext;
    private readonly BranchAttendancePolicyService _service;

    public BranchAttendancePolicyServiceTests()
    {
        _policyRepo = new FakeBranchAttendancePolicyRepository();
        _branchRepo = new FakeBranchRepository();
        _uow = new FakeUnitOfWork();
        _tenantContext = new FakeTenantContext();

        _service = new BranchAttendancePolicyService(
            _policyRepo,
            _branchRepo,
            _uow,
            _tenantContext
        );
    }

    [Fact]
    public async Task GetByBranchIdAsync_WhenPolicyNotExists_ReturnsDefaultFallbackPolicy()
    {
        // Arrange
        var branch = new Branch
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.CurrentTenantId,
            BranchCode = "CN01",
            Name = "Chi nhánh Trung tâm"
        };
        _branchRepo.Branches.Add(branch);

        // Act
        var result = await _service.GetByBranchIdAsync(branch.Id);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(branch.Id, result.BranchId);
        Assert.Equal(8.00m, result.StandardDayHours);
        Assert.True(result.IsConvertHoursToDays);
        Assert.False(result.IsCustomized);
        Assert.Equal(0, result.LateThresholdMinutes);
    }

    [Fact]
    public async Task GetByBranchIdAsync_WhenPolicyExists_ReturnsCustomizedPolicy()
    {
        // Arrange
        var branch = new Branch
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.CurrentTenantId,
            BranchCode = "CN01",
            Name = "Chi nhánh Trung tâm"
        };
        _branchRepo.Branches.Add(branch);

        var policy = new BranchAttendancePolicy
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.CurrentTenantId,
            BranchId = branch.Id,
            StandardDayHours = 7.50m,
            IsConvertHoursToDays = true,
            LateThresholdMinutes = 15,
            EarlyLeaveThresholdMinutes = 10
        };
        _policyRepo.Policies.Add(policy);

        // Act
        var result = await _service.GetByBranchIdAsync(branch.Id);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(7.50m, result.StandardDayHours);
        Assert.True(result.IsCustomized);
        Assert.Equal(15, result.LateThresholdMinutes);
        Assert.Equal(10, result.EarlyLeaveThresholdMinutes);
    }

    [Fact]
    public async Task GetByBranchIdAsync_WhenBranchNotFound_ThrowsNotFoundException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.GetByBranchIdAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task UpsertPolicyAsync_WhenPolicyNotExists_CreatesAndSaves()
    {
        // Arrange
        var branch = new Branch
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.CurrentTenantId,
            BranchCode = "CN02",
            Name = "Chi nhánh HCM"
        };
        _branchRepo.Branches.Add(branch);

        var request = new UpsertAttendancePolicyRequest(
            StandardDayHours: 8.00m,
            IsConvertHoursToDays: true,
            ConversionRulesJson: null,
            EnableLateThreshold: true,
            LateThresholdMinutes: 10,
            EnableEarlyLeaveThreshold: true,
            EarlyLeaveThresholdMinutes: 5
        );

        // Act
        var result = await _service.UpsertPolicyAsync(branch.Id, request);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.IsCustomized);
        Assert.Equal(10, result.LateThresholdMinutes);
        Assert.Single(_policyRepo.Policies);
        Assert.True(_uow.SaveChangesCalled);
    }

    [Fact]
    public async Task UpsertPolicyAsync_WhenPolicyExists_UpdatesExistingPolicy()
    {
        // Arrange
        var branch = new Branch
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.CurrentTenantId,
            BranchCode = "CN03",
            Name = "Chi nhánh Đà Nẵng"
        };
        _branchRepo.Branches.Add(branch);

        var policy = new BranchAttendancePolicy
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.CurrentTenantId,
            BranchId = branch.Id,
            StandardDayHours = 8.00m,
            LateThresholdMinutes = 0
        };
        _policyRepo.Policies.Add(policy);

        var request = new UpsertAttendancePolicyRequest(
            StandardDayHours: 8.50m,
            IsConvertHoursToDays: true,
            LateThresholdMinutes: 20
        );

        // Act
        var result = await _service.UpsertPolicyAsync(branch.Id, request);

        // Assert
        Assert.Equal(8.50m, result.StandardDayHours);
        Assert.Equal(20, result.LateThresholdMinutes);
        Assert.Single(_policyRepo.Policies);
        Assert.True(_uow.SaveChangesCalled);
    }

    [Fact]
    public async Task CopyPolicyAsync_WhenValid_CopiesPolicyToAllTargetBranches()
    {
        // Arrange
        var sourceBranch = new Branch { Id = Guid.NewGuid(), TenantId = _tenantContext.CurrentTenantId, Name = "Chi nhánh Nguồn" };
        var target1 = new Branch { Id = Guid.NewGuid(), TenantId = _tenantContext.CurrentTenantId, Name = "Chi nhánh Đích 1" };
        var target2 = new Branch { Id = Guid.NewGuid(), TenantId = _tenantContext.CurrentTenantId, Name = "Chi nhánh Đích 2" };

        _branchRepo.Branches.AddRange(new[] { sourceBranch, target1, target2 });

        var sourcePolicy = new BranchAttendancePolicy
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.CurrentTenantId,
            BranchId = sourceBranch.Id,
            StandardDayHours = 8.00m,
            LateThresholdMinutes = 15
        };
        _policyRepo.Policies.Add(sourcePolicy);

        var request = new CopyAttendancePolicyRequest(
            SourceBranchId: sourceBranch.Id,
            TargetBranchIds: new[] { target1.Id, target2.Id }
        );

        // Act
        var result = await _service.CopyPolicyAsync(request);

        // Assert
        Assert.Equal(2, result.CopiedCount);
        Assert.True(_uow.SaveChangesCalled);

        var target1Policy = _policyRepo.Policies.FirstOrDefault(p => p.BranchId == target1.Id);
        Assert.NotNull(target1Policy);
        Assert.Equal(15, target1Policy.LateThresholdMinutes);

        var target2Policy = _policyRepo.Policies.FirstOrDefault(p => p.BranchId == target2.Id);
        Assert.NotNull(target2Policy);
        Assert.Equal(15, target2Policy.LateThresholdMinutes);
    }

    [Fact]
    public void UpsertValidator_WhenConvertHoursEnabledAndRulesEmpty_ShouldFail()
    {
        var validator = new MT_GRMS.Application.HR.AttendancePolicies.Validators.UpsertAttendancePolicyRequestValidator();
        var request = new UpsertAttendancePolicyRequest(
            StandardDayHours: 8m,
            IsConvertHoursToDays: true,
            ConversionRulesJson: "[]",
            EnableLateThreshold: false,
            LateThresholdMinutes: 0,
            EnableEarlyLeaveThreshold: false,
            EarlyLeaveThresholdMinutes: 0);

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void UpsertValidator_WhenRuleTimeExceeds24Hours_ShouldFail()
    {
        var validator = new MT_GRMS.Application.HR.AttendancePolicies.Validators.UpsertAttendancePolicyRequestValidator();
        var rulesJson = """[{"type":"range","fromHours":8,"fromMinutes":0,"toHours":24,"toMinutes":30,"workUnits":1}]""";
        var request = new UpsertAttendancePolicyRequest(
            StandardDayHours: 8m,
            IsConvertHoursToDays: true,
            ConversionRulesJson: rulesJson,
            EnableLateThreshold: false,
            LateThresholdMinutes: 0,
            EnableEarlyLeaveThreshold: false,
            EarlyLeaveThresholdMinutes: 0);

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void UpsertValidator_WhenRulesOverlapOrMultipleGte_ShouldFail()
    {
        var validator = new MT_GRMS.Application.HR.AttendancePolicies.Validators.UpsertAttendancePolicyRequestValidator();
        var overlappingJson = """
        [
          {"type":"range","fromHours":4,"fromMinutes":0,"toHours":8,"toMinutes":0,"workUnits":1},
          {"type":"range","fromHours":6,"fromMinutes":0,"toHours":10,"toMinutes":0,"workUnits":1.5}
        ]
        """;
        var request1 = new UpsertAttendancePolicyRequest(8m, true, overlappingJson, false, 0, false, 0);
        Assert.False(validator.Validate(request1).IsValid);

        var multiGteJson = """
        [
          {"type":"gte","fromHours":8,"fromMinutes":0,"workUnits":1},
          {"type":"gte","fromHours":10,"fromMinutes":0,"workUnits":1.5}
        ]
        """;
        var request2 = new UpsertAttendancePolicyRequest(8m, true, multiGteJson, false, 0, false, 0);
        Assert.False(validator.Validate(request2).IsValid);
    }

    [Fact]
    public void UpsertValidator_WhenContiguousRangesAndSingleGte_ShouldPass()
    {
        var validator = new MT_GRMS.Application.HR.AttendancePolicies.Validators.UpsertAttendancePolicyRequestValidator();
        var validJson = """
        [
          {"type":"range","fromHours":0,"fromMinutes":0,"toHours":4,"toMinutes":0,"workUnits":0.5},
          {"type":"range","fromHours":4,"fromMinutes":0,"toHours":8,"toMinutes":0,"workUnits":1.0},
          {"type":"gte","fromHours":8,"fromMinutes":0,"workUnits":1.5}
        ]
        """;
        var request = new UpsertAttendancePolicyRequest(8m, true, validJson, true, 15, true, 10);

        var result = validator.Validate(request);

        Assert.True(result.IsValid);
    }
}
