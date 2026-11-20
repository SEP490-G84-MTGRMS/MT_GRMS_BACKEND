using FluentValidation;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Promotions.Dtos;
using MT_GRMS.Application.Promotions.Validators;
using MT_GRMS.Domain.Entities.Sales;
using MT_GRMS.Domain.Exceptions;

namespace MT_GRMS.Application.Promotions;

public class LoyaltyService : ILoyaltyService
{
    private readonly IPromotionRepository _promotionRepository;
    private readonly ITenantContext _tenantContext;

    public LoyaltyService(IPromotionRepository promotionRepository, ITenantContext tenantContext)
    {
        _promotionRepository = promotionRepository;
        _tenantContext = tenantContext;
    }

    public async Task<LoyaltyConfigDto> GetConfigAsync(CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.CurrentTenantId;
        var setting = await _promotionRepository.GetLoyaltySettingAsync(tenantId, cancellationToken);

        if (setting == null)
        {
            return new LoyaltyConfigDto(
                IsEnabled: true,
                EarningRateAmount: 10000m,
                EarningPoints: 1,
                RedemptionRatePoints: 1,
                RedemptionRateAmount: 1000m,
                MinRedemptionPoints: 20,
                AllowPointsWithPromotions: true
            );
        }

        return new LoyaltyConfigDto(
            setting.IsEnabled,
            setting.EarningRateAmount,
            setting.EarningPoints,
            setting.RedemptionRatePoints,
            setting.RedemptionRateAmount,
            setting.MinRedemptionPoints,
            setting.AllowPointsWithPromotions
        );
    }

    public async Task<LoyaltyConfigDto> UpdateConfigAsync(UpdateLoyaltyConfigRequest request, CancellationToken cancellationToken = default)
    {
        var validator = new UpdateLoyaltyConfigRequestValidator();
        var validationResult = await validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            throw new BusinessException("VALIDATION_ERROR", validationResult.Errors[0].ErrorMessage);
        }

        var tenantId = _tenantContext.CurrentTenantId;
        var setting = new LoyaltySetting
        {
            TenantId = tenantId,
            IsEnabled = request.IsEnabled,
            EarningRateAmount = request.EarningRateAmount,
            EarningPoints = request.EarningPoints,
            RedemptionRatePoints = request.RedemptionRatePoints,
            RedemptionRateAmount = request.RedemptionRateAmount,
            MinRedemptionPoints = request.MinRedemptionPoints,
            AllowPointsWithPromotions = request.AllowPointsWithPromotions
        };

        await _promotionRepository.SaveLoyaltySettingAsync(setting, cancellationToken);

        return new LoyaltyConfigDto(
            setting.IsEnabled,
            setting.EarningRateAmount,
            setting.EarningPoints,
            setting.RedemptionRatePoints,
            setting.RedemptionRateAmount,
            setting.MinRedemptionPoints,
            setting.AllowPointsWithPromotions
        );
    }
}

