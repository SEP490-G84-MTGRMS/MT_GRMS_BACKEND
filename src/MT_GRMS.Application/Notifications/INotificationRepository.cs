using MT_GRMS.Domain.Entities.System;

namespace MT_GRMS.Application.Notifications;

public interface INotificationRepository
{
    Task<IReadOnlyList<Notification>> GetByRecipientAsync(
        string tenantId,
        string recipientUserId,
        int limit,
        CancellationToken cancellationToken = default);

    Task<int> CountUnreadAsync(
        string tenantId,
        string recipientUserId,
        CancellationToken cancellationToken = default);

    Task<Notification?> GetByIdIgnoringFilterAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task AddRangeAsync(
        IEnumerable<Notification> notifications,
        CancellationToken cancellationToken = default);

    Task<int> MarkAllAsReadAsync(
        string tenantId,
        string recipientUserId,
        DateTime readAtUtc,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetOwnerUserIdsByTenantAsync(
        string tenantId,
        CancellationToken cancellationToken = default);
}
