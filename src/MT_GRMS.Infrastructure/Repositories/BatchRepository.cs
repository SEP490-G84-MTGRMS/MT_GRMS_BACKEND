using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Batches;
using MT_GRMS.Application.Batches.Dtos;
using MT_GRMS.Domain.Entities.Inventory;
using MT_GRMS.Infrastructure.Persistence;

namespace MT_GRMS.Infrastructure.Repositories;

public class BatchRepository : GenericRepository<Batch>, IBatchRepository
{
    private readonly AppDbContext _appContext;

    public BatchRepository(AppDbContext context) : base(context)
    {
        _appContext = context;
    }

    public async Task<BatchPagedResultDto> GetPagedBatchesAsync(
        string tenantId,
        BatchFilterQuery query,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow.Date;

        // Nguồn tồn theo lô thực tế: BranchStocks (GRN complete ghi vào đây). Chỉ hiện lô còn tồn.
        var stocksQuery = _appContext.BranchStocks.AsNoTracking()
            .Where(bs => !bs.IsDeleted && bs.QuantityOnHand > 0);

        if (query.BranchId.HasValue && query.BranchId.Value != Guid.Empty)
        {
            stocksQuery = stocksQuery.Where(bs => bs.BranchId == query.BranchId.Value);
        }

        var stocks = await stocksQuery.ToListAsync(cancellationToken);

        var productIds = stocks.Select(s => s.ProductId).Distinct().ToList();
        var branchIds = stocks.Select(s => s.BranchId).Distinct().ToList();
        var supplierIds = stocks.Where(s => s.SupplierId.HasValue).Select(s => s.SupplierId!.Value).Distinct().ToList();

        var products = await _appContext.Products.AsNoTracking()
            .Include(p => p.Category)
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        var branches = await _appContext.Branches.AsNoTracking()
            .Where(b => branchIds.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id, cancellationToken);

        var suppliers = supplierIds.Count == 0
            ? new Dictionary<Guid, Domain.Entities.Supplier.Supplier>()
            : await _appContext.Suppliers.AsNoTracking()
                .Where(s => supplierIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, cancellationToken);

        var stockIds = stocks.Select(s => s.Id).ToList();
        var grnByStockId = await _appContext.GoodsReceiptLines.AsNoTracking()
            .Where(l => l.BranchStockId != null && stockIds.Contains(l.BranchStockId.Value) && !l.IsDeleted)
            .Select(l => new { StockId = l.BranchStockId!.Value, GrnCode = l.GoodsReceipt!.Code })
            .ToListAsync(cancellationToken);

        var grnLookup = grnByStockId
            .GroupBy(x => x.StockId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.GrnCode).FirstOrDefault(c => !string.IsNullOrWhiteSpace(c)));

        var allItems = stocks
            .Select(bs => MapBranchStockToListItem(
                bs,
                products,
                branches,
                suppliers,
                now,
                grnLookup.TryGetValue(bs.Id, out var grnCode) ? grnCode : null))
            .ToList();

        var kpiSummary = new BatchKpiSummaryDto(
            TotalBatches: allItems.Count,
            ExpiredCount: allItems.Count(x => x.Status == "Expired"),
            CriticalCount: allItems.Count(x => x.Status == "Critical"),
            WarningCount: allItems.Count(x => x.Status == "Warning"),
            NormalCount: allItems.Count(x => x.Status == "Normal"),
            ExpiredValue: allItems.Where(x => x.Status == "Expired").Sum(x => x.TotalValuation)
        );

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var tokens = query.Keyword
                .Trim()
                .ToLower()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            allItems = allItems.Where(x =>
            {
                var batchCode = x.BatchCode.ToLower();
                var sku = x.Sku.ToLower();
                var productName = x.ProductName.ToLower();
                var supplierName = x.SupplierName.ToLower();
                var grnNo = (x.OriginalGrnNo ?? string.Empty).ToLower();
                return tokens.All(tok =>
                    batchCode.Contains(tok) ||
                    sku.Contains(tok) ||
                    productName.Contains(tok) ||
                    supplierName.Contains(tok) ||
                    grnNo.Contains(tok));
            }).ToList();
        }

        if (!string.IsNullOrWhiteSpace(query.Status) && !query.Status.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            allItems = allItems.Where(x => x.Status.Equals(query.Status, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        allItems = allItems.OrderBy(x => x.DaysLeft).ToList();

        var totalCount = allItems.Count;
        var pageIndex = query.PageIndex < 1 ? 1 : query.PageIndex;
        var pageSize = query.PageSize < 1 ? 20 : query.PageSize;
        var pagedItems = allItems
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return new BatchPagedResultDto(
            Items: pagedItems,
            TotalCount: totalCount,
            PageIndex: pageIndex,
            PageSize: pageSize,
            KpiSummary: kpiSummary
        );
    }

    public async Task<BatchListItemDto?> GetBatchByIdAsync(
        string tenantId,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow.Date;

        var branchStock = await _appContext.BranchStocks.AsNoTracking()
            .FirstOrDefaultAsync(bs => bs.Id == id && !bs.IsDeleted, cancellationToken);

        if (branchStock != null)
        {
            var product = await _appContext.Products.AsNoTracking()
                .Include(p => p.Category)
                .FirstOrDefaultAsync(p => p.Id == branchStock.ProductId, cancellationToken);

            var branch = await _appContext.Branches.AsNoTracking()
                .FirstOrDefaultAsync(b => b.Id == branchStock.BranchId, cancellationToken);

            Domain.Entities.Supplier.Supplier? supplier = null;
            if (branchStock.SupplierId.HasValue)
            {
                supplier = await _appContext.Suppliers.AsNoTracking()
                    .FirstOrDefaultAsync(s => s.Id == branchStock.SupplierId.Value, cancellationToken);
            }

            var products = product != null
                ? new Dictionary<Guid, Domain.Entities.Catalog.Product> { [product.Id] = product }
                : new Dictionary<Guid, Domain.Entities.Catalog.Product>();
            var branches = branch != null
                ? new Dictionary<Guid, Domain.Entities.SaaS.Branch> { [branch.Id] = branch }
                : new Dictionary<Guid, Domain.Entities.SaaS.Branch>();
            var suppliers = supplier != null
                ? new Dictionary<Guid, Domain.Entities.Supplier.Supplier> { [supplier.Id] = supplier }
                : new Dictionary<Guid, Domain.Entities.Supplier.Supplier>();

            string? grnNo = await _appContext.GoodsReceiptLines.AsNoTracking()
                .Where(l => l.BranchStockId == branchStock.Id && !l.IsDeleted)
                .Select(l => l.GoodsReceipt!.Code)
                .FirstOrDefaultAsync(cancellationToken);

            return MapBranchStockToListItem(branchStock, products, branches, suppliers, now, grnNo);
        }

        var batch = await _appContext.Batches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (batch == null) return null;

        var legacyProduct = await _appContext.Products.AsNoTracking()
            .Include(p => p.Category)
            .FirstOrDefaultAsync(p => p.Id == batch.ProductId, cancellationToken);

        var legacyBranch = await _appContext.Branches.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == batch.BranchId, cancellationToken);

        var stock = await _appContext.StockOnHands.AsNoTracking()
            .FirstOrDefaultAsync(s => s.BatchId == batch.Id, cancellationToken);

        var daysLeft = (batch.ExpiryDate.Date - now).Days;
        string status = ResolveStatus(daysLeft);

        decimal currentQty = stock?.Quantity ?? batch.InitialQuantity;
        decimal totalValuation = currentQty * batch.LandedCostPerUnit;

        return new BatchListItemDto(
            Id: batch.Id,
            BatchCode: batch.BatchCode,
            ProductId: batch.ProductId,
            Sku: legacyProduct?.ProductCode ?? string.Empty,
            ProductName: legacyProduct?.Name ?? "Sản phẩm",
            CategoryName: legacyProduct?.Category?.Name ?? "Chung",
            BranchId: batch.BranchId,
            BranchName: legacyBranch?.Name ?? "Chi nhánh",
            SupplierId: null,
            SupplierName: "—",
            MfgDate: batch.ManufactureDate,
            ExpiryDate: batch.ExpiryDate,
            DaysLeft: daysLeft,
            QtyInitial: batch.InitialQuantity,
            QtyCurrent: currentQty,
            Unit: legacyProduct?.BaseUnit ?? "Cái",
            CostPrice: batch.LandedCostPerUnit,
            TotalValuation: totalValuation,
            Status: status,
            ZoneArea: null,
            ShelfLocation: null,
            OriginalGrnNo: null
        );
    }

    public async Task<Batch> AddBatchAsync(
        Batch batch,
        CancellationToken cancellationToken = default)
    {
        await _appContext.Batches.AddAsync(batch, cancellationToken);

        var stock = new StockOnHand
        {
            TenantId = batch.TenantId,
            BranchId = batch.BranchId,
            ProductId = batch.ProductId,
            BatchId = batch.Id,
            Quantity = batch.InitialQuantity,
            ReservedQuantity = 0
        };
        await _appContext.StockOnHands.AddAsync(stock, cancellationToken);

        return batch;
    }

    public async Task<IReadOnlyList<ExpiryAlertDto>> GetNearExpiryBatchesAsync(
        string tenantId,
        int daysThreshold,
        Guid? branchId,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow.Date;
        var thresholdDate = now.AddDays(daysThreshold);

        var query = _appContext.BranchStocks.AsNoTracking()
            .Where(bs => !bs.IsDeleted
                && bs.QuantityOnHand > 0
                && bs.ExpiryDate.HasValue
                && bs.ExpiryDate.Value.Date <= thresholdDate);

        if (branchId.HasValue && branchId.Value != Guid.Empty)
        {
            query = query.Where(bs => bs.BranchId == branchId.Value);
        }

        var stocks = await query.ToListAsync(cancellationToken);
        var productIds = stocks.Select(s => s.ProductId).Distinct().ToList();

        var products = await _appContext.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        return stocks.Select(bs =>
        {
            products.TryGetValue(bs.ProductId, out var product);
            var expiry = bs.ExpiryDate!.Value;
            var daysRemaining = (expiry.Date - now).Days;

            return new ExpiryAlertDto(
                BatchId: bs.Id,
                BatchCode: string.IsNullOrWhiteSpace(bs.BatchCode) ? "—" : bs.BatchCode,
                ProductName: product?.Name ?? "Sản phẩm",
                ExpiryDate: expiry,
                DaysRemaining: daysRemaining,
                RemainingQuantity: bs.QuantityOnHand
            );
        }).OrderBy(x => x.DaysRemaining).ToList();
    }

    private static string ResolveStatus(int daysLeft) => daysLeft switch
    {
        <= 0 => "Expired",
        <= 3 => "Critical",
        <= 30 => "Warning",
        _ => "Normal"
    };

    private static BatchListItemDto MapBranchStockToListItem(
        BranchStock bs,
        IReadOnlyDictionary<Guid, Domain.Entities.Catalog.Product> products,
        IReadOnlyDictionary<Guid, Domain.Entities.SaaS.Branch> branches,
        IReadOnlyDictionary<Guid, Domain.Entities.Supplier.Supplier> suppliers,
        DateTime now,
        string? originalGrnNo = null)
    {
        products.TryGetValue(bs.ProductId, out var product);
        branches.TryGetValue(bs.BranchId, out var branch);

        string supplierName = "—";
        if (bs.SupplierId.HasValue && suppliers.TryGetValue(bs.SupplierId.Value, out var supplier))
        {
            supplierName = supplier.Name;
        }

        var mfgDate = bs.ManufactureDate ?? DateTime.MinValue;
        var expiryDate = bs.ExpiryDate ?? DateTime.MaxValue.Date;
        var daysLeft = bs.ExpiryDate.HasValue
            ? (bs.ExpiryDate.Value.Date - now).Days
            : 9999;
        var status = ResolveStatus(daysLeft);
        var qty = bs.QuantityOnHand;
        var totalValuation = qty * bs.LandedCostPerUnit;

        return new BatchListItemDto(
            Id: bs.Id,
            BatchCode: string.IsNullOrWhiteSpace(bs.BatchCode) ? "—" : bs.BatchCode!,
            ProductId: bs.ProductId,
            Sku: product?.ProductCode ?? string.Empty,
            ProductName: product?.Name ?? "Sản phẩm",
            CategoryName: product?.Category?.Name ?? "Chung",
            BranchId: bs.BranchId,
            BranchName: branch?.Name ?? "Chi nhánh",
            SupplierId: bs.SupplierId,
            SupplierName: supplierName,
            MfgDate: mfgDate,
            ExpiryDate: expiryDate,
            DaysLeft: daysLeft,
            QtyInitial: qty,
            QtyCurrent: qty,
            Unit: product?.BaseUnit ?? "Cái",
            CostPrice: bs.LandedCostPerUnit,
            TotalValuation: totalValuation,
            Status: status,
            ZoneArea: bs.ZoneArea,
            ShelfLocation: bs.ShelfLocation,
            OriginalGrnNo: originalGrnNo
        );
    }
}
