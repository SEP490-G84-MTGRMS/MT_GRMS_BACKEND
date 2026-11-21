using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MT_GRMS.Application.Admin.Dtos;
using MT_GRMS.Application.Admin.Services;
using MT_GRMS.Application.Common;

namespace MT_GRMS.Api.Controllers;

[ApiController]
[Route("api/platform")]
[AllowAnonymous]
public class PlatformPublicConfigController : BaseApiController
{
    private readonly IPlatformSettingService _settingService;

    public PlatformPublicConfigController(IPlatformSettingService settingService)
    {
        _settingService = settingService;
    }

    /// <summary>
    /// Lấy cấu hình công khai an toàn của hệ thống (CSKH, hotline, VietQR, bảo trì)
    /// </summary>
    [HttpGet("public-config")]
    public async Task<IActionResult> GetPublicConfig(CancellationToken cancellationToken)
    {
        var config = await _settingService.GetPublicConfigAsync(cancellationToken);
        return Ok(ApiResponse<PlatformPublicConfigDto>.Ok(config));
    }
}
