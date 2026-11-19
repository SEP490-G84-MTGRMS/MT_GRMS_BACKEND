using MT_GRMS.Application.Notifications.Dtos;

namespace MT_GRMS.Application.Notifications;

public interface INotificationService
{
    Task CreateAndPushAsync(
        string tenantId,
        IEnumerable<string> recipientUserIds,
        string type,
        string title,
        string message,
        string? actionUrl = null,
        CancellationToken cancellationToken = default);

    Task NotifyTenantOwnersAsync(
        string tenantId,
        string type,
        string title,
        string message,
        string? actionUrl = null,
        CancellationToken cancellationToken = default);

    Task<NotificationListResponse> GetMyNotificationsAsync(
        int limit = 20,
        CancellationToken cancellationToken = default);

    Task<bool> MarkAsReadAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<int> MarkAllAsReadAsync(
        CancellationToken cancellationToken = default);
}
