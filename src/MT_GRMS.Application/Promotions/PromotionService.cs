using System.Security.Cryptography;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Promotions.Dtos;
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
        var activePromos = await _promotionRepository.GetActivePromotionsAsync(now, request.BranchId?.ToString(), cancellationToken);

        decimal totalDiscount = 0.00m;
        var appliedPromos = new List<string>();
        var giftItems = new List<CartGiftItemDto>();

        foreach (var promo in activePromos)
        {
            // Kiểm tra ngân sách khuyến mại nếu có
            if (promo.BudgetLimit.HasValue && promo.TotalDiscountGiven >= promo.BudgetLimit.Value)
                continue;

            // Kiểm tra giới hạn số lượt dùng nếu có
            if (promo.UsageLimit.HasValue && promo.UsedCount >= promo.UsageLimit.Value)
                continue;

            // 1. ORDER_DISCOUNT hoặc PERCENT / FIXED_AMOUNT
            if (promo.Type is "ORDER_DISCOUNT" or "PERCENT" or "FIXED_AMOUNT")
            {
                if (request.SubTotal >= promo.MinOrderValue)
                {
                    decimal discount = 0;
                    if (promo.Type == "PERCENT" || (promo.Type == "ORDER_DISCOUNT" && promo.DiscountValue <= 100))
                    {
                        discount = Math.Round(request.SubTotal * (promo.DiscountValue / 100.00m), 2);
                        if (promo.MaxDiscountAmount.HasValue && discount > promo.MaxDiscountAmount.Value)
                        {
                            discount = promo.MaxDiscountAmount.Value;
                        }
                    }
                    else
                    {
                        discount = promo.DiscountValue;
                    }

                    discount = Math.Min(discount, Math.Max(0, request.SubTotal - totalDiscount));
                    if (discount > 0)
                    {
                        totalDiscount += discount;
                        appliedPromos.Add($"{promo.PromotionName} (-{discount:N0} đ)");
                    }
                }
            }
            // 2. BUY_X_GET_Y (Mua X tặng Y)
            else if (promo.Type == "BUY_X_GET_Y" && promo.Conditions.Count > 0)
            {
                var buyConditions = promo.Conditions.Where(c => c.ProductRole == "BUY").ToList();
                var giftConditions = promo.Conditions.Where(c => c.ProductRole == "GIFT").ToList();

                if (buyConditions.Count > 0 && giftConditions.Count > 0)
                {
                    bool isEligible = true;
                    int minMultiple = int.MaxValue;

                    foreach (var buyCond in buyConditions)
                    {
                        var matchingCartItem = request.Items.FirstOrDefault(i => i.ProductId == buyCond.ProductId);
                        if (matchingCartItem == null || matchingCartItem.Quantity < buyCond.MinQty)
                        {
                            isEligible = false;
                            break;
                        }

                        int multiple = (int)(matchingCartItem.Quantity / buyCond.MinQty);
                        if (multiple < minMultiple) minMultiple = multiple;
                    }

                    if (isEligible && minMultiple > 0 && minMultiple != int.MaxValue)
                    {
                        foreach (var giftCond in giftConditions)
                        {
                            decimal giftQty = minMultiple * giftCond.MinQty;
                            giftItems.Add(new CartGiftItemDto(
                                giftCond.ProductId,
                                giftCond.Product?.Name ?? "Quà tặng khuyến mãi",
                                giftQty,
                                $"{promo.PromotionName} (Mua đủ tặng {giftQty} sản phẩm)"
                            ));
                        }
                        appliedPromos.Add($"{promo.PromotionName} (Đủ điều kiện nhận quà tặng)");
                    }
                }
            }
        }

        // 3. Thẩm định Voucher Coupon nếu có nhập mã
        ValidateVoucherResponse? voucherResult = null;
        if (!string.IsNullOrWhiteSpace(request.CouponCode))
        {
            voucherResult = await ValidateVoucherAsync(new ValidateVoucherRequest(
                request.CouponCode,
                Math.Max(0, request.SubTotal - totalDiscount),
                request.CustomerId
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
            VoucherResult: voucherResult
        );
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
            Description = request.Description,
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
        if (now < promo.StartDate)
            return new ValidateVoucherResponse(false, $"Voucher chỉ có hiệu lực từ ngày {promo.StartDate:dd/MM/yyyy}", 0, codeEntity.Code);

        if (now > promo.EndDate)
            return new ValidateVoucherResponse(false, $"Voucher đã hết hạn từ ngày {promo.EndDate:dd/MM/yyyy}", 0, codeEntity.Code);

        if (request.CartTotal < promo.MinOrderValue)
            return new ValidateVoucherResponse(false, $"Đơn hàng chưa đạt giá trị tối thiểu {promo.MinOrderValue:N0} đ để dùng voucher này", 0, codeEntity.Code);

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
            DiscountValue: promo.DiscountValue
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
            p.CreatedAt
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
