using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MT_GRMS.Api.Controllers;
using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Infrastructure.Persistence;

namespace MT_GRMS.IntegrationTests;

public class AdminDashboardOverviewTests
{
    [Fact]
    public async Task Overview_FiltersPeriodAndPurchases_AndAggregatesAcrossTenants()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var context = new AppDbContext(options, new TenantContext());
        var now = DateTime.UtcNow;
        var local = now.AddHours(7);
        var previous = new DateTime(local.Year, local.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddHours(-7).AddDays(-1);
        foreach (var id in new[] { "one", "two" })
        {
            context.Tenants.Add(new Tenant { Id = id, Code = id, Subdomain = id, Name = id, Status = "ACTIVE" });
            context.Branches.Add(new Branch { TenantId = id, Name = id, BranchCode = id });
        }
        foreach (var (type, status, deleted, date) in new[] {
            ("PURCHASE_PLAN", "SUCCESS", false, previous),
            ("TOPUP", "SUCCESS", false, previous),
            ("PURCHASE_ADDON", "SUCCESS", false, previous),
            ("PURCHASE_PLAN", "FAILED", false, previous),
            ("PURCHASE_PLAN", "SUCCESS", true, previous),
            ("PURCHASE_PLAN", "SUCCESS", false, now) })
            context.WalletTransactions.Add(new WalletTransaction { TenantId = "one", Amount = 99000,
                Type = type, Status = status, IsDeleted = deleted, CreatedAt = date });
        await context.SaveChangesAsync();
        var controller = new AdminDashboardOverviewController(context);
        var response = Assert.IsType<OkObjectResult>(await controller.Get("lastMonth"));
        var json = JsonSerializer.SerializeToElement(response.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var data = json.GetProperty("data");
        Assert.Equal(1, data.GetProperty("purchaseCount").GetInt32());
        Assert.Equal(99000, data.GetProperty("purchaseAmount").GetDecimal());
        Assert.Equal(2, data.GetProperty("totalBranches").GetInt32());
        Assert.Equal(2, data.GetProperty("activeTenants").GetInt32());
        Assert.Equal(99000, data.GetProperty("points").EnumerateArray().Sum(p => p.GetProperty("amount").GetDecimal()));
        Assert.IsType<BadRequestObjectResult>(await controller.Get("invalid"));
    }
}
