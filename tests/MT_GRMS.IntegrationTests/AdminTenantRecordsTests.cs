using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MT_GRMS.Api.Controllers;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Admin.Services;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Infrastructure.Persistence;
using MT_GRMS.Infrastructure.Repositories;

namespace MT_GRMS.IntegrationTests;

public class AdminTenantRecordsTests
{
    [Fact]
    public async Task Records_OnlyExposePlanPurchasesForRequestedTenant()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var context = new AppDbContext(options, new TenantContext());
        foreach (var tenantId in new[] { "target", "other" })
        {
            context.Tenants.Add(new Tenant { Id = tenantId, Name = tenantId, Code = tenantId, Subdomain = tenantId });
            context.Branches.Add(new Branch { TenantId = tenantId, Name = "Private branch", BranchCode = tenantId });
            foreach (var (type, deleted) in new[] {
                ("PURCHASE_PLAN", false), ("TOPUP", false), ("PURCHASE_ADDON", false), ("PURCHASE_PLAN", true) })
                context.WalletTransactions.Add(new WalletTransaction {
                    TenantId = tenantId, TransactionCode = tenantId, Type = type, Status = "SUCCESS", IsDeleted = deleted });
        }
        await context.SaveChangesAsync();
        var controller = new AdminTenantRecordsController(context);
        var response = Assert.IsType<OkObjectResult>(await controller.Get("target"));
        var json = JsonSerializer.SerializeToElement(response.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var data = json.GetProperty("data");
        Assert.Equal(1, data.GetProperty("totalItems").GetInt32());
        var item = Assert.Single(data.GetProperty("items").EnumerateArray());
        Assert.Equal("target", item.GetProperty("code").GetString());
        Assert.False(item.TryGetProperty("balanceAfter", out _));
        Assert.False(item.TryGetProperty("referenceCode", out _));
        foreach (var section in new[] { "owners", "employees", "branches", "transactions", "invoices" })
            Assert.IsType<BadRequestObjectResult>(await controller.Get("target", section));
        Assert.IsType<NotFoundObjectResult>(await controller.Get("missing"));
        context.ChangeTracker.Clear();
        var detail = await new TenantService(new TenantRepository(context), null!).GetByIdAsync("target");
        Assert.Empty(detail.Branches);
        Assert.Equal(1, detail.BranchCount);
    }
}
