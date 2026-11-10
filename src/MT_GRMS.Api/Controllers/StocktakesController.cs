using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Stocktakes;
using MT_GRMS.Application.Stocktakes.Dtos;
using MT_GRMS.Application.SupplierReturns.Dtos;

namespace MT_GRMS.Api.Controllers;

[ApiController]
[Route("api/stocktakes")]
public class StocktakesController : BaseApiController
{
    private readonly IStocktakeService _service;

    public StocktakesController(IStocktakeService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> GetPaged(
        [FromQuery] StocktakeFilterQuery query,
        CancellationToken cancellationToken)
    {
        var result = await _service.GetPagedAsync(query, cancellationToken);
        return Ok(ApiResponse<PagedResult<StocktakeListItemDto>>.Ok(result));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await _service.GetByIdAsync(id, cancellationToken);
        return Ok(ApiResponse<StocktakeDetailDto>.Ok(result));
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateStocktakeCommand command,
        CancellationToken cancellationToken)
    {
        var result = await _service.CreateAsync(command, cancellationToken);
        return CreatedAtAction(
            nameof(GetById),
            new { id = result.Id },
            ApiResponse<StocktakeDetailDto>.Ok(result, "Tạo phiếu kiểm kê hàng hóa thành công!"));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateStocktakeCommand command,
        CancellationToken cancellationToken)
    {
        var result = await _service.UpdateAsync(id, command, cancellationToken);
        return Ok(ApiResponse<StocktakeDetailDto>.Ok(result, "Đã cập nhật phiếu kiểm kê thành công."));
    }

    [HttpPost("{id:guid}/submit")]
    public async Task<IActionResult> Submit(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await _service.SubmitAsync(id, cancellationToken);
        return Ok(ApiResponse<StocktakeDetailDto>.Ok(result, "Đã gửi phiếu kiểm kê lên Store Owner phê duyệt."));
    }

    [HttpPost("{id:guid}/review")]
    public async Task<IActionResult> Review(
        Guid id,
        [FromBody] ReviewStocktakeCommand command,
        CancellationToken cancellationToken)
    {
        var result = await _service.ReviewAsync(id, command, cancellationToken);
        return Ok(ApiResponse<StocktakeDetailDto>.Ok(result, "Đã ghi nhận kết quả phê duyệt phiếu kiểm kê."));
    }

    [HttpPost("{id:guid}/create-return")]
    public async Task<IActionResult> CreateReturnFromStocktake(
        Guid id,
        [FromBody] CreateReturnFromStocktakeCommand command,
        CancellationToken cancellationToken)
    {
        var result = await _service.CreateReturnFromStocktakeAsync(id, command, cancellationToken);
        return Ok(ApiResponse<SupplierReturnDetailDto>.Ok(result, "Đã tạo phiếu yêu cầu trả hàng nhà cung cấp từ phiếu kiểm kê."));
    }
}

