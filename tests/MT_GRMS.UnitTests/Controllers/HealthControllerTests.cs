using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Moq;
using MT_GRMS.Api.Controllers;
using MT_GRMS.Application.Common;
using Xunit;

namespace MT_GRMS.UnitTests.Controllers;

public class HealthControllerTests
{
    [Theory]
    [InlineData(HealthStatus.Healthy, 200)]
    [InlineData(HealthStatus.Unhealthy, 503)]
    public async Task GetStatus_ReflectsDatabaseReadiness(HealthStatus status, int expected)
    {
        var healthChecks = new Mock<HealthCheckService>();
        healthChecks.Setup(service => service.CheckHealthAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HealthReport(new Dictionary<string, HealthReportEntry>(), status, TimeSpan.Zero));
        var controller = new HealthController(healthChecks.Object);
        var result = await controller.GetStatus();

        var response = Assert.IsAssignableFrom<ObjectResult>(result.Result);
        var body = Assert.IsType<ApiResponse<object>>(response.Value);
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(status == HealthStatus.Healthy, body.Success);
    }
}
