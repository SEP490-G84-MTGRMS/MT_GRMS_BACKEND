using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Inventory;
using MT_GRMS.Application.Inventory.Dtos;
using MT_GRMS.Application.Reports;
using MT_GRMS.Application.Reports.Dtos;
using MT_GRMS.Application.Tax;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Infrastructure.Persistence;

namespace MT_GRMS.Infrastructure.Services;

public sealed class StoreOwnerDashboardService : IStoreOwnerDashboardService
{
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IPnLReportService _pnl;
    private readonly IInventoryService _inventory;

    public StoreOwnerDashboardService(AppDbContext db, ITenantContext tenant, IPnLReportService pnl, IInventoryService inventory)
    {
        _db = db;
        _tenant = tenant;
        _pnl = pnl;
        _inventory = inventory;
    }

    public async Task<StoreOwnerDashboardResponse?> GetAsync(string period, Guid? branchId, CancellationToken ct = default)
    {
        var tenantId = _tenant.CurrentTenantId;
        if (string.IsNullOrWhiteSpace(tenantId)) return null;

        var branches = await _db.Branches.AsNoTracking()
            .Where(b => b.TenantId == tenantId && !b.IsDeleted)
            .OrderBy(b => b.Name)
            .Select(b => new DashboardBranchDto(b.Id, b.Name))
            .ToListAsync(ct);
        if (branchId.HasValue && !branches.Any(b => b.Id == branchId.Value)) return null;

        var asOfUtc = DateTime.UtcNow;
        var vnZone = HouseholdTaxService.GetVietnamTimeZone();
        var todayVn = TimeZoneInfo.ConvertTimeFromUtc(asOfUtc, vnZone).Date;
        var fromVn = period switch
        {
            "yesterday" => todayVn.AddDays(-1),
            "last7days" => todayVn.AddDays(-6),
            "thisMonth" => new DateTime(todayVn.Year, todayVn.Month, 1),
            _ => todayVn
        };
        var fromUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(fromVn, DateTimeKind.Unspecified), vnZone);
        var toUtc = period == "yesterday"
            ? TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(todayVn, DateTimeKind.Unspecified), vnZone)
            : asOfUtc;
        // The existing P&L repository uses an inclusive upper bound.
        var pnlToUtc = toUtc.AddTicks(-1);
        var summary = await _pnl.GetPnLSummaryAsync(new PnLFilterRequest(branchId, fromUtc, pnlToUtc), ct);
        var previousDuration = toUtc - fromUtc;
        var previous = await _pnl.GetPnLSummaryAsync(
            new PnLFilterRequest(branchId, fromUtc - previousDuration, fromUtc.AddTicks(-1)), ct);
        IReadOnlyList<DashboardRevenueTrendPointDto> trend = period is "today" or "yesterday"
            ? await GetHourlyRevenueTrendAsync(tenantId, branchId, fromUtc, toUtc, fromVn, vnZone, ct)
            : (await _pnl.GetTrendAsync(new PnLFilterRequest(branchId, fromUtc, pnlToUtc), ct))
                .Select(point => new DashboardRevenueTrendPointDto(point.Date, point.Revenue)).ToList();
        decimal? growth = previous.NetRevenue > 0
            ? Math.Round((summary.NetRevenue - previous.NetRevenue) / previous.NetRevenue * 100m, 2, MidpointRounding.AwayFromZero)
            : null;

        var orders = _db.PurchaseOrders.AsNoTracking()
            .Where(po => po.TenantId == tenantId && !po.IsDeleted && (!branchId.HasValue || po.BranchId == branchId.Value));
        var pendingApproval = await orders.CountAsync(po => po.Status == "PENDING_APPROVAL", ct);
        var awaitingReceipt = await orders.CountAsync(po => po.Status == "APPROVED" || po.Status == "DISPATCHED" ||
            po.Status == "SENT" || po.Status == "CONFIRMED" || po.Status == "PARTIALLY_RECEIVED", ct);
        var receivedAmount = await _db.GoodsReceipts.AsNoTracking()
            .Where(grn => grn.TenantId == tenantId && !grn.IsDeleted && grn.Status == "COMPLETED" &&
                grn.ConfirmedAtUtc >= fromUtc && grn.ConfirmedAtUtc < toUtc &&
                (!branchId.HasValue || grn.BranchId == branchId.Value))
            .SumAsync(grn => (decimal?)grn.GrandTotal, ct) ?? 0m;

        var stock = await _inventory.GetPagedStockSummaryAsync(
            new InventoryFilterQuery { BranchId = branchId, Status = "LOW_STOCK", PageIndex = 1, PageSize = 5 }, ct);
        var branchNames = branches.ToDictionary(b => b.Id, b => b.Name);
        var lowStockItems = stock.Items.Select(item => new DashboardStockItemDto(
            item.ProductId, item.BranchId, item.ProductName, item.BranchName,
            item.AvailableQty, item.MinStock, item.Unit)).ToList();
        var expiryCutoffUtc = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(todayVn.AddDays(31), DateTimeKind.Unspecified), vnZone);
        var expiringBatches = await (
            from bs in _db.BranchStocks.AsNoTracking()
            join product in _db.Products.AsNoTracking() on bs.ProductId equals product.Id
            where bs.TenantId == tenantId && product.TenantId == tenantId && !bs.IsDeleted && !product.IsDeleted &&
                (!branchId.HasValue || bs.BranchId == branchId.Value) &&
                bs.QuantityOnHand > 0 && bs.ExpiryDate.HasValue && bs.ExpiryDate.Value < expiryCutoffUtc
            orderby bs.ExpiryDate
            select new { bs.ProductId, bs.BranchId, ProductName = product.Name, bs.BatchCode, bs.ExpiryDate, bs.QuantityOnHand }
        ).Take(5).ToListAsync(ct);
        var expiryItems = expiringBatches.Select(item => new DashboardExpiryItemDto(
            item.ProductId, item.BranchId, item.ProductName,
            branchNames.GetValueOrDefault(item.BranchId, "Chi nhánh"), item.BatchCode,
            item.ExpiryDate!.Value, item.QuantityOnHand)).ToList();

        var employees = _db.Employees.AsNoTracking()
            .Where(e => e.TenantId == tenantId && !e.IsDeleted && (!branchId.HasValue || e.BranchId == branchId.Value));
        var totalEmployees = await employees.CountAsync(ct);
        var activeEmployees = await employees.CountAsync(e => e.Status == EmployeeStatus.Active, ct);

        return new StoreOwnerDashboardResponse(
            new DashboardScopeDto(fromUtc, toUtc, asOfUtc, branchId,
                branchId.HasValue ? branchNames[branchId.Value] : "Toàn bộ chi nhánh"),
            branches,
            new DashboardSalesDto(summary.NetRevenue, summary.TotalOrders, summary.AverageOrderValue,
                growth, trend, (summary.TopProducts ?? new List<TopProductPnLDto>()).Take(5).ToList()),
            new DashboardFinanceDto(summary.GrossProfit, summary.OperatingExpenses.TotalAmount,
                summary.NetProfit, summary.CogsSource, summary.CogsWarningNotice),
            new DashboardProcurementDto(pendingApproval, awaitingReceipt, receivedAmount),
            new DashboardInventoryDto(stock.KpiSummary.LowStockCount, stock.KpiSummary.OutOfStockCount,
                stock.KpiSummary.ExpiryWarningCount, lowStockItems, expiryItems),
            new DashboardEmployeesDto(totalEmployees, activeEmployees));
    }

    private async Task<IReadOnlyList<DashboardRevenueTrendPointDto>> GetHourlyRevenueTrendAsync(
        string tenantId, Guid? branchId, DateTime fromUtc, DateTime toUtc, DateTime dayVn,
        TimeZoneInfo vnZone, CancellationToken ct)
    {
        var orders = await _db.Orders.AsNoTracking()
            .Where(o => o.TenantId == tenantId && !o.IsDeleted &&
                (!branchId.HasValue || o.BranchId == branchId.Value) &&
                o.CreatedAt >= fromUtc && o.CreatedAt < toUtc &&
                (o.PaymentStatus == "PAID" || o.PaymentStatus == "Paid" ||
                 o.PaymentStatus == "DEBT" || o.PaymentStatus == "Debt") &&
                o.Status != "Cancelled")
            .Select(o => new { o.CreatedAt, o.TotalAmount, o.DiscountAmount })
            .ToListAsync(ct);
        var returns = await _db.CustomerDebtEntries.AsNoTracking()
            .Where(e => e.TenantId == tenantId && !e.IsDeleted &&
                (!branchId.HasValue || e.BranchId == branchId.Value) &&
                e.EntryType == CustomerDebtType.GoodsReturnOffset &&
                e.OccurredAtUtc >= fromUtc && e.OccurredAtUtc < toUtc)
            .Select(e => new { e.OccurredAtUtc, e.Amount })
            .ToListAsync(ct);

        int HourInVietnam(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(utc, DateTimeKind.Utc), vnZone).Hour;
        var revenueByHour = orders.GroupBy(o => HourInVietnam(o.CreatedAt))
            .ToDictionary(group => group.Key, group => group.Sum(o => o.TotalAmount - o.DiscountAmount));
        var returnsByHour = returns.GroupBy(e => HourInVietnam(e.OccurredAtUtc))
            .ToDictionary(group => group.Key, group => group.Sum(e => e.Amount));

        return Enumerable.Range(0, 24).Select(hour => new DashboardRevenueTrendPointDto(
            TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(dayVn.AddHours(hour), DateTimeKind.Unspecified), vnZone),
            revenueByHour.GetValueOrDefault(hour) - returnsByHour.GetValueOrDefault(hour))).ToList();
    }
}
