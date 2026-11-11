using System.Linq.Expressions;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.HR.ShiftConfigs;
using MT_GRMS.Application.HR.ShiftConfigs.Dtos;
using MT_GRMS.Domain.Entities.HR;
using MT_GRMS.Domain.Exceptions;
using Xunit;

namespace MT_GRMS.UnitTests;

public class ShiftConfigServiceTests
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

    private class FakeShiftConfigRepository : IShiftConfigRepository
    {
        public List<ShiftConfig> ShiftConfigs { get; } = new();

        public Task<ShiftConfig?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ShiftConfigs.FirstOrDefault(s => s.Id == id && !s.IsDeleted));
        }

        public Task<IReadOnlyList<ShiftConfig>> GetByBranchIdAsync(Guid? branchId, bool? isActive = null, CancellationToken cancellationToken = default)
        {
            var query = ShiftConfigs.Where(s => !s.IsDeleted);
            if (branchId.HasValue && branchId.Value != Guid.Empty)
            {
                query = query.Where(s => s.BranchId == branchId.Value);
            }
            if (isActive.HasValue)
            {
                query = query.Where(s => s.IsActive == isActive.Value);
            }
            return Task.FromResult<IReadOnlyList<ShiftConfig>>(query.OrderBy(s => s.StartTime).ToList());
        }

        public Task<(IReadOnlyList<ShiftConfig> Items, int TotalCount)> GetPagedListAsync(ShiftConfigQueryFilter filter, CancellationToken cancellationToken = default)
        {
            var query = ShiftConfigs.Where(s => !s.IsDeleted);
            if (!string.IsNullOrWhiteSpace(filter.Keyword))
            {
                var kw = filter.Keyword.Trim().ToLower();
                query = query.Where(s => s.ShiftCode.ToLower().Contains(kw) || s.ShiftName.ToLower().Contains(kw));
            }
            if (filter.BranchIds != null && filter.BranchIds.Count > 0)
            {
                var bStrs = filter.BranchIds.Select(b => b.ToString()).ToList();
                query = query.Where(s => filter.BranchIds.Contains(s.BranchId) || (s.BranchIds != null && bStrs.Any(bStr => s.BranchIds.Contains(bStr))));
            }
            else if (filter.BranchId.HasValue && filter.BranchId.Value != Guid.Empty)
            {
                var bStr = filter.BranchId.Value.ToString();
                query = query.Where(s => s.BranchId == filter.BranchId.Value || (s.BranchIds != null && s.BranchIds.Contains(bStr)));
            }
            if (filter.IsActive.HasValue)
            {
                query = query.Where(s => s.IsActive == filter.IsActive.Value);
            }

            var totalCount = query.Count();
            var page = filter.Page > 0 ? filter.Page : 1;
            var pageSize = filter.PageSize > 0 ? filter.PageSize : 20;
            var items = query.OrderBy(s => s.StartTime).ThenBy(s => s.ShiftCode).Skip((page - 1) * pageSize).Take(pageSize).ToList();
            return Task.FromResult<(IReadOnlyList<ShiftConfig> Items, int TotalCount)>((items, totalCount));
        }

        public Task<ShiftConfig?> GetByCodeAsync(Guid branchId, string shiftCode, CancellationToken cancellationToken = default)
        {
            var normalized = shiftCode.Trim().ToUpper();
            return Task.FromResult(ShiftConfigs.FirstOrDefault(s => s.BranchId == branchId && !s.IsDeleted && s.ShiftCode.ToUpper() == normalized));
        }

        public Task<bool> ExistsAsync(Expression<Func<ShiftConfig, bool>> predicate, CancellationToken cancellationToken = default)
        {
            var compiled = predicate.Compile();
            return Task.FromResult(ShiftConfigs.Where(s => !s.IsDeleted).Any(compiled));
        }

        public Task AddAsync(ShiftConfig shiftConfig, CancellationToken cancellationToken = default)
        {
            ShiftConfigs.Add(shiftConfig);
            return Task.CompletedTask;
        }

        public void Update(ShiftConfig shiftConfig)
        {
            var index = ShiftConfigs.FindIndex(s => s.Id == shiftConfig.Id);
            if (index >= 0)
            {
                ShiftConfigs[index] = shiftConfig;
            }
        }

        public void Delete(ShiftConfig shiftConfig)
        {
            shiftConfig.IsDeleted = true;
            Update(shiftConfig);
        }
    }

    [Fact]
    public async Task CreateShiftConfig_StandardDayShift_CalculatesDurationAndCheckInTimes()
    {
        // Arrange
        var repo = new FakeShiftConfigRepository();
        var uow = new FakeUnitOfWork();
        var tenantContext = new FakeTenantContext();
        var service = new ShiftConfigService(repo, uow, tenantContext);

        var branchId = Guid.NewGuid();
        var request = new CreateShiftConfigRequest(
            BranchId: branchId,
            ShiftCode: "SANG",
            ShiftName: "Ca sáng chuẩn",
            StartTime: new TimeSpan(6, 0, 0),
            EndTime: new TimeSpan(14, 0, 0)
        );

        // Act
        var result = await service.CreateShiftConfigAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("SANG", result.ShiftCode);
        Assert.Equal("Ca sáng chuẩn", result.ShiftName);
        Assert.Equal(8.00m, result.DurationHours);
        Assert.Equal(new TimeSpan(5, 30, 0), result.AllowedCheckInStart);
        Assert.Equal(new TimeSpan(7, 0, 0), result.AllowedCheckInEnd);
        Assert.True(result.IsActive);
        Assert.True(uow.SaveChangesCalled);

        var saved = repo.ShiftConfigs.First(s => s.Id == result.Id);
        Assert.Equal("tenant_test_01", saved.TenantId);
        Assert.Equal(branchId, saved.BranchId);
    }

    [Fact]
    public async Task CreateShiftConfig_OvernightShift_CalculatesCorrectDurationAcrossMidnight()
    {
        // Arrange
        var repo = new FakeShiftConfigRepository();
        var uow = new FakeUnitOfWork();
        var tenantContext = new FakeTenantContext();
        var service = new ShiftConfigService(repo, uow, tenantContext);

        var branchId = Guid.NewGuid();
        var request = new CreateShiftConfigRequest(
            BranchId: branchId,
            ShiftCode: "DEM",
            ShiftName: "Ca đêm vắt qua ngày hôm sau",
            StartTime: new TimeSpan(22, 0, 0),
            EndTime: new TimeSpan(6, 0, 0)
        );

        // Act
        var result = await service.CreateShiftConfigAsync(request);

        // Assert
        Assert.Equal(8.00m, result.DurationHours);
        Assert.Equal(new TimeSpan(21, 30, 0), result.AllowedCheckInStart);
        Assert.Equal(new TimeSpan(23, 0, 0), result.AllowedCheckInEnd);
    }

    [Fact]
    public async Task CreateShiftConfig_DuplicateShiftCodeInSameBranch_ThrowsBusinessException()
    {
        // Arrange
        var repo = new FakeShiftConfigRepository();
        var uow = new FakeUnitOfWork();
        var tenantContext = new FakeTenantContext();
        var service = new ShiftConfigService(repo, uow, tenantContext);

        var branchId = Guid.NewGuid();
        repo.ShiftConfigs.Add(new ShiftConfig
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant_test_01",
            BranchId = branchId,
            ShiftCode = "SANG",
            ShiftName = "Ca sáng hiện tại",
            StartTime = new TimeSpan(6, 0, 0),
            EndTime = new TimeSpan(14, 0, 0),
            IsActive = true,
            IsDeleted = false
        });

        var request = new CreateShiftConfigRequest(
            BranchId: branchId,
            ShiftCode: "sang", // case-insensitive check
            ShiftName: "Ca sáng trùng lặp",
            StartTime: new TimeSpan(6, 0, 0),
            EndTime: new TimeSpan(14, 0, 0)
        );

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.CreateShiftConfigAsync(request));
        Assert.Equal("DUPLICATE_SHIFT_CODE", ex.ErrorCode);
        Assert.Contains("SANG", ex.Message);
    }

    [Fact]
    public async Task GetShiftConfigsByBranch_FiltersByIsActive_ReturnsOrderedList()
    {
        // Arrange
        var repo = new FakeShiftConfigRepository();
        var uow = new FakeUnitOfWork();
        var tenantContext = new FakeTenantContext();
        var service = new ShiftConfigService(repo, uow, tenantContext);

        var branchId = Guid.NewGuid();
        var otherBranchId = Guid.NewGuid();

        repo.ShiftConfigs.AddRange(new[]
        {
            new ShiftConfig
            {
                Id = Guid.NewGuid(),
                TenantId = "tenant_test_01",
                BranchId = branchId,
                ShiftCode = "TOI",
                ShiftName = "Ca tối (ngừng hoạt động)",
                StartTime = new TimeSpan(18, 0, 0),
                EndTime = new TimeSpan(22, 0, 0),
                IsActive = false
            },
            new ShiftConfig
            {
                Id = Guid.NewGuid(),
                TenantId = "tenant_test_01",
                BranchId = branchId,
                ShiftCode = "SANG",
                ShiftName = "Ca sáng (đang chạy)",
                StartTime = new TimeSpan(6, 0, 0),
                EndTime = new TimeSpan(14, 0, 0),
                IsActive = true
            },
            new ShiftConfig
            {
                Id = Guid.NewGuid(),
                TenantId = "tenant_test_01",
                BranchId = branchId,
                ShiftCode = "CHIEU",
                ShiftName = "Ca chiều (đang chạy)",
                StartTime = new TimeSpan(14, 0, 0),
                EndTime = new TimeSpan(22, 0, 0),
                IsActive = true
            },
            new ShiftConfig
            {
                Id = Guid.NewGuid(),
                TenantId = "tenant_test_01",
                BranchId = otherBranchId,
                ShiftCode = "SANG",
                ShiftName = "Ca sáng chi nhánh khác",
                StartTime = new TimeSpan(6, 0, 0),
                EndTime = new TimeSpan(14, 0, 0),
                IsActive = true
            }
        });

        // Act - lọc isActive = true
        var activeShifts = await service.GetShiftConfigsByBranchAsync(branchId, isActive: true);

        // Assert
        Assert.Equal(2, activeShifts.Count);
        Assert.Equal("SANG", activeShifts[0].ShiftCode);
        Assert.Equal("CHIEU", activeShifts[1].ShiftCode);

        // Act - lấy toàn bộ
        var allShifts = await service.GetShiftConfigsByBranchAsync(branchId, isActive: null);
        Assert.Equal(3, allShifts.Count);
    }

    [Fact]
    public async Task GetShiftConfigsPagedAsync_ShouldReturnPagedResultWithCorrectCountAndPaging()
    {
        // Arrange
        var repo = new FakeShiftConfigRepository();
        var uow = new FakeUnitOfWork();
        var tenantContext = new FakeTenantContext();
        var service = new ShiftConfigService(repo, uow, tenantContext);

        var branchId = Guid.NewGuid();
        for (int i = 1; i <= 25; i++)
        {
            repo.ShiftConfigs.Add(new ShiftConfig
            {
                Id = Guid.NewGuid(),
                TenantId = "tenant_test_01",
                BranchId = branchId,
                ShiftCode = $"CA-{i:D2}",
                ShiftName = $"Ca làm việc số {i}",
                StartTime = TimeSpan.FromHours(i % 24),
                EndTime = TimeSpan.FromHours((i + 4) % 24),
                IsActive = i % 2 == 0
            });
        }

        // Act - Page 1 with PageSize 10
        var filterPage1 = new ShiftConfigQueryFilter(BranchId: branchId, Page: 1, PageSize: 10);
        var paged1 = await service.GetShiftConfigsPagedAsync(filterPage1);

        // Assert
        Assert.Equal(25, paged1.TotalItems);
        Assert.Equal(10, paged1.Items.Count);
        Assert.Equal(1, paged1.Page);
        Assert.Equal(10, paged1.PageSize);

        // Act - Filter with IsActive = true
        var filterActive = new ShiftConfigQueryFilter(BranchId: branchId, IsActive: true, Page: 1, PageSize: 50);
        var pagedActive = await service.GetShiftConfigsPagedAsync(filterActive);
        Assert.Equal(12, pagedActive.TotalItems);
        Assert.All(pagedActive.Items, item => Assert.True(item.IsActive));
    }

    [Fact]
    public async Task UpdateShiftConfig_ValidRequest_UpdatesFieldsAndRecalculatesDuration()
    {
        // Arrange
        var repo = new FakeShiftConfigRepository();
        var uow = new FakeUnitOfWork();
        var tenantContext = new FakeTenantContext();
        var service = new ShiftConfigService(repo, uow, tenantContext);

        var shiftId = Guid.NewGuid();
        var shift = new ShiftConfig
        {
            Id = shiftId,
            TenantId = "tenant_test_01",
            BranchId = Guid.NewGuid(),
            ShiftCode = "SANG",
            ShiftName = "Ca sáng cũ",
            StartTime = new TimeSpan(6, 0, 0),
            EndTime = new TimeSpan(14, 0, 0),
            DurationHours = 8.00m,
            IsActive = true
        };
        repo.ShiftConfigs.Add(shift);

        var updateReq = new UpdateShiftConfigRequest(
            ShiftName: "Ca sáng mới cập nhật",
            StartTime: new TimeSpan(6, 0, 0),
            EndTime: new TimeSpan(14, 30, 0)
        );

        // Act
        var result = await service.UpdateShiftConfigAsync(shiftId, updateReq);

        // Assert
        Assert.Equal("Ca sáng mới cập nhật", result.ShiftName);
        Assert.Equal(8.50m, result.DurationHours);
        Assert.NotNull(result.UpdatedAt);
        Assert.True(uow.SaveChangesCalled);
    }

    [Fact]
    public async Task UpdateShiftConfig_NonExistentId_ThrowsNotFoundException()
    {
        // Arrange
        var repo = new FakeShiftConfigRepository();
        var uow = new FakeUnitOfWork();
        var tenantContext = new FakeTenantContext();
        var service = new ShiftConfigService(repo, uow, tenantContext);

        var nonExistentId = Guid.NewGuid();
        var updateReq = new UpdateShiftConfigRequest(
            ShiftName: "Ca mới",
            StartTime: new TimeSpan(6, 0, 0),
            EndTime: new TimeSpan(14, 0, 0)
        );

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(() => service.UpdateShiftConfigAsync(nonExistentId, updateReq));
    }

    [Fact]
    public async Task UpdateShiftConfigStatus_TogglesIsActive()
    {
        // Arrange
        var repo = new FakeShiftConfigRepository();
        var uow = new FakeUnitOfWork();
        var tenantContext = new FakeTenantContext();
        var service = new ShiftConfigService(repo, uow, tenantContext);

        var shiftId = Guid.NewGuid();
        repo.ShiftConfigs.Add(new ShiftConfig
        {
            Id = shiftId,
            TenantId = "tenant_test_01",
            BranchId = Guid.NewGuid(),
            ShiftCode = "SANG",
            ShiftName = "Ca sáng",
            IsActive = true
        });

        // Act
        var result = await service.UpdateShiftConfigStatusAsync(shiftId, new UpdateShiftConfigStatusRequest(false));

        // Assert
        Assert.False(result.IsActive);
        Assert.NotNull(result.UpdatedAt);
        Assert.True(uow.SaveChangesCalled);
    }

    [Fact]
    public async Task DeleteShiftConfig_ExistingShift_PerformsSoftDelete()
    {
        // Arrange
        var repo = new FakeShiftConfigRepository();
        var uow = new FakeUnitOfWork();
        var tenantContext = new FakeTenantContext();
        var service = new ShiftConfigService(repo, uow, tenantContext);

        var shiftId = Guid.NewGuid();
        var shift = new ShiftConfig
        {
            Id = shiftId,
            TenantId = "tenant_test_01",
            BranchId = Guid.NewGuid(),
            ShiftCode = "SANG",
            ShiftName = "Ca sáng",
            IsActive = true,
            IsDeleted = false
        };
        repo.ShiftConfigs.Add(shift);

        // Act
        await service.DeleteShiftConfigAsync(shiftId);

        // Assert
        Assert.True(shift.IsDeleted);
        Assert.True(uow.SaveChangesCalled);

        // Khi truy vấn lại thì không tìm thấy
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetShiftConfigByIdAsync(shiftId));
    }

    [Fact]
    public async Task UpdateShiftConfig_SHF_VAL_04_DuplicateCodeInSameBranch_ThrowsBusinessException()
    {
        var repo = new FakeShiftConfigRepository();
        var uow = new FakeUnitOfWork();
        var tenantContext = new FakeTenantContext();
        var service = new ShiftConfigService(repo, uow, tenantContext);

        var branchId = Guid.NewGuid();
        var shift1Id = Guid.NewGuid();
        var shift2Id = Guid.NewGuid();

        repo.ShiftConfigs.Add(new ShiftConfig
        {
            Id = shift1Id,
            TenantId = "tenant_test_01",
            BranchId = branchId,
            ShiftCode = "CA_SANG",
            ShiftName = "Ca Sáng",
            StartTime = new TimeSpan(7, 0, 0),
            EndTime = new TimeSpan(15, 0, 0),
            DurationHours = 8m
        });
        repo.ShiftConfigs.Add(new ShiftConfig
        {
            Id = shift2Id,
            TenantId = "tenant_test_01",
            BranchId = branchId,
            ShiftCode = "CA_CHIEU",
            ShiftName = "Ca Chiều",
            StartTime = new TimeSpan(14, 0, 0),
            EndTime = new TimeSpan(22, 0, 0),
            DurationHours = 8m
        });

        var updateReq = new UpdateShiftConfigRequest(
            ShiftName: "Ca Chiều Đổi Mã",
            ShiftCode: "CA_SANG",
            StartTime: new TimeSpan(14, 0, 0),
            EndTime: new TimeSpan(22, 0, 0),
            BranchId: branchId
        );

        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.UpdateShiftConfigAsync(shift2Id, updateReq));
        Assert.Equal("DUPLICATE_SHIFT_CODE", ex.ErrorCode);
    }

    [Fact]
    public async Task CreateShiftConfig_SHF_VAL_03_SameStartAndEndTimeWithoutDuration_ThrowsBusinessException()
    {
        var repo = new FakeShiftConfigRepository();
        var uow = new FakeUnitOfWork();
        var tenantContext = new FakeTenantContext();
        var service = new ShiftConfigService(repo, uow, tenantContext);

        var createReq = new CreateShiftConfigRequest(
            ShiftName: "Ca Lỗi 0 Giờ",
            StartTime: new TimeSpan(8, 0, 0),
            EndTime: new TimeSpan(8, 0, 0),
            BranchId: Guid.NewGuid()
        );

        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.CreateShiftConfigAsync(createReq));
        Assert.Equal("INVALID_SHIFT_DURATION", ex.ErrorCode);
    }
}

