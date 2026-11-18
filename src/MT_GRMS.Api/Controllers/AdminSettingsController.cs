using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MT_GRMS.Application.Admin.Dtos;
using MT_GRMS.Application.Admin.Services;
using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Constants;

namespace MT_GRMS.Api.Controllers;

[ApiController]
[Route("api/admin/settings")]
[Authorize(Roles = RoleConstants.AdminOnly)]
public class AdminSettingsController : BaseApiController
{
    private readonly IPlatformSettingService _settingService;
    private readonly IValidator<UpdatePlatformSettingRequest> _validator;

    public AdminSettingsController(
        IPlatformSettingService settingService,
        IValidator<UpdatePlatformSettingRequest> validator)
    {
        _settingService = settingService;
        _validator = validator;
    }

    /// <summary>
    /// Lấy toàn bộ thông tin Cấu hình Nền tảng Toàn cục SaaS (REQ-FE-ADMIN-05)
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetSettings(CancellationToken cancellationToken)
    {
        var settings = await _settingService.GetSettingsAsync(cancellationToken);
        return Ok(ApiResponse<PlatformSettingDto>.Ok(settings));
    }

    /// <summary>
    /// Cập nhật Cấu hình Nền tảng Toàn cục SaaS (REQ-FE-ADMIN-05)
    /// </summary>
    [HttpPut]
    public async Task<IActionResult> UpdateSettings([FromBody] UpdatePlatformSettingRequest request, CancellationToken cancellationToken)
    {
        var validationResult = await _validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            var errors = string.Join("; ", validationResult.Errors.Select(e => e.ErrorMessage));
            return BadRequest(ApiResponse<PlatformSettingDto>.Fail("VALIDATION_ERROR", errors));
        }

        var username = User.Identity?.Name ?? User.FindFirst("unique_name")?.Value ?? "Admin";
        var updated = await _settingService.UpdateSettingsAsync(request, username, cancellationToken);
        return Ok(ApiResponse<PlatformSettingDto>.Ok(updated, "Cập nhật cấu hình nền tảng thành công."));
    }
}
