using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Promotions.Dtos;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Entities.Sales;
using MT_GRMS.Domain.Exceptions;

namespace MT_GRMS.Application.Promotions;

public class PromotionService : IPromotionService
{
    private readonly IPromotionRepository _promotionRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITenantContext _tenantContext;

    public PromotionService(
        IPromotionRepository promotionRepository,
        IUnitOfWork unitOfWork,
        ITenantContext tenantContext)
    {
        _promotionRepository = promotionRepository;
        _unitOfWork = unitOfWork;
        _tenantContext = tenantContext;
    }

    #region 1. Promotions Management
    public async Task<PagedResult<PromotionResponse>> GetPagedPromotionsAsync(PromotionFilterQuery query, CancellationToken cancellationToken = default)
    {
        var (items, totalCount) = await _promotionRepository.GetPagedListAsync(query, cancellationToken);
        var dtos = items.Select(MapToResponse).ToList();
        return new PagedResult<PromotionResponse> { Items = dtos, Page = query.PageIndex, PageSize = query.PageSize, TotalItems = totalCount };
    }

    public async Task<PromotionResponse> GetPromotionByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var promo = await _promotionRepository.GetByIdWithConditionsAsync(id, cancellationToken);
        if (promo == null)
            throw new NotFoundException("Chương trình khuyến mại", id);

        return MapToResponse(promo);
    }

    public async Task<PromotionResponse> CreatePromotionAsync(PromotionCreateRequest request, CancellationToken cancellationToken = default)
    {
        if (await _promotionRepository.ExistsByCodeAsync(request.PromotionCode, cancellationToken))
            throw new BusinessException("DUPLICATE_CODE", $"Mã khuyến mại '{request.PromotionCode}' đã tồn tại trong hệ thống.");

        var promo = new Promotion
        {
            TenantId = _tenantContext.CurrentTenantId,
            PromotionCode = request.PromotionCode.Trim().ToUpper(),
            PromotionName = request.PromotionName.Trim(),
            Type = request.Type.Trim().ToUpper(),
            DiscountValue = request.DiscountValue,
            MinOrderValue = request.MinOrderValue,
            MaxDiscountAmount = request.MaxDiscountAmount,
            UsageLimit = request.UsageLimit,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            IsActive = request.IsActive,
            Description = request.Description,
            TargetBranches = string.IsNullOrWhiteSpace(request.TargetBranches) ? "ALL" : request.TargetBranches,
            BudgetLimit = request.BudgetLimit,
        };

        if (request.Conditions != null && request.Conditions.Count > 0)
        {
            foreach (var cond in request.Conditions)
            {
                if (cond.ProductId != Guid.Empty)
                {
                    promo.Conditions.Add(new PromotionCondition
                    {
                        TenantId = _tenantContext.CurrentTenantId,
                        PromotionId = promo.Id,
                        ProductId = cond.ProductId,
                        ProductRole = string.IsNullOrWhiteSpace(cond.ProductRole) ? "BUY" : cond.ProductRole.ToUpper(),
                        MinQty = cond.MinQty <= 0 ? 1 : cond.MinQty,
                        DiscountValue = cond.DiscountValue,
                        Note = cond.Note
                    });
                }
            }
        }

        await _promotionRepository.AddAsync(promo, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return MapToResponse(promo);
    }

    public async Task<PromotionResponse> UpdatePromotionAsync(Guid id, PromotionUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var promo = await _promotionRepository.GetByIdWithConditionsAsync(id, cancellationToken);
        if (promo == null)
            throw new NotFoundException("Chương trình khuyến mại", id);

        promo.PromotionName = request.PromotionName.Trim();
        promo.Type = request.Type.Trim().ToUpper();
        promo.DiscountValue = request.DiscountValue;
        promo.MinOrderValue = request.MinOrderValue;
        promo.MaxDiscountAmount = request.MaxDiscountAmount;
        promo.UsageLimit = request.UsageLimit;
        promo.StartDate = request.StartDate;
        promo.EndDate = request.EndDate;
        promo.IsActive = request.IsActive;
        promo.Description = request.Description;
        promo.TargetBranches = string.IsNullOrWhiteSpace(request.TargetBranches) ? "ALL" : request.TargetBranches;
        promo.BudgetLimit = request.BudgetLimit;

        // Cập nhật Conditions
        var newConditions = new List<PromotionCondition>();
        if (request.Conditions != null && request.Conditions.Count > 0)
        {
            foreach (var cond in request.Conditions)
            {
                if (cond.ProductId != Guid.Empty)
                {
                    newConditions.Add(new PromotionCondition
                    {
                        TenantId = _tenantContext.CurrentTenantId,
                        PromotionId = promo.Id,
                        ProductId = cond.ProductId,
                        ProductRole = string.IsNullOrWhiteSpace(cond.ProductRole) ? "BUY" : cond.ProductRole.ToUpper(),
                        MinQty = cond.MinQty <= 0 ? 1 : cond.MinQty,
                        DiscountValue = cond.DiscountValue,
                        Note = cond.Note
                    });
                }
            }
        }
        await _promotionRepository.ReplaceConditionsAsync(promo.Id, newConditions, cancellationToken);

        _promotionRepository.Update(promo);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var updated = await _promotionRepository.GetByIdWithConditionsAsync(id, cancellationToken);
        return MapToResponse(updated ?? promo);
    }

    public async Task<bool> TogglePromotionStatusAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var promo = await _promotionRepository.GetByIdAsync(id, cancellationToken);
        if (promo == null)
            throw new NotFoundException("Chương trình khuyến mại", id);

        promo.IsActive = !promo.IsActive;
        _promotionRepository.Update(promo);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return promo.IsActive;
    }

    public async Task<bool> DeletePromotionAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var promo = await _promotionRepository.GetByIdAsync(id, cancellationToken);
        if (promo == null)
            throw new NotFoundException("Chương trình khuyến mại", id);

        promo.IsDeleted = true;
        _promotionRepository.Update(promo);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }
    #endregion

    #region 2. POS Cart Evaluation (Auto Discount & Buy X Get Y)
    public async Task<EvaluateCartResponse> EvaluateCartAsync(EvaluateCartRequest request, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var activePromos = await _promotionRepository.GetActivePromotionsAsync(now, request.BranchId, cancellationToken);

        decimal totalDiscount = 0.00m;
        var appliedPromos = new List<string>();
        var giftItems = new List<CartGiftItemDto>();
        var itemDiscounts = new List<CartItemDiscountDto>();
        Guid? winningPromoId = null;

        // Tập hợp các sản phẩm quà tặng tiềm năng để tra cứu đơn giá bán phục vụ quy đổi Best-Offer & BudgetLimit
        var potentialGiftProductIds = new HashSet<Guid>();
        foreach (var p in activePromos.Where(p => p.Type == "BUY_X_GET_Y"))
        {
            foreach (var c in p.Conditions.Where(c => c.ProductRole == "GIFT" && c.ProductId != Guid.Empty))
            {
                potentialGiftProductIds.Add(c.ProductId);
            }
            foreach (var tier in ParseTiersFromDescription(p.Description))
            {
                if (tier.GiftProductId.HasValue && tier.GiftProductId.Value != Guid.Empty)
                {
                    potentialGiftProductIds.Add(tier.GiftProductId.Value);
                }
            }
        }

        var giftPriceLookup = potentialGiftProductIds.Count > 0
            ? await _promotionRepository.GetProductSellingPricesAsync(potentialGiftProductIds, cancellationToken)
            : new Dictionary<Guid, decimal>();

        var promoCandidates = new List<(
            Guid PromoId,
            decimal ComparisonValue,
            decimal MonetaryDiscount,
            string Label,
            List<CartItemDiscountDto> Items,
            List<CartGiftItemDto> Gifts)>();

        foreach (var promo in activePromos)
        {
            // Kiểm tra ngân sách khuyến mại còn lại nếu có
            decimal? remainingBudget = null;
            if (promo.BudgetLimit.HasValue)
            {
                remainingBudget = promo.BudgetLimit.Value - promo.TotalDiscountGiven;
                if (remainingBudget <= 0m)
                    continue;
            }

            // Kiểm tra giới hạn số lượt dùng nếu có
            if (promo.UsageLimit.HasValue && promo.UsedCount >= promo.UsageLimit.Value)
                continue;

            // 1. ORDER_DISCOUNT hoặc PERCENT / FIXED_AMOUNT (hỗ trợ đa bậc [TIERS_JSON])
            if (promo.Type is "ORDER_DISCOUNT" or "PERCENT" or "FIXED_AMOUNT")
            {
                decimal candidateDiscount = EvaluateOrderDiscountPromotion(promo, request.SubTotal);
                if (remainingBudget.HasValue)
                {
                    candidateDiscount = Math.Min(candidateDiscount, remainingBudget.Value);
                }
                candidateDiscount = Math.Min(candidateDiscount, Math.Max(0m, request.SubTotal));

                if (candidateDiscount > 0m)
                {
                    promoCandidates.Add((
                        promo.Id,
                        candidateDiscount,
                        candidateDiscount,
                        $"{promo.PromotionName} (-{candidateDiscount:N0} đ)",
                        new List<CartItemDiscountDto>(),
                        new List<CartGiftItemDto>()
                    ));
                }
            }
            // 2. BUY_X_GET_Y (Mua X tặng Y — hỗ trợ cả [TIERS_JSON] và PromotionConditions, tham gia Best-Offer)
            else if (promo.Type == "BUY_X_GET_Y")
            {
                var (candidateGifts, totalGiftValue) = EvaluateBuyXGetYPromotion(
                    promo,
                    request.Items,
                    giftPriceLookup,
                    remainingBudget);

                if (candidateGifts.Count > 0)
                {
                    // Nếu sản phẩm tặng chưa khai báo giá bán (VD trong test đơn giản), dùng 0.01m để vẫn được chọn khi không có KM tiền tốt hơn
                    decimal comparisonVal = totalGiftValue > 0m ? totalGiftValue : 0.01m;
                    var giftSummary = string.Join(", ", candidateGifts.Select(g => $"{g.Quantity:0.##} {g.UnitName ?? "SP"} {g.ProductName}".Trim()));
                    promoCandidates.Add((
                        promo.Id,
                        comparisonVal,
                        0m,
                        $"{promo.PromotionName} (Tặng: {giftSummary})",
                        new List<CartItemDiscountDto>(),
                        candidateGifts
                    ));
                }
            }
            // 3. PRODUCT_DISCOUNT (Giảm giá theo từng sản phẩm/mặt hàng)
            else if (promo.Type == "PRODUCT_DISCOUNT" && promo.Conditions.Count > 0)
            {
                var discountConds = promo.Conditions.Where(c => c.ProductRole is "DISCOUNT" or "BUY").ToList();
                decimal promoItemDiscountTotal = 0m;
                var candidateItems = new List<CartItemDiscountDto>();

                foreach (var cond in discountConds)
                {
                    var matchingCartItems = request.Items.Where(i => i.ProductId == cond.ProductId).ToList();
                    var totalBoughtBaseQty = matchingCartItems.Sum(i => GetCartItemBaseQty(i));
                    if (matchingCartItems.Count == 0 || totalBoughtBaseQty < cond.MinQty)
                        continue;

                    decimal discountPerItem = cond.DiscountValue ?? promo.DiscountValue;
                    decimal lineDiscount = 0m;

                    foreach (var cartItem in matchingCartItems)
                    {
                        decimal itemLineDiscount = 0m;
                        if (discountPerItem <= 100m)
                        {
                            itemLineDiscount = Math.Round(cartItem.UnitPrice * (discountPerItem / 100.00m) * cartItem.Quantity, 2);
                        }
                        else
                        {
                            var rate = cartItem.ConversionRate > 0m ? cartItem.ConversionRate : 1.00m;
                            itemLineDiscount = discountPerItem * cartItem.Quantity * rate;
                        }
                        lineDiscount += itemLineDiscount;
                    }

                    if (promo.MaxDiscountAmount.HasValue && lineDiscount > promo.MaxDiscountAmount.Value)
                    {
                        lineDiscount = promo.MaxDiscountAmount.Value;
                    }

                    lineDiscount = Math.Min(lineDiscount, Math.Max(0, request.SubTotal - promoItemDiscountTotal));
                    if (lineDiscount > 0)
                    {
                        promoItemDiscountTotal += lineDiscount;
                        candidateItems.Add(new CartItemDiscountDto(cond.ProductId, lineDiscount));
                    }
                }

                if (remainingBudget.HasValue)
                {
                    promoItemDiscountTotal = Math.Min(promoItemDiscountTotal, remainingBudget.Value);
                }
                promoItemDiscountTotal = Math.Min(promoItemDiscountTotal, Math.Max(0m, request.SubTotal));

                if (promoItemDiscountTotal > 0)
                {
                    promoCandidates.Add((
                        promo.Id,
                        promoItemDiscountTotal,
                        promoItemDiscountTotal,
                        $"{promo.PromotionName} (-{promoItemDiscountTotal:N0} đ)",
                        candidateItems,
                        new List<CartGiftItemDto>()
                    ));
                }
            }
        }

        // Giải quyết xung đột toàn cục (Best-Offer không cộng dồn): chọn 1 chương trình KM tự động có giá trị ưu đãi cao nhất
        var bestOffer = promoCandidates
            .OrderByDescending(c => c.ComparisonValue)
            .ThenByDescending(c => c.MonetaryDiscount)
            .FirstOrDefault();

        if (bestOffer.ComparisonValue > 0m)
        {
            winningPromoId = bestOffer.PromoId;
            totalDiscount += bestOffer.MonetaryDiscount;
            appliedPromos.Insert(0, bestOffer.Label);
            if (bestOffer.Items.Count > 0)
            {
                itemDiscounts.AddRange(bestOffer.Items);
            }
            if (bestOffer.Gifts.Count > 0)
            {
                giftItems.AddRange(bestOffer.Gifts);
            }
        }

        // 4. Thẩm định Voucher Coupon nếu có nhập mã
        ValidateVoucherResponse? voucherResult = null;
        if (!string.IsNullOrWhiteSpace(request.CouponCode))
        {
            voucherResult = await ValidateVoucherAsync(new ValidateVoucherRequest(
                request.CouponCode,
                Math.Max(0, request.SubTotal - totalDiscount),
                request.CustomerId,
                HasOtherPromotions: totalDiscount > 0m
            ), cancellationToken);

            if (voucherResult.IsValid && voucherResult.DiscountAmount > 0)
            {
                var effectiveVoucherDiscount = Math.Min(voucherResult.DiscountAmount, Math.Max(0, request.SubTotal - totalDiscount));
                totalDiscount += effectiveVoucherDiscount;
                appliedPromos.Add($"Voucher [{voucherResult.Code}] (-{effectiveVoucherDiscount:N0} đ)");
            }
        }

        totalDiscount = Math.Min(totalDiscount, request.SubTotal);
        decimal finalAmount = Math.Max(0, request.SubTotal - totalDiscount);

        return new EvaluateCartResponse(
            SubTotal: request.SubTotal,
            TotalDiscountAmount: totalDiscount,
            FinalAmount: finalAmount,
            AppliedPromotions: appliedPromos,
            GiftItems: giftItems,
            VoucherResult: voucherResult,
            ItemDiscounts: itemDiscounts,
            AppliedPromotionId: winningPromoId
        );
    }

    private static readonly Regex TiersJsonRegex = new(@"\[TIERS_JSON\]([\s\S]*?)\[/TIERS_JSON\]", RegexOptions.Compiled);
    private static readonly Regex ConfigJsonRegex = new(@"\[CONFIG_JSON\]([\s\S]*?)\[/CONFIG_JSON\]", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions TiersJsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static bool ParseAllowWithPromotions(string? description)
    {
        if (string.IsNullOrWhiteSpace(description) || !description.Contains("[CONFIG_JSON]", StringComparison.Ordinal))
            return true;

        var match = ConfigJsonRegex.Match(description);
        if (!match.Success || string.IsNullOrWhiteSpace(match.Groups[1].Value))
            return true;

        try
        {
            using var doc = JsonDocument.Parse(match.Groups[1].Value);
            if (doc.RootElement.TryGetProperty("allowWithPromotions", out var prop))
            {
                return prop.GetBoolean();
            }
        }
        catch
        {
            // fallback cho phép gộp mặc định
        }

        return true;
    }

    private sealed class PromotionTierMetadata
    {
        public decimal? MinOrderValue { get; set; }
        public string? DiscountType { get; set; }
        public decimal DiscountValue { get; set; }
        public decimal? MaxDiscount { get; set; }
        public decimal? RequiredQty { get; set; }
        public Guid? BuyProductId { get; set; }
        public string? BuyProductName { get; set; }
        public string? BuyUnit { get; set; }
        public Guid? GiftProductId { get; set; }
        public string? GiftProductName { get; set; }
        public string? GiftUnit { get; set; }
        public decimal? GiftQty { get; set; }
    }

    private static List<PromotionTierMetadata> ParseTiersFromDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description) || !description.Contains("[TIERS_JSON]", StringComparison.Ordinal))
            return new List<PromotionTierMetadata>();

        var match = TiersJsonRegex.Match(description);
        if (!match.Success || string.IsNullOrWhiteSpace(match.Groups[1].Value))
            return new List<PromotionTierMetadata>();

        try
        {
            return JsonSerializer.Deserialize<List<PromotionTierMetadata>>(match.Groups[1].Value, TiersJsonOptions)
                   ?? new List<PromotionTierMetadata>();
        }
        catch
        {
            return new List<PromotionTierMetadata>();
        }
    }

    private static decimal ResolveGiftUnitPrice(
        Guid giftProductId,
        Product? navProduct,
        List<CartItemDto> cartItems,
        Dictionary<Guid, decimal> giftPriceLookup)
    {
        if (navProduct != null && navProduct.SellingPrice > 0m)
            return navProduct.SellingPrice;

        if (giftProductId != Guid.Empty && giftPriceLookup.TryGetValue(giftProductId, out var dbPrice) && dbPrice > 0m)
            return dbPrice;

        var inCart = cartItems.FirstOrDefault(i => i.ProductId == giftProductId);
        if (inCart != null && inCart.UnitPrice > 0m)
            return inCart.UnitPrice;

        return 0m;
    }

    private static decimal GetCartItemBaseQty(CartItemDto item)
    {
        return item.Quantity * (item.ConversionRate > 0m ? item.ConversionRate : 1.00m);
    }

    private static (List<CartGiftItemDto> Gifts, decimal TotalGiftValue) EvaluateBuyXGetYPromotion(
        Promotion promo,
        List<CartItemDto> cartItems,
        Dictionary<Guid, decimal> giftPriceLookup,
        decimal? remainingBudget)
    {
        var gifts = new List<CartGiftItemDto>();
        decimal totalGiftValue = 0m;
        decimal? budgetLeft = remainingBudget;

        var tiers = ParseTiersFromDescription(promo.Description);
        var validGiftTiers = tiers
            .Where(t => (t.RequiredQty ?? 0m) > 0m && ((t.GiftProductId.HasValue && t.GiftProductId.Value != Guid.Empty) || !string.IsNullOrWhiteSpace(t.GiftProductName)))
            .ToList();

        if (validGiftTiers.Count > 0)
        {
            // Chọn bậc cao nhất đạt điều kiện mua trong danh sách bậc thang [TIERS_JSON]
            var qualifyingTier = validGiftTiers
                .Select(t =>
                {
                    decimal reqQty = Math.Max(1m, t.RequiredQty ?? 1m);
                    decimal boughtQty = (t.BuyProductId.HasValue && t.BuyProductId.Value != Guid.Empty)
                        ? cartItems
                            .Where(i => i.ProductId == t.BuyProductId.Value &&
                                        (string.IsNullOrWhiteSpace(t.BuyUnit) || string.Equals(i.Unit, t.BuyUnit, StringComparison.OrdinalIgnoreCase)))
                            .Sum(i => GetCartItemBaseQty(i))
                        : cartItems.Sum(i => GetCartItemBaseQty(i));
                    int mult = boughtQty >= reqQty ? (int)(boughtQty / reqQty) : 0;
                    return (Tier: t, ReqQty: reqQty, Multiple: mult);
                })
                .Where(x => x.Multiple > 0)
                .OrderByDescending(x => x.ReqQty)
                .ThenByDescending(x => (x.Tier.GiftQty ?? 1m) * x.Multiple)
                .FirstOrDefault();

            if (qualifyingTier.Multiple > 0 && qualifyingTier.Tier != null)
            {
                var t = qualifyingTier.Tier;
                var giftProdId = t.GiftProductId ?? Guid.Empty;
                var condMatch = promo.Conditions.FirstOrDefault(c => c.ProductRole == "GIFT" && c.ProductId == giftProdId);
                decimal unitPrice = ResolveGiftUnitPrice(giftProdId, condMatch?.Product, cartItems, giftPriceLookup);
                decimal rawGiftQty = qualifyingTier.Multiple * Math.Max(1m, t.GiftQty ?? 1m);

                if (budgetLeft.HasValue && unitPrice > 0m)
                {
                    decimal affordable = Math.Floor(budgetLeft.Value / unitPrice);
                    rawGiftQty = Math.Min(rawGiftQty, Math.Max(0m, affordable));
                }

                if (rawGiftQty > 0m)
                {
                    decimal eqVal = unitPrice * rawGiftQty;
                    var giftName = !string.IsNullOrWhiteSpace(t.GiftProductName)
                        ? t.GiftProductName!
                        : (condMatch?.Product?.Name ?? "Quà tặng khuyến mãi");
                    gifts.Add(new CartGiftItemDto(
                        giftProdId,
                        giftName,
                        rawGiftQty,
                        $"{promo.PromotionName} (Mua đủ tặng {rawGiftQty:0.##} {t.GiftUnit ?? "sản phẩm"})",
                        promo.Id,
                        t.GiftUnit,
                        eqVal
                    ));
                    totalGiftValue += eqVal;
                }
                return (gifts, totalGiftValue);
            }

            return (gifts, 0m);
        }

        // Nhánh chuẩn theo PromotionConditions (BUY + GIFT, hoặc fallback tổng SL giỏ nếu chỉ lưu GIFT)
        if (promo.Conditions.Count > 0)
        {
            var buyConditions = promo.Conditions.Where(c => c.ProductRole == "BUY").ToList();
            var giftConditions = promo.Conditions.Where(c => c.ProductRole == "GIFT").ToList();

            if (giftConditions.Count > 0)
            {
                bool isEligible = true;
                int minMultiple = int.MaxValue;

                if (buyConditions.Count > 0)
                {
                    foreach (var buyCond in buyConditions)
                    {
                        var matchingCartItems = cartItems.Where(i => i.ProductId == buyCond.ProductId).ToList();
                        var boughtQty = matchingCartItems.Sum(i => GetCartItemBaseQty(i));
                        var reqMin = Math.Max(1m, buyCond.MinQty);
                        if (matchingCartItems.Count == 0 || boughtQty < reqMin)
                        {
                            isEligible = false;
                            break;
                        }

                        int multiple = (int)(boughtQty / reqMin);
                        if (multiple < minMultiple) minMultiple = multiple;
                    }
                }
                else
                {
                    var totalCartQty = cartItems.Sum(i => GetCartItemBaseQty(i));
                    var reqMin = Math.Max(1m, giftConditions.Min(g => g.MinQty));
                    if (totalCartQty >= reqMin)
                    {
                        minMultiple = (int)(totalCartQty / reqMin);
                    }
                    else
                    {
                        isEligible = false;
                    }
                }

                if (isEligible && minMultiple > 0 && minMultiple != int.MaxValue)
                {
                    foreach (var giftCond in giftConditions)
                    {
                        decimal unitPrice = ResolveGiftUnitPrice(giftCond.ProductId, giftCond.Product, cartItems, giftPriceLookup);
                        decimal baseGiftQty = buyConditions.Count > 0 ? Math.Max(1m, giftCond.MinQty) : 1m;
                        decimal giftQty = minMultiple * baseGiftQty;

                        if (budgetLeft.HasValue && unitPrice > 0m)
                        {
                            decimal affordable = Math.Floor(budgetLeft.Value / unitPrice);
                            giftQty = Math.Min(giftQty, Math.Max(0m, affordable));
                        }

                        if (giftQty <= 0m) continue;

                        decimal eqVal = unitPrice * giftQty;
                        if (budgetLeft.HasValue)
                        {
                            budgetLeft = Math.Max(0m, budgetLeft.Value - eqVal);
                        }

                        string? unitName = null;
                        if (!string.IsNullOrWhiteSpace(giftCond.Note) && giftCond.Note.StartsWith("ĐVT:", StringComparison.OrdinalIgnoreCase))
                        {
                            unitName = giftCond.Note.Substring(4).Trim();
                        }

                        gifts.Add(new CartGiftItemDto(
                            giftCond.ProductId,
                            giftCond.Product?.Name ?? "Quà tặng khuyến mãi",
                            giftQty,
                            $"{promo.PromotionName} (Mua đủ tặng {giftQty:0.##} sản phẩm)",
                            promo.Id,
                            unitName ?? giftCond.Product?.BaseUnit,
                            eqVal
                        ));
                        totalGiftValue += eqVal;
                    }
                }
            }
        }

        return (gifts, totalGiftValue);
    }

    private static decimal EvaluateOrderDiscountPromotion(Promotion promo, decimal subTotal)
    {
        if (!string.IsNullOrWhiteSpace(promo.Description) && promo.Description.Contains("[TIERS_JSON]", StringComparison.Ordinal))
        {
            var match = TiersJsonRegex.Match(promo.Description);
            if (match.Success && !string.IsNullOrWhiteSpace(match.Groups[1].Value))
            {
                try
                {
                    var tiers = JsonSerializer.Deserialize<List<PromotionTierMetadata>>(match.Groups[1].Value, TiersJsonOptions);
                    if (tiers != null && tiers.Count > 0)
                    {
                        var qualifyingTiers = tiers
                            .Where(t => (t.MinOrderValue ?? 0m) <= subTotal && t.DiscountValue > 0m)
                            .Select(t =>
                            {
                                bool isPercent = string.Equals(t.DiscountType, "PERCENT", StringComparison.OrdinalIgnoreCase)
                                    || (string.IsNullOrWhiteSpace(t.DiscountType) && t.DiscountValue <= 100m);
                                decimal disc = isPercent
                                    ? Math.Round(subTotal * (t.DiscountValue / 100.00m), 2)
                                    : t.DiscountValue;
                                var maxCap = t.MaxDiscount ?? promo.MaxDiscountAmount;
                                if (maxCap.HasValue && maxCap.Value > 0m && disc > maxCap.Value)
                                {
                                    disc = maxCap.Value;
                                }
                                return (MinOrder: t.MinOrderValue ?? 0m, Discount: disc);
                            })
                            .OrderByDescending(x => x.MinOrder)
                            .ThenByDescending(x => x.Discount)
                            .ToList();

                        if (qualifyingTiers.Count > 0)
                        {
                            return qualifyingTiers[0].Discount;
                        }
                        return 0m;
                    }
                }
                catch
                {
                    // Fallback sang thông số gốc của Promotion nếu JSON lỗi
                }
            }
        }

        if (subTotal < promo.MinOrderValue)
            return 0m;

        if (promo.Type == "PERCENT" || (promo.Type == "ORDER_DISCOUNT" && promo.DiscountValue <= 100m))
        {
            decimal discount = Math.Round(subTotal * (promo.DiscountValue / 100.00m), 2);
            if (promo.MaxDiscountAmount.HasValue && discount > promo.MaxDiscountAmount.Value)
            {
                discount = promo.MaxDiscountAmount.Value;
            }
            return discount;
        }

        return promo.DiscountValue;
    }
    #endregion

    #region 3. Voucher Batches & Coupon Codes
    public async Task<PagedResult<VoucherBatchResponse>> GetPagedVoucherBatchesAsync(int pageIndex, int pageSize, string? searchTerm = null, string? status = null, CancellationToken cancellationToken = default)
    {
        var (batches, totalCount) = await _promotionRepository.GetPagedVoucherBatchesAsync(pageIndex, pageSize, searchTerm, status, cancellationToken);
        var dtos = batches.Select(MapToVoucherBatchResponse).ToList();
        return new PagedResult<VoucherBatchResponse> { Items = dtos, Page = pageIndex, PageSize = pageSize, TotalItems = totalCount };
    }

    public async Task<VoucherBatchResponse> GetVoucherBatchByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var batch = await _promotionRepository.GetByIdAsync(id, cancellationToken);
        if (batch == null || batch.Type != "VOUCHER")
            throw new NotFoundException("Đợt phát hành voucher", id);

        return MapToVoucherBatchResponse(batch);
    }

    public async Task<VoucherBatchResponse> CreateVoucherBatchAsync(VoucherBatchCreateRequest request, CancellationToken cancellationToken = default)
    {
        if (await _promotionRepository.ExistsByCodeAsync(request.BatchCode, cancellationToken))
            throw new BusinessException("DUPLICATE_CODE", $"Mã đợt voucher '{request.BatchCode}' đã tồn tại.");

        string? finalDescription = request.Description;
        if (request.AllowWithPromotions.HasValue)
        {
            var configJson = $"[CONFIG_JSON]{{\"allowWithPromotions\":{request.AllowWithPromotions.Value.ToString().ToLowerInvariant()}}}[/CONFIG_JSON]";
            finalDescription = string.IsNullOrWhiteSpace(finalDescription)
                ? configJson
                : $"{finalDescription.Trim()}\n{configJson}";
        }

        var batch = new Promotion
        {
            TenantId = _tenantContext.CurrentTenantId,
            PromotionCode = request.BatchCode.Trim().ToUpper(),
            PromotionName = request.Name.Trim(),
            Type = "VOUCHER",
            DiscountValue = request.DiscountValue,
            MinOrderValue = request.MinOrderValue,
            MaxDiscountAmount = request.MaxDiscountAmount,
            UsageLimit = request.TotalQuantity,
            UsedCount = 0,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            IsActive = true,
            Description = finalDescription,
            VoucherPrefix = request.Prefix.Trim().ToUpper(),
            TotalVoucherQuantity = 0
        };

        await _promotionRepository.AddAsync(batch, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Tự động sinh mã voucher ngay khi tạo đợt nếu có TotalQuantity
        if (request.TotalQuantity > 0)
        {
            await GenerateVoucherCodesAsync(batch.Id, new GenerateVoucherCodesRequest(request.TotalQuantity, request.Prefix), cancellationToken);
        }

        return MapToVoucherBatchResponse(batch);
    }

    public async Task<int> GenerateVoucherCodesAsync(Guid batchId, GenerateVoucherCodesRequest request, CancellationToken cancellationToken = default)
    {
        var batch = await _promotionRepository.GetByIdAsync(batchId, cancellationToken);
        if (batch == null || batch.Type != "VOUCHER")
            throw new NotFoundException("Đợt phát hành voucher", batchId);

        var prefix = string.IsNullOrWhiteSpace(request.Prefix)
            ? (string.IsNullOrWhiteSpace(batch.VoucherPrefix) ? "VC" : batch.VoucherPrefix)
            : request.Prefix.Trim().ToUpper();

        var newCodes = new List<VoucherCode>();
        var generatedStrings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        int targetQuantity = request.Quantity;
        int attempts = 0;
        int maxAttempts = targetQuantity * 10;

        while (newCodes.Count < targetQuantity && attempts < maxAttempts)
        {
            attempts++;
            var randomSuffix = GenerateRandomAlphanumeric(6);
            var fullCode = $"{prefix}-{randomSuffix}";

            if (generatedStrings.Add(fullCode))
            {
                if (!await _promotionRepository.VoucherCodeExistsAsync(fullCode, cancellationToken))
                {
                    newCodes.Add(new VoucherCode
                    {
                        TenantId = _tenantContext.CurrentTenantId,
                        PromotionId = batch.Id,
                        Code = fullCode,
                        Status = "UNUSED",
                    });
                }
            }
        }

        if (newCodes.Count > 0)
        {
            await _promotionRepository.AddVoucherCodesAsync(newCodes, cancellationToken);
            batch.TotalVoucherQuantity += newCodes.Count;
            _promotionRepository.Update(batch);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return newCodes.Count;
    }

    public async Task<PagedResult<VoucherCodeDto>> GetVoucherCodesAsync(Guid batchId, int pageIndex, int pageSize, string? status = null, CancellationToken cancellationToken = default)
    {
        var (codes, totalCount) = await _promotionRepository.GetPagedVoucherCodesAsync(batchId, pageIndex, pageSize, status, cancellationToken);
        var dtos = codes.Select(c => new VoucherCodeDto(
            c.Id,
            c.PromotionId,
            c.Code,
            c.Status,
            c.CustomerId,
            c.CustomerName,
            c.CustomerPhone,
            c.OrderId,
            c.OrderCode,
            c.UsedAt,
            c.CreatedAt
        )).ToList();

        return new PagedResult<VoucherCodeDto> { Items = dtos, Page = pageIndex, PageSize = pageSize, TotalItems = totalCount };
    }

    public async Task<ValidateVoucherResponse> ValidateVoucherAsync(ValidateVoucherRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
            return new ValidateVoucherResponse(false, "Mã voucher không được để trống", 0);

        var codeEntity = await _promotionRepository.GetVoucherCodeAsync(request.Code.Trim().ToUpper(), cancellationToken);
        if (codeEntity == null)
            return new ValidateVoucherResponse(false, "Mã voucher không tồn tại", 0);

        if (codeEntity.Status != "UNUSED")
            return new ValidateVoucherResponse(false, $"Mã voucher đã ở trạng thái '{codeEntity.Status}' (không khả dụng)", 0, codeEntity.Code);

        var promo = codeEntity.Promotion;
        if (promo == null || !promo.IsActive || promo.IsDeleted)
            return new ValidateVoucherResponse(false, "Chương trình ưu đãi của voucher này đã bị ngưng", 0, codeEntity.Code);

        var now = DateTime.UtcNow;
        var todayLocal = now.AddHours(7).Date;
        if (todayLocal < promo.StartDate.Date && now < promo.StartDate)
            return new ValidateVoucherResponse(false, $"Voucher chỉ có hiệu lực từ ngày {promo.StartDate:dd/MM/yyyy}", 0, codeEntity.Code);

        var endOfExpiry = promo.EndDate.Date.AddDays(1).AddTicks(-1);
        if (now > endOfExpiry && promo.EndDate > DateTime.MinValue)
            return new ValidateVoucherResponse(false, $"Voucher đã hết hạn từ ngày {promo.EndDate:dd/MM/yyyy}", 0, codeEntity.Code);

        if (codeEntity.CustomerId.HasValue)
        {
            if (string.IsNullOrWhiteSpace(request.CustomerId) ||
                !Guid.TryParse(request.CustomerId, out var parsedCustId) ||
                parsedCustId != codeEntity.CustomerId.Value)
            {
                return new ValidateVoucherResponse(false, "Voucher này được cấp riêng cho khách hàng khác", 0, codeEntity.Code);
            }
        }

        if (request.CartTotal < promo.MinOrderValue)
            return new ValidateVoucherResponse(false, $"Đơn hàng chưa đạt giá trị tối thiểu {promo.MinOrderValue:N0} đ để dùng voucher này", 0, codeEntity.Code);

        bool allowWithPromotions = ParseAllowWithPromotions(promo.Description);
        if (!allowWithPromotions && request.HasOtherPromotions)
        {
            return new ValidateVoucherResponse(
                IsValid: false,
                Message: "Voucher này không áp dụng đồng thời với chương trình khuyến mãi khác",
                DiscountAmount: 0,
                Code: codeEntity.Code,
                PromotionId: promo.Id,
                PromotionName: promo.PromotionName,
                DiscountType: promo.DiscountValue <= 100 && promo.MaxDiscountAmount.HasValue ? "PERCENT" : "FIXED_AMOUNT",
                DiscountValue: promo.DiscountValue,
                AllowWithPromotions: false
            );
        }

        // Tính toán số tiền chiết khấu
        decimal discount = 0;
        string discountType = promo.DiscountValue <= 100 && promo.DiscountValue > 0 && promo.MaxDiscountAmount.HasValue
            ? "PERCENT"
            : "FIXED_AMOUNT";

        if (discountType == "PERCENT")
        {
            discount = Math.Round(request.CartTotal * (promo.DiscountValue / 100.00m), 2);
            if (promo.MaxDiscountAmount.HasValue && discount > promo.MaxDiscountAmount.Value)
            {
                discount = promo.MaxDiscountAmount.Value;
            }
        }
        else
        {
            discount = promo.DiscountValue;
        }

        discount = Math.Min(discount, request.CartTotal);

        return new ValidateVoucherResponse(
            IsValid: true,
            Message: "Mã voucher hợp lệ",
            DiscountAmount: discount,
            Code: codeEntity.Code,
            PromotionId: promo.Id,
            PromotionName: promo.PromotionName,
            DiscountType: discountType,
            DiscountValue: promo.DiscountValue,
            AllowWithPromotions: allowWithPromotions
        );
    }

    public async Task<RedeemVoucherResponse> RedeemVoucherAsync(RedeemVoucherRequest request, CancellationToken cancellationToken = default)
    {
        var success = await _promotionRepository.RedeemVoucherCodeAtomicAsync(
            request.Code.Trim().ToUpper(),
            request.OrderId,
            request.OrderCode,
            request.CustomerId,
            request.CustomerName,
            request.CustomerPhone,
            cancellationToken
        );

        if (!success)
            return new RedeemVoucherResponse(false, "Không thể đổi mã voucher (mã không tồn tại hoặc đã được sử dụng bởi giao dịch khác)", request.Code);

        var voucher = await _promotionRepository.GetVoucherCodeAsync(request.Code.Trim().ToUpper(), cancellationToken);
        if (voucher?.Promotion != null)
        {
            voucher.Promotion.UsedCount += 1;
            _promotionRepository.Update(voucher.Promotion);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return new RedeemVoucherResponse(true, "Đổi mã voucher thành công", request.Code, DateTime.UtcNow);
    }
    #endregion

    #region 3. Seeder Dữ Liệu Mẫu Cho Kiểm Thử API
    public async Task<int> SeedSampleDataAsync(CancellationToken cancellationToken = default)
    {
        var tenantId = string.IsNullOrWhiteSpace(_tenantContext.CurrentTenantId) ? "tenant-grocery-001" : _tenantContext.CurrentTenantId;
        var addedCount = 0;
        var now = DateTime.UtcNow;

        // 1. Giảm 10% đơn từ 200k, tối đa 50k
        if (!await _promotionRepository.ExistsByCodeAsync("KM-GIAM10-TOIDA50K", cancellationToken))
        {
            var p1 = new Promotion
            {
                TenantId = tenantId,
                PromotionCode = "KM-GIAM10-TOIDA50K",
                PromotionName = "Giảm 10% cho đơn từ 200.000đ (Tối đa 50.000đ)",
                Type = "ORDER_DISCOUNT",
                DiscountValue = 10.00m,
                MinOrderValue = 200000.00m,
                MaxDiscountAmount = 50000.00m,
                UsageLimit = 500,
                UsedCount = 15,
                BudgetLimit = 10000000.00m,
                TotalDiscountGiven = 750000.00m,
                StartDate = now.AddDays(-30),
                EndDate = now.AddDays(180),
                IsActive = true,
                Description = "Chương trình tri ân khách hàng: Giảm 10% cho hóa đơn từ 200.000đ, trần chiết khấu 50.000đ.",
                TargetBranches = "ALL"
            };
            await _promotionRepository.AddAsync(p1, cancellationToken);
            addedCount++;
        }

        // 2. Giảm 30.000đ đơn từ 150k
        if (!await _promotionRepository.ExistsByCodeAsync("KM-GIAM30K-DON150K", cancellationToken))
        {
            var p2 = new Promotion
            {
                TenantId = tenantId,
                PromotionCode = "KM-GIAM30K-DON150K",
                PromotionName = "Giảm thẳng 30.000đ cho đơn từ 150.000đ",
                Type = "FIXED_AMOUNT",
                DiscountValue = 30000.00m,
                MinOrderValue = 150000.00m,
                MaxDiscountAmount = null,
                UsageLimit = 200,
                UsedCount = 28,
                BudgetLimit = 6000000.00m,
                TotalDiscountGiven = 840000.00m,
                StartDate = now.AddDays(-15),
                EndDate = now.AddDays(90),
                IsActive = true,
                Description = "Ưu đãi trực tiếp: Trừ ngay 30.000 VNĐ cho hóa đơn mua sắm từ 150.000 VNĐ.",
                TargetBranches = "ALL"
            };
            await _promotionRepository.AddAsync(p2, cancellationToken);
            addedCount++;
        }

        // 3. Mua 3 bánh Chocopie tặng 1 Coca-Cola
        if (!await _promotionRepository.ExistsByCodeAsync("KM-MUA3TANG1-COCA", cancellationToken))
        {
            var p3 = new Promotion
            {
                TenantId = tenantId,
                PromotionCode = "KM-MUA3TANG1-COCA",
                PromotionName = "Mua 3 Bánh Chocopie tặng 1 Coca-Cola lon",
                Type = "BUY_X_GET_Y",
                DiscountValue = 0.00m,
                MinOrderValue = 0.00m,
                UsageLimit = 100,
                UsedCount = 8,
                StartDate = now.AddDays(-10),
                EndDate = now.AddDays(60),
                IsActive = true,
                Description = "Ưu đãi giải khát: Khi mua đủ 3 hộp Bánh Chocopie Orion, tặng ngay 1 lốc Coca-Cola lon 320ml.",
                TargetBranches = "ALL"
            };

            p3.Conditions.Add(new PromotionCondition
            {
                TenantId = tenantId,
                PromotionId = p3.Id,
                ProductId = Guid.Parse("aaaaaaaa-1111-1111-1111-111111111111"),
                ProductRole = "BUY",
                MinQty = 3.00m,
                Note = "Mua tối thiểu 3 hộp Bánh Chocopie Orion"
            });

            p3.Conditions.Add(new PromotionCondition
            {
                TenantId = tenantId,
                PromotionId = p3.Id,
                ProductId = Guid.Parse("aaaaaaaa-3333-3333-3333-333333333333"),
                ProductRole = "GIFT",
                MinQty = 1.00m,
                Note = "Tặng 1 lốc nước ngọt Coca-Cola 320ml"
            });

            await _promotionRepository.AddAsync(p3, cancellationToken);
            addedCount++;
        }

        // 4. Đợt phát hành Voucher VIP 50k
        if (!await _promotionRepository.ExistsByCodeAsync("VOUCHER-VIP50K", cancellationToken))
        {
            var p4 = new Promotion
            {
                TenantId = tenantId,
                PromotionCode = "VOUCHER-VIP50K",
                PromotionName = "Chiến dịch Voucher VIP Giảm 50.000đ",
                Type = "VOUCHER",
                DiscountValue = 50000.00m,
                MinOrderValue = 250000.00m,
                MaxDiscountAmount = null,
                UsageLimit = 10,
                UsedCount = 1,
                BudgetLimit = 500000.00m,
                TotalDiscountGiven = 50000.00m,
                StartDate = now.AddDays(-20),
                EndDate = now.AddDays(120),
                IsActive = true,
                Description = "Mã voucher tri ân giảm 50.000đ cho khách hàng VIP đạt cấp độ Vàng/Kim Cương.",
                TargetBranches = "ALL",
                VoucherPrefix = "VIP50K",
                TotalVoucherQuantity = 4
            };

            p4.VoucherCodes.Add(new VoucherCode
            {
                TenantId = tenantId,
                PromotionId = p4.Id,
                Code = "VIP50K-TEST01",
                Status = "UNUSED"
            });

            p4.VoucherCodes.Add(new VoucherCode
            {
                TenantId = tenantId,
                PromotionId = p4.Id,
                Code = "VIP50K-TEST02",
                Status = "UNUSED"
            });

            p4.VoucherCodes.Add(new VoucherCode
            {
                TenantId = tenantId,
                PromotionId = p4.Id,
                Code = "VIP50K-TEST03",
                Status = "UNUSED"
            });

            p4.VoucherCodes.Add(new VoucherCode
            {
                TenantId = tenantId,
                PromotionId = p4.Id,
                Code = "VIP50K-DA-DUNG",
                Status = "USED",
                OrderCode = "ORD-20261015-001",
                OrderId = Guid.Parse("f0000001-1111-1111-1111-111111111111"),
                UsedAt = now.AddDays(-2)
            });

            await _promotionRepository.AddAsync(p4, cancellationToken);
            addedCount++;
        }

        // 5. Đợt phát hành Voucher Giảm 20% Cuối Tuần
        if (!await _promotionRepository.ExistsByCodeAsync("VOUCHER-CUOITUAN20", cancellationToken))
        {
            var p5 = new Promotion
            {
                TenantId = tenantId,
                PromotionCode = "VOUCHER-CUOITUAN20",
                PromotionName = "Voucher Giảm 20% Mua Sắm Cuối Tuần (Tối đa 40.000đ)",
                Type = "VOUCHER",
                DiscountValue = 20.00m,
                MinOrderValue = 100000.00m,
                MaxDiscountAmount = 40000.00m,
                UsageLimit = 20,
                UsedCount = 0,
                StartDate = now.AddDays(-5),
                EndDate = now.AddDays(90),
                IsActive = true,
                Description = "Mã voucher giảm 20% áp dụng cho khách hàng thanh toán tại quầy POS vào cuối tuần.",
                TargetBranches = "ALL",
                VoucherPrefix = "WEEKEND",
                TotalVoucherQuantity = 3
            };

            p5.VoucherCodes.Add(new VoucherCode
            {
                TenantId = tenantId,
                PromotionId = p5.Id,
                Code = "WEEKEND-20PCT01",
                Status = "UNUSED"
            });

            p5.VoucherCodes.Add(new VoucherCode
            {
                TenantId = tenantId,
                PromotionId = p5.Id,
                Code = "WEEKEND-20PCT02",
                Status = "UNUSED"
            });

            p5.VoucherCodes.Add(new VoucherCode
            {
                TenantId = tenantId,
                PromotionId = p5.Id,
                Code = "WEEKEND-20PCT03",
                Status = "UNUSED"
            });

            await _promotionRepository.AddAsync(p5, cancellationToken);
            addedCount++;
        }

        // 6. Khuyến mãi đã hết hạn (Phục vụ test lọc trạng thái EXPIRED)
        if (!await _promotionRepository.ExistsByCodeAsync("KM-TET2025-HETHAN", cancellationToken))
        {
            var p6 = new Promotion
            {
                TenantId = tenantId,
                PromotionCode = "KM-TET2025-HETHAN",
                PromotionName = "Khuyến mãi Tết 2025 (Đã hết hạn)",
                Type = "ORDER_DISCOUNT",
                DiscountValue = 15.00m,
                MinOrderValue = 100000.00m,
                MaxDiscountAmount = 30000.00m,
                UsageLimit = 100,
                UsedCount = 100,
                StartDate = now.AddDays(-400),
                EndDate = now.AddDays(-340),
                IsActive = false,
                Description = "Chương trình khuyến mãi mùa Tết năm cũ, đã hết hạn để test bộ lọc trạng thái.",
                TargetBranches = "ALL"
            };
            await _promotionRepository.AddAsync(p6, cancellationToken);
            addedCount++;
        }

        if (addedCount > 0)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return addedCount;
    }
    #endregion

    #region Helpers & Mappers
    private static PromotionResponse MapToResponse(Promotion p)
    {
        return new PromotionResponse(
            p.Id,
            p.PromotionCode,
            p.PromotionName,
            p.Type,
            p.DiscountValue,
            p.MinOrderValue,
            p.MaxDiscountAmount,
            p.UsageLimit,
            p.UsedCount,
            p.StartDate,
            p.EndDate,
            p.IsActive,
            p.Description,
            p.TargetBranches,
            p.BudgetLimit,
            p.TotalDiscountGiven,
            p.CreatedAt,
            p.Conditions.Select(c => new PromotionConditionDto(
                c.Id,
                c.ProductId,
                c.Product?.Name,
                c.ProductRole,
                c.MinQty,
                c.DiscountValue,
                c.Note
            )).ToList()
        );
    }

    private static VoucherBatchResponse MapToVoucherBatchResponse(Promotion p)
    {
        var now = DateTime.UtcNow;
        string status = !p.IsActive ? "PAUSED"
            : now > p.EndDate ? "EXPIRED"
            : (p.UsageLimit.HasValue && p.UsedCount >= p.UsageLimit.Value) ? "DEPLETED"
            : "ACTIVE";

        string discountType = p.DiscountValue <= 100 && p.MaxDiscountAmount.HasValue ? "PERCENT" : "FIXED_AMOUNT";
        bool allowWithPromotions = ParseAllowWithPromotions(p.Description);

        return new VoucherBatchResponse(
            p.Id,
            p.PromotionCode,
            p.PromotionName,
            discountType,
            p.DiscountValue,
            p.MinOrderValue,
            p.MaxDiscountAmount,
            p.TotalVoucherQuantity > 0 ? p.TotalVoucherQuantity : (p.UsageLimit ?? 0),
            p.UsedCount,
            p.StartDate,
            p.EndDate,
            status,
            p.VoucherPrefix ?? "VC",
            p.Description,
            p.CreatedAt,
            allowWithPromotions
        );
    }

    private static string GenerateRandomAlphanumeric(int length)
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        Span<byte> randomBytes = stackalloc byte[length];
        RandomNumberGenerator.Fill(randomBytes);

        var result = new char[length];
        for (int i = 0; i < length; i++)
        {
            result[i] = chars[randomBytes[i] % chars.Length];
        }
        return new string(result);
    }
    #endregion
}
