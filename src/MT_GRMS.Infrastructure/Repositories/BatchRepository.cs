using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Batches;
using MT_GRMS.Application.Batches.Dtos;
using MT_GRMS.Application.Inventory;
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
            .Include(p => p.Units)
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

        var packageStats = await _appContext.Packages.AsNoTracking()
            .Where(p => !p.IsDeleted
                && stockIds.Contains(p.BranchStockId)
                && (p.Status == "AVAILABLE" || p.Status == "QUARANTINED"))
            .GroupBy(p => p.BranchStockId)
            .Select(g => new { BranchStockId = g.Key, Weight = g.Sum(x => x.NetWeight), Count = g.Count() })
            .ToDictionaryAsync(x => x.BranchStockId, cancellationToken);

        var grnByStockId = await _appContext.GoodsReceiptLines.AsNoTracking()
            .Where(l => l.BranchStockId != null && stockIds.Contains(l.BranchStockId.Value) && !l.IsDeleted)
            .Select(l => new { StockId = l.BranchStockId!.Value, GrnId = l.GoodsReceiptId, GrnCode = l.GoodsReceipt!.Code, AcceptedQty = l.AcceptedQty })
            .ToListAsync(cancellationToken);

        var grnLookup = grnByStockId
            .GroupBy(x => x.StockId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.GrnCode).FirstOrDefault(c => !string.IsNullOrWhiteSpace(c)));

        var grnIdLookup = grnByStockId
            .GroupBy(x => x.StockId)
            .ToDictionary(g => g.Key, g => g.Select(x => (Guid?)x.GrnId).FirstOrDefault());

        var initialQtyLookup = grnByStockId
            .GroupBy(x => x.StockId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.AcceptedQty));

        var allItems = new List<BatchListItemDto>();

        // Nhóm 1 (Product.HasBatches == true): Hiển thị chi tiết từng dòng lô theo từng cửa hàng (BranchId)
        var batchManagedStocks = stocks
            .Where(bs => products.TryGetValue(bs.ProductId, out var p) && p.HasBatches)
            .GroupBy(bs => new
            {
                bs.BranchId,
                bs.ProductId,
                BatchCodeKey = (bs.BatchCode ?? string.Empty).Trim().ToUpperInvariant(),
                MfgKey = bs.ManufactureDate?.Date,
                ExpKey = bs.ExpiryDate?.Date
            });

        foreach (var lotGroup in batchManagedStocks)
        {
            var representative = lotGroup
                .OrderByDescending(x => x.UpdatedAt ?? x.CreatedAt)
                .First();
            var totalLotQty = lotGroup.Sum(x => x.QuantityOnHand);
            var grnCode = lotGroup
                .Select(x => grnLookup.TryGetValue(x.Id, out var code) ? code : null)
                .FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));
            var grnId = lotGroup
                .Select(x => grnIdLookup.TryGetValue(x.Id, out var gid) ? gid : null)
                .FirstOrDefault(g => g.HasValue);
            var initQty = lotGroup
                .Sum(x => initialQtyLookup.TryGetValue(x.Id, out var iq) ? iq : x.QuantityOnHand);

            decimal packagedQty = 0m;
            var packageCount = 0;
            foreach (var s in lotGroup)
            {
                if (packageStats.TryGetValue(s.Id, out var ps))
                {
                    packagedQty += ps.Weight;
                    packageCount += ps.Count;
                }
            }

            var mergedLotStock = new BranchStock
            {
                Id = representative.Id,
                TenantId = representative.TenantId,
                BranchId = representative.BranchId,
                ProductId = representative.ProductId,
                SupplierId = representative.SupplierId,
                BatchCode = representative.BatchCode,
                ManufactureDate = representative.ManufactureDate,
                ExpiryDate = representative.ExpiryDate,
                QuantityOnHand = totalLotQty,
                QuantityReserved = lotGroup.Sum(x => x.QuantityReserved),
                QuantityQuarantined = lotGroup.Sum(x => x.QuantityQuarantined),
                LandedCostPerUnit = representative.LandedCostPerUnit,
                ZoneArea = representative.ZoneArea,
                ShelfLocation = representative.ShelfLocation,
                CreatedAt = representative.CreatedAt,
                UpdatedAt = representative.UpdatedAt
            };

            allItems.Add(MapBranchStockToListItem(
                mergedLotStock,
                products,
                branches,
                suppliers,
                now,
                grnCode,
                isBatchManaged: true,
                originalGrnId: grnId,
                initialQtyOverride: initQty,
                packagedQty: packagedQty,
                packageCount: packageCount));
        }

        // Nhóm 2 (Product.HasBatches == false): Gom nhóm (GroupBy) theo (BranchId, ProductId) thành 1 bản ghi duy nhất cho mỗi cửa hàng
        var nonBatchManagedGroups = stocks
            .Where(bs => !products.TryGetValue(bs.ProductId, out var p) || !p.HasBatches)
            .GroupBy(bs => new { bs.BranchId, bs.ProductId });

        foreach (var group in nonBatchManagedGroups)
        {
            var representative = group
                .OrderByDescending(x => x.UpdatedAt ?? x.CreatedAt)
                .First();
            var totalQty = group.Sum(x => x.QuantityOnHand);
            var grnCode = group
                .Select(x => grnLookup.TryGetValue(x.Id, out var code) ? code : null)
                .FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));
            var grnId = group
                .Select(x => grnIdLookup.TryGetValue(x.Id, out var gid) ? gid : null)
                .FirstOrDefault(g => g.HasValue);
            var initQty = group
                .Sum(x => initialQtyLookup.TryGetValue(x.Id, out var iq) ? iq : x.QuantityOnHand);

            var mergedNonBatchStock = new BranchStock
            {
                Id = representative.Id,
                TenantId = representative.TenantId,
                BranchId = representative.BranchId,
                ProductId = representative.ProductId,
                SupplierId = representative.SupplierId ?? group.Select(x => x.SupplierId).FirstOrDefault(s => s.HasValue),
                BatchCode = null,
                ManufactureDate = null,
                ExpiryDate = null,
                QuantityOnHand = totalQty,
                QuantityReserved = group.Sum(x => x.QuantityReserved),
                QuantityQuarantined = group.Sum(x => x.QuantityQuarantined),
                LandedCostPerUnit = representative.LandedCostPerUnit,
                ZoneArea = representative.ZoneArea ?? group.Select(x => x.ZoneArea).FirstOrDefault(z => !string.IsNullOrWhiteSpace(z)),
                ShelfLocation = representative.ShelfLocation ?? group.Select(x => x.ShelfLocation).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)),
                CreatedAt = representative.CreatedAt,
                UpdatedAt = representative.UpdatedAt
            };

            decimal packagedQty = 0m;
            var packageCount = 0;
            foreach (var s in group)
            {
                if (packageStats.TryGetValue(s.Id, out var ps))
                {
                    packagedQty += ps.Weight;
                    packageCount += ps.Count;
                }
            }

            allItems.Add(MapBranchStockToListItem(
                mergedNonBatchStock,
                products,
                branches,
                suppliers,
                now,
                grnCode,
                isBatchManaged: false,
                originalGrnId: grnId,
                initialQtyOverride: initQty,
                packagedQty: packagedQty,
                packageCount: packageCount));
        }

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

            var isBatchManaged = product?.HasBatches == true;
            if (!isBatchManaged)
            {
                var totalQty = await _appContext.BranchStocks.AsNoTracking()
                    .Where(bs => !bs.IsDeleted && bs.BranchId == branchStock.BranchId && bs.ProductId == branchStock.ProductId && bs.QuantityOnHand > 0)
                    .SumAsync(bs => bs.QuantityOnHand, cancellationToken);
                branchStock.QuantityOnHand = totalQty;
                branchStock.BatchCode = null;
                branchStock.ManufactureDate = null;
                branchStock.ExpiryDate = null;
            }

            string? grnNo = await _appContext.GoodsReceiptLines.AsNoTracking()
                .Where(l => l.BranchStockId == branchStock.Id && !l.IsDeleted)
                .Select(l => l.GoodsReceipt!.Code)
                .FirstOrDefaultAsync(cancellationToken);

            return MapBranchStockToListItem(branchStock, products, branches, suppliers, now, grnNo, isBatchManaged);
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
        string? originalGrnNo = null,
        bool? isBatchManaged = null,
        Guid? originalGrnId = null,
        decimal? initialQtyOverride = null,
        decimal packagedQty = 0m,
        int packageCount = 0)
    {
        products.TryGetValue(bs.ProductId, out var product);
        branches.TryGetValue(bs.BranchId, out var branch);

        bool hasBatches = isBatchManaged ?? (product?.HasBatches == true);
        var isWeight = product != null && WeightProductHelper.IsWeightProduct(product);
        var allowRepack = isWeight && product!.AllowRepack;

        string supplierName = "—";
        if (bs.SupplierId.HasValue && suppliers.TryGetValue(bs.SupplierId.Value, out var supplier))
        {
            supplierName = supplier.Name;
        }

        var mfgDate = hasBatches && bs.ManufactureDate.HasValue
            ? bs.ManufactureDate.Value
            : DateTime.MinValue;
        var expiryDate = hasBatches && bs.ExpiryDate.HasValue
            ? bs.ExpiryDate.Value
            : DateTime.MaxValue.Date;
        var daysLeft = hasBatches && bs.ExpiryDate.HasValue
            ? (bs.ExpiryDate.Value.Date - now).Days
            : 9999;
        var status = hasBatches ? ResolveStatus(daysLeft) : "Normal";
        var qty = bs.QuantityOnHand;
        var reservedQty = bs.QuantityReserved;
        var quarantinedQty = bs.QuantityQuarantined;
        var effectivePackaged = isWeight ? Math.Max(0m, packagedQty) : 0m;
        var availableQty = isWeight
            ? BranchStockAvailability.GetRawSellableKg(bs, effectivePackaged, now)
            : ((hasBatches && bs.ExpiryDate.HasValue && bs.ExpiryDate.Value.Date < now)
                ? 0m
                : Math.Max(0, qty - reservedQty - quarantinedQty));
        var initialQty = initialQtyOverride.HasValue && initialQtyOverride.Value > 0 ? initialQtyOverride.Value : qty;
        var costPrice = bs.LandedCostPerUnit > 0 ? bs.LandedCostPerUnit : (product?.CostPrice ?? 0);
        var totalValuation = qty * costPrice;
        var batchCode = !hasBatches || string.IsNullOrWhiteSpace(bs.BatchCode)
            ? "—"
            : bs.BatchCode!;

        var stockUnit = product?.BaseUnit ?? "Cái";
        var purchaseUnit = stockUnit;
        var purchaseToStockRate = 1m;
        if (product?.Units != null)
        {
            var activeUnits = product.Units.Where(u => !u.IsDeleted).ToList();
            var purchase = activeUnits.FirstOrDefault(u => u.IsPurchaseUnit)
                ?? activeUnits.FirstOrDefault(u => u.IsBaseUnit);
            if (purchase != null)
            {
                purchaseUnit = string.IsNullOrWhiteSpace(purchase.UnitName) ? stockUnit : purchase.UnitName;
                purchaseToStockRate = purchase.ConversionRate > 0 ? purchase.ConversionRate : 1m;
            }
        }

        return new BatchListItemDto(
            Id: bs.Id,
            BatchCode: batchCode,
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
            QtyInitial: initialQty,
            QtyCurrent: qty,
            Unit: purchaseUnit,
            StockUnit: stockUnit,
            PurchaseToStockRate: purchaseToStockRate,
            CostPrice: costPrice,
            TotalValuation: totalValuation,
            Status: status,
            ZoneArea: bs.ZoneArea,
            ShelfLocation: bs.ShelfLocation,
            OriginalGrnNo: originalGrnNo,
            QtyReserved: reservedQty,
            QtyQuarantined: quarantinedQty,
            QtyAvailable: availableQty,
            OriginalGrnId: originalGrnId,
            IsWeightProduct: isWeight,
            AllowRepack: isWeight || allowRepack,
            PackagedQty: effectivePackaged,
            PackageCount: isWeight ? packageCount : 0,
            RawSellableQty: availableQty
        );
    }
}
