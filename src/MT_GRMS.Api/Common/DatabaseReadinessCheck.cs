using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MT_GRMS.Infrastructure.Persistence;

namespace MT_GRMS.Api.Common;

public sealed class DatabaseReadinessCheck(AppDbContext database) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!await database.Database.CanConnectAsync(cancellationToken))
            return HealthCheckResult.Unhealthy("Database connection failed.");

        if (database.Database.IsNpgsql())
        {
            await database.Branches.IgnoreQueryFilters().Select(branch => branch.ConversionRulesJson)
                .Take(1).ToListAsync(cancellationToken);
            await database.ExpenseVouchers.IgnoreQueryFilters().Select(voucher => voucher.DueDate)
                .Take(1).ToListAsync(cancellationToken);
            await database.TaxFilingConfirmations.Select(confirmation => confirmation.ConfirmedAtUtc)
                .Take(1).ToListAsync(cancellationToken);
            await database.Notifications.IgnoreQueryFilters().Select(notification => notification.Id)
                .Take(1).ToListAsync(cancellationToken);
        }

        return HealthCheckResult.Healthy();
    }
}
