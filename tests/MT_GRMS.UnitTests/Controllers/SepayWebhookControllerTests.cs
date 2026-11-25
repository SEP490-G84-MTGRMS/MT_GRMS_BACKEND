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

    [Fact]
    public async Task ReceiveSepayWebhook_MissingAuthorizationHeader_ReturnsUnauthorized()
    {
        var controller = new SepayWebhookController(
            _mockWalletService.Object,
            Options.Create(new SePaySettings { ApiKey = "SECRET_KEY_ABC" }),
            _mockLogger.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var res = await controller.ReceiveSepayWebhook(new SepayWebhookPayload { Id = 1, TransferAmount = 100_000m, Content = "AN02HD1" }, CancellationToken.None);
        Assert.IsType<UnauthorizedObjectResult>(res);
        _mockWalletService.Verify(w => w.ProcessSepayWebhookAsync(It.IsAny<SepayWebhookPayload>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReceiveSepayWebhook_CaseInsensitiveApikeyScheme_ProcessesPayload()
    {
        var controller = new SepayWebhookController(
            _mockWalletService.Object,
            Options.Create(new SePaySettings { ApiKey = "SECRET_KEY_ABC" }),
            _mockLogger.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        controller.Request.Headers["Authorization"] = "apikey SECRET_KEY_ABC";

        var payload = new SepayWebhookPayload { Id = 999, TransferAmount = 80_000m, Content = "AN02HD261125001", ReferenceCode = "FT999" };
        _mockWalletService.Setup(w => w.ProcessSepayWebhookAsync(payload, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SepayWebhookResult(true, "Already processed", "AN02HD261125001", "tenant_02", 80_000m));

        var res = await controller.ReceiveSepayWebhook(payload, CancellationToken.None);
        Assert.IsType<OkObjectResult>(res);
        _mockWalletService.Verify(w => w.ProcessSepayWebhookAsync(payload, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("")]
    [InlineData("SEPAY_API_KEY_SECRET")]
    public async Task ReceiveSepayWebhook_UnconfiguredApiKey_Returns503ServiceUnavailable(string apiKey)
    {
        var controller = new SepayWebhookController(
            _mockWalletService.Object,
            Options.Create(new SePaySettings { ApiKey = apiKey }),
            _mockLogger.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        var payload = new SepayWebhookPayload { Id = 42, TransferAmount = 50_000m, Content = "TOPUP_TN01" };

        var res = await controller.ReceiveSepayWebhook(payload, CancellationToken.None);
        var statusResult = Assert.IsType<ObjectResult>(res);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, statusResult.StatusCode);
        _mockWalletService.Verify(w => w.ProcessSepayWebhookAsync(It.IsAny<SepayWebhookPayload>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
