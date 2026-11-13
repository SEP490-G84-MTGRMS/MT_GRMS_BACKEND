using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Inventory;
using MT_GRMS.Application.Inventory.Dtos;
using MT_GRMS.Domain.Entities.Inventory;
using MT_GRMS.Infrastructure.Persistence;

namespace MT_GRMS.Infrastructure.Repositories;

public class InventoryRepository : GenericRepository<StockOnHand>, IInventoryRepository
{
    private readonly AppDbContext _appContext;

    public InventoryRepository(AppDbContext context) : base(context)
    {
        _appContext = context;
    }

    public async Task<InventoryPagedResultDto> GetPagedStockSummaryAsync(
        string tenantId,
        InventoryFilterQuery query,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow.Date;

        var branches = await _appContext.Branches.AsNoTracking()
            .Where(b => b.TenantId == tenantId && !b.IsDeleted)
            .ToListAsync(cancellationToken);

        if (query.BranchId.HasValue && query.BranchId.Value != Guid.Empty)
        {
            branches = branches.Where(b => b.Id == query.BranchId.Value).ToList();
        }

        var branchIds = branches.Select(b => b.Id).ToList();

        // Catalog theo chi nhánh: hiện mọi SP đã gán CN, kể cả chưa có BranchStock.
        var branchCatalogQuery = _appContext.BranchProducts.AsNoTracking()
            .Where(bp =>
                bp.TenantId == tenantId
                && !bp.IsDeleted
                && branchIds.Contains(bp.BranchId)
                && bp.Status != "Inactive"
                && bp.Status != "Deleted");

        var catalogPairs = await branchCatalogQuery
            .Select(bp => new { bp.BranchId, bp.ProductId })
            .Distinct()
            .ToListAsync(cancellationToken);

        // Gộp thêm SP đang có tồn nhưng chưa (hoặc không còn) trong catalog để không mất dòng tồn thực tế.
        var stockOnlyPairs = await _appContext.BranchStocks.AsNoTracking()
            .Where(bs =>
                bs.TenantId == tenantId
                && !bs.IsDeleted
                && branchIds.Contains(bs.BranchId)
                && (bs.QuantityOnHand > 0 || bs.QuantityReserved > 0 || bs.QuantityQuarantined > 0))
            .Select(bs => new { bs.BranchId, bs.ProductId })
            .Distinct()
            .ToListAsync(cancellationToken);

        var pairSet = catalogPairs
            .Concat(stockOnlyPairs)
            .GroupBy(x => (x.BranchId, x.ProductId))
            .Select(g => g.First())
            .ToList();
        catalogPairs = pairSet;

        var productIds = catalogPairs.Select(x => x.ProductId).Distinct().ToList();

        var productsQuery = _appContext.Products.AsNoTracking()
            .Include(p => p.Category)
            .Where(p => p.TenantId == tenantId && !p.IsDeleted && productIds.Contains(p.Id));

        if (!string.IsNullOrWhiteSpace(query.Category) && !query.Category.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            productsQuery = productsQuery.Where(p => p.Category != null && p.Category.Name == query.Category);
        }

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var kw = query.Keyword.Trim().ToLower();
            productsQuery = productsQuery.Where(p =>
                p.ProductCode.ToLower().Contains(kw) ||
                (p.Barcode != null && p.Barcode.ToLower().Contains(kw)) ||
                p.Name.ToLower().Contains(kw));
        }

        var products = await productsQuery.ToListAsync(cancellationToken);
        var productById = products.ToDictionary(p => p.Id);
        productIds = products.Select(p => p.Id).ToList();

        var branchStocks = productIds.Count == 0
            ? new List<BranchStock>()
            : await _appContext.BranchStocks.AsNoTracking()
                .Where(bs =>
                    bs.TenantId == tenantId
                    && !bs.IsDeleted
                    && branchIds.Contains(bs.BranchId)
                    && productIds.Contains(bs.ProductId))
                .ToListAsync(cancellationToken);

        var pendingReturnLines = productIds.Count == 0
            ? new List<(Guid BranchId, Guid ProductId, decimal RequestedQty)>()
            : (await _appContext.SupplierReturnLines.AsNoTracking()
                .Include(l => l.SupplierReturn)
                .Where(l => l.TenantId == tenantId && productIds.Contains(l.ProductId) &&
                            l.SupplierReturn != null &&
                            (l.SupplierReturn.Status == "PENDING_SUPPLIER" || l.SupplierReturn.Status == "APPROVED"))
                .Select(l => new { l.SupplierReturn!.BranchId, l.ProductId, l.RequestedQty })
                .ToListAsync(cancellationToken))
                .Select(x => (x.BranchId, x.ProductId, x.RequestedQty))
                .ToList();

        var allItems = new List<InventoryStockItemDto>();
        var branchById = branches.ToDictionary(b => b.Id);

        foreach (var pair in catalogPairs)
        {
            if (!productById.TryGetValue(pair.ProductId, out var product))
                continue;
            if (!branchById.TryGetValue(pair.BranchId, out var branch))
                continue;

            var stocks = branchStocks
                .Where(bs => bs.BranchId == branch.Id && bs.ProductId == product.Id)
                .ToList();

            var physicalQty = stocks.Sum(s => s.QuantityOnHand);
            var reservedQty = stocks.Sum(s => s.QuantityReserved);
            var quarantinedQty = stocks.Sum(s => s.QuantityQuarantined);
            var expiredQty = stocks
                .Where(s => s.ExpiryDate.HasValue && s.ExpiryDate.Value.Date <= now)
                .Sum(s => s.QuantityOnHand);
            var availableQty = BranchStockAvailability.SumSellableAvailable(stocks, now);
            var pendingQty = pendingReturnLines
                .Where(l => l.BranchId == branch.Id && l.ProductId == product.Id)
                .Sum(l => l.RequestedQty);

            var minStock = product.MinStock > 0 ? product.MinStock : 10;
            var weightedCost = physicalQty > 0
                ? stocks.Sum(s => s.QuantityOnHand * s.LandedCostPerUnit) / physicalQty
                : (product.CostPrice ?? 0m);
            if (weightedCost <= 0)
            {
                weightedCost = product.CostPrice ?? (product.SellingPrice * 0.7m);
            }

            var totalValue = physicalQty * weightedCost;

            var batchDtos = stocks
                .OrderBy(s => s.ExpiryDate ?? DateTime.MaxValue)
                .Select(s =>
                {
                    var days = s.ExpiryDate.HasValue ? (s.ExpiryDate.Value.Date - now).Days : 9999;
                    var bStatus = !s.ExpiryDate.HasValue
                        ? "Normal"
                        : days switch
                        {
                            <= 0 => "Expired",
                            <= 3 => "Critical",
                            <= 30 => "Warning",
                            _ => "Normal"
                        };
                    return new InventoryBatchSummaryDto(
                        s.Id,
                        string.IsNullOrWhiteSpace(s.BatchCode) ? "—" : s.BatchCode!,
                        s.ManufactureDate ?? DateTime.MinValue,
                        s.ExpiryDate ?? DateTime.MaxValue.Date,
                        days,
                        BranchStockAvailability.GetSellableAvailable(s, now),
                        bStatus
                    );
                })
                .ToList();

            string status;
            if (availableQty <= 0) status = "OUT_OF_STOCK";
            else if (availableQty <= minStock) status = "LOW_STOCK";
            else if (batchDtos.Any(b => b.DaysLeft <= 30 && b.DaysLeft < 9999)) status = "NEAR_EXPIRY";
            else status = "OK";

            allItems.Add(new InventoryStockItemDto(
                ProductId: product.Id,
                Sku: product.ProductCode,
                Barcode: product.Barcode ?? string.Empty,
                ProductName: product.Name,
                CategoryName: product.Category?.Name ?? "Chung",
                BranchId: branch.Id,
                BranchName: branch.Name,
                Unit: product.BaseUnit,
                PhysicalQty: physicalQty,
                AvailableQty: availableQty,
                PendingReturnQty: pendingQty,
                ExpiredQty: expiredQty,
                DamagedQty: 0,
                QuarantinedQty: quarantinedQty,
                ReservedQty: reservedQty,
                MinStock: minStock,
                AvgCost: Math.Round(weightedCost, 2),
                TotalValue: Math.Round(totalValue, 2),
                Status: status,
                BatchCount: stocks.Count,
                Batches: batchDtos
            ));
        }

        var kpis = new InventoryKpiSummaryDto(
            TotalSkus: allItems.Select(x => x.ProductId).Distinct().Count(),
            TotalValue: allItems.Sum(x => x.TotalValue),
            LowStockCount: allItems.Count(x => x.Status == "LOW_STOCK"),
            OutOfStockCount: allItems.Count(x => x.Status == "OUT_OF_STOCK"),
            ExpiryWarningCount: allItems.Count(x => x.Status == "NEAR_EXPIRY" || x.ExpiredQty > 0)
        );

        if (!string.IsNullOrWhiteSpace(query.Status) && !query.Status.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            allItems = allItems.Where(x => x.Status.Equals(query.Status, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        // Giữ toàn bộ sản phẩm theo chi nhánh, kể cả chưa có tồn (PhysicalQty = 0).
        allItems = allItems
            .OrderBy(x => x.BranchName)
            .ThenBy(x => x.ProductName)
            .ToList();

        var totalItems = allItems.Count;
        var pagedItems = allItems
            .Skip((query.PageIndex - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToList();

        return new InventoryPagedResultDto(
            Items: pagedItems,
            TotalCount: totalItems,
            PageIndex: query.PageIndex,
            PageSize: query.PageSize,
            KpiSummary: kpis
        );
    }

    public async Task<IReadOnlyList<InventoryBatchSummaryDto>> GetProductBatchesAsync(
        string tenantId,
        Guid branchId,
        Guid productId,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow.Date;
        var stocks = await _appContext.BranchStocks.AsNoTracking()
            .Where(bs => bs.TenantId == tenantId
                         && bs.BranchId == branchId
                         && bs.ProductId == productId
                         && !bs.IsDeleted
                         && bs.QuantityOnHand > 0)
            .OrderBy(bs => bs.ExpiryDate ?? DateTime.MaxValue)
            .ToListAsync(cancellationToken);

        return stocks.Select(s =>
        {
            var days = s.ExpiryDate.HasValue ? (s.ExpiryDate.Value.Date - now).Days : 9999;
            var status = !s.ExpiryDate.HasValue
                ? "Normal"
                : days switch
                {
                    <= 0 => "Expired",
                    <= 3 => "Critical",
                    <= 30 => "Warning",
                    _ => "Normal"
                };

            return new InventoryBatchSummaryDto(
                s.Id,
                string.IsNullOrWhiteSpace(s.BatchCode) ? "—" : s.BatchCode!,
                s.ManufactureDate ?? DateTime.MinValue,
                s.ExpiryDate ?? DateTime.MaxValue.Date,
                days,
                BranchStockAvailability.GetSellableAvailable(s, now),
                status
            );
        }).ToList();
    }
}
