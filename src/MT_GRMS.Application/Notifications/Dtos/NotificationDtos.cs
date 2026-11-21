namespace MT_GRMS.Application.Notifications.Dtos;

public record NotificationDto(
    Guid Id,
    string Type,
    string Title,
    string Message,
    string? ActionUrl,
    bool IsRead,
    DateTime CreatedAt);

public record NotificationListResponse(
    IReadOnlyList<NotificationDto> Items,
    int UnreadCount);
