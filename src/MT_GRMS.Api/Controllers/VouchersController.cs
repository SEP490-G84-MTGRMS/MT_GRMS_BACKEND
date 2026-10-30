using Microsoft.AspNetCore.Mvc;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Promotions;
using MT_GRMS.Application.Promotions.Dtos;

namespace MT_GRMS.Api.Controllers;

[ApiController]
[Route("api/v1/vouchers")]
public class VouchersController : BaseApiController
{
    private readonly IPromotionService _promotionService;

    public VouchersController(IPromotionService promotionService)
    {
        _promotionService = promotionService;
    }

    /// <summary>
    /// Lấy danh sách các đợt phát hành voucher có phân trang và lọc (REQ-PV04)
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetBatches(
        [FromQuery] int pageIndex = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? searchTerm = null,
        [FromQuery] string? status = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _promotionService.GetPagedVoucherBatchesAsync(pageIndex, pageSize, searchTerm, status, cancellationToken);
        return Ok(ApiResponse<PagedResult<VoucherBatchResponse>>.Ok(result));
    }

    /// <summary>
    /// Lấy chi tiết đợt phát hành voucher (REQ-PV04)
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetBatchById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _promotionService.GetVoucherBatchByIdAsync(id, cancellationToken);
        return Ok(ApiResponse<VoucherBatchResponse>.Ok(result));
    }

    /// <summary>
    /// Tạo mới đợt phát hành voucher & tự động sinh mã (REQ-PV04)
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> CreateBatch([FromBody] VoucherBatchCreateRequest request, CancellationToken cancellationToken)
    {
        var result = await _promotionService.CreateVoucherBatchAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetBatchById), new { id = result.Id }, ApiResponse<VoucherBatchResponse>.Ok(result, "Tạo đợt phát hành voucher thành công"));
    }

    /// <summary>
    /// Sinh bổ sung mã voucher cho đợt (REQ-PV04)
    /// </summary>
    [HttpPost("{id:guid}/generate-codes")]
    public async Task<IActionResult> GenerateCodes(Guid id, [FromBody] GenerateVoucherCodesRequest request, CancellationToken cancellationToken)
    {
        var generatedCount = await _promotionService.GenerateVoucherCodesAsync(id, request, cancellationToken);
        return Ok(ApiResponse<int>.Ok(generatedCount, $"Đã sinh thành công {generatedCount} mã voucher"));
    }

    /// <summary>
    /// Lấy danh sách mã voucher con của một đợt phát hành (REQ-PV04)
    /// </summary>
    [HttpGet("{id:guid}/codes")]
    public async Task<IActionResult> GetCodes(
        Guid id,
        [FromQuery] int pageIndex = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? status = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _promotionService.GetVoucherCodesAsync(id, pageIndex, pageSize, status, cancellationToken);
        return Ok(ApiResponse<PagedResult<VoucherCodeDto>>.Ok(result));
    }

    /// <summary>
    /// Kiểm tra tính hợp lệ của mã voucher cho đơn hàng bán lẻ tại quầy POS (REQ-PV05)
    /// </summary>
    [HttpPost("validate")]
    public async Task<IActionResult> ValidateVoucher([FromBody] ValidateVoucherRequest request, CancellationToken cancellationToken)
    {
        var result = await _promotionService.ValidateVoucherAsync(request, cancellationToken);
        return Ok(ApiResponse<ValidateVoucherResponse>.Ok(result));
    }

    /// <summary>
    /// Đánh dấu sử dụng mã voucher khi hoàn tất đơn hàng POS (REQ-PV05)
    /// </summary>
    [HttpPost("redeem")]
    public async Task<IActionResult> RedeemVoucher([FromBody] RedeemVoucherRequest request, CancellationToken cancellationToken)
    {
        var result = await _promotionService.RedeemVoucherAsync(request, cancellationToken);
        if (!result.Success)
        {
            return BadRequest(ApiResponse<RedeemVoucherResponse>.Fail("REDEEM_FAILED", result.Message));
        }
        return Ok(ApiResponse<RedeemVoucherResponse>.Ok(result, result.Message));
    }
}
