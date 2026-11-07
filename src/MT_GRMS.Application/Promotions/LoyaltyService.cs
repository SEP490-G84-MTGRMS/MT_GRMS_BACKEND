using MT_GRMS.Application.Common;
using MT_GRMS.Application.Promotions.Dtos;
using MT_GRMS.Domain.Entities.Sales;

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
        var tenantId = _tenantContext.CurrentTenantId;
        var setting = new LoyaltySetting
        {
            TenantId = tenantId,
            IsEnabled = request.IsEnabled,
            EarningRateAmount = request.EarningRateAmount <= 0 ? 10000m : request.EarningRateAmount,
            EarningPoints = request.EarningPoints <= 0 ? 1 : request.EarningPoints,
            RedemptionRatePoints = request.RedemptionRatePoints <= 0 ? 1 : request.RedemptionRatePoints,
            RedemptionRateAmount = request.RedemptionRateAmount <= 0 ? 1000m : request.RedemptionRateAmount,
            MinRedemptionPoints = request.MinRedemptionPoints < 0 ? 0 : request.MinRedemptionPoints,
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

