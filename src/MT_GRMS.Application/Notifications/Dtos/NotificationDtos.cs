using MT_GRMS.Domain.Entities.System;

namespace MT_GRMS.Application.Notifications.Dtos;

public record NotificationDto(
    Guid Id,
    string Type,
    string Title,
    string Message,
    string? ActionUrl,
    bool IsRead,
    DateTime CreatedAt)
{
    public static NotificationDto FromEntity(Notification notification) => new(
        notification.Id, notification.Type, notification.Title, notification.Message, notification.ActionUrl,
        notification.IsRead, notification.CreatedAt.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(notification.CreatedAt, DateTimeKind.Utc)
            : notification.CreatedAt.ToUniversalTime());
}

public record NotificationListResponse(
    IReadOnlyList<NotificationDto> Items,
    int UnreadCount);
