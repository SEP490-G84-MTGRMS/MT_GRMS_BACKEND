using MT_GRMS.Application.Promotions.Dtos;

namespace MT_GRMS.Application.Promotions;

public interface ILoyaltyService
{
    Task<LoyaltyConfigDto> GetConfigAsync(CancellationToken cancellationToken = default);
    Task<LoyaltyConfigDto> UpdateConfigAsync(UpdateLoyaltyConfigRequest request, CancellationToken cancellationToken = default);
}

