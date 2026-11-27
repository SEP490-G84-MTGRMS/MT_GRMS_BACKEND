using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Notifications;
using MT_GRMS.Application.Reports;
using MT_GRMS.Application.Tax;
using MT_GRMS.Domain.Constants;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Infrastructure.Persistence;

namespace MT_GRMS.Infrastructure.Workers;

public sealed class OperationalReminderWorker(
    IServiceScopeFactory scopes, IConfiguration settings, ILogger<OperationalReminderWorker> logger,
    TimeProvider? timeProvider = null) : BackgroundService
{
    private const long SchedulerLockId = 0x4D5447524D531019;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        do
        {
            var tenants = settings.GetSection("Notifications:OperatingRules").GetChildren();
            foreach (var rules in tenants)
            {
                try { await EvaluateAsync(rules, stoppingToken); }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogError(ex, "Unable to evaluate operational notifications for tenant {TenantId}.", rules.Key);
                }
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task EvaluateAsync(IConfigurationSection rules, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var services = scope.ServiceProvider;
        var tenant = services.GetRequiredService<ITenantContext>();
        tenant.SetContext(rules.Key, Guid.Empty, null);
        tenant.SetRole(SystemRoles.StoreOwner);
        var db = services.GetRequiredService<AppDbContext>();
        if (!await db.Tenants.AsNoTracking().AnyAsync(item => item.Id == rules.Key && !item.IsDeleted && item.Status == "ACTIVE", ct)) return;

        var outbox = services.GetRequiredService<NotificationOutbox>();
        var zone = HouseholdTaxService.GetVietnamTimeZone();
        var utcNow = (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utcNow, zone));
        await services.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(async token =>
        {
            if (db.Database.IsNpgsql() && !await db.Database.SqlQuery<bool>(
                    $"SELECT pg_try_advisory_xact_lock({SchedulerLockId}) AS \"Value\"").SingleAsync(token)) return;

            if (decimal.TryParse(rules["WeeklyRevenueDropPercent"], NumberStyles.Number, CultureInfo.InvariantCulture, out var threshold)
                && threshold is > 0 and <= 100 && outbox.IsEnabled(BusinessNotificationTypes.WeeklyRevenueDrop))
            {
                await QueueRevenueAlertAsync(db, services.GetRequiredService<IPnLReportRepository>(), outbox,
                    rules.Key, today, zone, threshold, token);
            }

            if (int.TryParse(rules["TaxReminderDaysBefore"], out var daysBefore) && daysBefore is >= 0 and <= 30
                && outbox.IsEnabled(BusinessNotificationTypes.TaxFilingReminder))
            {
                await QueueTaxRemindersAsync(db, outbox, rules.Key, today, daysBefore, token);
            }

            await db.SaveChangesAsync(token);
        }, ct);
    }

    private static async Task QueueRevenueAlertAsync(AppDbContext db, IPnLReportRepository reports, NotificationOutbox outbox,
        string tenantId, DateOnly today, TimeZoneInfo zone, decimal threshold, CancellationToken ct)
    {
        var daysSinceMonday = ((int)today.DayOfWeek + 6) % 7;
        var weekEnd = today.AddDays(-daysSinceMonday);
        var sourceId = new Guid(weekEnd.DayNumber, 0, 0, new byte[8]);
        if (await AlreadyQueuedAsync(db, tenantId, BusinessNotificationTypes.WeeklyRevenueDrop, sourceId, ct)) return;

        DateTime StartUtc(DateOnly date) => TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.MinValue), zone);
        var start = StartUtc(weekEnd.AddDays(-7));
        var previous = await reports.GetRevenueAsync(null, StartUtc(weekEnd.AddDays(-14)), start.AddTicks(-1), ct);
        var previousNet = previous.GrossRevenue - previous.Discounts - previous.SalesReturns;
        if (previousNet <= 0) return;

        var current = await reports.GetRevenueAsync(null, start, StartUtc(weekEnd).AddTicks(-1), ct);
        var currentNet = Math.Max(0, current.GrossRevenue - current.Discounts - current.SalesReturns);
        var decrease = 100m * (previousNet - currentNet) / previousNet;
        if (decrease < threshold) return;

        await outbox.EnqueueAsync(tenantId, sourceId, new BusinessNotification(BusinessNotificationTypes.WeeklyRevenueDrop,
            "Doanh thu tuần giảm qua ngưỡng", $"Doanh thu thuần tuần {weekEnd.AddDays(-7):dd/MM}–{weekEnd.AddDays(-1):dd/MM/yyyy} giảm {decrease:N1}% so với tuần trước.",
            FormattableString.Invariant($"/store-owner/finance?tab=pnl&startDate={weekEnd.AddDays(-7):yyyy-MM-dd}&endDate={weekEnd.AddDays(-1):yyyy-MM-dd}")), ct);
    }

    private static async Task QueueTaxRemindersAsync(AppDbContext db, NotificationOutbox outbox, string tenantId,
        DateOnly today, int daysBefore, CancellationToken ct)
    {
        var snapshots = await db.HouseholdTaxSnapshots.AsNoTracking()
            .Where(row => row.TenantId == tenantId && row.BranchId == null && row.Status == TaxSnapshotStatus.Finalized
                && row.Year >= today.Year - 1 && row.Year <= today.Year
                && !db.TaxFilingConfirmations.Any(receipt => receipt.TenantId == tenantId && receipt.SnapshotId == row.Id))
            .Select(row => new { row.Id, row.Year, row.PeriodType, row.PeriodNumber, row.IsTaxExempt, row.AppliedPitMethod }).ToListAsync(ct);

        foreach (var row in snapshots)
        {
            var isAnnualPitSettlement = row.PeriodType == TaxPeriodType.Year
                && !row.IsTaxExempt
                && row.AppliedPitMethod == TaxCalculationMethod.DeclarationBased;
            var deadline = DateOnly.FromDateTime(HouseholdTaxService.GetFilingDeadlineLocal(
                row.Year, row.PeriodType, row.PeriodNumber, isAnnualPitSettlement));
            if (deadline < today || deadline > today.AddDays(daysBefore)) continue;
            if (await AlreadyQueuedAsync(db, tenantId, BusinessNotificationTypes.TaxFilingReminder, row.Id, ct)) continue;

            var period = row.PeriodType switch
            {
                TaxPeriodType.Month => $"tháng {row.PeriodNumber}/{row.Year}",
                TaxPeriodType.Quarter => $"quý {row.PeriodNumber}/{row.Year}",
                _ => $"năm {row.Year}"
            };
            await outbox.EnqueueAsync(tenantId, row.Id, new BusinessNotification(BusinessNotificationTypes.TaxFilingReminder,
                "Kiểm tra hồ sơ thuế sắp đến hạn", $"Hồ sơ {period} có ngày nộp dự kiến {deadline:dd/MM/yyyy}. Hãy đối chiếu lịch áp dụng của cửa hàng.",
                $"/store-owner/tax-snapshots?snapshotId={row.Id}", TaxFilingSnapshotId: row.Id), ct);
        }
    }

    private static Task<bool> AlreadyQueuedAsync(AppDbContext db, string tenantId, string type, Guid sourceId, CancellationToken ct)
    {
        var key = NotificationOutbox.BuildKey(type, sourceId);
        return db.OutboxMessages.IgnoreQueryFilters().AnyAsync(row => row.TenantId == tenantId && row.IdempotencyKey == key, ct);
    }
}
