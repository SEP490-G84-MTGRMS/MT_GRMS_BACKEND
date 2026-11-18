using MT_GRMS.Application.Admin.Dtos;

namespace MT_GRMS.Application.Admin.Services;

public interface IPlatformSettingService
{
    Task<PlatformSettingDto> GetSettingsAsync(CancellationToken cancellationToken = default);
    Task<PlatformSettingDto> UpdateSettingsAsync(UpdatePlatformSettingRequest request, string? updatedBy, CancellationToken cancellationToken = default);
    Task<PlatformPublicConfigDto> GetPublicConfigAsync(CancellationToken cancellationToken = default);
}
