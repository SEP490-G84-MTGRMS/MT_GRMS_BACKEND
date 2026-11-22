using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MT_GRMS.Api.Controllers;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Notifications;
using MT_GRMS.Application.Reports;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Domain.Entities.Sales;
using MT_GRMS.Domain.Entities.System;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Infrastructure.Persistence;
using MT_GRMS.Infrastructure.Repositories;
using MT_GRMS.Infrastructure.Workers;

namespace MT_GRMS.UnitTests;

public class OperationalReminderTests
{
    private const string TenantId = "tenant-rules";

    [Fact]
    public async Task QueuesDebtAlertOnlyWhenDebtCrossesTenantThreshold()
    {
        var settings = CreateSettings();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var outbox = new NotificationOutbox(new GenericRepository<OutboxMessage>(db), settings);
        var customer = new Customer { TenantId = TenantId, CustomerCode = "KH01", TotalDebt = 110m };
        await outbox.EnqueueDebtThresholdAsync(customer, Guid.NewGuid(), 20m, CancellationToken.None);
        customer.TotalDebt = 120m;
        await outbox.EnqueueDebtThresholdAsync(customer, Guid.NewGuid(), 10m, CancellationToken.None);
        customer.TenantId = "unconfigured";
        await outbox.EnqueueDebtThresholdAsync(customer, Guid.NewGuid(), 30m, CancellationToken.None);
        await db.SaveChangesAsync();

        var message = await db.OutboxMessages.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(TenantId, message.TenantId);
        var payload = JsonSerializer.Deserialize<BusinessNotification>(message.PayloadJson);
        Assert.Equal(BusinessNotificationTypes.CustomerDebtLimitExceeded, payload?.Type);
    }

    [Fact]
    public async Task QueuesWeeklyDropAndUnconfirmedTaxSnapshotOncePerSource()
    {
        var settings = CreateSettings();
        var databaseName = Guid.NewGuid().ToString();
        await using var services = new ServiceCollection()
            .AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(databaseName))
            .AddScoped<ITenantContext, TenantContext>()
            .AddScoped<IUnitOfWork, UnitOfWork>()
            .AddScoped<IRepository<OutboxMessage>, GenericRepository<OutboxMessage>>()
            .AddScoped<IPnLReportRepository, PnLReportRepository>()
            .AddScoped<NotificationOutbox>()
            .AddSingleton<IConfiguration>(settings)
            .BuildServiceProvider();
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var snapshot = new HouseholdTaxSnapshot { TenantId = TenantId, Year = 2026, PeriodType = TaxPeriodType.Quarter, PeriodNumber = 3 };
        var confirmed = new HouseholdTaxSnapshot { TenantId = TenantId, Year = 2026, PeriodType = TaxPeriodType.Month, PeriodNumber = 9 };
        db.Tenants.Add(new Tenant { Id = TenantId });
        db.HouseholdTaxSnapshots.AddRange(snapshot, confirmed);
        db.Orders.AddRange(
            new Order { TenantId = TenantId, Status = "Completed", PaymentStatus = "PAID", TotalAmount = 100m, CreatedAt = new DateTime(2026, 10, 7, 8, 0, 0, DateTimeKind.Utc) },
            new Order { TenantId = TenantId, Status = "Completed", PaymentStatus = "PAID", TotalAmount = 80m, CreatedAt = new DateTime(2026, 10, 14, 8, 0, 0, DateTimeKind.Utc) });
        await db.SaveChangesAsync();

        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        tenant.SetContext("other-tenant", Guid.NewGuid(), null);
        tenant.SetRole("STORE_OWNER");
        var controller = new TaxFilingConfirmationsController(db, tenant);
        Assert.IsType<NotFoundResult>(await controller.Confirm(confirmed.Id, CancellationToken.None));
        tenant.SetContext(TenantId, Guid.NewGuid(), null);
        Assert.IsType<OkObjectResult>(await controller.Confirm(confirmed.Id, CancellationToken.None));
        Assert.IsType<OkObjectResult>(await controller.Confirm(confirmed.Id, CancellationToken.None));
        Assert.Single(await db.TaxFilingConfirmations.ToListAsync());

        await RunWorkerAsync();
        await RunWorkerAsync();
        var messages = await db.OutboxMessages.IgnoreQueryFilters().ToListAsync();
        var payloads = messages.Select(message => JsonSerializer.Deserialize<BusinessNotification>(message.PayloadJson)!).ToList();
        Assert.Equal(2, payloads.Count);
        Assert.Equal(snapshot.Id, Assert.Single(payloads, item => item.Type == BusinessNotificationTypes.TaxFilingReminder).TaxFilingSnapshotId);
        Assert.Single(payloads, item => item.Type == BusinessNotificationTypes.WeeklyRevenueDrop);

        async Task RunWorkerAsync()
        {
            using var worker = new OperationalReminderWorker(services.GetRequiredService<IServiceScopeFactory>(), settings,
                NullLogger<OperationalReminderWorker>.Instance, new FixedClock());
            await worker.StartAsync(CancellationToken.None);
            await worker.StopAsync(CancellationToken.None);
        }
    }

    private static IConfiguration CreateSettings() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        [$"Notifications:OperatingRules:{TenantId}:CustomerDebtLimit"] = "100",
        [$"Notifications:OperatingRules:{TenantId}:WeeklyRevenueDropPercent"] = "15",
        [$"Notifications:OperatingRules:{TenantId}:TaxReminderDaysBefore"] = "14"
    }).Build();

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 20, 8, 0, 0, TimeSpan.Zero);
    }
}
