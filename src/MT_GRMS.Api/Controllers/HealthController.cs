using Microsoft.AspNetCore.Mvc;
using MT_GRMS.Application.Common;

namespace MT_GRMS.Api.Controllers;

public class HealthController : BaseApiController
{
    [HttpGet]
    public ActionResult<ApiResponse<object>> GetStatus()
    {
        return Ok(ApiResponse<object>.Ok(new
        {
            service = "MT-GRMS Web API",
            status = "Healthy",
            timestamp = DateTime.UtcNow
        }, "Hệ thống hoạt động bình thường."));
    }
}
