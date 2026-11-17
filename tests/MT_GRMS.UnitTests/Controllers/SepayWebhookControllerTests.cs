using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using MT_GRMS.Api.Controllers;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.SaaS.Dtos;
using MT_GRMS.Application.SaaS.Services;
using Xunit;

namespace MT_GRMS.UnitTests.Controllers;

public class SepayWebhookControllerTests
{
    private readonly Mock<IWalletService> _mockWalletService;
    private readonly Mock<ILogger<SepayWebhookController>> _mockLogger;

    public SepayWebhookControllerTests()
    {
        _mockWalletService = new Mock<IWalletService>();
        _mockLogger = new Mock<ILogger<SepayWebhookController>>();
    }

    [Fact]
    public async Task ReceiveSepayWebhook_ShouldHandleUnauthorized_AndServiceFail_AndSuccess()
    {
        var settings = new SePaySettings
        {
            ApiKey = "REAL_SECRET_KEY_123"
        };
        var options = Options.Create(settings);
        var controller = new SepayWebhookController(_mockWalletService.Object, options, _mockLogger.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        var payload = new SepayWebhookPayload
        {
            Id = 12345,
            Gateway = "VietinBank",
            TransferAmount = 500_000m,
            Content = "TOPUP_TN01",
            ReferenceCode = "REF123"
        };

        // 1. Wrong Auth Header -> 401 Unauthorized
        controller.Request.Headers["Authorization"] = "Bearer WRONG_KEY";
        var resUnauthorized = await controller.ReceiveSepayWebhook(payload, CancellationToken.None);
        Assert.IsType<UnauthorizedObjectResult>(resUnauthorized);

        // 2. Correct Auth Header, Wallet service returns failure -> 200 OK with success = false
        controller.Request.Headers["Authorization"] = "Apikey REAL_SECRET_KEY_123";
        _mockWalletService.Setup(w => w.ProcessSepayWebhookAsync(payload, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SepayWebhookResult(
                Success: false,
                Message: "Tenant not found for topup code",
                OrderCode: "TOPUP_TN01"
            ));

        var resFail = await controller.ReceiveSepayWebhook(payload, CancellationToken.None);
        var okFail = Assert.IsType<OkObjectResult>(resFail);
        Assert.NotNull(okFail.Value);

        // 3. Correct Auth Header, Wallet service returns success -> 200 OK with success = true
        _mockWalletService.Setup(w => w.ProcessSepayWebhookAsync(payload, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SepayWebhookResult(
                Success: true,
                Message: "Topup successful",
                OrderCode: "TOPUP_TN01",
                TenantId: "tenant-01",
                Amount: 500_000m
            ));

        var resSuccess = await controller.ReceiveSepayWebhook(payload, CancellationToken.None);
        var okSuccess = Assert.IsType<OkObjectResult>(resSuccess);
        Assert.NotNull(okSuccess.Value);
    }
}
