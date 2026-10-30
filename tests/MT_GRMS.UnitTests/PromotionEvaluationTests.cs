using System.Linq.Expressions;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Promotions;
using MT_GRMS.Application.Promotions.Dtos;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Entities.Sales;
using Xunit;

namespace MT_GRMS.UnitTests;

public class PromotionEvaluationTests
{
    private readonly FakePromotionRepository _repo;
    private readonly FakeUnitOfWork _uow;
    private readonly FakeTenantContext _tenantContext;
    private readonly PromotionService _service;

    private readonly Guid _productAId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private readonly Guid _productBId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    public PromotionEvaluationTests()
    {
        _repo = new FakePromotionRepository();
        _uow = new FakeUnitOfWork();
        _tenantContext = new FakeTenantContext("tenant-test-1");
        _service = new PromotionService(_repo, _uow, _tenantContext);
    }

    [Fact]
    public async Task REQ_PV03_EvaluateCart_PercentageDiscount_WithCapMaxDiscount_ClampsCorrectly()
    {
        // Arrange: Giảm 20% cho đơn từ 200k, chặn trần max 50k
        _repo.Promotions.Add(new Promotion
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-test-1",
            PromotionCode = "KM-20PCT",
            PromotionName = "Giảm 20% đơn từ 200k tối đa 50k",
            Type = "ORDER_DISCOUNT",
            DiscountValue = 20,
            MinOrderValue = 200000,
            MaxDiscountAmount = 50000,
            StartDate = DateTime.UtcNow.AddDays(-1),
            EndDate = DateTime.UtcNow.AddDays(10),
            IsActive = true
        });

        var request = new EvaluateCartRequest(
            Items: new List<CartItemDto>
            {
                new(_productAId, "Sữa tươi Vinamilk", 100000, 5) // Tổng = 500k
            },
            SubTotal: 500000
        );

        // Act
        var result = await _service.EvaluateCartAsync(request);

        // Assert: 20% của 500k = 100k nhưng bị chặn trần 50k -> TotalDiscount = 50k, Final = 450k
        Assert.Equal(50000, result.TotalDiscountAmount);
        Assert.Equal(450000, result.FinalAmount);
        Assert.Single(result.AppliedPromotions);
    }

    [Fact]
    public async Task REQ_PV03_EvaluateCart_FixedAmountDiscount_CalculatesCorrectly()
    {
        // Arrange: Giảm 30.000đ cho đơn từ 150k
        _repo.Promotions.Add(new Promotion
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-test-1",
            PromotionCode = "KM-FIXED30",
            PromotionName = "Giảm 30k",
            Type = "FIXED_AMOUNT",
            DiscountValue = 30000,
            MinOrderValue = 150000,
            StartDate = DateTime.UtcNow.AddDays(-1),
            EndDate = DateTime.UtcNow.AddDays(10),
            IsActive = true
        });

        var request = new EvaluateCartRequest(
            Items: new List<CartItemDto>
            {
                new(_productAId, "Gạo ST25", 200000, 1)
            },
            SubTotal: 200000
        );

        // Act
        var result = await _service.EvaluateCartAsync(request);

        // Assert
        Assert.Equal(30000, result.TotalDiscountAmount);
        Assert.Equal(170000, result.FinalAmount);
    }

    [Fact]
    public async Task REQ_PV03_EvaluateCart_DiscountNeverExceedsSubTotal()
    {
        // Arrange: Giảm 100.000đ nhưng đơn hàng chỉ 80.000đ
        _repo.Promotions.Add(new Promotion
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-test-1",
            PromotionCode = "KM-BIG100",
            PromotionName = "Giảm 100k",
            Type = "FIXED_AMOUNT",
            DiscountValue = 100000,
            MinOrderValue = 50000,
            StartDate = DateTime.UtcNow.AddDays(-1),
            EndDate = DateTime.UtcNow.AddDays(10),
            IsActive = true
        });

        var request = new EvaluateCartRequest(
            Items: new List<CartItemDto> { new(_productAId, "Bánh AFC", 80000, 1) },
            SubTotal: 80000
        );

        // Act
        var result = await _service.EvaluateCartAsync(request);

        // Assert: Discount bị clamp về đúng 80k, FinalAmount = 0 (không âm tiền)
        Assert.Equal(80000, result.TotalDiscountAmount);
        Assert.Equal(0, result.FinalAmount);
    }

    [Fact]
    public async Task REQ_PV03_EvaluateCart_BuyXGetY_AwardsCorrectGiftItems()
    {
        // Arrange: Mua 3 SP A tặng 1 SP B
        var promoId = Guid.NewGuid();
        var promo = new Promotion
        {
            Id = promoId,
            TenantId = "tenant-test-1",
            PromotionCode = "KM-BUY3GET1",
            PromotionName = "Mua 3 A Tặng 1 B",
            Type = "BUY_X_GET_Y",
            StartDate = DateTime.UtcNow.AddDays(-1),
            EndDate = DateTime.UtcNow.AddDays(10),
            IsActive = true,
            Conditions = new List<PromotionCondition>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    PromotionId = promoId,
                    ProductId = _productAId,
                    ProductRole = "BUY",
                    MinQty = 3
                },
                new()
                {
                    Id = Guid.NewGuid(),
                    PromotionId = promoId,
                    ProductId = _productBId,
                    Product = new Product { Id = _productBId, Name = "Nước ngọt Coca-Cola 330ml" },
                    ProductRole = "GIFT",
                    MinQty = 1
                }
            }
        };
        _repo.Promotions.Add(promo);

        // Khách mua 7 sản phẩm A (7 / 3 = 2 lần quà -> được tặng 2 sản phẩm B)
        var request = new EvaluateCartRequest(
            Items: new List<CartItemDto>
            {
                new(_productAId, "Sản phẩm A", 20000, 7)
            },
            SubTotal: 140000
        );

        // Act
        var result = await _service.EvaluateCartAsync(request);

        // Assert
        Assert.Single(result.GiftItems);
        Assert.Equal(_productBId, result.GiftItems[0].ProductId);
        Assert.Equal(2, result.GiftItems[0].Quantity);
        Assert.Equal("Nước ngọt Coca-Cola 330ml", result.GiftItems[0].ProductName);
    }

    [Fact]
    public async Task REQ_PV03_EvaluateCart_WithBothAutoPromoAndVoucher_AppliesBothCorrectly()
    {
        // Arrange 1: Khuyến mại tự động giảm 10k cho đơn từ 50k
        _repo.Promotions.Add(new Promotion
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-test-1",
            PromotionCode = "KM-AUTO10K",
            PromotionName = "Giảm tự động 10k",
            Type = "FIXED_AMOUNT",
            DiscountValue = 10000,
            MinOrderValue = 50000,
            StartDate = DateTime.UtcNow.AddDays(-1),
            EndDate = DateTime.UtcNow.AddDays(10),
            IsActive = true
        });

        // Arrange 2: Voucher giảm 20k
        var batchId = Guid.NewGuid();
        _repo.Promotions.Add(new Promotion
        {
            Id = batchId,
            TenantId = "tenant-test-1",
            PromotionCode = "VOUCHER-20K",
            PromotionName = "Voucher 20k",
            Type = "VOUCHER",
            DiscountValue = 20000,
            MinOrderValue = 100000,
            StartDate = DateTime.UtcNow.AddDays(-1),
            EndDate = DateTime.UtcNow.AddDays(10),
            IsActive = true
        });

        _repo.VoucherCodes.Add(new VoucherCode
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-test-1",
            PromotionId = batchId,
            Code = "VIP20K-NOW",
            Status = "UNUSED"
        });

        // Đơn hàng 200k nhập mã VIP20K-NOW
        var request = new EvaluateCartRequest(
            Items: new List<CartItemDto> { new(_productAId, "Hàng hóa", 200000, 1) },
            SubTotal: 200000,
            CouponCode: "VIP20K-NOW"
        );

        // Act
        var result = await _service.EvaluateCartAsync(request);

        // Assert: Auto giảm 10k + Voucher giảm 20k = 30k -> Final = 170k
        Assert.Equal(30000, result.TotalDiscountAmount);
        Assert.Equal(170000, result.FinalAmount);
        Assert.Equal(2, result.AppliedPromotions.Count);
        Assert.NotNull(result.VoucherResult);
        Assert.True(result.VoucherResult.IsValid);
        Assert.Equal(20000, result.VoucherResult.DiscountAmount);
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
        {
            var list = Promotions.Where(p => !p.IsDeleted && p.IsActive && p.StartDate <= atUtc && p.EndDate >= atUtc && p.Type != "VOUCHER").ToList();
            return Task.FromResult((IReadOnlyList<Promotion>)list);
        }

        public Task ReplaceConditionsAsync(Guid promotionId, IEnumerable<PromotionCondition> conditions, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<(IReadOnlyList<Promotion> Batches, int TotalCount)> GetPagedVoucherBatchesAsync(int pageIndex, int pageSize, string? searchTerm = null, string? status = null, CancellationToken cancellationToken = default)
            => Task.FromResult(((IReadOnlyList<Promotion>)Promotions.Where(p => p.Type == "VOUCHER").ToList(), 0));

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
            => Task.FromResult(((IReadOnlyList<VoucherCode>)VoucherCodes, VoucherCodes.Count));

        public Task<bool> RedeemVoucherCodeAtomicAsync(string code, Guid orderId, string orderCode, Guid? customerId, string? customerName, string? customerPhone, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<IReadOnlyList<Promotion>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult((IReadOnlyList<Promotion>)Promotions);
        public Task<IReadOnlyList<Promotion>> FindAsync(Expression<Func<Promotion, bool>> predicate, CancellationToken cancellationToken = default) => Task.FromResult((IReadOnlyList<Promotion>)Promotions.Where(predicate.Compile()).ToList());
        public Task AddAsync(Promotion entity, CancellationToken cancellationToken = default) { Promotions.Add(entity); return Task.CompletedTask; }
        public void Update(Promotion entity) { }
        public void Delete(Promotion entity) => entity.IsDeleted = true;
    }

    private class FakeUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);
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

