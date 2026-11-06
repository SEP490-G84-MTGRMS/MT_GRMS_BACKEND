using Microsoft.AspNetCore.Mvc;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.HR.ShiftConfigs;
using MT_GRMS.Application.HR.ShiftConfigs.Dtos;
using MT_GRMS.Domain.Exceptions;

namespace MT_GRMS.Api.Controllers;

[ApiController]
[Route("api/shift-configs")]
public class ShiftConfigsController : BaseApiController
{
    private readonly IShiftConfigService _shiftConfigService;

    public ShiftConfigsController(IShiftConfigService shiftConfigService)
    {
        _shiftConfigService = shiftConfigService;
    }

    /// <summary>
    /// Lấy danh sách cấu hình ca làm việc có phân trang, tìm kiếm và lọc theo chi nhánh, trạng thái
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetShiftConfigs(
        [FromQuery] ShiftConfigQueryFilter filter,
        CancellationToken cancellationToken)
    {
        var result = await _shiftConfigService.GetShiftConfigsPagedAsync(filter, cancellationToken);
        return Ok(ApiResponse<PagedResult<ShiftConfigResponse>>.Ok(result));
    }

    /// <summary>
    /// Lấy chi tiết cấu hình một ca làm việc theo ID
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _shiftConfigService.GetShiftConfigByIdAsync(id, cancellationToken);
            return Ok(ApiResponse<ShiftConfigResponse>.Ok(result));
        }
        catch (NotFoundException ex)
        {
            return NotFound(ApiResponse<object>.Fail("NOT_FOUND", ex.Message));
        }
    }

    /// <summary>
    /// Tạo mới một ca làm việc mẫu cho chi nhánh
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateShiftConfigRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _shiftConfigService.CreateShiftConfigAsync(request, cancellationToken);
            return CreatedAtAction(nameof(GetById), new { id = result.Id }, ApiResponse<ShiftConfigResponse>.Ok(result));
        }
        catch (BusinessException ex) when (ex.ErrorCode == "DUPLICATE_SHIFT_CODE")
        {
            return Conflict(ApiResponse<object>.Fail(ex.ErrorCode, ex.Message));
        }
        catch (BusinessException ex)
        {
            return BadRequest(ApiResponse<object>.Fail(ex.ErrorCode, ex.Message));
        }
    }

    /// <summary>
    /// Cập nhật thông tin cấu hình ca làm việc
    /// </summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateShiftConfigRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _shiftConfigService.UpdateShiftConfigAsync(id, request, cancellationToken);
            return Ok(ApiResponse<ShiftConfigResponse>.Ok(result));
        }
        catch (NotFoundException ex)
        {
            return NotFound(ApiResponse<object>.Fail("NOT_FOUND", ex.Message));
        }
        catch (BusinessException ex)
        {
            return BadRequest(ApiResponse<object>.Fail(ex.ErrorCode, ex.Message));
        }
    }

    /// <summary>
    /// Bật/Tắt trạng thái hoạt động (IsActive) của ca làm việc
    /// </summary>
    [HttpPatch("{id:guid}/status")]
    public async Task<IActionResult> UpdateStatus(
        Guid id,
        [FromBody] UpdateShiftConfigStatusRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _shiftConfigService.UpdateShiftConfigStatusAsync(id, request, cancellationToken);
            return Ok(ApiResponse<ShiftConfigResponse>.Ok(result));
        }
        catch (NotFoundException ex)
        {
            return NotFound(ApiResponse<object>.Fail("NOT_FOUND", ex.Message));
        }
    }

    /// <summary>
    /// Xóa mềm một ca làm việc
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(
        Guid id,
        CancellationToken cancellationToken)
    {
        try
        {
            await _shiftConfigService.DeleteShiftConfigAsync(id, cancellationToken);
            return NoContent();
        }
        catch (NotFoundException ex)
        {
            return NotFound(ApiResponse<object>.Fail("NOT_FOUND", ex.Message));
        }
    }
}

