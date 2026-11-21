using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Notifications;
using MT_GRMS.Application.Notifications.Dtos;
using MT_GRMS.Domain.Constants;
using MT_GRMS.Domain.Exceptions;

namespace MT_GRMS.Api.Controllers;

[Authorize(Roles = SystemRoles.AllStaffAndOwner)]
[ApiController]
[Route("api/notifications")]
public class NotificationsController : BaseApiController
{
    private readonly INotificationService _notificationService;

    public NotificationsController(INotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<NotificationListResponse>>> GetMyNotifications(
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _notificationService.GetMyNotificationsAsync(limit, cancellationToken);
        return Ok(ApiResponse<NotificationListResponse>.Ok(result));
    }

    [HttpPatch("{id:guid}/read")]
    public async Task<IActionResult> MarkAsRead(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _notificationService.MarkAsReadAsync(id, cancellationToken);
            return Ok(ApiResponse<bool>.Ok(result));
        }
        catch (NotFoundException ex)
        {
            return NotFound(ApiResponse<object>.Fail("NOT_FOUND", ex.Message));
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<object>.Fail(ErrorCodes.Forbidden, ex.Message));
        }
    }

    [HttpPatch("read-all")]
    public async Task<ActionResult<ApiResponse<int>>> MarkAllAsRead(
        CancellationToken cancellationToken = default)
    {
        var updated = await _notificationService.MarkAllAsReadAsync(cancellationToken);
        return Ok(ApiResponse<int>.Ok(updated));
    }
}
