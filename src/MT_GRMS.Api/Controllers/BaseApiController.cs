using Microsoft.AspNetCore.Mvc;

namespace MT_GRMS.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public abstract class BaseApiController : ControllerBase
{
}
