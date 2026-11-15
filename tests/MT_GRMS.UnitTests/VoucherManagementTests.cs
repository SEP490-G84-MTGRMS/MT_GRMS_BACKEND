using System.Linq.Expressions;
using System.Text.Json;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Promotions;
using MT_GRMS.Application.Promotions.Dtos;
using MT_GRMS.Domain.Entities.Sales;
using MT_GRMS.Domain.Exceptions;
using Xunit;

namespace MT_GRMS.UnitTests;

public class VoucherManagementTests
{
    private readonly FakePromotionRepository _repo;
    private readonly FakeUnitOfWork _uow;
    private readonly FakeTenantContext _tenantContext;
    private readonly PromotionService _service;

    public VoucherManagementTests()
    {
        _repo = new FakePromotionRepository();
        _uow = new FakeUnitOfWork();
        _tenantContext = new FakeTenantContext("tenant-test-1");
        _service = new PromotionService(_repo, _uow, _tenantContext);

        // Seed a voucher batch
        var batchId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var batch = new Promotion
        {
            Id = batchId,
            TenantId = "tenant-test-1",
            PromotionCode = "VOUCHER-TET26",
            PromotionName = "Voucher Đón Tết 2026",
            Type = "VOUCHER",
            DiscountValue = 50000,
            MinOrderValue = 200000,
            UsageLimit = 100,
            UsedCount = 1,
            StartDate = DateTime.UtcNow.AddDays(-10),
            EndDate = DateTime.UtcNow.AddDays(20),
            IsActive = true,
            VoucherPrefix = "TET26",
            TotalVoucherQuantity = 5
        };
        _repo.Promotions.Add(batch);

        // Seed sample voucher codes
        _repo.VoucherCodes.AddRange(new[]
        {
            new VoucherCode
            {
                Id = Guid.NewGuid(),
                TenantId = "tenant-test-1",
                PromotionId = batchId,
                Code = "TET26-ABC123",
                Status = "UNUSED"
            },
            new VoucherCode
            {
                Id = Guid.NewGuid(),
                TenantId = "tenant-test-1",
                PromotionId = batchId,
                Code = "TET26-XYZ999",
                Status = "USED",
                OrderCode = "HD-00100",
                UsedAt = DateTime.UtcNow.AddHours(-3)
            }
        });
    }

    [Fact]
    public async Task REQ_PV04_CreateVoucherBatch_AutoGeneratesCodesWithPrefix()
    {
        // Arrange
        var request = new VoucherBatchCreateRequest(
            BatchCode: "BATCH-TEST",
            Name: "Đợt Voucher Test",
            DiscountType: "FIXED_AMOUNT",
            DiscountValue: 20000,
            MinOrderValue: 100000,
            MaxDiscountAmount: null,
            TotalQuantity: 10,
            StartDate: DateTime.UtcNow,
            EndDate: DateTime.UtcNow.AddDays(30),
            Prefix: "TEST20K",
            Description: "Sinh mã test"
        );

        // Act
        var result = await _service.CreateVoucherBatchAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("BATCH-TEST", result.BatchCode);
        Assert.Equal(10, result.TotalQuantity);

        // Kiểm tra đúng 10 mã đã được sinh
        var codes = _repo.VoucherCodes.Where(v => v.PromotionId == result.Id).ToList();
        Assert.Equal(10, codes.Count);
        Assert.All(codes, c => Assert.StartsWith("TEST20K-", c.Code));
        Assert.All(codes, c => Assert.Equal("UNUSED", c.Status));
        // Kiểm tra tính độc nhất
        Assert.Equal(10, codes.Select(c => c.Code).Distinct().Count());
    }

    [Fact]
    public async Task REQ_PV05_ValidateVoucher_ValidCodeAndOrder_ReturnsValidDiscount()
    {
        // Arrange
        var request = new ValidateVoucherRequest(
            Code: "TET26-ABC123",
            CartTotal: 300000
        );

        // Act
        var result = await _service.ValidateVoucherAsync(request);

        // Assert
        Assert.True(result.IsValid);
        Assert.Equal(50000, result.DiscountAmount);
        Assert.Equal("TET26-ABC123", result.Code);
    }

    [Fact]
    public async Task REQ_PV05_ValidateVoucher_OrderBelowMinOrderValue_ReturnsInvalid()
    {
        // Arrange: Đơn hàng chỉ 150k trong khi minOrderValue là 200k
        var request = new ValidateVoucherRequest(
            Code: "TET26-ABC123",
            CartTotal: 150000
        );

        // Act
        var result = await _service.ValidateVoucherAsync(request);

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal(0, result.DiscountAmount);
        Assert.Contains("tối thiểu", result.Message);
    }

    [Fact]
    public async Task REQ_PV05_ValidateVoucher_AlreadyUsedCode_ReturnsInvalid()
    {
        // Arrange
        var request = new ValidateVoucherRequest(
            Code: "TET26-XYZ999", // Đã USED
            CartTotal: 500000
        );

        // Act
        var result = await _service.ValidateVoucherAsync(request);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("USED", result.Message);
    }

    [Fact]
    public async Task REQ_PV05_RedeemVoucher_ValidCode_ChangesStatusToUsed()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var request = new RedeemVoucherRequest(
            Code: "TET26-ABC123",
            OrderId: orderId,
            OrderCode: "HD-99999",
            CustomerId: Guid.NewGuid(),
            CustomerName: "Trần Văn B",
            CustomerPhone: "0912345678"
        );

        // Act
        var result = await _service.RedeemVoucherAsync(request);

        // Assert
        Assert.True(result.Success);

        // Kiểm tra trong repo trạng thái đã chuyển sang USED
        var code = _repo.VoucherCodes.First(v => v.Code == "TET26-ABC123");
        Assert.Equal("USED", code.Status);
        Assert.Equal("HD-99999", code.OrderCode);
        Assert.NotNull(code.UsedAt);
    }

    [Fact]
    public async Task REQ_PV05_RedeemVoucher_AlreadyUsedCode_ReturnsFailure()
    {
        // Arrange
        var request = new RedeemVoucherRequest(
            Code: "TET26-XYZ999", // Đã USED từ trước
            OrderId: Guid.NewGuid(),
            OrderCode: "HD-88888"
        );

        // Act
        var result = await _service.RedeemVoucherAsync(request);

        // Assert
        Assert.False(result.Success);
    }

    [Fact]
    public async Task REQ_PV05_ValidateVoucher_JsonPayloadNonExistentCode_DeserializesAndReturnsNotExists()
    {
        // Arrange: Giả lập JSON camelCase gửi từ Frontend POS /vouchers/validate
        const string json = """
            {
              "code": "MA_KHONG_TON_TAI",
              "cartTotal": 150000,
              "customerId": "walk-in",
              "hasOtherPromotions": false
            }
            """;

        var webOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var request = JsonSerializer.Deserialize<ValidateVoucherRequest>(json, webOptions);

        Assert.NotNull(request);
        Assert.Equal("MA_KHONG_TON_TAI", request!.Code);
        Assert.Equal(150000m, request.CartTotal);
        Assert.Equal("walk-in", request.CustomerId);

        // Act
        var result = await _service.ValidateVoucherAsync(request);

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal("Mã voucher không tồn tại", result.Message);
        Assert.Equal(0m, result.DiscountAmount);
    }

    [Theory]
    [InlineData("", "Đợt hợp lệ", "FIXED_AMOUNT", "VC", 10, 20000, 0, null, 10)]
    [InlineData("BATCH_OVER_50_CHARS_12345678901234567890123456789012", "Đợt hợp lệ", "FIXED_AMOUNT", "VC", 10, 20000, 0, null, 10)]
    [InlineData("B-01", "", "FIXED_AMOUNT", "VC", 10, 20000, 0, null, 10)]
    [InlineData("B-01", "Đợt hợp lệ", "", "VC", 10, 20000, 0, null, 10)]
    [InlineData("B-01", "Đợt hợp lệ", "INVALID_DISC", "VC", 10, 20000, 0, null, 10)]
    [InlineData("B-01", "Đợt hợp lệ", "FIXED_AMOUNT", "", 10, 20000, 0, null, 10)]
    [InlineData("B-01", "Đợt hợp lệ", "FIXED_AMOUNT", "PREFIX_OVER_20_CHARSS", 10, 20000, 0, null, 10)]
    [InlineData("B-01", "Đợt hợp lệ", "FIXED_AMOUNT", "VC", 0, 20000, 0, null, 10)]
    [InlineData("B-01", "Đợt hợp lệ", "FIXED_AMOUNT", "VC", 10001, 20000, 0, null, 10)]
    [InlineData("B-01", "Đợt hợp lệ", "FIXED_AMOUNT", "VC", 10, 0, 0, null, 10)]
    [InlineData("B-01", "Đợt hợp lệ", "PERCENT", "VC", 10, 101, 0, 50000.0, 10)]
    [InlineData("B-01", "Đợt hợp lệ", "FIXED_AMOUNT", "VC", 10, 20000, -1, null, 10)]
    [InlineData("B-01", "Đợt hợp lệ", "PERCENT", "VC", 10, 10, 0, 0.0, 10)]
    [InlineData("B-01", "Đợt hợp lệ", "FIXED_AMOUNT", "VC", 10, 20000, 0, null, 0)]
    [InlineData("B-01", "Đợt hợp lệ", "FIXED_AMOUNT", "VC", 10, 20000, 0, null, -5)]
    public async Task CreateVoucherBatch_InvalidInput_ThrowsValidationException(
        string batchCode,
        string name,
        string discountType,
        string prefix,
        int totalQty,
        double discountValue,
        double minOrderValue,
        double? maxDiscountAmount,
        int endOffsetDays)
    {
        var start = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var request = new VoucherBatchCreateRequest(
            BatchCode: batchCode,
            Name: name,
            DiscountType: discountType,
            DiscountValue: (decimal)discountValue,
            MinOrderValue: (decimal)minOrderValue,
            MaxDiscountAmount: maxDiscountAmount.HasValue ? (decimal)maxDiscountAmount.Value : null,
            TotalQuantity: totalQty,
            StartDate: start,
            EndDate: start.AddDays(endOffsetDays),
            Prefix: prefix);

        var ex = await Assert.ThrowsAsync<BusinessException>(() => _service.CreateVoucherBatchAsync(request));
        Assert.Equal("VALIDATION_ERROR", ex.ErrorCode);
    }

    [Fact]
    public async Task CreateVoucherBatch_DuplicateCode_ThrowsBusinessException()
    {
        var request = new VoucherBatchCreateRequest(
            BatchCode: "VOUCHER-TET26",
            Name: "Trùng mã đợt",
            DiscountType: "FIXED_AMOUNT",
            DiscountValue: 10000m,
            MinOrderValue: 0m,
            MaxDiscountAmount: null,
            TotalQuantity: 5,
            StartDate: DateTime.UtcNow,
            EndDate: DateTime.UtcNow.AddDays(10),
            Prefix: "DUP");

        var ex = await Assert.ThrowsAsync<BusinessException>(() => _service.CreateVoucherBatchAsync(request));
        Assert.Equal("DUPLICATE_CODE", ex.ErrorCode);
    }

    [Theory]
    [InlineData(0, "VC")]
    [InlineData(-5, "VC")]
    [InlineData(10001, "VC")]
    [InlineData(10, "PREFIX_LONGER_THAN_20_CHARS")]
    public async Task GenerateVoucherCodes_InvalidRequest_ThrowsValidationException(int qty, string prefix)
    {
        var batchId = _repo.Promotions.First().Id;
        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => _service.GenerateVoucherCodesAsync(batchId, new GenerateVoucherCodesRequest(qty, prefix)));
        Assert.Equal("VALIDATION_ERROR", ex.ErrorCode);
    }

    [Fact]
    public async Task GenerateVoucherCodes_ValidRequest_AppendsCodesAndUpdatesBatchTotal()
    {
        var batch = _repo.Promotions.First();
        var initialQty = batch.TotalVoucherQuantity;

        var added = await _service.GenerateVoucherCodesAsync(batch.Id, new GenerateVoucherCodesRequest(3, "EXTRA"));

        Assert.Equal(3, added);
        Assert.Equal(initialQty + 3, batch.TotalVoucherQuantity);
    }

    [Fact]
    public async Task GenerateVoucherCodes_NonExistentBatch_ThrowsNotFoundException()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => _service.GenerateVoucherCodesAsync(Guid.NewGuid(), new GenerateVoucherCodesRequest(5, "VC")));
    }

    [Fact]
    public async Task ValidateVoucher_EdgeCases_EmptyNegativePausedNotStartedExpiredCustomerAndStacking()
    {
        // Empty code & negative cartTotal
        var emptyResult = await _service.ValidateVoucherAsync(new ValidateVoucherRequest("   ", 250000m));
        Assert.False(emptyResult.IsValid);

        var negTotalResult = await _service.ValidateVoucherAsync(new ValidateVoucherRequest("TET26-ABC123", -1000m));
        Assert.False(negTotalResult.IsValid);

        // Paused promo
        var pausedPromoId = Guid.NewGuid();
        _repo.Promotions.Add(new Promotion
        {
            Id = pausedPromoId,
            PromotionCode = "V-PAUSE",
            PromotionName = "Tạm dừng",
            Type = "VOUCHER",
            DiscountValue = 20000m,
            MinOrderValue = 0m,
            StartDate = DateTime.UtcNow.AddDays(-5),
            EndDate = DateTime.UtcNow.AddDays(5),
            IsActive = false
        });
        _repo.VoucherCodes.Add(new VoucherCode { Id = Guid.NewGuid(), PromotionId = pausedPromoId, Code = "PAUSE-01", Status = "UNUSED" });
        var pausedResult = await _service.ValidateVoucherAsync(new ValidateVoucherRequest("PAUSE-01", 300000m));
        Assert.False(pausedResult.IsValid);

        // Expired promo
        var expiredPromoId = Guid.NewGuid();
        _repo.Promotions.Add(new Promotion
        {
            Id = expiredPromoId,
            PromotionCode = "V-EXP",
            PromotionName = "Hết hạn",
            Type = "VOUCHER",
            DiscountValue = 20000m,
            MinOrderValue = 0m,
            StartDate = DateTime.UtcNow.AddDays(-20),
            EndDate = DateTime.UtcNow.AddDays(-2),
            IsActive = true
        });
        _repo.VoucherCodes.Add(new VoucherCode { Id = Guid.NewGuid(), PromotionId = expiredPromoId, Code = "EXP-01", Status = "UNUSED" });
        var expiredResult = await _service.ValidateVoucherAsync(new ValidateVoucherRequest("EXP-01", 300000m));
        Assert.False(expiredResult.IsValid);

        // Customer-specific voucher
        var ownerId = Guid.NewGuid();
        var promoId = _repo.Promotions.First(p => p.PromotionCode == "VOUCHER-TET26").Id;
        _repo.VoucherCodes.Add(new VoucherCode { Id = Guid.NewGuid(), PromotionId = promoId, Code = "VIP-ONLY", Status = "UNUSED", CustomerId = ownerId });
        var wrongCustResult = await _service.ValidateVoucherAsync(new ValidateVoucherRequest("VIP-ONLY", 300000m, Guid.NewGuid().ToString()));
        Assert.False(wrongCustResult.IsValid);
        var rightCustResult = await _service.ValidateVoucherAsync(new ValidateVoucherRequest("VIP-ONLY", 300000m, ownerId.ToString()));
        Assert.True(rightCustResult.IsValid);

        // Percent voucher with MaxDiscountAmount & disallow stacking
        var pctNoStackId = Guid.NewGuid();
        _repo.Promotions.Add(new Promotion
        {
            Id = pctNoStackId,
            PromotionCode = "V-PCT-NOSTK",
            PromotionName = "Giảm 50% tối đa 30k",
            Type = "VOUCHER",
            DiscountValue = 50m,
            MaxDiscountAmount = 30000m,
            MinOrderValue = 0m,
            StartDate = DateTime.UtcNow.AddDays(-2),
            EndDate = DateTime.UtcNow.AddDays(10),
            IsActive = true,
            Description = "[CONFIG_JSON]{\"allowWithPromotions\":false}[/CONFIG_JSON]"
        });
        _repo.VoucherCodes.Add(new VoucherCode { Id = Guid.NewGuid(), PromotionId = pctNoStackId, Code = "PCT-01", Status = "UNUSED" });

        var blockedStack = await _service.ValidateVoucherAsync(new ValidateVoucherRequest("PCT-01", 200000m, HasOtherPromotions: true));
        Assert.False(blockedStack.IsValid);
        Assert.False(blockedStack.AllowWithPromotions);

        var cappedPct = await _service.ValidateVoucherAsync(new ValidateVoucherRequest("PCT-01", 200000m, HasOtherPromotions: false));
        Assert.True(cappedPct.IsValid);
        Assert.Equal(30000m, cappedPct.DiscountAmount);
    }

    [Theory]
    [InlineData("", true, "HD-001")]
    [InlineData("TET26-ABC123", false, "HD-001")]
    [InlineData("TET26-ABC123", true, "")]
    public async Task RedeemVoucher_InvalidRequest_ThrowsValidationException(string code, bool validOrderId, string orderCode)
    {
        var req = new RedeemVoucherRequest(
            Code: code,
            OrderId: validOrderId ? Guid.NewGuid() : Guid.Empty,
            OrderCode: orderCode);

        var ex = await Assert.ThrowsAsync<BusinessException>(() => _service.RedeemVoucherAsync(req));
        Assert.Equal("VALIDATION_ERROR", ex.ErrorCode);
    }

    #region Fakes
    private class FakePromotionRepository : IPromotionRepository
    {
        public List<Promotion> Promotions { get; } = new();
        public List<VoucherCode> VoucherCodes { get; } = new();

        public Task<Promotion?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(Promotions.FirstOrDefault(p => p.Id == id && !p.IsDeleted));

        public Task<Promotion?> GetByIdWithConditionsAsync(Guid id, CancellationToken cancellationToken = default)
            => GetByIdAsync(id, cancellationToken);

        public Task<bool> ExistsByCodeAsync(string code, CancellationToken cancellationToken = default)
            => Task.FromResult(Promotions.Any(p => p.PromotionCode.Equals(code, StringComparison.OrdinalIgnoreCase) && !p.IsDeleted));

        public Task<(IReadOnlyList<Promotion> Items, int TotalCount)> GetPagedListAsync(PromotionFilterQuery query, CancellationToken cancellationToken = default)
            => Task.FromResult(((IReadOnlyList<Promotion>)Promotions, Promotions.Count));

        public Task<IReadOnlyList<Promotion>> GetActivePromotionsAsync(DateTime atUtc, string? branchId = null, CancellationToken cancellationToken = default)
            => Task.FromResult((IReadOnlyList<Promotion>)Promotions.Where(p => p.IsActive && p.StartDate <= atUtc && p.EndDate >= atUtc).ToList());

        public Task ReplaceConditionsAsync(Guid promotionId, IEnumerable<PromotionCondition> conditions, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

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
            => Task.FromResult(VoucherCodes.Any(v => v.Code.Equals(code, StringComparison.OrdinalIgnoreCase) && !v.IsDeleted));

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
        public void SetContext(string tenantId, Guid userId, Guid? branchId) { }
        public void SetRole(string? role) { }
        public FakeTenantContext(string tenantId) => CurrentTenantId = tenantId;
    }
    #endregion
}

