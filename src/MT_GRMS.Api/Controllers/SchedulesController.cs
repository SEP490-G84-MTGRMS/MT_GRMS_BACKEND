using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.HR.Schedules;
using MT_GRMS.Application.HR.Schedules.Dtos;
using MT_GRMS.Domain.Constants;
using MT_GRMS.Domain.Exceptions;

namespace MT_GRMS.Api.Controllers;

[Authorize(Roles = SystemRoles.AllStaffAndOwner)]
[ApiController]
[Route("api/schedules")]
[Route("api/employee-schedules")]
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
    [Authorize(Roles = SystemRoles.OwnerOrAdmin)]
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
    [Authorize(Roles = SystemRoles.AllStaffAndOwner)]
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
    [Authorize(Roles = SystemRoles.AllStaffAndOwner)]
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _scheduleService.GetByIdAsync(id, cancellationToken);

            var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                             ?? User.FindFirst("sub")?.Value
                             ?? (_tenantContext.CurrentUserId != Guid.Empty ? _tenantContext.CurrentUserId.ToString() : null);

            var currentRole = User.FindFirst(ClaimTypes.Role)?.Value
                           ?? User.FindFirst("role")?.Value
                           ?? _tenantContext.CurrentRole;

            var isOwnerOrAdmin = User.IsInRole(SystemRoles.StoreOwner) || User.IsInRole(SystemRoles.Admin)
                              || string.Equals(currentRole, SystemRoles.StoreOwner, StringComparison.OrdinalIgnoreCase)
                              || string.Equals(currentRole, SystemRoles.Admin, StringComparison.OrdinalIgnoreCase);

            if (!isOwnerOrAdmin)
            {
                if (string.IsNullOrWhiteSpace(currentUserId) || result.UserId != currentUserId)
                {
                    return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<object>.Fail(ErrorCodes.Forbidden, "Bạn không có quyền truy cập ca làm việc của nhân sự khác."));
                }
            }

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
    [Authorize(Roles = SystemRoles.OwnerOrAdmin)]
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
    [Authorize(Roles = SystemRoles.OwnerOrAdmin)]
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
    [Authorize(Roles = SystemRoles.OwnerOrAdmin)]
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
    [Authorize(Roles = SystemRoles.OwnerOrAdmin)]
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
    [Authorize(Roles = SystemRoles.OwnerOrAdmin)]
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
    [Authorize(Roles = SystemRoles.OwnerOrAdmin)]
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
    [Authorize(Roles = SystemRoles.OwnerOrAdmin)]
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

    /// <summary>
    /// Gửi phiếu yêu cầu (Xin nghỉ, Đổi ca, Chấm công sự cố, Xin OT) cho ca làm việc
    /// </summary>
    [HttpPost("{scheduleId:guid}/requests")]
    public async Task<IActionResult> CreateScheduleRequest(
        [FromRoute] Guid scheduleId,
        [FromBody] CreateScheduleApprovalRequestDto dto,
        [FromServices] IScheduleApprovalService approvalService,
        CancellationToken cancellationToken)
    {
        var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? throw new UnauthorizedAccessException("Không tìm thấy thông tin người dùng.");

        var result = await approvalService.CreateRequestAsync(scheduleId, currentUserId, dto, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, ApiResponse<ScheduleApprovalRequestDto>.Ok(result, "Gửi yêu cầu thành công."));
    }

    /// <summary>
    /// Lấy danh sách toàn bộ các phiếu yêu cầu đã gửi của một ca làm việc
    /// </summary>
    [HttpGet("{scheduleId:guid}/requests")]
    public async Task<IActionResult> GetScheduleRequests(
        [FromRoute] Guid scheduleId,
        [FromServices] IScheduleApprovalService approvalService,
        CancellationToken cancellationToken)
    {
        var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? throw new UnauthorizedAccessException("Không tìm thấy thông tin người dùng.");

        var isOwnerOrAdmin = User.IsInRole(SystemRoles.StoreOwner) || User.IsInRole(SystemRoles.Admin);
        var result = await approvalService.GetRequestsByScheduleIdAsync(scheduleId, currentUserId, isOwnerOrAdmin, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<ScheduleApprovalRequestDto>>.Ok(result));
    }
}

