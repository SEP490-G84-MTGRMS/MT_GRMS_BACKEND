using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MT_GRMS.Application.PurchaseOrders.Dispatch;
using MT_GRMS.Domain.Entities.System;
using MT_GRMS.Infrastructure.Persistence;

namespace MT_GRMS.Infrastructure.Workers;

/// <summary>
/// Poll Outbox + package SCHEDULED để generate Excel / gửi email (không SMTP trong request API).
/// </summary>
public class SupplierOrderDispatchWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SupplierOrderDispatchWorker> _logger;
    private readonly string _workerId = $"{Environment.MachineName}-{Guid.NewGuid():N}"[..40];

    public SupplierOrderDispatchWorker(IServiceScopeFactory scopeFactory, ILogger<SupplierOrderDispatchWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SupplierOrderDispatchWorker started ({WorkerId}).", _workerId);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "SupplierOrderDispatchWorker loop error.");
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    private async Task ProcessOnceAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var packageService = scope.ServiceProvider.GetRequiredService<ISupplierDispatchPackageService>();

        var now = DateTime.UtcNow;
        var messages = await db.OutboxMessages
            .Where(m => !m.IsDeleted
                && m.Type == OutboxMessageTypes.SupplierOrderPackageProcess
                && (m.Status == OutboxStatuses.Pending || m.Status == OutboxStatuses.Failed)
                && (m.NextAttemptAtUtc == null || m.NextAttemptAtUtc <= now)
                && (m.LockedUntilUtc == null || m.LockedUntilUtc < now))
            .OrderBy(m => m.CreatedAt)
            .Take(10)
            .ToListAsync(ct);

        foreach (var msg in messages)
        {
            // Claim
            msg.Status = OutboxStatuses.Processing;
            msg.LockedBy = _workerId;
            msg.LockedUntilUtc = now.AddMinutes(3);
            msg.AttemptCount += 1;
            await db.SaveChangesAsync(ct);

            try
            {
                var packageId = ExtractPackageId(msg.PayloadJson);
                if (packageId == Guid.Empty)
                {
                    msg.Status = OutboxStatuses.Dead;
                    msg.LastError = "Payload thiếu PackageId.";
                    msg.ProcessedAtUtc = DateTime.UtcNow;
                    await db.SaveChangesAsync(ct);
                    continue;
                }

                await packageService.ProcessPackageAsync(packageId, ct);

                msg.Status = OutboxStatuses.Processed;
                msg.ProcessedAtUtc = DateTime.UtcNow;
                msg.LastError = null;
                msg.LockedUntilUtc = null;
                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                msg.Status = msg.AttemptCount >= 8 ? OutboxStatuses.Dead : OutboxStatuses.Failed;
                msg.LastError = ex.Message.Length > 1900 ? ex.Message[..1900] : ex.Message;
                msg.NextAttemptAtUtc = DateTime.UtcNow.AddSeconds(Math.Min(300, 10 * msg.AttemptCount));
                msg.LockedUntilUtc = null;
                await db.SaveChangesAsync(ct);
                _logger.LogWarning(ex, "Outbox {Id} failed (attempt {Attempt}).", msg.Id, msg.AttemptCount);
            }
        }

        // Đánh thức lịch gửi đã đến giờ (không phụ thuộc outbox còn pending)
        var duePackages = await db.SupplierDispatchPackages
            .Where(p => !p.IsDeleted
                && p.EmailStatus == PackageEmailStatuses.Scheduled
                && p.GenerationStatus == PackageGenerationStatuses.Generated
                && p.ScheduledSendAtUtc != null
                && p.ScheduledSendAtUtc <= now
                && (p.ProcessingLeaseUntilUtc == null || p.ProcessingLeaseUntilUtc < now))
            .OrderBy(p => p.ScheduledSendAtUtc)
            .Take(5)
            .Select(p => p.Id)
            .ToListAsync(ct);

        foreach (var id in duePackages)
        {
            try
            {
                await packageService.ProcessPackageAsync(id, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Scheduled package {Id} send failed.", id);
            }
        }

        // Recovery: package stuck GENERATING / FAILED / PENDING email while outbox already PROCESSED
        var stuckPackages = await db.SupplierDispatchPackages
            .Where(p => !p.IsDeleted
                && (p.ProcessingLeaseUntilUtc == null || p.ProcessingLeaseUntilUtc < now)
                && (
                    (p.EmailStatus == PackageEmailStatuses.Pending
                        && (p.GenerationStatus == PackageGenerationStatuses.Pending
                            || p.GenerationStatus == PackageGenerationStatuses.Failed
                            || p.GenerationStatus == PackageGenerationStatuses.Generating))
                    || (p.EmailStatus == PackageEmailStatuses.Failed
                        && p.GenerationStatus == PackageGenerationStatuses.Generated)
                ))
            .OrderBy(p => p.CreatedAt)
            .Take(5)
            .Select(p => p.Id)
            .ToListAsync(ct);

        foreach (var id in stuckPackages)
        {
            try
            {
                await packageService.ProcessPackageAsync(id, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Stuck package {Id} recovery failed.", id);
            }
        }
    }

    private static Guid ExtractPackageId(string payloadJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            if (doc.RootElement.TryGetProperty("PackageId", out var el)
                && el.TryGetGuid(out var id))
            {
                return id;
            }

            if (doc.RootElement.TryGetProperty("packageId", out var el2)
                && el2.TryGetGuid(out var id2))
            {
                return id2;
            }
        }
        catch
        {
            // ignore
        }

        return Guid.Empty;
    }
}
