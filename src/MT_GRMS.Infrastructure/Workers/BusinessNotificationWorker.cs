using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MT_GRMS.Application.Auth;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Notifications;
using MT_GRMS.Application.Notifications.Dtos;
using MT_GRMS.Application.PurchaseOrders.Dispatch;
using MT_GRMS.Domain.Entities.System;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Infrastructure.Persistence;

namespace MT_GRMS.Infrastructure.Workers;

public sealed class BusinessNotificationWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<BusinessNotificationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
        do
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Unable to poll the business notification outbox.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task ProcessBatchAsync(CancellationToken ct)
    {
        using var queryScope = scopeFactory.CreateScope();
        var db = queryScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var candidates = await Pending(db)
            .Where(message => message.NextAttemptAtUtc == null || message.NextAttemptAtUtc <= now)
            .OrderBy(message => message.CreatedAt)
            .ThenBy(message => message.Id)
            .Take(25)
            .Select(message => new { message.Id, message.AttemptCount })
            .ToListAsync(ct);

        foreach (var candidate in candidates)
        {
            try
            {
                await ProcessAsync(candidate.Id, now, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                var attempt = candidate.AttemptCount + 1;
                var retryAt = DateTime.UtcNow.AddSeconds(Math.Min(300, attempt * 10));
                var failed = Pending(db).Where(message => message.Id == candidate.Id && message.AttemptCount == candidate.AttemptCount);
                var status = attempt >= 8 ? OutboxStatuses.Dead : OutboxStatuses.Failed;
                if (db.Database.IsRelational())
                {
                    await failed.ExecuteUpdateAsync(update => update
                        .SetProperty(message => message.AttemptCount, attempt)
                        .SetProperty(message => message.Status, status)
                        .SetProperty(message => message.NextAttemptAtUtc, retryAt)
                        .SetProperty(message => message.LastError, ex.GetType().Name), ct);
                }
                else if (await failed.SingleOrDefaultAsync(ct) is { } message)
                {
                    message.AttemptCount = attempt;
                    message.Status = status;
                    message.NextAttemptAtUtc = retryAt;
                    message.LastError = ex.GetType().Name;
                    await db.SaveChangesAsync(ct);
                    db.ChangeTracker.Clear();
                }
                logger.LogWarning(ex, "Business notification outbox {EventId} failed on attempt {Attempt}.", candidate.Id, attempt);
            }
        }
    }

    private async Task ProcessAsync(Guid id, DateTime now, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<AppDbContext>();
        var unitOfWork = services.GetRequiredService<IUnitOfWork>();
        var notifications = await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            // A competing worker waits for this transaction and then rechecks the pending state.
            // If this process exits before commit, both the claim and notifications roll back.
            db.ChangeTracker.Clear();
            var due = Pending(db)
                .Where(message => message.Id == id && (message.NextAttemptAtUtc == null || message.NextAttemptAtUtc <= now));
            OutboxMessage? message;
            if (db.Database.IsRelational())
            {
                var claimed = await due.ExecuteUpdateAsync(update => update.SetProperty(item => item.Status, OutboxStatuses.Processing), token);
                if (claimed == 0) return Array.Empty<Notification>();
                message = await db.OutboxMessages.IgnoreQueryFilters().SingleAsync(item => item.Id == id, token);
            }
            else
            {
                // The development in-memory database has one worker and no relational transactions.
                message = await due.SingleOrDefaultAsync(token);
                if (message == null) return Array.Empty<Notification>();
            }

            var payload = JsonSerializer.Deserialize<BusinessNotification>(message.PayloadJson)
                ?? throw new InvalidOperationException("Missing business notification payload.");
            IReadOnlyList<Recipient> recipients = Array.Empty<Recipient>();
            if (await IsStillApplicableAsync(db, message.TenantId, payload, token))
            {
                recipients = await GetRecipientsAsync(db, message.TenantId, payload, token);
            }
            var entries = recipients.Select(recipient => new Notification
            {
                TenantId = message.TenantId,
                RecipientUserId = recipient.UserId,
                Type = payload.Type,
                Title = payload.Title,
                Message = payload.Message,
                ActionUrl = recipient.Role == UserRole.Staff ? payload.StaffUrl : payload.OwnerUrl,
                CreatedAt = message.CreatedAt,
            }).ToArray();

            db.Notifications.AddRange(entries);
            message.Status = OutboxStatuses.Processed;
            message.ProcessedAtUtc = DateTime.UtcNow;
            message.LastError = null;
            message.NextAttemptAtUtc = null;
            await db.SaveChangesAsync(token);
            return entries;
        }, ct);

        var dispatcher = services.GetService<INotificationRealtimeDispatcher>();
        if (notifications.Length == 0 || dispatcher == null) return;

        try
        {
            var payloads = notifications.ToDictionary(item => item.RecipientUserId, NotificationDto.FromEntity);
            await dispatcher.PushToUsersAsync(notifications[0].TenantId, payloads.Keys, payloads, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Realtime push failed for business notification outbox {EventId}.", id);
        }
    }

    private static IQueryable<OutboxMessage> Pending(AppDbContext db) => db.OutboxMessages.IgnoreQueryFilters()
        .Where(message => !message.IsDeleted && message.Type == NotificationOutbox.MessageType
            && (message.Status == OutboxStatuses.Pending || message.Status == OutboxStatuses.Failed));

    private static async Task<bool> IsStillApplicableAsync(AppDbContext db, string tenantId, BusinessNotification payload, CancellationToken ct)
    {
        if (payload.ExpenseReminder is { } reminder)
            return await ExpenseReminderWorker.OpenExpenses(db).AnyAsync(voucher => voucher.TenantId == tenantId
                && voucher.Id == reminder.VoucherId && voucher.DueDate == reminder.DueDate, ct);

        if (payload.TaxFilingSnapshotId is Guid snapshotId)
            return await db.HouseholdTaxSnapshots.IgnoreQueryFilters().AnyAsync(row => row.TenantId == tenantId
                && row.Id == snapshotId && !row.IsDeleted && row.Status == TaxSnapshotStatus.Finalized
                && !db.TaxFilingConfirmations.Any(receipt => receipt.TenantId == tenantId && receipt.SnapshotId == snapshotId), ct);

        return true;
    }

    private sealed record Recipient(string UserId, UserRole Role);

    private static async Task<IReadOnlyList<Recipient>> GetRecipientsAsync(AppDbContext db, string tenantId,
        BusinessNotification payload, CancellationToken ct)
    {
        var branchId = payload.StaffBranchId;
        var activeMembers = await db.TenantUsers.AsNoTracking()
            .Where(member => member.TenantId == tenantId && member.IsActive
                && db.Users.Any(user => user.Id == member.UserId && user.IsActive && !user.IsDeleted && !user.IsAdmin))
            .Select(member => new
            {
                member.UserId,
                member.Role,
                IsAtBranch = branchId.HasValue && db.Employees.IgnoreQueryFilters().Any(employee =>
                    employee.TenantId == tenantId && !employee.IsDeleted && employee.Status == EmployeeStatus.Active
                    && employee.BranchId == branchId && employee.UserId == member.UserId)
                    && db.Branches.IgnoreQueryFilters().Any(branch => branch.TenantId == tenantId && branch.Id == branchId
                        && !branch.IsDeleted && branch.IsActive)
            }).ToListAsync(ct);
        var recipients = new List<Recipient>();
        var owners = new List<Recipient>();
        foreach (var member in activeMembers)
        {
            UserRole role;
            try { role = LegacyRoleReader.ReadStoredRole(member.Role); }
            catch (ArgumentException) { continue; }

            if (role == UserRole.StoreOwner) owners.Add(new Recipient(member.UserId, role));
            if (role == UserRole.Staff && member.IsAtBranch) recipients.Add(new Recipient(member.UserId, role));
        }

        if (payload.IncludeOwners || recipients.Count == 0)
            recipients.AddRange(owners);

        return recipients;
    }
}
