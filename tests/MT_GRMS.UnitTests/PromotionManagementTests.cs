using System.Linq.Expressions;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Promotions;
using MT_GRMS.Application.Promotions.Dtos;
using MT_GRMS.Domain.Entities.Sales;
using MT_GRMS.Domain.Exceptions;
using Xunit;

namespace MT_GRMS.UnitTests;

public class PromotionManagementTests
{
    private readonly FakePromotionRepository _repo;
    private readonly FakeUnitOfWork _uow;
    private readonly FakeTenantContext _tenantContext;
    private readonly PromotionService _service;

    public PromotionManagementTests()
    {
        _repo = new FakePromotionRepository();
        _uow = new FakeUnitOfWork();
        _tenantContext = new FakeTenantContext("tenant-test-1");
        _service = new PromotionService(_repo, _uow, _tenantContext);

        // Seed initial promotions
        _repo.Promotions.AddRange(new[]
        {
            new Promotion
            {
                Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                TenantId = "tenant-test-1",
                PromotionCode = "KM-SALE10",
                PromotionName = "Giảm giá 10% đơn hàng",
                Type = "ORDER_DISCOUNT",
                DiscountValue = 10,
                MinOrderValue = 100000,
                MaxDiscountAmount = 50000,
                StartDate = DateTime.UtcNow.AddDays(-5),
                EndDate = DateTime.UtcNow.AddDays(10),
                IsActive = true
            },
            new Promotion
            {
                Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                TenantId = "tenant-test-1",
                PromotionCode = "KM-FIXED20K",
                PromotionName = "Giảm 20.000đ cho đơn từ 150k",
                Type = "FIXED_AMOUNT",
                DiscountValue = 20000,
                MinOrderValue = 150000,
                StartDate = DateTime.UtcNow.AddDays(-2),
                EndDate = DateTime.UtcNow.AddDays(5),
                IsActive = true
            },
            // Tenant khác (cô lập dữ liệu)
            new Promotion
            {
                Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                TenantId = "tenant-other",
                PromotionCode = "KM-OTHER",
                PromotionName = "Khuyến mãi tenant khác",
                Type = "ORDER_DISCOUNT",
                DiscountValue = 15,
                StartDate = DateTime.UtcNow.AddDays(-1),
                EndDate = DateTime.UtcNow.AddDays(5),
                IsActive = true
            }
        });
    }

    [Fact]
    public async Task REQ_PV01_CreatePromotion_ValidRequest_SuccessWithGuidV7()
    {
        // Arrange
        var request = new PromotionCreateRequest(
            PromotionCode: "KM-TESTNEW",
            PromotionName: "Chương trình thử nghiệm",
            Type: "ORDER_DISCOUNT",
            DiscountValue: 15,
            MinOrderValue: 200000,
            MaxDiscountAmount: 30000,
            UsageLimit: 100,
            StartDate: DateTime.UtcNow.AddDays(1),
            EndDate: DateTime.UtcNow.AddDays(15),
            IsActive: true,
            Description: "Mô tả test",
            Conditions: new List<PromotionConditionDto>
            {
                new(null, Guid.NewGuid(), "Mì Hảo Hảo", "BUY", 3, null, "Mua 3 gói")
            }
        );

        // Act
        var result = await _service.CreatePromotionAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal("KM-TESTNEW", result.PromotionCode);
        Assert.Equal(15, result.DiscountValue);
        Assert.True(result.IsActive);
        Assert.Single(result.Conditions);
        Assert.True(_uow.HasSaved);
    }

    [Fact]
    public async Task REQ_PV01_CreatePromotion_DuplicateCode_ThrowsBusinessException()
    {
        // Arrange
        var request = new PromotionCreateRequest(
            PromotionCode: "KM-SALE10", // đã tồn tại
            PromotionName: "Trùng mã",
            Type: "ORDER_DISCOUNT",
            DiscountValue: 10,
            MinOrderValue: 50000,
            MaxDiscountAmount: null,
            UsageLimit: null,
            StartDate: DateTime.UtcNow,
            EndDate: DateTime.UtcNow.AddDays(5)
        );

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BusinessException>(() => _service.CreatePromotionAsync(request));
        Assert.Equal("DUPLICATE_CODE", ex.ErrorCode);
    }

    [Fact]
    public async Task REQ_PV01_GetPagedPromotions_FilteredBySearchAndType_ReturnsMatching()
    {
        // Arrange
        var query = new PromotionFilterQuery
        {
            SearchTerm = "SALE10",
            Type = "ORDER_DISCOUNT",
            PageIndex = 1,
            PageSize = 10
        };

        // Act
        var result = await _service.GetPagedPromotionsAsync(query);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1, result.TotalItems);
        Assert.Single(result.Items);
        Assert.Equal("KM-SALE10", result.Items[0].PromotionCode);
    }

    [Fact]
    public async Task REQ_PV01_TogglePromotionStatus_TogglesActiveState()
    {
        // Arrange
        var id = Guid.Parse("11111111-1111-1111-1111-111111111111");

        // Act
        var newStatus = await _service.TogglePromotionStatusAsync(id);

        // Assert
        Assert.False(newStatus); // ban đầu true -> toggle thành false
        Assert.True(_uow.HasSaved);
    }

    [Fact]
    public async Task REQ_PV06_MultiTenancy_TenantCannotSeeOtherTenantPromotions()
    {
        // Arrange: Tenant hiện tại là "tenant-test-1"
        var query = new PromotionFilterQuery { PageIndex = 1, PageSize = 10 };

        // Act
        var result = await _service.GetPagedPromotionsAsync(query);

        // Assert: Không bao gồm KM-OTHER của tenant-other
        Assert.Equal(2, result.TotalItems);
        Assert.DoesNotContain(result.Items, p => p.PromotionCode == "KM-OTHER");
    }

    [Fact]
    public async Task REQ_PV01_SeedSampleData_WhenCalled_SeedsSamplePromotionsAndVouchers()
    {
        // Act: Lần 1 nạp dữ liệu mẫu
        var addedCount = await _service.SeedSampleDataAsync();

        // Assert
        Assert.True(addedCount >= 5);
        Assert.True(_uow.HasSaved);
        Assert.Contains(_repo.Promotions, p => p.PromotionCode == "KM-GIAM10-TOIDA50K");
        Assert.Contains(_repo.Promotions, p => p.PromotionCode == "KM-GIAM30K-DON150K");
        Assert.Contains(_repo.Promotions, p => p.PromotionCode == "KM-MUA3TANG1-COCA");
        Assert.Contains(_repo.Promotions, p => p.PromotionCode == "VOUCHER-VIP50K");
        Assert.Contains(_repo.Promotions, p => p.PromotionCode == "VOUCHER-CUOITUAN20");

        // Act: Lần 2 chạy lại (Idempotency check) -> Không trùng lặp dữ liệu
        var secondRun = await _service.SeedSampleDataAsync();
        Assert.Equal(0, secondRun);
    }

    private static PromotionCreateRequest BuildValidCreateRequest(
        string code = "KM-VALID-01",
        string name = "Chương trình hợp lệ",
        string type = "ORDER_DISCOUNT",
        decimal discountValue = 10m,
        decimal minOrderValue = 100000m,
        decimal? maxDiscountAmount = 50000m,
        int? usageLimit = 100,
        DateTime? startDate = null,
        DateTime? endDate = null,
        decimal? budgetLimit = 5000000m,
        string targetBranches = "ALL",
        List<PromotionConditionDto>? conditions = null)
    {
        var start = startDate ?? DateTime.UtcNow;
        var end = endDate ?? start.AddDays(10);
        return new PromotionCreateRequest(
            PromotionCode: code,
            PromotionName: name,
            Type: type,
            DiscountValue: discountValue,
            MinOrderValue: minOrderValue,
            MaxDiscountAmount: maxDiscountAmount,
            UsageLimit: usageLimit,
            StartDate: start,
            EndDate: end,
            IsActive: true,
            Description: null,
            TargetBranches: targetBranches,
            BudgetLimit: budgetLimit,
            Conditions: conditions
        );
    }

    [Theory]
    [InlineData("", "Valid Name", "ORDER_DISCOUNT", 10, 0, null, null, null, 1)]
    [InlineData("   ", "Valid Name", "ORDER_DISCOUNT", 10, 0, null, null, null, 1)]
    [InlineData("CODE_OVER_50_CHARS_12345678901234567890123456789012345", "Valid Name", "ORDER_DISCOUNT", 10, 0, null, null, null, 1)]
    [InlineData("KM-01", "", "ORDER_DISCOUNT", 10, 0, null, null, null, 1)]
    [InlineData("KM-01", "Valid Name", "", 10, 0, null, null, null, 1)]
    [InlineData("KM-01", "Valid Name", "VOUCHER", 10, 0, null, null, null, 1)]
    [InlineData("KM-01", "Valid Name", "UNKNOWN_TYPE", 10, 0, null, null, null, 1)]
    [InlineData("KM-01", "Valid Name", "ORDER_DISCOUNT", -1, 0, null, null, null, 1)]
    [InlineData("KM-01", "Valid Name", "PERCENT", 0, 0, null, null, null, 1)]
    [InlineData("KM-01", "Valid Name", "PERCENT", 101, 0, null, null, null, 1)]
    [InlineData("KM-01", "Valid Name", "FIXED_AMOUNT", 0, 0, null, null, null, 1)]
    [InlineData("KM-01", "Valid Name", "ORDER_DISCOUNT", 10, -1, null, null, null, 1)]
    [InlineData("KM-01", "Valid Name", "ORDER_DISCOUNT", 10, 0, -500.0, null, null, 1)]
    [InlineData("KM-01", "Valid Name", "ORDER_DISCOUNT", 10, 0, null, 0, null, 1)]
    [InlineData("KM-01", "Valid Name", "ORDER_DISCOUNT", 10, 0, null, null, 0.0, 1)]
    [InlineData("KM-01", "Valid Name", "ORDER_DISCOUNT", 10, 0, null, null, -1000.0, 1)]
    [InlineData("KM-01", "Valid Name", "ORDER_DISCOUNT", 10, 0, null, null, null, 0)]
    [InlineData("KM-01", "Valid Name", "ORDER_DISCOUNT", 10, 0, null, null, null, -2)]
    public async Task CreatePromotion_InvalidInput_ThrowsValidationException(
        string code,
        string name,
        string type,
        double discountValue,
        double minOrderValue,
        double? maxDiscountAmount,
        int? usageLimit,
        double? budgetLimit,
        int endOffsetDays)
    {
        var start = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var request = BuildValidCreateRequest(
            code: code,
            name: name,
            type: type,
            discountValue: (decimal)discountValue,
            minOrderValue: (decimal)minOrderValue,
            maxDiscountAmount: maxDiscountAmount.HasValue ? (decimal)maxDiscountAmount.Value : null,
            usageLimit: usageLimit,
            startDate: start,
            endDate: start.AddDays(endOffsetDays),
            budgetLimit: budgetLimit.HasValue ? (decimal)budgetLimit.Value : null);

        var ex = await Assert.ThrowsAsync<BusinessException>(() => _service.CreatePromotionAsync(request));
        Assert.Equal("VALIDATION_ERROR", ex.ErrorCode);
    }

    [Fact]
    public async Task CreatePromotion_NameOver150Chars_AndInvalidCondition_ThrowsValidationException()
    {
        var longNameRequest = BuildValidCreateRequest(name: new string('A', 151));
        var ex1 = await Assert.ThrowsAsync<BusinessException>(() => _service.CreatePromotionAsync(longNameRequest));
        Assert.Equal("VALIDATION_ERROR", ex1.ErrorCode);

        var badRoleRequest = BuildValidCreateRequest(
            type: "BUY_X_GET_Y",
            discountValue: 0m,
            conditions: new List<PromotionConditionDto>
            {
                new(null, Guid.NewGuid(), "SP A", "INVALID_ROLE", 2, 0m, null)
            });
        var ex2 = await Assert.ThrowsAsync<BusinessException>(() => _service.CreatePromotionAsync(badRoleRequest));
        Assert.Equal("VALIDATION_ERROR", ex2.ErrorCode);

        var badMinQtyRequest = BuildValidCreateRequest(
            type: "BUY_X_GET_Y",
            discountValue: 0m,
            conditions: new List<PromotionConditionDto>
            {
                new(null, Guid.NewGuid(), "SP A", "BUY", 0, 0m, null)
            });
        var ex3 = await Assert.ThrowsAsync<BusinessException>(() => _service.CreatePromotionAsync(badMinQtyRequest));
        Assert.Equal("VALIDATION_ERROR", ex3.ErrorCode);
    }

    [Fact]
    public async Task CreatePromotion_ValidBoundaries_BuyXGetYZeroDiscount_AndPercent100_Succeed()
    {
        var buyXGetY = BuildValidCreateRequest(
            code: "KM-BOGO-ZERO",
            type: "BUY_X_GET_Y",
            discountValue: 0m,
            maxDiscountAmount: null);
        var res1 = await _service.CreatePromotionAsync(buyXGetY);
        Assert.Equal("BUY_X_GET_Y", res1.Type);
        Assert.Equal(0m, res1.DiscountValue);

        var pct100 = BuildValidCreateRequest(
            code: "KM-PCT-100",
            type: "PERCENT",
            discountValue: 100m,
            maxDiscountAmount: 0m);
        var res2 = await _service.CreatePromotionAsync(pct100);
        Assert.Equal(100m, res2.DiscountValue);
    }

    [Fact]
    public async Task UpdatePromotion_ValidRequest_UpdatesFieldsAndReplacesConditions()
    {
        var promoId = _repo.Promotions.First(p => p.PromotionCode == "KM-SALE10").Id;
        var newProductId = Guid.NewGuid();
        var start = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
        var updateRequest = new PromotionUpdateRequest(
            PromotionName: "Tên đã cập nhật",
            Type: "PRODUCT_DISCOUNT",
            DiscountValue: 25000m,
            MinOrderValue: 100000m,
            MaxDiscountAmount: 80000m,
            UsageLimit: 50,
            StartDate: start,
            EndDate: start.AddDays(15),
            IsActive: false,
            Description: "Mô tả mới",
            TargetBranches: "Chi nhánh Ba Đình",
            BudgetLimit: 2000000m,
            Conditions: new List<PromotionConditionDto>
            {
                new(null, newProductId, "Sản phẩm mới", "DISCOUNT", 2, 25000m, "Note")
            });

        var updated = await _service.UpdatePromotionAsync(promoId, updateRequest);

        Assert.Equal("Tên đã cập nhật", updated.PromotionName);
        Assert.Equal("PRODUCT_DISCOUNT", updated.Type);
        Assert.Equal(25000m, updated.DiscountValue);
        Assert.False(updated.IsActive);
        Assert.Equal("Chi nhánh Ba Đình", updated.TargetBranches);
        Assert.Single(updated.Conditions);
        Assert.Equal(newProductId, updated.Conditions[0].ProductId);
    }

    [Theory]
    [InlineData("", "ORDER_DISCOUNT", 10, 1)]
    [InlineData("Valid", "VOUCHER", 10, 1)]
    [InlineData("Valid", "PERCENT", 150, 1)]
    [InlineData("Valid", "ORDER_DISCOUNT", 10, 0)]
    public async Task UpdatePromotion_InvalidInput_ThrowsValidationException(
        string name,
        string type,
        double discountValue,
        int endOffsetDays)
    {
        var promoId = _repo.Promotions.First(p => p.PromotionCode == "KM-SALE10").Id;
        var start = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
        var updateRequest = new PromotionUpdateRequest(
            PromotionName: name,
            Type: type,
            DiscountValue: (decimal)discountValue,
            MinOrderValue: 0m,
            MaxDiscountAmount: null,
            UsageLimit: null,
            StartDate: start,
            EndDate: start.AddDays(endOffsetDays),
            IsActive: true);

        var ex = await Assert.ThrowsAsync<BusinessException>(() => _service.UpdatePromotionAsync(promoId, updateRequest));
        Assert.Equal("VALIDATION_ERROR", ex.ErrorCode);
    }

    [Fact]
    public async Task PromotionLifecycle_NotFoundIds_ThrowNotFoundException_AndDeleteSoftDeletes()
    {
        var missingId = Guid.NewGuid();
        var start = DateTime.UtcNow;
        var validUpdate = new PromotionUpdateRequest("Name", "ORDER_DISCOUNT", 10m, 0m, null, null, start, start.AddDays(1), true);

        await Assert.ThrowsAsync<NotFoundException>(() => _service.UpdatePromotionAsync(missingId, validUpdate));
        await Assert.ThrowsAsync<NotFoundException>(() => _service.TogglePromotionStatusAsync(missingId));
        await Assert.ThrowsAsync<NotFoundException>(() => _service.DeletePromotionAsync(missingId));

        var existingId = _repo.Promotions.First(p => p.PromotionCode == "KM-SALE10").Id;
        _repo.Promotions.First(p => p.Id == existingId).IsActive = false;
        var deleted = await _service.DeletePromotionAsync(existingId);
        Assert.True(deleted);
        await Assert.ThrowsAsync<NotFoundException>(() => _service.GetPromotionByIdAsync(existingId));
    }

    [Fact]
    public async Task DeletePromotion_WhenCurrentlyRunning_ThrowsBusinessException()
    {
        var promo = _repo.Promotions.First(p => p.PromotionCode == "KM-SALE10");
        promo.IsActive = true;
        promo.StartDate = DateTime.UtcNow.AddMinutes(-5);
        promo.EndDate = DateTime.UtcNow.AddMinutes(5);

        var ex = await Assert.ThrowsAsync<BusinessException>(() => _service.DeletePromotionAsync(promo.Id));

        Assert.Equal("PROMOTION_IS_RUNNING", ex.ErrorCode);
        Assert.False(promo.IsDeleted);
    }

    #region Fakes
    private class FakePromotionRepository : IPromotionRepository
    {
        public List<Promotion> Promotions { get; } = new();
        public List<PromotionCondition> Conditions { get; } = new();
        public List<VoucherCode> VoucherCodes { get; } = new();

        public Task<Promotion?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Promotions.FirstOrDefault(p => p.Id == id && !p.IsDeleted));
        }

        public Task<Promotion?> GetByIdWithConditionsAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var promo = Promotions.FirstOrDefault(p => p.Id == id && !p.IsDeleted);
            if (promo != null)
            {
                promo.Conditions = Conditions.Where(c => c.PromotionId == id).ToList();
            }
            return Task.FromResult(promo);
        }

        public Task<bool> ExistsByCodeAsync(string code, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Promotions.Any(p => p.PromotionCode.Equals(code, StringComparison.OrdinalIgnoreCase) && !p.IsDeleted));
        }

        public Task<(IReadOnlyList<Promotion> Items, int TotalCount)> GetPagedListAsync(PromotionFilterQuery query, CancellationToken cancellationToken = default)
        {
            var q = Promotions.Where(p => !p.IsDeleted && p.TenantId == "tenant-test-1" && p.Type != "VOUCHER");

            if (!string.IsNullOrWhiteSpace(query.SearchTerm))
            {
                q = q.Where(p => p.PromotionCode.Contains(query.SearchTerm, StringComparison.OrdinalIgnoreCase) ||
                                 p.PromotionName.Contains(query.SearchTerm, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(query.Type) && !string.Equals(query.Type, "ALL", StringComparison.OrdinalIgnoreCase))
            {
                q = q.Where(p => p.Type.Equals(query.Type, StringComparison.OrdinalIgnoreCase));
            }

            var list = q.ToList();
            return Task.FromResult(((IReadOnlyList<Promotion>)list, list.Count));
        }

        public Task<IReadOnlyList<Promotion>> GetActivePromotionsAsync(DateTime atUtc, string? branchId = null, CancellationToken cancellationToken = default)
        {
            var list = Promotions.Where(p => !p.IsDeleted && p.IsActive && p.StartDate <= atUtc && p.EndDate >= atUtc && p.Type != "VOUCHER").ToList();
            return Task.FromResult((IReadOnlyList<Promotion>)list);
        }

        public Task ReplaceConditionsAsync(Guid promotionId, IEnumerable<PromotionCondition> conditions, CancellationToken cancellationToken = default)
        {
            Conditions.RemoveAll(c => c.PromotionId == promotionId);
            Conditions.AddRange(conditions);
            return Task.CompletedTask;
        }

        public Task<(IReadOnlyList<Promotion> Batches, int TotalCount)> GetPagedVoucherBatchesAsync(int pageIndex, int pageSize, string? searchTerm = null, string? status = null, CancellationToken cancellationToken = default)
        {
            var q = Promotions.Where(p => !p.IsDeleted && p.Type == "VOUCHER").ToList();
            return Task.FromResult(((IReadOnlyList<Promotion>)q, q.Count));
        }

        public Task AddVoucherCodesAsync(IEnumerable<VoucherCode> codes, CancellationToken cancellationToken = default)
        {
            VoucherCodes.AddRange(codes);
            return Task.CompletedTask;
        }

        public Task<bool> VoucherCodeExistsAsync(string code, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(VoucherCodes.Any(v => v.Code.Equals(code, StringComparison.OrdinalIgnoreCase) && !v.IsDeleted));
        }

        public Task<VoucherCode?> GetVoucherCodeAsync(string code, CancellationToken cancellationToken = default)
        {
            var vc = VoucherCodes.FirstOrDefault(v => v.Code.Equals(code, StringComparison.OrdinalIgnoreCase) && !v.IsDeleted);
            if (vc != null)
            {
                vc.Promotion = Promotions.FirstOrDefault(p => p.Id == vc.PromotionId);
            }
            return Task.FromResult(vc);
        }

        public Task<(IReadOnlyList<VoucherCode> Codes, int TotalCount)> GetPagedVoucherCodesAsync(Guid promotionId, int pageIndex, int pageSize, string? status = null, CancellationToken cancellationToken = default)
        {
            var q = VoucherCodes.Where(v => v.PromotionId == promotionId && !v.IsDeleted).ToList();
            return Task.FromResult(((IReadOnlyList<VoucherCode>)q, q.Count));
        }

        public Task<bool> RedeemVoucherCodeAtomicAsync(string code, Guid orderId, string orderCode, Guid? customerId, string? customerName, string? customerPhone, CancellationToken cancellationToken = default)
        {
            var v = VoucherCodes.FirstOrDefault(x => x.Code.Equals(code, StringComparison.OrdinalIgnoreCase) && x.Status == "UNUSED" && !x.IsDeleted);
            if (v == null) return Task.FromResult(false);
            v.Status = "USED";
            v.OrderId = orderId;
            v.OrderCode = orderCode;
            v.CustomerId = customerId;
            v.CustomerName = customerName;
            v.CustomerPhone = customerPhone;
            v.UsedAt = DateTime.UtcNow;
            return Task.FromResult(true);
        }

        public Task<IReadOnlyList<Promotion>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult((IReadOnlyList<Promotion>)Promotions);
        public Task<IReadOnlyList<Promotion>> FindAsync(Expression<Func<Promotion, bool>> predicate, CancellationToken cancellationToken = default) => Task.FromResult((IReadOnlyList<Promotion>)Promotions.Where(predicate.Compile()).ToList());
        public Task AddAsync(Promotion entity, CancellationToken cancellationToken = default) { Promotions.Add(entity); return Task.CompletedTask; }
        public void Update(Promotion entity) { }
        public void Delete(Promotion entity) => entity.IsDeleted = true;

        private LoyaltySetting? _loyaltySetting;
        public Task<LoyaltySetting?> GetLoyaltySettingAsync(string tenantId, CancellationToken cancellationToken = default) => Task.FromResult(_loyaltySetting);
        public Task SaveLoyaltySettingAsync(LoyaltySetting setting, CancellationToken cancellationToken = default) { _loyaltySetting = setting; return Task.CompletedTask; }
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
        public Guid CurrentUserId { get; set; } = Guid.NewGuid();
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
    #endregion
}

