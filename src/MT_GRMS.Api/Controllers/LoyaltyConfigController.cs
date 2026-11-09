using Microsoft.AspNetCore.Mvc;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Promotions;
using MT_GRMS.Application.Promotions.Dtos;

namespace MT_GRMS.Api.Controllers;

[ApiController]
[Route("api/loyalty-config")]
public class LoyaltyConfigController : BaseApiController
{
    private readonly ILoyaltyService _loyaltyService;

    public LoyaltyConfigController(ILoyaltyService loyaltyService)
    {
        _loyaltyService = loyaltyService;
    }

    /// <summary>
    /// Lấy cấu hình hệ thống tích điểm và tiêu điểm thành viên (REQ-LOYALTY-01)
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetConfig(CancellationToken cancellationToken)
    {
        var config = await _loyaltyService.GetConfigAsync(cancellationToken);
        return Ok(ApiResponse<LoyaltyConfigDto>.Ok(config));
    }

    /// <summary>
    /// Cập nhật cấu hình tích điểm và tiêu điểm thành viên (REQ-LOYALTY-01)
    /// </summary>
    [HttpPut]
    public async Task<IActionResult> UpdateConfig([FromBody] UpdateLoyaltyConfigRequest request, CancellationToken cancellationToken)
    {
        var updated = await _loyaltyService.UpdateConfigAsync(request, cancellationToken);
        return Ok(ApiResponse<LoyaltyConfigDto>.Ok(updated, "Cập nhật cấu hình tích điểm thành công"));
    }
}

