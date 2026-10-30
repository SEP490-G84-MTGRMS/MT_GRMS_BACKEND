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

        var productsQuery = _appContext.Products.AsNoTracking()
            .Include(p => p.Category)
            .Where(p => p.TenantId == tenantId && !p.IsDeleted);

        if (!string.IsNullOrWhiteSpace(query.Category) && !query.Category.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            productsQuery = productsQuery.Where(p => p.Category != null && p.Category.Name == query.Category);
        }

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var kw = query.Keyword.Trim().ToLower();
            productsQuery = productsQuery.Where(p =>
                p.ProductCode.ToLower().Contains(kw) ||
                p.Barcode.ToLower().Contains(kw) ||
                p.Name.ToLower().Contains(kw));
        }

        var products = await productsQuery.ToListAsync(cancellationToken);
        var productIds = products.Select(p => p.Id).ToList();

        // Branches
        var branches = await _appContext.Branches.AsNoTracking()
            .Where(b => b.TenantId == tenantId && !b.IsDeleted)
            .ToListAsync(cancellationToken);

        if (query.BranchId.HasValue && query.BranchId.Value != Guid.Empty)
        {
            branches = branches.Where(b => b.Id == query.BranchId.Value).ToList();
        }

        // BranchStocks
        var branchStocks = await _appContext.BranchStocks.AsNoTracking()
            .Where(bs => bs.TenantId == tenantId && productIds.Contains(bs.ProductId))
            .ToListAsync(cancellationToken);

        // Batches
        var batches = await _appContext.Batches.AsNoTracking()
            .Where(b => b.TenantId == tenantId && productIds.Contains(b.ProductId))
            .ToListAsync(cancellationToken);

        // Pending Returns
        var pendingReturnLines = await _appContext.SupplierReturnLines.AsNoTracking()
            .Include(l => l.SupplierReturn)
            .Where(l => l.TenantId == tenantId && productIds.Contains(l.ProductId) &&
                        l.SupplierReturn != null &&
                        (l.SupplierReturn.Status == "PENDING_SUPPLIER" || l.SupplierReturn.Status == "APPROVED"))
            .ToListAsync(cancellationToken);

        var allItems = new List<InventoryStockItemDto>();

        foreach (var branch in branches)
        {
            foreach (var product in products)
            {
                var stock = branchStocks.FirstOrDefault(bs => bs.BranchId == branch.Id && bs.ProductId == product.Id);
                var productBatches = batches.Where(b => b.BranchId == branch.Id && b.ProductId == product.Id).ToList();
                var pendingQty = pendingReturnLines.Where(l => l.SupplierReturn?.BranchId == branch.Id && l.ProductId == product.Id).Sum(l => l.RequestedQty);

                var expiredBatches = productBatches.Where(b => b.ExpiryDate.Date <= now).ToList();
                var expiredQty = expiredBatches.Sum(b => b.InitialQuantity);

                decimal physicalQty = stock != null ? stock.QuantityOnHand : productBatches.Sum(b => b.InitialQuantity);
                decimal availableQty = Math.Max(0, physicalQty - pendingQty - expiredQty);
                decimal minStock = product.MinStock > 0 ? product.MinStock : 10;
                decimal costPrice = product.CostPrice ?? (product.SellingPrice * 0.7m);
                decimal totalValue = physicalQty * costPrice;

                var batchDtos = productBatches.Select(b =>
                {
                    var days = (b.ExpiryDate.Date - now).Days;
                    string bStatus = days switch
                    {
                        <= 0 => "Expired",
                        <= 3 => "Critical",
                        <= 30 => "Warning",
                        _ => "Normal"
                    };
                    return new InventoryBatchSummaryDto(
                        b.Id,
                        b.BatchCode,
                        b.ManufactureDate,
                        b.ExpiryDate,
                        days,
                        b.InitialQuantity,
                        bStatus
                    );
                }).OrderBy(b => b.DaysLeft).ToList();

                string status;
                if (physicalQty <= 0) status = "OUT_OF_STOCK";
                else if (physicalQty <= minStock) status = "LOW_STOCK";
                else if (batchDtos.Any(b => b.DaysLeft <= 30)) status = "NEAR_EXPIRY";
                else status = "OK";

                allItems.Add(new InventoryStockItemDto(
                    ProductId: product.Id,
                    Sku: product.ProductCode,
                    Barcode: product.Barcode,
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
                    MinStock: minStock,
                    AvgCost: costPrice,
                    TotalValue: totalValue,
                    Status: status,
                    Batches: batchDtos
                ));
            }
        }

        // Calculate KPI summary
        var kpis = new InventoryKpiSummaryDto(
            TotalSkus: allItems.Count,
            TotalValue: allItems.Sum(x => x.TotalValue),
            LowStockCount: allItems.Count(x => x.Status == "LOW_STOCK"),
            OutOfStockCount: allItems.Count(x => x.Status == "OUT_OF_STOCK"),
            ExpiryWarningCount: allItems.Count(x => x.Status == "NEAR_EXPIRY" || x.ExpiredQty > 0)
        );

        if (!string.IsNullOrWhiteSpace(query.Status) && !query.Status.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            allItems = allItems.Where(x => x.Status.Equals(query.Status, StringComparison.OrdinalIgnoreCase)).ToList();
        }

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
        var batches = await _appContext.Batches.AsNoTracking()
            .Where(b => b.TenantId == tenantId && b.BranchId == branchId && b.ProductId == productId)
            .OrderBy(b => b.ExpiryDate)
            .ToListAsync(cancellationToken);

        return batches.Select(b =>
        {
            var days = (b.ExpiryDate.Date - now).Days;
            string status = days switch
            {
                <= 0 => "Expired",
                <= 3 => "Critical",
                <= 30 => "Warning",
                _ => "Normal"
            };

            return new InventoryBatchSummaryDto(
                b.Id,
                b.BatchCode,
                b.ManufactureDate,
                b.ExpiryDate,
                days,
                b.InitialQuantity,
                status
            );
        }).ToList();
    }
}

