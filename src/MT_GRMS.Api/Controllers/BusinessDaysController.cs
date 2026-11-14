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

    [HttpGet("current")]
    public async Task<ActionResult<ApiResponse<BusinessDateResponse>>> Current(
        [FromQuery] Guid branchId,
        CancellationToken cancellationToken)
    {
        var result = await _service.GetCurrentBusinessDateAsync(branchId, cancellationToken);
        return Ok(ApiResponse<BusinessDateResponse>.Ok(result));
    }

    [HttpGet("preview")]
    public async Task<ActionResult<ApiResponse<BusinessDayClosingPreview>>> Preview(
        [FromQuery] Guid branchId,
        [FromQuery] DateOnly businessDate,
        CancellationToken cancellationToken)
    {
        var result = await _service.PreviewAsync(branchId, businessDate, cancellationToken);
        return Ok(ApiResponse<BusinessDayClosingPreview>.Ok(result));
    }

    [HttpPost("close")]
    [RequireIdempotency]
    public async Task<ActionResult<ApiResponse<BusinessDayClosingResponse>>> Close(
        [FromBody] CloseBusinessDayRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _service.CloseAsync(request, cancellationToken);
        return Ok(ApiResponse<BusinessDayClosingResponse>.Ok(result, "Khóa sổ ngày nghiệp vụ thành công."));
    }

    [HttpGet("{branchId:guid}/{businessDate}/history")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<BusinessDayClosingVersionResponse>>>> History(
        Guid branchId, DateOnly businessDate, CancellationToken cancellationToken)
    {
        var result = await _service.GetHistoryAsync(branchId, businessDate, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<BusinessDayClosingVersionResponse>>.Ok(result));
    }

    [HttpPost("{id:guid}/reopen")]
    [RequireIdempotency]
    public async Task<ActionResult<ApiResponse<BusinessDayClosingVersionResponse>>> Reopen(
        Guid id, [FromBody] ReopenBusinessDayRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.ReopenAsync(id, request, cancellationToken);
        return Ok(ApiResponse<BusinessDayClosingVersionResponse>.Ok(result, "Đã mở lại ngày nghiệp vụ."));
    }
}
