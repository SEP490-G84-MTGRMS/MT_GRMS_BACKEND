using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Notifications;
using MT_GRMS.Domain.Constants;
using MT_GRMS.Domain.Entities.System;
using MT_GRMS.Infrastructure.Persistence;

namespace MT_GRMS.Infrastructure.Repositories;

public class NotificationRepository : INotificationRepository
{
    private readonly AppDbContext _dbContext;

    public NotificationRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<Notification>> GetByRecipientAsync(
        string tenantId,
        string recipientUserId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var take = Math.Clamp(limit, 1, 100);
        return await _dbContext.Notifications
            .AsNoTracking()
            .Where(n => n.TenantId == tenantId && n.RecipientUserId == recipientUserId)
            .OrderByDescending(n => n.CreatedAt)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> CountUnreadAsync(
        string tenantId,
        string recipientUserId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.Notifications
            .AsNoTracking()
            .CountAsync(n => n.TenantId == tenantId && n.RecipientUserId == recipientUserId && !n.IsRead, cancellationToken);
    }

    public async Task<Notification?> GetByIdIgnoringFilterAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.Notifications
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(n => n.Id == id, cancellationToken);
    }

    public async Task AddRangeAsync(
        IEnumerable<Notification> notifications,
        CancellationToken cancellationToken = default)
    {
        await _dbContext.Notifications.AddRangeAsync(notifications, cancellationToken);
    }

    public async Task<int> MarkAllAsReadAsync(
        string tenantId,
        string recipientUserId,
        DateTime readAtUtc,
        CancellationToken cancellationToken = default)
    {
        var unreadItems = await _dbContext.Notifications
            .Where(n => n.TenantId == tenantId && n.RecipientUserId == recipientUserId && !n.IsRead)
            .ToListAsync(cancellationToken);

        if (unreadItems.Count == 0)
        {
            return 0;
        }

        foreach (var item in unreadItems)
        {
            item.IsRead = true;
            item.ReadAtUtc = readAtUtc;
        }

        return unreadItems.Count;
    }

    public async Task<IReadOnlyList<string>> GetOwnerUserIdsByTenantAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.TenantUsers
            .AsNoTracking()
            .Where(tu => tu.TenantId == tenantId
                      && tu.IsActive
                      && (tu.Role == SystemRoles.StoreOwner || tu.Role == SystemRoles.Admin))
            .Select(tu => tu.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);
    }
}
