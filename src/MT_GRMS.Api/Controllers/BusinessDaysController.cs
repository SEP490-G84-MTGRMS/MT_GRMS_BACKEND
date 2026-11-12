using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MT_GRMS.Api.Filters;
using MT_GRMS.Application.BusinessDays;
using MT_GRMS.Application.BusinessDays.Dtos;
using MT_GRMS.Application.Common;

namespace MT_GRMS.Api.Controllers;

[ApiController]
[Authorize(Roles = "STORE_OWNER,ADMIN")]
[Route("api/business-days")]
public class BusinessDaysController : BaseApiController
{
    private readonly IBusinessDayClosingService _service;
    public BusinessDaysController(IBusinessDayClosingService service) => _service = service;

    [HttpPost("close")]
    [RequireIdempotency]
    public async Task<ActionResult<ApiResponse<BusinessDayClosingResponse>>> Close(
        [FromBody] CloseBusinessDayRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _service.CloseAsync(request, cancellationToken);
        return Ok(ApiResponse<BusinessDayClosingResponse>.Ok(result, "Khóa sổ ngày nghiệp vụ thành công."));
    }
}
