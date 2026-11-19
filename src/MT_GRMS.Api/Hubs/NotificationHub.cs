using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using MT_GRMS.Application.Notifications;
using MT_GRMS.Application.Notifications.Dtos;

namespace MT_GRMS.Api.Hubs;

[Authorize]
public class NotificationHub : Hub
{
    public static string BuildUserGroup(string tenantId, string userId)
        => $"tenant:{tenantId}:user:{userId}";

    public override async Task OnConnectedAsync()
    {
        var tenantId = Context.User?.FindFirst("tenant_id")?.Value;
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                  ?? Context.User?.FindFirst("sub")?.Value;

        if (!string.IsNullOrWhiteSpace(tenantId) && !string.IsNullOrWhiteSpace(userId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, BuildUserGroup(tenantId, userId));
        }

        await base.OnConnectedAsync();
    }
}

public class SignalRNotificationDispatcher : INotificationRealtimeDispatcher
{
    private readonly IHubContext<NotificationHub> _hubContext;

    public SignalRNotificationDispatcher(IHubContext<NotificationHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public async Task PushToUsersAsync(
        string tenantId,
        IEnumerable<string> recipientUserIds,
        IReadOnlyDictionary<string, NotificationDto> userNotifications,
        CancellationToken cancellationToken = default)
    {
        foreach (var userId in recipientUserIds)
        {
            if (userNotifications.TryGetValue(userId, out var dto))
            {
                await _hubContext.Clients
                    .Group(NotificationHub.BuildUserGroup(tenantId, userId))
                    .SendAsync("ReceiveNotification", dto, cancellationToken);
            }
        }
    }
}
