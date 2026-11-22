using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MT_GRMS.Application.Common;

namespace MT_GRMS.Api.Controllers;

public class HealthController(HealthCheckService healthChecks) : BaseApiController
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<object>>> GetStatus(CancellationToken cancellationToken = default)
    {
        var report = await healthChecks.CheckHealthAsync(cancellationToken);
        if (report.Status != HealthStatus.Healthy)
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                ApiResponse<object>.Fail("DATABASE_UNAVAILABLE", "Cơ sở dữ liệu chưa sẵn sàng.", StatusCodes.Status503ServiceUnavailable));

        return Ok(ApiResponse<object>.Ok(new
        {
            service = "MT-GRMS Web API",
            status = "Healthy",
            timestamp = DateTime.UtcNow
        }, "Hệ thống hoạt động bình thường."));
    }
}
