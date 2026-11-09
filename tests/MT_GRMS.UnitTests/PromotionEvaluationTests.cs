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

    [Fact]
    public async Task REQ_PROMO_02_EvaluateCart_ProductDiscount_ComputesCorrectly()
    {
        // Arrange: Giảm 20.000đ mỗi sản phẩm A khi mua từ 2 sản phẩm trở lên
        var promoId = Guid.NewGuid();
        _repo.Promotions.Add(new Promotion
        {
            Id = promoId,
            TenantId = "tenant-test-1",
            PromotionCode = "KM-SP-A",
            PromotionName = "Giảm 20k cho mỗi sản phẩm A khi mua từ 2 món",
            Type = "PRODUCT_DISCOUNT",
            DiscountValue = 20000,
            MinOrderValue = 0,
            StartDate = DateTime.UtcNow.AddDays(-1),
            EndDate = DateTime.UtcNow.AddDays(10),
            IsActive = true,
            Conditions = new List<PromotionCondition>
            {
                new PromotionCondition
                {
                    Id = Guid.NewGuid(),
                    PromotionId = promoId,
                    ProductId = _productAId,
                    ProductRole = "DISCOUNT",
                    MinQty = 2,
                    DiscountValue = 20000
                }
            }
        });

        var request = new EvaluateCartRequest(
            Items: new List<CartItemDto>
            {
                new(_productAId, "Sản phẩm A", 100000, 3)
            },
            SubTotal: 300000
        );

        // Act
        var result = await _service.EvaluateCartAsync(request);

        // Assert: 3 * 20.000 = 60.000đ giảm giá
        Assert.Equal(60000, result.TotalDiscountAmount);
        Assert.Equal(240000, result.FinalAmount);
        Assert.NotEmpty(result.AppliedPromotions);
        Assert.NotNull(result.ItemDiscounts);
        Assert.Single(result.ItemDiscounts);
        Assert.Equal(_productAId, result.ItemDiscounts[0].ProductId);
        Assert.Equal(60000, result.ItemDiscounts[0].DiscountAmount);
    }

    [Fact]
    public async Task REQ_POS_PROMO_02_EvaluateCart_MultiTierOrderDiscount_SelectsHighestQualifyingTier()
    {
        var tiersJson = "[{\"id\":\"t1\",\"tierLevel\":1,\"minOrderValue\":100000,\"discountType\":\"PERCENT\",\"discountValue\":5},{\"id\":\"t2\",\"tierLevel\":2,\"minOrderValue\":300000,\"discountType\":\"PERCENT\",\"discountValue\":10,\"maxDiscount\":50000}]";
        _repo.Promotions.Add(new Promotion
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-test-1",
            PromotionCode = "KM-TIERS",
            PromotionName = "Khuyến mãi bậc thang cuối tuần",
            Type = "ORDER_DISCOUNT",
            DiscountValue = 5,
            MinOrderValue = 100000,
            Description = $"Ưu đãi theo mốc hóa đơn\n[TIERS_JSON]{tiersJson}[/TIERS_JSON]",
            StartDate = DateTime.UtcNow.AddDays(-1),
            EndDate = DateTime.UtcNow.AddDays(10),
            IsActive = true
        });

        var request = new EvaluateCartRequest(
            Items: new List<CartItemDto> { new(_productAId, "Gạo lứt huyết rồng", 350000, 1) },
            SubTotal: 350000
        );

        var result = await _service.EvaluateCartAsync(request);

        Assert.Equal(35000, result.TotalDiscountAmount);
        Assert.Equal(315000, result.FinalAmount);
        Assert.Single(result.AppliedPromotions);
    }

    [Fact]
    public async Task REQ_POS_PROMO_02_EvaluateCart_ConflictingOrderDiscounts_SelectsSingleBestOfferWithoutStacking()
    {
        _repo.Promotions.Add(new Promotion
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-test-1",
            PromotionCode = "KM-10PCT",
            PromotionName = "Giảm 10% đơn từ 200k",
            Type = "ORDER_DISCOUNT",
            DiscountValue = 10,
            MinOrderValue = 200000,
            StartDate = DateTime.UtcNow.AddDays(-1),
            EndDate = DateTime.UtcNow.AddDays(10),
            IsActive = true
        });

        _repo.Promotions.Add(new Promotion
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-test-1",
            PromotionCode = "KM-30K",
            PromotionName = "Giảm ngay 30k đơn từ 200k",
            Type = "FIXED_AMOUNT",
            DiscountValue = 30000,
            MinOrderValue = 200000,
            StartDate = DateTime.UtcNow.AddDays(-1),
            EndDate = DateTime.UtcNow.AddDays(10),
            IsActive = true
        });

        var request = new EvaluateCartRequest(
            Items: new List<CartItemDto> { new(_productAId, "Sữa bột", 200000, 1) },
            SubTotal: 200000
        );

        var result = await _service.EvaluateCartAsync(request);

        // Best-Offer: chỉ chọn KM-30K (30.000đ > 20.000đ), không cộng dồn thành 50.000đ
        Assert.Equal(30000, result.TotalDiscountAmount);
        Assert.Equal(170000, result.FinalAmount);
        Assert.Single(result.AppliedPromotions);
        Assert.Contains("Giảm ngay 30k", result.AppliedPromotions[0]);
    }

    [Fact]
    public async Task REQ_ORD_BOGO_01_EvaluateCart_BuyXGetY_WithTiersJsonAndBuyProductId_AwardsExactGift()
    {
        var promoId = Guid.NewGuid();
        var tiersJson = $"[{{\"id\":\"t1\",\"tierLevel\":1,\"buyProductId\":\"{_productAId}\",\"buyProductName\":\"Mì Hảo Hảo\",\"requiredQty\":2,\"giftProductId\":\"{_productBId}\",\"giftProductName\":\"Coca-Cola 330ml\",\"giftUnit\":\"Lon\",\"giftQty\":1}}]";

        _repo.ProductPrices[_productBId] = 12000m;
        _repo.Promotions.Add(new Promotion
        {
            Id = promoId,
            TenantId = "tenant-test-1",
            PromotionCode = "KM-BOGO-TIER",
            PromotionName = "Mua 2 Mì tặng 1 Coca",
            Type = "BUY_X_GET_Y",
            Description = $"Mua 2 gói mì tặng 1 lon Coca\n[TIERS_JSON]{tiersJson}[/TIERS_JSON]",
            StartDate = DateTime.UtcNow.AddDays(-1),
            EndDate = DateTime.UtcNow.AddDays(10),
            IsActive = true
        });

        // Case 1: Mua 4 sản phẩm B (không phải sản phẩm X cần mua) -> Không được tặng quà
        var nonQualifyingRes = await _service.EvaluateCartAsync(new EvaluateCartRequest(
            Items: new List<CartItemDto> { new(_productBId, "Coca-Cola 330ml", 12000, 4) },
            SubTotal: 48000
        ));
        Assert.Empty(nonQualifyingRes.GiftItems);
        Assert.Null(nonQualifyingRes.AppliedPromotionId);

        // Case 2: Mua 5 sản phẩm A (5 / 2 = 2 suất quà) -> Tặng 2 lon Coca, giá trị quy đổi 24.000đ
        var qualifyingRes = await _service.EvaluateCartAsync(new EvaluateCartRequest(
            Items: new List<CartItemDto> { new(_productAId, "Mì Hảo Hảo", 5000, 5) },
            SubTotal: 25000
        ));
        Assert.Single(qualifyingRes.GiftItems);
        Assert.Equal(_productBId, qualifyingRes.GiftItems[0].ProductId);
        Assert.Equal(2m, qualifyingRes.GiftItems[0].Quantity);
        Assert.Equal("Lon", qualifyingRes.GiftItems[0].UnitName);
        Assert.Equal(24000m, qualifyingRes.GiftItems[0].EquivalentValue);
        Assert.Equal(promoId, qualifyingRes.AppliedPromotionId);
    }

    [Fact]
    public async Task REQ_ORD_BOGO_01_EvaluateCart_BestOffer_BuyXGetYVsOrderDiscount_PicksHigherValueNoStacking()
    {
        var bogoPromoId = Guid.NewGuid();
        var orderPromoId = Guid.NewGuid();

        _repo.ProductPrices[_productBId] = 15000m;
        _repo.Promotions.Add(new Promotion
        {
            Id = bogoPromoId,
            TenantId = "tenant-test-1",
            PromotionCode = "KM-BOGO-15K",
            PromotionName = "Mua 2 A tặng 1 B (trị giá 15k)",
            Type = "BUY_X_GET_Y",
            StartDate = DateTime.UtcNow.AddDays(-1),
            EndDate = DateTime.UtcNow.AddDays(10),
            IsActive = true,
            Conditions = new List<PromotionCondition>
            {
                new() { Id = Guid.NewGuid(), PromotionId = bogoPromoId, ProductId = _productAId, ProductRole = "BUY", MinQty = 2 },
                new() { Id = Guid.NewGuid(), PromotionId = bogoPromoId, ProductId = _productBId, Product = new Product { Id = _productBId, Name = "Trà xanh 0 độ", SellingPrice = 15000m }, ProductRole = "GIFT", MinQty = 1 }
            }
        });

        var orderDiscountPromo = new Promotion
        {
            Id = orderPromoId,
            TenantId = "tenant-test-1",
            PromotionCode = "KM-ORD-10K",
            PromotionName = "Giảm hóa đơn 10k",
            Type = "FIXED_AMOUNT",
            DiscountValue = 10000m,
            MinOrderValue = 50000m,
            StartDate = DateTime.UtcNow.AddDays(-1),
            EndDate = DateTime.UtcNow.AddDays(10),
            IsActive = true
        };
        _repo.Promotions.Add(orderDiscountPromo);

        var cart = new EvaluateCartRequest(
            Items: new List<CartItemDto> { new(_productAId, "Sản phẩm A", 50000, 2) },
            SubTotal: 100000
        );

        // Sub-case A: Quà tặng 15.000đ > Giảm tiền 10.000đ -> Chọn BOGO, không cộng dồn giảm tiền
        var resWhenBogoWins = await _service.EvaluateCartAsync(cart);
        Assert.Equal(bogoPromoId, resWhenBogoWins.AppliedPromotionId);
        Assert.Single(resWhenBogoWins.GiftItems);
        Assert.Equal(15000m, resWhenBogoWins.GiftItems[0].EquivalentValue);
        Assert.Equal(0m, resWhenBogoWins.TotalDiscountAmount);
        Assert.Equal(100000m, resWhenBogoWins.FinalAmount);
        Assert.Single(resWhenBogoWins.AppliedPromotions);

        // Sub-case B: Tăng giảm tiền hóa đơn lên 25.000đ (> 15.000đ) -> Chọn giảm tiền, không tặng quà
        orderDiscountPromo.DiscountValue = 25000m;
        var resWhenOrderDiscountWins = await _service.EvaluateCartAsync(cart);
        Assert.Equal(orderPromoId, resWhenOrderDiscountWins.AppliedPromotionId);
        Assert.Empty(resWhenOrderDiscountWins.GiftItems);
        Assert.Equal(25000m, resWhenOrderDiscountWins.TotalDiscountAmount);
        Assert.Equal(75000m, resWhenOrderDiscountWins.FinalAmount);
        Assert.Single(resWhenOrderDiscountWins.AppliedPromotions);
    }

    [Fact]
    public async Task REQ_ORD_BOGO_01_EvaluateCart_BuyXGetY_BudgetLimitClampsGiftQuantity()
    {
        var promoId = Guid.NewGuid();
        _repo.Promotions.Add(new Promotion
        {
            Id = promoId,
            TenantId = "tenant-test-1",
            PromotionCode = "KM-BOGO-BUDGET",
            PromotionName = "Mua 1 tặng 1 giới hạn ngân sách",
            Type = "BUY_X_GET_Y",
            BudgetLimit = 25000m,
            TotalDiscountGiven = 10000m, // Ngân sách còn lại: 15.000đ -> chỉ đủ 1 phần quà 10.000đ
            StartDate = DateTime.UtcNow.AddDays(-1),
            EndDate = DateTime.UtcNow.AddDays(10),
            IsActive = true,
            Conditions = new List<PromotionCondition>
            {
                new() { Id = Guid.NewGuid(), PromotionId = promoId, ProductId = _productAId, ProductRole = "BUY", MinQty = 1 },
                new() { Id = Guid.NewGuid(), PromotionId = promoId, ProductId = _productBId, Product = new Product { Id = _productBId, Name = "Bánh Oreo", SellingPrice = 10000m }, ProductRole = "GIFT", MinQty = 1 }
            }
        });

        var result = await _service.EvaluateCartAsync(new EvaluateCartRequest(
            Items: new List<CartItemDto> { new(_productAId, "Sữa tươi", 30000, 3) },
            SubTotal: 90000
        ));

        Assert.Single(result.GiftItems);
        Assert.Equal(1m, result.GiftItems[0].Quantity);
        Assert.Equal(10000m, result.GiftItems[0].EquivalentValue);
    }

    [Fact]
    public async Task REQ_FIX_PROMO_01_EvaluateCart_WithStringBranchAndCustomer_MatchesExactBranchAndAppliesDiscount()
    {
        var promoId = Guid.NewGuid();
        _repo.Promotions.Add(new Promotion
        {
            Id = promoId,
            TenantId = "tenant-test-1",
            PromotionCode = "KM-BRANCH-AN01",
            PromotionName = "Giảm 10k tại chi nhánh Ba Đình",
            Type = "FIXED_AMOUNT",
            DiscountValue = 10000m,
            MinOrderValue = 50000m,
            TargetBranches = "BR-AN-01,BR-AN-02",
            StartDate = DateTime.UtcNow.AddDays(-1),
            EndDate = DateTime.UtcNow.AddDays(10),
            IsActive = true
        });

        // Act 1: String BranchId "BR-AN-01" và string CustomerId "kh_retail"
        var request = new EvaluateCartRequest(
            Items: new List<CartItemDto> { new(_productAId, "Sữa tươi", 50000, 1) },
            SubTotal: 50000,
            BranchId: "BR-AN-01",
            CustomerId: "kh_retail"
        );
        var result = await _service.EvaluateCartAsync(request);

        // Assert 1: Áp dụng thành công
        Assert.Equal(10000m, result.TotalDiscountAmount);
        Assert.Equal(40000m, result.FinalAmount);
        Assert.Equal(promoId, result.AppliedPromotionId);

        // Act 2: Chống khớp nhầm chuỗi con "BR-AN-010"
        var requestSubstr = new EvaluateCartRequest(
            Items: new List<CartItemDto> { new(_productAId, "Sữa tươi", 50000, 1) },
            SubTotal: 50000,
            BranchId: "BR-AN-010",
            CustomerId: "kh_retail"
        );
        var resultSubstr = await _service.EvaluateCartAsync(requestSubstr);

        // Assert 2: Không áp dụng vì BR-AN-010 không khớp BR-AN-01
        Assert.Equal(0m, resultSubstr.TotalDiscountAmount);
        Assert.Equal(50000m, resultSubstr.FinalAmount);
        Assert.Null(resultSubstr.AppliedPromotionId);
    }

    [Fact]
    public async Task REQ_FIX_PROMO_02_EvaluateCart_BOGO_CalculatesBoughtQuantityUsingConversionRate()
    {
        var promoId = Guid.NewGuid();
        _repo.Promotions.Add(new Promotion
        {
            Id = promoId,
            TenantId = "tenant-test-1",
            PromotionCode = "KM-BOGO-LOC4",
            PromotionName = "Mua 4 hộp sữa tặng 1 bánh Oreo",
            Type = "BUY_X_GET_Y",
            StartDate = DateTime.UtcNow.AddDays(-1),
            EndDate = DateTime.UtcNow.AddDays(10),
            IsActive = true,
            Conditions = new List<PromotionCondition>
            {
                new() { Id = Guid.NewGuid(), PromotionId = promoId, ProductId = _productAId, ProductRole = "BUY", MinQty = 4 },
                new() { Id = Guid.NewGuid(), PromotionId = promoId, ProductId = _productBId, Product = new Product { Id = _productBId, Name = "Bánh Oreo", SellingPrice = 12000m }, ProductRole = "GIFT", MinQty = 1 }
            }
        });

        // Khách mua 1 lốc (Quantity = 1) nhưng đơn vị quy đổi ConversionRate = 4 (tổng 4 hộp cơ sở)
        var request = new EvaluateCartRequest(
            Items: new List<CartItemDto>
            {
                new(_productAId, "Lốc Sữa tươi 4 hộp", 40000, 1, 4.00m)
            },
            SubTotal: 40000
        );

        var result = await _service.EvaluateCartAsync(request);

        // Assert: 1 lốc x 4 = 4 hộp thỏa mãn MinQty = 4 -> Nhận 1 Bánh Oreo
        Assert.Single(result.GiftItems);
        Assert.Equal(_productBId, result.GiftItems[0].ProductId);
        Assert.Equal(1m, result.GiftItems[0].Quantity);
        Assert.Equal(12000m, result.GiftItems[0].EquivalentValue);
    }

    #region Fakes
    private class FakePromotionRepository : IPromotionRepository
    {
        public List<Promotion> Promotions { get; } = new();
        public List<VoucherCode> VoucherCodes { get; } = new();
        public Dictionary<Guid, decimal> ProductPrices { get; } = new();

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
            var cleanBranch = branchId?.Trim();
            var list = Promotions.Where(p => !p.IsDeleted && p.IsActive && p.StartDate <= atUtc && p.EndDate >= atUtc && p.Type != "VOUCHER")
                .Where(p =>
                    string.IsNullOrWhiteSpace(cleanBranch) ||
                    string.Equals(cleanBranch, "ALL", StringComparison.OrdinalIgnoreCase) ||
                    string.IsNullOrWhiteSpace(p.TargetBranches) ||
                    string.Equals(p.TargetBranches, "ALL", StringComparison.OrdinalIgnoreCase) ||
                    p.TargetBranches.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(b => b.Trim())
                        .Contains(cleanBranch, StringComparer.OrdinalIgnoreCase)
                )
                .ToList();
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

        public Task<Dictionary<Guid, decimal>> GetProductSellingPricesAsync(IEnumerable<Guid> productIds, CancellationToken cancellationToken = default)
        {
            var ids = productIds.ToHashSet();
            var result = ProductPrices
                .Where(kv => ids.Contains(kv.Key))
                .ToDictionary(kv => kv.Key, kv => kv.Value);
            return Task.FromResult(result);
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

