using System.Linq.Expressions;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Promotions;
using MT_GRMS.Application.Promotions.Dtos;
using MT_GRMS.Domain.Entities.Sales;
using MT_GRMS.Domain.Exceptions;
using Xunit;

namespace MT_GRMS.UnitTests;

public class LoyaltyServiceTests
{
    private readonly FakePromotionRepository _repo;
    private readonly FakeTenantContext _tenantContext;
    private readonly LoyaltyService _loyaltyService;

    public LoyaltyServiceTests()
    {
        _repo = new FakePromotionRepository();
        _tenantContext = new FakeTenantContext("tenant-unit-test");
        _loyaltyService = new LoyaltyService(_repo, _tenantContext);
    }

    [Fact]
    public async Task REQ_LOYALTY_01_GetConfigAsync_WhenNoConfig_ReturnsDefault()
    {
        // Act
        var result = await _loyaltyService.GetConfigAsync();

        // Assert
        Assert.NotNull(result);
        Assert.True(result.IsEnabled);
        Assert.Equal(10000m, result.EarningRateAmount);
        Assert.Equal(1, result.EarningPoints);
        Assert.Equal(1, result.RedemptionRatePoints);
        Assert.Equal(1000m, result.RedemptionRateAmount);
        Assert.Equal(20, result.MinRedemptionPoints);
        Assert.True(result.AllowPointsWithPromotions);
    }

    [Fact]
    public async Task REQ_LOYALTY_01_UpdateConfigAsync_SavesAndReturnsUpdatedConfig()
    {
        // Arrange
        var request = new UpdateLoyaltyConfigRequest(
            IsEnabled: true,
            EarningRateAmount: 20000m,
            EarningPoints: 2,
            RedemptionRatePoints: 1,
            RedemptionRateAmount: 2000m,
            MinRedemptionPoints: 50,
            AllowPointsWithPromotions: false
        );

        // Act
        var result = await _loyaltyService.UpdateConfigAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(20000m, result.EarningRateAmount);
        Assert.Equal(2, result.EarningPoints);
        Assert.Equal(2000m, result.RedemptionRateAmount);
        Assert.Equal(50, result.MinRedemptionPoints);
        Assert.False(result.AllowPointsWithPromotions);

        // Verify retrieval matches
        var retrieved = await _loyaltyService.GetConfigAsync();
        Assert.Equal(20000m, retrieved.EarningRateAmount);
        Assert.Equal(50, retrieved.MinRedemptionPoints);
    }

    [Theory]
    [InlineData(0, 1, 1, 1000, 0)]
    [InlineData(-10000, 1, 1, 1000, 0)]
    [InlineData(10000, 0, 1, 1000, 0)]
    [InlineData(10000, -1, 1, 1000, 0)]
    [InlineData(10000, 1, 0, 1000, 0)]
    [InlineData(10000, 1, -1, 1000, 0)]
    [InlineData(10000, 1, 1, 0, 0)]
    [InlineData(10000, 1, 1, -500, 0)]
    [InlineData(10000, 1, 1, 1000, -1)]
    public async Task REQ_LOYALTY_02_UpdateConfigAsync_InvalidValues_ThrowsValidationException(
        double earningRateAmount,
        int earningPoints,
        int redemptionRatePoints,
        double redemptionRateAmount,
        int minRedemptionPoints)
    {
        var request = new UpdateLoyaltyConfigRequest(
            IsEnabled: true,
            EarningRateAmount: (decimal)earningRateAmount,
            EarningPoints: earningPoints,
            RedemptionRatePoints: redemptionRatePoints,
            RedemptionRateAmount: (decimal)redemptionRateAmount,
            MinRedemptionPoints: minRedemptionPoints,
            AllowPointsWithPromotions: true
        );

        var ex = await Assert.ThrowsAsync<BusinessException>(() => _loyaltyService.UpdateConfigAsync(request));
        Assert.Equal("VALIDATION_ERROR", ex.ErrorCode);
    }

    [Fact]
    public async Task REQ_LOYALTY_02_UpdateConfigAsync_DisabledAndZeroMinPoints_SavesSuccessfully()
    {
        var request = new UpdateLoyaltyConfigRequest(
            IsEnabled: false,
            EarningRateAmount: 10000m,
            EarningPoints: 1,
            RedemptionRatePoints: 1,
            RedemptionRateAmount: 1000m,
            MinRedemptionPoints: 0,
            AllowPointsWithPromotions: false
        );

        var result = await _loyaltyService.UpdateConfigAsync(request);
        Assert.False(result.IsEnabled);
        Assert.Equal(0, result.MinRedemptionPoints);
        Assert.False(result.AllowPointsWithPromotions);
    }

    [Fact]
    public async Task REQ_LOYALTY_03_MultiTenancy_ConfigIsolatedByTenant()
    {
        await _loyaltyService.UpdateConfigAsync(new UpdateLoyaltyConfigRequest(
            IsEnabled: true,
            EarningRateAmount: 50000m,
            EarningPoints: 5,
            RedemptionRatePoints: 1,
            RedemptionRateAmount: 1000m,
            MinRedemptionPoints: 10,
            AllowPointsWithPromotions: true
        ));

        var otherTenantService = new LoyaltyService(_repo, new FakeTenantContext("tenant-other"));
        var otherConfig = await otherTenantService.GetConfigAsync();

        // Tenant khác vẫn nhận cấu hình mặc định, không bị ghi đè bởi tenant-unit-test
        Assert.Equal(10000m, otherConfig.EarningRateAmount);
        Assert.Equal(1, otherConfig.EarningPoints);
        Assert.Equal(20, otherConfig.MinRedemptionPoints);
    }

    #region Fakes
    private class FakePromotionRepository : IPromotionRepository
    {
        private readonly Dictionary<string, LoyaltySetting> _settings = new();

        public Task<LoyaltySetting?> GetLoyaltySettingAsync(string tenantId, CancellationToken cancellationToken = default)
            => Task.FromResult(_settings.TryGetValue(tenantId, out var setting) ? setting : null);

        public Task SaveLoyaltySettingAsync(LoyaltySetting setting, CancellationToken cancellationToken = default)
        {
            _settings[setting.TenantId] = setting;
            return Task.CompletedTask;
        }

        public Task<Promotion?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<Promotion?>(null);
        public Task<Promotion?> GetByIdWithConditionsAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<Promotion?>(null);
        public Task<bool> ExistsByCodeAsync(string code, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<(IReadOnlyList<Promotion> Items, int TotalCount)> GetPagedListAsync(PromotionFilterQuery query, CancellationToken cancellationToken = default) => Task.FromResult(((IReadOnlyList<Promotion>)new List<Promotion>(), 0));
        public Task<IReadOnlyList<Promotion>> GetActivePromotionsAsync(DateTime atUtc, string? branchId = null, CancellationToken cancellationToken = default) => Task.FromResult((IReadOnlyList<Promotion>)new List<Promotion>());
        public Task ReplaceConditionsAsync(Guid promotionId, IEnumerable<PromotionCondition> conditions, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<(IReadOnlyList<Promotion> Batches, int TotalCount)> GetPagedVoucherBatchesAsync(int pageIndex, int pageSize, string? searchTerm = null, string? status = null, CancellationToken cancellationToken = default) => Task.FromResult(((IReadOnlyList<Promotion>)new List<Promotion>(), 0));
        public Task AddVoucherCodesAsync(IEnumerable<VoucherCode> codes, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> VoucherCodeExistsAsync(string code, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<VoucherCode?> GetVoucherCodeAsync(string code, CancellationToken cancellationToken = default) => Task.FromResult<VoucherCode?>(null);
        public Task<(IReadOnlyList<VoucherCode> Codes, int TotalCount)> GetPagedVoucherCodesAsync(Guid promotionId, int pageIndex, int pageSize, string? status = null, CancellationToken cancellationToken = default) => Task.FromResult(((IReadOnlyList<VoucherCode>)new List<VoucherCode>(), 0));
        public Task<bool> RedeemVoucherCodeAtomicAsync(string code, Guid orderId, string orderCode, Guid? customerId, string? customerName, string? customerPhone, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<IReadOnlyList<Promotion>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult((IReadOnlyList<Promotion>)new List<Promotion>());
        public Task<IReadOnlyList<Promotion>> FindAsync(Expression<Func<Promotion, bool>> predicate, CancellationToken cancellationToken = default) => Task.FromResult((IReadOnlyList<Promotion>)new List<Promotion>());
        public Task AddAsync(Promotion entity, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Update(Promotion entity) { }
        public void Delete(Promotion entity) { }
    }

    private class FakeTenantContext : ITenantContext
    {
        public string CurrentTenantId { get; set; }
        public Guid CurrentUserId { get; set; } = Guid.NewGuid();
        public Guid? CurrentBranchId { get; set; } = null;
        public string CurrentRole { get; set; } = "STORE_OWNER";
        public void SetContext(string tenantId, Guid userId, Guid? branchId) { }
        public void SetRole(string? role) { }
        public FakeTenantContext(string tenantId) => CurrentTenantId = tenantId;
    }
    #endregion
}

