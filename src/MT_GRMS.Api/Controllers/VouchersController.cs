using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.PosOrders;
using MT_GRMS.Application.Promotions;
using MT_GRMS.Application.Promotions.Dtos;
using MT_GRMS.Domain.Constants;

namespace MT_GRMS.Api.Controllers;

[ApiController]
[Route("api/vouchers")]
[Authorize]
public class VouchersController : BaseApiController
{
    private readonly IPromotionService _promotionService;
    private readonly IThermalPrintService _thermalPrintService;

    public VouchersController(IPromotionService promotionService, IThermalPrintService thermalPrintService)
    {
        _promotionService = promotionService;
        _thermalPrintService = thermalPrintService;
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
    /// Cập nhật đợt phát hành voucher (REQ-PV04)
    /// </summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = RoleConstants.StoreOwnerOrAdmin)]
    public async Task<IActionResult> UpdateBatch(Guid id, [FromBody] VoucherBatchUpdateRequest request, CancellationToken cancellationToken)
    {
        var result = await _promotionService.UpdateVoucherBatchAsync(id, request, cancellationToken);
        return Ok(ApiResponse<VoucherBatchResponse>.Ok(result, "Cập nhật đợt phát hành voucher thành công"));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = RoleConstants.StoreOwnerOrAdmin)]
    public async Task<IActionResult> DeleteBatch(Guid id, CancellationToken cancellationToken)
    {
        await _promotionService.DeletePromotionAsync(id, cancellationToken);
        return Ok(ApiResponse<bool>.Ok(true, "Xóa đợt voucher thành công"));
    }

    [HttpPatch("{id:guid}/status")]
    [Authorize(Roles = RoleConstants.StoreOwnerOrAdmin)]
    public async Task<IActionResult> ToggleBatchStatus(Guid id, CancellationToken cancellationToken)
    {
        var isActive = await _promotionService.TogglePromotionStatusAsync(id, cancellationToken);
        return Ok(ApiResponse<bool>.Ok(isActive, isActive ? "Đã kích hoạt đợt voucher" : "Đã tạm dừng đợt voucher"));
    }

    /// <summary>
    /// Tạo mới đợt phát hành voucher & tự động sinh mã (REQ-PV04)
    /// </summary>
    [HttpPost]
    [Authorize(Roles = RoleConstants.StoreOwnerOrAdmin)]
    public async Task<IActionResult> CreateBatch([FromBody] VoucherBatchCreateRequest request, CancellationToken cancellationToken)
    {
        var result = await _promotionService.CreateVoucherBatchAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetBatchById), new { id = result.Id }, ApiResponse<VoucherBatchResponse>.Ok(result, "Tạo đợt phát hành voucher thành công", StatusCodes.Status201Created));
    }

    /// <summary>
    /// Sinh bổ sung mã voucher cho đợt (REQ-PV04)
    /// </summary>
    [HttpPost("{id:guid}/generate-codes")]
    [Authorize(Roles = RoleConstants.StoreOwnerOrAdmin)]
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

    /// <summary>
    /// In trực tiếp phiếu voucher ra máy in nhiệt POS-58 (khổ 43mm/58mm) qua Windows Spooler RAW
    /// </summary>
    [HttpPost("print")]
    public IActionResult PrintVoucher([FromBody] PrintVoucherRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.VoucherCode))
        {
            return BadRequest(ApiResponse<PrintVoucherResponse>.Fail("INVALID_PAYLOAD", "Thông tin phiếu voucher không hợp lệ."));
        }

        try
        {
            string printer = !string.IsNullOrWhiteSpace(request.PrinterName) ? request.PrinterName : "POS-58";
            bool printed = _thermalPrintService.PrintVoucher(request, printer);

            if (!printed)
            {
                return Ok(ApiResponse<PrintVoucherResponse>.Ok(
                    new PrintVoucherResponse(false, "Lệnh in đã gửi nhưng máy in POS-58 chưa sẵn sàng hoặc mất kết nối USB. Vui lòng kiểm tra nguồn và cáp USB máy in!")
                ));
            }

            return Ok(ApiResponse<PrintVoucherResponse>.Ok(
                new PrintVoucherResponse(true, $"Đã gửi lệnh in thành công phiếu voucher [{request.VoucherCode}] tới máy in {printer}!")
            ));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<PrintVoucherResponse>.Ok(
                new PrintVoucherResponse(false, $"Lỗi kết nối máy in POS-58: {ex.Message}")
            ));
        }
    }
}

