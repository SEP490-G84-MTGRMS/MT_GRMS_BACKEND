using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.SaaS.Dtos;
using MT_GRMS.Application.SaaS.Services;
using MT_GRMS.Domain.Constants;

namespace MT_GRMS.Api.Controllers;

[ApiController]
[Route("api/v1/tenant/wallet")]
[Authorize(Roles = RoleConstants.StoreOwnerOrAdmin)]
public class WalletController : BaseApiController
{
    private readonly IWalletService _walletService;

    public WalletController(IWalletService walletService)
    {
        _walletService = walletService;
    }

    /// <summary>
    /// Tạo yêu cầu nạp tiền vào ví gian hàng và trả về VietQR động
    /// </summary>
    [HttpPost("topup-order")]
    public async Task<IActionResult> CreateTopupOrder(
        [FromBody] CreateTopupOrderRequest request,
        CancellationToken cancellationToken)
    {
        var tenantId = User.FindFirst("tenant_id")?.Value ?? "DEFAULT";
        try
        {
            var res = await _walletService.CreateTopupOrderAsync(tenantId, request, cancellationToken);
            return Ok(ApiResponse<TopupOrderResponse>.Ok(res, "Tạo đơn nạp ví thành công."));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiResponse<TopupOrderResponse>.Fail("INVALID_AMOUNT", ex.Message));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<TopupOrderResponse>.Fail("TENANT_NOT_FOUND", ex.Message));
        }
    }

    /// <summary>
    /// Tra cứu trạng thái đơn nạp tiền ví gian hàng (Polling)
    /// </summary>
    [HttpGet("topup-status/{orderCode}")]
    public async Task<IActionResult> GetTopupStatus(
        [FromRoute] string orderCode,
        CancellationToken cancellationToken)
    {
        var tenantId = User.FindFirst("tenant_id")?.Value ?? "DEFAULT";
        var res = await _walletService.GetTopupOrderStatusAsync(tenantId, orderCode, cancellationToken);
        if (res == null)
        {
            return NotFound(ApiResponse<TopupOrderStatusResponse>.Fail("ORDER_NOT_FOUND", "Không tìm thấy đơn nạp tiền."));
        }

        return Ok(ApiResponse<TopupOrderStatusResponse>.Ok(res));
    }

    /// <summary>
    /// Lấy số dư ví khả dụng hiện tại và tổng nạp/chi
    /// </summary>
    [HttpGet("balance")]
    public async Task<IActionResult> GetBalance(CancellationToken cancellationToken)
    {
        var tenantId = User.FindFirst("tenant_id")?.Value ?? "DEFAULT";
        var res = await _walletService.GetWalletBalanceAsync(tenantId, cancellationToken);
        return Ok(ApiResponse<WalletBalanceResponse>.Ok(res));
    }

    /// <summary>
    /// Lấy danh sách lịch sử biến động số dư ví (Sổ cái)
    /// </summary>
    [HttpGet("transactions")]
    public async Task<IActionResult> GetTransactions(
        [FromQuery] string? type,
        CancellationToken cancellationToken)
    {
        var tenantId = User.FindFirst("tenant_id")?.Value ?? "DEFAULT";
        var res = await _walletService.GetWalletTransactionsAsync(tenantId, type, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<WalletTransactionDto>>.Ok(res));
    }
}

