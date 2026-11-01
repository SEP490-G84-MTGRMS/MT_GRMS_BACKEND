using Microsoft.AspNetCore.Mvc;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Promotions;
using MT_GRMS.Application.Promotions.Dtos;

namespace MT_GRMS.Api.Controllers;

[ApiController]
[Route("api/v1/promotions")]
public class PromotionsController : BaseApiController
{
    private readonly IPromotionService _promotionService;

    public PromotionsController(IPromotionService promotionService)
    {
        _promotionService = promotionService;
    }

    /// <summary>
    /// Lấy danh sách chương trình khuyến mại có phân trang và bộ lọc (REQ-PV01)
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetPromotions([FromQuery] PromotionFilterQuery query, CancellationToken cancellationToken)
    {
        var result = await _promotionService.GetPagedPromotionsAsync(query, cancellationToken);
        return Ok(ApiResponse<PagedResult<PromotionResponse>>.Ok(result));
    }

    /// <summary>
    /// Lấy chi tiết chương trình khuyến mại theo ID kèm điều kiện sản phẩm (REQ-PV01, REQ-PV02)
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _promotionService.GetPromotionByIdAsync(id, cancellationToken);
        return Ok(ApiResponse<PromotionResponse>.Ok(result));
    }

    /// <summary>
    /// Tạo mới chương trình khuyến mại (REQ-PV01)
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] PromotionCreateRequest request, CancellationToken cancellationToken)
    {
        var result = await _promotionService.CreatePromotionAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, ApiResponse<PromotionResponse>.Ok(result, "Tạo chương trình khuyến mại thành công"));
    }

    /// <summary>
    /// Cập nhật chương trình khuyến mại (REQ-PV01)
    /// </summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] PromotionUpdateRequest request, CancellationToken cancellationToken)
    {
        var result = await _promotionService.UpdatePromotionAsync(id, request, cancellationToken);
        return Ok(ApiResponse<PromotionResponse>.Ok(result, "Cập nhật chương trình khuyến mại thành công"));
    }

    /// <summary>
    /// Bật/Tắt trạng thái kích hoạt chương trình khuyến mại (REQ-PV01)
    /// </summary>
    [HttpPatch("{id:guid}/status")]
    public async Task<IActionResult> ToggleStatus(Guid id, CancellationToken cancellationToken)
    {
        var isActive = await _promotionService.TogglePromotionStatusAsync(id, cancellationToken);
        return Ok(ApiResponse<bool>.Ok(isActive, isActive ? "Đã kích hoạt khuyến mại" : "Đã tạm dừng khuyến mại"));
    }

    /// <summary>
    /// Xóa mềm chương trình khuyến mại (REQ-PV01)
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _promotionService.DeletePromotionAsync(id, cancellationToken);
        return Ok(ApiResponse<bool>.Ok(true, "Xóa khuyến mại thành công"));
    }

    /// <summary>
    /// Thẩm định và tính toán tự động chiết khấu / quà tặng giỏ hàng bán lẻ POS (REQ-PV03)
    /// </summary>
    [HttpPost("evaluate-cart")]
    public async Task<IActionResult> EvaluateCart([FromBody] EvaluateCartRequest request, CancellationToken cancellationToken)
    {
        var result = await _promotionService.EvaluateCartAsync(request, cancellationToken);
        return Ok(ApiResponse<EvaluateCartResponse>.Ok(result));
    }
}

