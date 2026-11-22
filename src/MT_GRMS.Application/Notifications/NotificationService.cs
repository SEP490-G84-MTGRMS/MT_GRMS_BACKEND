using MT_GRMS.Application.Common;
using MT_GRMS.Application.Notifications.Dtos;
using MT_GRMS.Domain.Entities.System;
using MT_GRMS.Domain.Exceptions;

namespace MT_GRMS.Application.Notifications;

public class NotificationService : INotificationService
{
    private readonly INotificationRepository _notificationRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITenantContext _tenantContext;
    private readonly INotificationRealtimeDispatcher? _realtimeDispatcher;

    public NotificationService(
        INotificationRepository notificationRepository,
        IUnitOfWork unitOfWork,
        ITenantContext tenantContext,
        INotificationRealtimeDispatcher? realtimeDispatcher = null)
    {
        _notificationRepository = notificationRepository;
        _unitOfWork = unitOfWork;
        _tenantContext = tenantContext;
        _realtimeDispatcher = realtimeDispatcher;
    }

    public async Task CreateAndPushAsync(
        string tenantId,
        IEnumerable<string> recipientUserIds,
        string type,
        string title,
        string message,
        string? actionUrl = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return;
        }

        var distinctUsers = recipientUserIds
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Select(u => u.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (distinctUsers.Count == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var entities = distinctUsers.Select(userId => new Notification
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            RecipientUserId = userId,
            Type = type,
            Title = title.Length > 200 ? title[..200] : title,
            Message = message.Length > 500 ? message[..500] : message,
            ActionUrl = actionUrl,
            IsRead = false,
            CreatedAt = now,
        }).ToList();

        await _notificationRepository.AddRangeAsync(entities, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (_realtimeDispatcher == null)
        {
            return;
        }

        try
        {
            var payloadMap = entities.ToDictionary(
                e => e.RecipientUserId,
                NotificationDto.FromEntity,
                StringComparer.Ordinal);
            await _realtimeDispatcher.PushToUsersAsync(tenantId, distinctUsers, payloadMap, cancellationToken);
        }
        catch
        {
            // Bỏ qua lỗi đẩy real-time để không ảnh hưởng giao dịch nghiệp vụ chính
        }
    }

    public async Task NotifyTenantOwnersAsync(
        string tenantId,
        string type,
        string title,
        string message,
        string? actionUrl = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return;
        }

        var ownerIds = await _notificationRepository.GetOwnerUserIdsByTenantAsync(tenantId, cancellationToken);
        if (ownerIds.Count == 0)
        {
            return;
        }

        await CreateAndPushAsync(tenantId, ownerIds, type, title, message, actionUrl, cancellationToken);
    }

    public async Task<NotificationListResponse> GetMyNotificationsAsync(
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.CurrentTenantId;
        var userId = _tenantContext.CurrentRawUserId;
        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(userId))
        {
            return new NotificationListResponse(Array.Empty<NotificationDto>(), 0);
        }

        var items = await _notificationRepository.GetByRecipientAsync(tenantId, userId, limit, cancellationToken);
        var unreadCount = await _notificationRepository.CountUnreadAsync(tenantId, userId, cancellationToken);

        return new NotificationListResponse(items.Select(NotificationDto.FromEntity).ToList(), unreadCount);
    }

    public async Task<bool> MarkAsReadAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.CurrentTenantId;
        var userId = _tenantContext.CurrentRawUserId;
        var notification = await _notificationRepository.GetByIdIgnoringFilterAsync(id, cancellationToken);

        if (notification == null || notification.IsDeleted)
        {
            throw new NotFoundException($"Không tìm thấy thông báo với ID: {id}");
        }

        if (!string.Equals(notification.TenantId, tenantId, StringComparison.Ordinal) ||
            !string.Equals(notification.RecipientUserId, userId, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("Bạn không có quyền thao tác trên thông báo này.");
        }

        if (!notification.IsRead)
        {
            notification.IsRead = true;
            notification.ReadAtUtc = DateTime.UtcNow;
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return true;
    }

    public async Task<int> MarkAllAsReadAsync(CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.CurrentTenantId;
        var userId = _tenantContext.CurrentRawUserId;
        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(userId))
        {
            return 0;
        }

        var updated = await _notificationRepository.MarkAllAsReadAsync(tenantId, userId, DateTime.UtcNow, cancellationToken);
        if (updated > 0)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return updated;
    }

}
