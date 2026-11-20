using MT_GRMS.Application.Notifications.Dtos;

namespace MT_GRMS.Application.Notifications;

public interface INotificationRealtimeDispatcher
{
    Task PushToUsersAsync(
        string tenantId,
        IEnumerable<string> recipientUserIds,
        IReadOnlyDictionary<string, NotificationDto> userNotifications,
        CancellationToken cancellationToken = default);
}
