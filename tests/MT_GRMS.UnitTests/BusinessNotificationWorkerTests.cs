using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Notifications;
using MT_GRMS.Application.Notifications.Dtos;
using MT_GRMS.Application.PurchaseOrders.Dispatch;
using MT_GRMS.Application.Tax;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Entities.HR;
using MT_GRMS.Domain.Entities.Identity;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Domain.Entities.System;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Infrastructure.Persistence;
using MT_GRMS.Infrastructure.Repositories;
using MT_GRMS.Infrastructure.Workers;

namespace MT_GRMS.UnitTests;

public class BusinessNotificationWorkerTests
{
    [Fact]
    public async Task ExpenseReminderQueuesOnceAndDropsVoucherPaidBeforeDelivery()
    {
        var name = Guid.NewGuid().ToString();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var dispatcher = new TestDispatcher(1);
        await using var provider = new ServiceCollection()
            .AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(name))
            .AddSingleton<IConfiguration>(configuration)
            .AddScoped<IUnitOfWork, UnitOfWork>()
            .AddScoped<IRepository<OutboxMessage>, GenericRepository<OutboxMessage>>()
            .AddScoped<NotificationOutbox>()
            .AddSingleton<INotificationRealtimeDispatcher>(dispatcher)
            .BuildServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, HouseholdTaxService.GetVietnamTimeZone()));
        var payable = MakeVoucher("PAYABLE", today.AddDays(-1));
        var upcoming = MakeVoucher("UPCOMING", today.AddDays(2));
        db.ExpenseVouchers.AddRange(payable, upcoming, MakeVoucher("OLD", null), MakeVoucher("CANCELLED", today, ExpenseVoucherStatus.Cancelled));
        db.Users.Add(new User { Id = "expense-owner", UserName = "expense-owner" });
        db.TenantUsers.Add(new TenantUser { TenantId = payable.TenantId, UserId = "expense-owner", Role = "STORE_OWNER" });
        await db.SaveChangesAsync();

        var factory = provider.GetRequiredService<IServiceScopeFactory>();
        var outbox = db.OutboxMessages.IgnoreQueryFilters().AsNoTracking();
        await RunSchedulerAsync(expectedCount: 1);
        Assert.Equal(NotificationOutbox.ExpenseReminderKeyPrefix + payable.Id, (await outbox.SingleAsync()).IdempotencyKey);
        configuration["Notifications:ExpenseDueDaysBefore"] = "2";
        await RunSchedulerAsync(expectedCount: 2);
        Assert.Equal(2, await outbox.CountAsync());

        payable.Status = ExpenseVoucherStatus.Paid;
        payable.PaidAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        using var worker = new BusinessNotificationWorker(factory, NullLogger<BusinessNotificationWorker>.Instance);
        await worker.StartAsync(CancellationToken.None);
        try { await dispatcher.Delivered.Task.WaitAsync(TimeSpan.FromSeconds(10)); }
        finally { await worker.StopAsync(CancellationToken.None); }

        var notice = await db.Notifications.IgnoreQueryFilters().AsNoTracking().SingleAsync();
        Assert.Equal(BusinessNotificationTypes.ExpensePaymentDue, notice.Type);
        Assert.Contains(upcoming.VoucherCode, notice.Message);

        async Task RunSchedulerAsync(int expectedCount)
        {
            using var scheduler = new ExpenseReminderWorker(factory, configuration, NullLogger<ExpenseReminderWorker>.Instance);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await scheduler.StartAsync(CancellationToken.None);
            try
            {
                while (await outbox.CountAsync(timeout.Token) < expectedCount)
                    await Task.Delay(20, timeout.Token);
            }
            finally { await scheduler.StopAsync(CancellationToken.None); }
        }
    }

    private static ExpenseVoucher MakeVoucher(string code, DateOnly? dueDate, ExpenseVoucherStatus status = ExpenseVoucherStatus.Approved) =>
        new() { TenantId = "expense-test", VoucherCode = code, DueDate = dueDate, Status = status };

    [Fact]
    public async Task RoutesToReceivingBranchAndFallsBackToOwnerWhenBranchHasNoStaff()
    {
        var name = Guid.NewGuid().ToString();
        var dispatcher = new TestDispatcher();
        await using var provider = new ServiceCollection()
            .AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(name))
            .AddScoped<IUnitOfWork, UnitOfWork>()
            .AddSingleton<INotificationRealtimeDispatcher>(dispatcher)
            .BuildServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        const string tenant = "tenant-notifications";
        const string otherTenant = "tenant-other";
        var receivingBranch = new Branch { TenantId = tenant, Name = "Receiving", IsActive = true };
        var emptyBranch = new Branch { TenantId = tenant, Name = "Empty", IsActive = true };
        var unrelatedBranch = new Branch { TenantId = tenant, Name = "Other", IsActive = true };
        db.Branches.AddRange(receivingBranch, emptyBranch, unrelatedBranch);
        AddMember("owner", tenant, "STORE_OWNER");
        AddMember("receiver", tenant, "EMPLOYEE", receivingBranch.Id);
        AddMember("other-branch", tenant, "STAFF", unrelatedBranch.Id);
        AddMember("other-tenant", otherTenant, "STAFF", receivingBranch.Id);
        AddMember("inactive", tenant, "STAFF", receivingBranch.Id, active: false);

        var outbox = new NotificationOutbox(new GenericRepository<OutboxMessage>(db));
        await outbox.EnqueueAsync(tenant, Guid.NewGuid(), CreateNotice(receivingBranch.Id), CancellationToken.None);
        await outbox.EnqueueAsync(tenant, Guid.NewGuid(), CreateNotice(emptyBranch.Id), CancellationToken.None);
        await db.SaveChangesAsync();

        using var worker = new BusinessNotificationWorker(
            provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<BusinessNotificationWorker>.Instance);
        await worker.StartAsync(CancellationToken.None);
        try
        {
            await dispatcher.Delivered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }

        var notices = await db.Notifications.IgnoreQueryFilters().AsNoTracking().ToListAsync();
        Assert.Equal(2, notices.Count);
        Assert.All(notices, notice => Assert.Equal(tenant, notice.TenantId));
        Assert.Equal("/employee/transfers/example", Assert.Single(notices, notice => notice.RecipientUserId == "receiver").ActionUrl);
        Assert.Equal("/store-owner/transfers/example", Assert.Single(notices, notice => notice.RecipientUserId == "owner").ActionUrl);
        Assert.All(await db.OutboxMessages.IgnoreQueryFilters().AsNoTracking().ToListAsync(),
            message => Assert.Equal(OutboxStatuses.Processed, message.Status));

        void AddMember(string userId, string tenantId, string role, Guid? branchId = null, bool active = true)
        {
            db.Users.Add(new User { Id = userId, UserName = userId, NormalizedEmail = userId });
            db.TenantUsers.Add(new TenantUser { TenantId = tenantId, UserId = userId, Role = role, IsActive = active });
            if (branchId is Guid id)
                db.Employees.Add(new Employee { TenantId = tenantId, BranchId = id, UserId = userId, EmployeeCode = userId });
        }
    }

    private static BusinessNotification CreateNotice(Guid branchId) => new(
        BusinessNotificationTypes.TransferReceiveRequired, "Chờ nhận hàng", "Phiếu điều chuyển",
        "/store-owner/transfers/example", branchId, "/employee/transfers/example", IncludeOwners: false);

    private sealed class TestDispatcher(int expectedCount = 2) : INotificationRealtimeDispatcher
    {
        private int _count;
        public TaskCompletionSource Delivered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task PushToUsersAsync(string tenantId, IEnumerable<string> recipientUserIds,
            IReadOnlyDictionary<string, NotificationDto> userNotifications, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _count) == expectedCount) Delivered.TrySetResult();
            return Task.CompletedTask;
        }
    }
}
