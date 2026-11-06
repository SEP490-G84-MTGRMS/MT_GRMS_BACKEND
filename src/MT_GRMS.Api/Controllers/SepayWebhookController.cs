using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.SaaS.Dtos;
using MT_GRMS.Application.SaaS.Services;

namespace MT_GRMS.Api.Controllers;

[ApiController]
[Route("api/webhooks/sepay")]
[Route("api/v1/webhooks/sepay")]
public class SepayWebhookController : ControllerBase
{
    private readonly IWalletService _walletService;
    private readonly SePaySettings _sepaySettings;
    private readonly ILogger<SepayWebhookController> _logger;

    public SepayWebhookController(
        IWalletService walletService,
        IOptions<SePaySettings> sepayOptions,
        ILogger<SepayWebhookController> logger)
    {
        _walletService = walletService;
        _sepaySettings = sepayOptions.Value;
        _logger = logger;
    }

    /// <summary>
    /// Tiếp nhận Webhook tức thời (IPN) từ cổng thanh toán SePay khi có biến động số dư VietQR
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> ReceiveSepayWebhook(
        [FromBody] SepayWebhookPayload payload,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("[SePay Webhook] Nhận giao dịch {Id}: Số tiền={Amount}, Nội dung='{Content}', Ref={RefCode}",
            payload.Id, payload.TransferAmount, payload.Content, payload.ReferenceCode);

        // 1. Kiểm tra xác thực Token từ SePay (nếu cấu hình ApiKey)
        if (!string.IsNullOrWhiteSpace(_sepaySettings.ApiKey) &&
            _sepaySettings.ApiKey != "SEPAY_API_KEY_SECRET")
        {
            var authHeader = Request.Headers.Authorization.ToString();
            var expectedBearer = $"Bearer {_sepaySettings.ApiKey}";
            var expectedApikey = $"Apikey {_sepaySettings.ApiKey}";

            if (!string.Equals(authHeader, expectedBearer, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(authHeader, expectedApikey, StringComparison.OrdinalIgnoreCase) &&
                !authHeader.Contains(_sepaySettings.ApiKey))
            {
                _logger.LogWarning("[SePay Webhook] Xác thực token thất bại: Header='{Header}'", authHeader);
                return Unauthorized(new { success = false, message = "Mã xác thực SePay không hợp lệ." });
            }
        }

        // 2. Xử lý nghiệp vụ cộng tiền và ghi nhận sổ cái giao dịch
        var result = await _walletService.ProcessSepayWebhookAsync(payload, cancellationToken);

        if (!result.Success)
        {
            _logger.LogWarning("[SePay Webhook] Xử lý không thành công: {Message}", result.Message);
            // SePay khuyến nghị trả về 200 kèm success: false để tránh bị retry vô tận với các đơn không khớp
            return Ok(new
            {
                success = false,
                message = result.Message,
                orderCode = result.OrderCode
            });
        }

        _logger.LogInformation("[SePay Webhook] Nạp tiền thành công cho Gian hàng {TenantId}, Số tiền={Amount}",
            result.TenantId, result.Amount);

        return Ok(new
        {
            success = true,
            message = result.Message,
            orderCode = result.OrderCode,
            tenantId = result.TenantId,
            amount = result.Amount
        });
    }
}

