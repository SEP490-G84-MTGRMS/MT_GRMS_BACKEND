using Microsoft.AspNetCore.Mvc;
using MT_GRMS.Api.Controllers;
using MT_GRMS.Application.Common;
using Xunit;

namespace MT_GRMS.UnitTests.Controllers;

public class HealthControllerTests
{
    [Fact]
    public void GetStatus_ShouldReturnOk()
    {
        var controller = new HealthController();
        var result = controller.GetStatus();

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var apiRes = Assert.IsType<ApiResponse<object>>(okResult.Value);
        Assert.True(apiRes.Success);
        Assert.Equal("Hệ thống hoạt động bình thường.", apiRes.Message);
    }
}
