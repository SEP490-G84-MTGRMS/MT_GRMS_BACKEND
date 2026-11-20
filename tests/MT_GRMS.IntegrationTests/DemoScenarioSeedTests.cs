using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Common;
using MT_GRMS.Infrastructure.Persistence;

namespace MT_GRMS.IntegrationTests;

public class DemoScenarioSeedTests
{
    [Fact]
    public async Task Seed_CreatesConsistentScenarios_AndPreservesChangesOnSecondRun()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new AppDbContext(options, new TenantContext());
        await DbInitializer.SeedDemoAsync(db);
        Assert.Equal(1, await db.Users.CountAsync(u => u.IsAdmin));
        Assert.Equal(8, await db.Tenants.CountAsync());
        Assert.Equal(12, await db.Branches.IgnoreQueryFilters().CountAsync());
        Assert.Equal(33, await db.Employees.IgnoreQueryFilters().CountAsync());
        Assert.Equal(77, await db.Orders.IgnoreQueryFilters().CountAsync());
        Assert.Equal(77, await db.Orders.IgnoreQueryFilters().Select(o => o.OrderCode).Distinct().CountAsync());
        var pro = await db.Tenants.SingleAsync(t => t.Id == "thviet-demo-pro-extra");
        Assert.Equal("PRO", pro.CurrentPlan);
        Assert.Equal(1, pro.ExtraBranches);
        var trial = await db.Tenants.SingleAsync(t => t.Id == "thviet-demo-trial");
        Assert.Equal(TimeSpan.FromDays(3), trial.TrialExpiresAt - trial.TrialStartedAt);
        Assert.Null((await db.Tenants.SingleAsync(t => t.Id == "thviet-demo-new")).CurrentPlan);
        Assert.Equal(6, await db.WalletTransactions.CountAsync(t => t.Type == "PURCHASE_PLAN"));
        foreach (var tenant in await db.Tenants.ToListAsync())
        {
            var transactions = await db.WalletTransactions.Where(t => t.TenantId == tenant.Id).OrderBy(t => t.CreatedAt).ToListAsync();
            decimal balance = 0;
            foreach (var transaction in transactions)
            {
                Assert.Equal(balance, transaction.BalanceBefore);
                balance += transaction.Type == "TOPUP" ? transaction.Amount : -transaction.Amount;
                Assert.Equal(balance, transaction.BalanceAfter);
            }
            Assert.Equal(tenant.WalletBalance, balance);
        }
        pro.Name = "Tên cửa hàng đã sửa";
        var plan = await db.SubscriptionPlans.SingleAsync(p => p.PlanCode == "PRO");
        plan.PriceMonthly = 300000;
        await db.SaveChangesAsync();
        await DbInitializer.SeedDemoAsync(db);
        Assert.Equal(8, await db.Tenants.CountAsync());
        Assert.Equal(77, await db.Orders.IgnoreQueryFilters().CountAsync());
        Assert.Equal("Tên cửa hàng đã sửa", pro.Name);
        Assert.Equal(300000, plan.PriceMonthly);
    }
}
