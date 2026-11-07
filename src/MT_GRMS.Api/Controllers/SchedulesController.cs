using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.HR.Schedules;
using MT_GRMS.Application.HR.Schedules.Dtos;
using MT_GRMS.Domain.Exceptions;

namespace MT_GRMS.Api.Controllers;

[ApiController]
[Route("api/schedules")]
public class SchedulesController : BaseApiController
{
    private readonly IScheduleService _scheduleService;
    private readonly ITenantContext _tenantContext;

    public SchedulesController(IScheduleService scheduleService, ITenantContext tenantContext)
    {
        _scheduleService = scheduleService;
        _tenantContext = tenantContext;
    }

    /// <summary>
    /// Tra cứu danh sách lịch làm việc theo chi nhánh, nhân viên, dải ngày và trạng thái
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetSchedules(
        [FromQuery] ScheduleQueryFilter filter,
        CancellationToken cancellationToken)
    {
        var result = await _scheduleService.GetListAsync(filter, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<ScheduleResponse>>.Ok(result));
    }

    /// <summary>
    /// Tra cứu lịch làm việc cá nhân của tài khoản nhân viên đang đăng nhập
    /// </summary>
    [HttpGet("my-schedule")]
    public async Task<IActionResult> GetMySchedule(
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate,
        CancellationToken cancellationToken)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                     ?? User.FindFirst("sub")?.Value
                     ?? (_tenantContext.CurrentUserId != Guid.Empty ? _tenantContext.CurrentUserId.ToString() : null);

        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized(ApiResponse<object>.Fail("UNAUTHORIZED", "Không xác định được danh tính người dùng hiện tại."));
        }

        var result = await _scheduleService.GetMySchedulesAsync(userId, fromDate, toDate, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<ScheduleResponse>>.Ok(result));
    }

    /// <summary>
    /// Lấy chi tiết lịch làm việc theo ID
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _scheduleService.GetByIdAsync(id, cancellationToken);
            return Ok(ApiResponse<ScheduleResponse>.Ok(result));
        }
        catch (NotFoundException ex)
        {
            return NotFound(ApiResponse<object>.Fail("NOT_FOUND", ex.Message));
        }
    }

    /// <summary>
    /// Phân ca làm việc đơn lẻ cho nhân viên
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateScheduleItemRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _scheduleService.CreateAsync(request, cancellationToken);
            return CreatedAtAction(nameof(GetById), new { id = result.Id }, ApiResponse<ScheduleResponse>.Ok(result, "Tạo ca làm việc thành công."));
        }
        catch (NotFoundException ex)
        {
            return NotFound(ApiResponse<object>.Fail("NOT_FOUND", ex.Message));
        }
        catch (BusinessException ex)
        {
            return BadRequest(ApiResponse<object>.Fail(ex.ErrorCode ?? "SCHEDULE_CONFLICT", ex.Message));
        }
    }

    /// <summary>
    /// Phân ca làm việc hàng loạt (Batch) theo tuần/tháng có kiểm tra chống xung đột thời gian
    /// </summary>
    [HttpPost("batch")]
    public async Task<IActionResult> BatchCreate(
        [FromBody] BatchCreateScheduleRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _scheduleService.BatchCreateAsync(request, cancellationToken);
            return StatusCode(201, ApiResponse<IReadOnlyList<ScheduleResponse>>.Ok(result, $"Phân ca hàng loạt thành công {result.Count} ca làm việc."));
        }
        catch (NotFoundException ex)
        {
            return NotFound(ApiResponse<object>.Fail("NOT_FOUND", ex.Message));
        }
        catch (BusinessException ex)
        {
            return BadRequest(ApiResponse<object>.Fail(ex.ErrorCode ?? "SCHEDULE_CONFLICT", ex.Message));
        }
    }

    /// <summary>
    /// Đặt lịch lặp lại cho nhân viên (khi không có ngày kết thúc: tự động tạo lịch đến hết tháng hiện tại và tháng sau; khi có ngày kết thúc: tạo lịch đến ngày chỉ định)
    /// </summary>
    [HttpPost("recurring")]
    public async Task<IActionResult> CreateRecurring(
        [FromBody] CreateRecurringScheduleRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _scheduleService.CreateRecurringAsync(request, cancellationToken);
            return StatusCode(201, ApiResponse<IReadOnlyList<ScheduleResponse>>.Ok(result, $"Đăng ký lịch lặp lại thành công {result.Count} ca làm việc."));
        }
        catch (NotFoundException ex)
        {
            return NotFound(ApiResponse<object>.Fail("NOT_FOUND", ex.Message));
        }
        catch (BusinessException ex)
        {
            return BadRequest(ApiResponse<object>.Fail(ex.ErrorCode ?? "SCHEDULE_CONFLICT", ex.Message));
        }
    }

    /// <summary>
    /// Cập nhật thông tin ca làm việc
    /// </summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateScheduleRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _scheduleService.UpdateAsync(id, request, cancellationToken);
            return Ok(ApiResponse<ScheduleResponse>.Ok(result, "Cập nhật ca làm việc thành công."));
        }
        catch (NotFoundException ex)
        {
            return NotFound(ApiResponse<object>.Fail("NOT_FOUND", ex.Message));
        }
        catch (BusinessException ex)
        {
            return BadRequest(ApiResponse<object>.Fail(ex.ErrorCode ?? "BAD_REQUEST", ex.Message));
        }
    }

    /// <summary>
    /// Xóa ca làm việc (chỉ xóa được ca chưa hoàn thành và chưa chấm công)
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(
        Guid id,
        CancellationToken cancellationToken)
    {
        try
        {
            await _scheduleService.DeleteAsync(id, cancellationToken);
            return NoContent();
        }
        catch (NotFoundException ex)
        {
            return NotFound(ApiResponse<object>.Fail("NOT_FOUND", ex.Message));
        }
        catch (BusinessException ex)
        {
            return BadRequest(ApiResponse<object>.Fail(ex.ErrorCode ?? "BAD_REQUEST", ex.Message));
        }
    }

    /// <summary>
    /// Ban hành ca làm việc (Publish) theo tuần cho chi nhánh
    /// </summary>
    [HttpPost("publish")]
    public async Task<IActionResult> Publish(
        [FromBody] PublishSchedulesRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _scheduleService.PublishAsync(request, cancellationToken);
            return Ok(ApiResponse<PublishSchedulesResponse>.Ok(result));
        }
        catch (NotFoundException ex)
        {
            return NotFound(ApiResponse<object>.Fail("NOT_FOUND", ex.Message));
        }
        catch (BusinessException ex)
        {
            return BadRequest(ApiResponse<object>.Fail(ex.ErrorCode ?? "BAD_REQUEST", ex.Message));
        }
    }

    /// <summary>
    /// Chấm công thủ công (Đi làm, Nghỉ phép có lương, Nghỉ phép không lương)
    /// </summary>
    [HttpPost("{id:guid}/attendance")]
    [HttpPut("{id:guid}/attendance")]
    public async Task<IActionResult> RecordAttendance(
        Guid id,
        [FromBody] ManualAttendanceRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _scheduleService.RecordManualAttendanceAsync(id, request, cancellationToken);
            return Ok(ApiResponse<ScheduleResponse>.Ok(result));
        }
        catch (NotFoundException ex)
        {
            return NotFound(ApiResponse<object>.Fail("NOT_FOUND", ex.Message));
        }
        catch (BusinessException ex)
        {
            return BadRequest(ApiResponse<object>.Fail(ex.ErrorCode ?? "BAD_REQUEST", ex.Message));
        }
        catch (Exception ex)
        {
            return StatusCode(500, ApiResponse<object>.Fail("INTERNAL_SERVER_ERROR", ex.Message));
        }
    }
}

