using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Notifications;
using MT_GRMS.Application.Tax;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Infrastructure.Persistence;

namespace MT_GRMS.Infrastructure.Workers;

public sealed class ExpenseReminderWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<ExpenseReminderWorker> logger) : BackgroundService
{
    private const long SchedulerLockId = 0x4D5447524D530701;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        do
        {
            try
            {
                if (!(bool.TryParse(configuration[$"Notifications:BusinessEvents:{BusinessNotificationTypes.ExpensePaymentDue}"], out var enabled) && !enabled))
                {
                    var vietnamNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, HouseholdTaxService.GetVietnamTimeZone());
                    await EnqueueDueExpensesAsync(DateOnly.FromDateTime(vietnamNow), stoppingToken);
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Unable to schedule expense payment reminders.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task EnqueueDueExpensesAsync(DateOnly today, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var outbox = scope.ServiceProvider.GetRequiredService<NotificationOutbox>();
        var daysBefore = int.TryParse(configuration["Notifications:ExpenseDueDaysBefore"], out var days) ? Math.Clamp(days, 0, 30) : 0;
        var cutoff = today.AddDays(daysBefore);

        await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            // Serialize schedulers on PostgreSQL until the outbox inserts commit.
            if (db.Database.IsNpgsql())
            {
                var locked = await db.Database.SqlQuery<bool>($"SELECT pg_try_advisory_xact_lock({SchedulerLockId}) AS \"Value\"")
                    .SingleAsync(token);
                if (!locked) return;
            }

            var expenses = await OpenExpenses(db)
                .Where(voucher => voucher.DueDate <= cutoff && !db.OutboxMessages.IgnoreQueryFilters().Any(message =>
                    message.TenantId == voucher.TenantId && message.IdempotencyKey == NotificationOutbox.ExpenseReminderKeyPrefix + voucher.Id.ToString()))
                .OrderBy(voucher => voucher.DueDate).ThenBy(voucher => voucher.Id).Take(100)
                .Select(voucher => new { voucher.Id, voucher.TenantId, voucher.VoucherCode, voucher.BranchId, voucher.DueDate })
                .ToListAsync(token);

            foreach (var voucher in expenses)
            {
                var dueDate = voucher.DueDate!.Value;
                var title = dueDate < today ? "Phiếu chi quá hạn thanh toán"
                    : dueDate == today ? "Phiếu chi đến hạn hôm nay" : "Phiếu chi sắp đến hạn";
                await outbox.EnqueueAsync(voucher.TenantId, voucher.Id, new BusinessNotification(
                    BusinessNotificationTypes.ExpensePaymentDue, title,
                    $"Phiếu chi {voucher.VoucherCode}, hạn thanh toán {dueDate:dd/MM/yyyy}.",
                    $"/store-owner/expenses?branchId={voucher.BranchId?.ToString() ?? "ALL"}&search={Uri.EscapeDataString(voucher.VoucherCode)}",
                    ExpenseReminder: new ExpenseReminderSource(voucher.Id, dueDate)), token);
            }

            await unitOfWork.SaveChangesAsync(token);
        }, ct);
    }

    internal static IQueryable<ExpenseVoucher> OpenExpenses(AppDbContext db) => db.ExpenseVouchers.IgnoreQueryFilters()
        .Where(voucher => !voucher.IsDeleted && voucher.Nature == FinancialNature.OperatingExpense
            && voucher.DueDate != null && !voucher.IsSettled && voucher.PaidAtUtc == null
            && (voucher.Status == ExpenseVoucherStatus.PendingApproval || voucher.Status == ExpenseVoucherStatus.Approved
                || voucher.Status == ExpenseVoucherStatus.PaymentFailed));
}
