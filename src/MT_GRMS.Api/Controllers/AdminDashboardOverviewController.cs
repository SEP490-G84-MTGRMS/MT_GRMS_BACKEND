using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MT_GRMS.Domain.Constants;
using MT_GRMS.Infrastructure.Persistence;

namespace MT_GRMS.Api.Controllers;

[ApiController]
[Route("api/admin/dashboard/overview")]
[Authorize(Roles = RoleConstants.AdminOnly)]
public class AdminDashboardOverviewController(AppDbContext context) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(string period = "thisMonth", CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var local = now.AddHours(7);
        var month = new DateTime(local.Year, local.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddHours(-7);
        var start = period switch
        {
            "thisMonth" => month,
            "lastMonth" => month.AddMonths(-1),
            "thisQuarter" => month.AddMonths(-((local.Month - 1) % 3)),
            "thisYear" => month.AddMonths(1 - local.Month),
            _ => DateTime.MinValue
        };
        if (start == DateTime.MinValue)
            return BadRequest(new { success = false, message = "Khoảng thời gian không hợp lệ." });
        var end = period == "lastMonth" ? month : now;
        var tenants = context.Tenants.AsNoTracking().Where(t => !t.IsDeleted);
        var active = await tenants.CountAsync(t => t.Status == "ACTIVE" && t.LockType == null &&
            (t.SubscriptionExpiresAt == null || t.SubscriptionExpiresAt > now), cancellationToken);
        var total = await tenants.CountAsync(cancellationToken);
        // Global aggregates only. Exclude deleted tenants when bypassing branch isolation.
        var tenantIds = tenants.Select(t => t.Id);
        var branches = context.Branches.IgnoreQueryFilters().AsNoTracking()
            .Where(b => !b.IsDeleted && b.IsActive && tenantIds.Contains(b.TenantId));
        var branchCount = await branches.CountAsync(cancellationToken);
        var newBranches = await branches.CountAsync(b => b.CreatedAt >= start && b.CreatedAt < end, cancellationToken);
        var purchases = await context.WalletTransactions.IgnoreQueryFilters().AsNoTracking()
            .Where(t => !t.IsDeleted && tenantIds.Contains(t.TenantId) && t.Status == "SUCCESS" &&
                t.Type == "PURCHASE_PLAN" && t.CreatedAt >= start && t.CreatedAt < end)
            .Select(t => new { t.CreatedAt, t.Amount }).ToListAsync(cancellationToken);
        var daily = purchases.GroupBy(t => t.CreatedAt.AddHours(7).Date)
            .ToDictionary(g => g.Key, g => g.Sum(t => t.Amount));
        var points = new List<object>();
        for (var day = start.AddHours(7).Date; day <= end.AddHours(7).Date && day < (period == "lastMonth" ? month.AddHours(7).Date : DateTime.MaxValue); day = day.AddDays(1))
            points.Add(new { date = day.ToString("yyyy-MM-dd"), amount = daily.GetValueOrDefault(day) });
        return Ok(new { success = true, data = new
        {
            totalTenants = total, activeTenants = active, totalBranches = branchCount, newBranches,
            averageBranches = total == 0 ? 0 : Math.Round((decimal)branchCount / total, 1),
            activePercentage = total == 0 ? 0 : Math.Round((decimal)active / total * 100, 1),
            purchaseAmount = purchases.Sum(t => t.Amount), purchaseCount = purchases.Count, points
        }});
    }
}
