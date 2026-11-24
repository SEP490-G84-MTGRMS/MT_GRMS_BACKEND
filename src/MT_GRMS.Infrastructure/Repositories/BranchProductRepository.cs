using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Branches;
using MT_GRMS.Application.Branches.Dtos;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Inventory;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Entities.Inventory;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Domain.Entities.Supplier;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Infrastructure.Persistence;

namespace MT_GRMS.Infrastructure.Repositories;

public class BranchProductRepository : IBranchProductRepository
{
    private readonly AppDbContext _context;
    private readonly ITenantContext _tenantContext;

    public BranchProductRepository(AppDbContext context, ITenantContext tenantContext)
    {
        _context = context;
        _tenantContext = tenantContext;
    }

    public Task<(IReadOnlyList<BranchProductDto> Items, int TotalCount)> GetPagedBranchProductsAsync(
        Guid branchId,
        string? searchTerm,
        string? category,
        string? status,
        string? stockAlertFilter,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var filter = new BranchProductQueryFilter
        {
            SearchTerm = searchTerm,
            Category = category,
            Status = status,
            StockAlertFilter = stockAlertFilter
        };
        return GetPagedBranchProductsAsync(branchId, filter, pageIndex, pageSize, cancellationToken);
    }

    public async Task<(IReadOnlyList<BranchProductDto> Items, int TotalCount)> GetPagedBranchProductsAsync(
        Guid branchId,
        BranchProductQueryFilter filter,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = _context.BranchProducts
            .Include(bp => bp.Product)!.ThenInclude(p => p!.Category)
            .Include(bp => bp.Branch)
            .AsNoTracking()
            .Where(bp => bp.Product != null && bp.Branch != null);

        if (branchId != Guid.Empty)
        {
            query = query.Where(bp => bp.BranchId == branchId);
        }

        if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
        {
            var kw = filter.SearchTerm.Trim().ToLower();

            var matchingSupplierIds = await _context.Suppliers
                .AsNoTracking()
                .Where(s => !s.IsDeleted && (s.Code.ToLower().Contains(kw) || s.Name.ToLower().Contains(kw)))
                .Select(s => s.Id)
                .ToListAsync(cancellationToken);

            var matchingProductIdsFromSP = await GetProductIdsForSuppliersAsync(matchingSupplierIds, cancellationToken);

            query = query.Where(bp =>
                (bp.Product != null && bp.Product.Name.ToLower().Contains(kw)) ||
                (bp.Product != null && bp.Product.ProductCode.ToLower().Contains(kw)) ||
                (bp.Product != null && bp.Product.Barcode != null && bp.Product.Barcode.ToLower().Contains(kw)) ||
                (bp.Branch != null && bp.Branch.BranchCode.ToLower().Contains(kw)) ||
                (bp.Branch != null && bp.Branch.Name.ToLower().Contains(kw)) ||
                matchingProductIdsFromSP.Contains(bp.ProductId));
        }

        var skuTerms = CleanTerms(filter.Skus);
        if (skuTerms.Count > 0)
        {
            query = WhereAnyContains(query, bp => bp.Product!.ProductCode, skuTerms, true);
        }

        var nameTerms = CleanTerms(filter.Names);
        if (nameTerms.Count > 0)
        {
            query = WhereAnyContains(query, bp => bp.Product!.Name, nameTerms, true);
        }

        var branchCodeTerms = CleanTerms(filter.BranchCodes);
        if (branchCodeTerms.Count > 0)
        {
            query = WhereAnyContains(query, bp => bp.Branch!.BranchCode, branchCodeTerms, true);
        }
        else if (!string.IsNullOrWhiteSpace(filter.BranchCode) && !string.Equals(filter.BranchCode, "all", StringComparison.OrdinalIgnoreCase))
        {
            var code = filter.BranchCode.Trim().ToLower();
            query = query.Where(bp => bp.Branch != null && bp.Branch.BranchCode.ToLower().Contains(code));
        }

        var branchNameTerms = CleanTerms(filter.BranchNames);
        if (branchNameTerms.Count > 0)
        {
            query = WhereAnyContains(query, bp => bp.Branch!.Name, branchNameTerms, true);
        }

        var supplierCodeTerms = CleanTerms(filter.SupplierCodes);
        if (supplierCodeTerms.Count > 0)
        {
            var matchedSupplierIds = await _context.Suppliers
                .AsNoTracking()
                .Where(s => !s.IsDeleted)
                .Select(s => new { s.Id, s.Code })
                .ToListAsync(cancellationToken);

            var ids = matchedSupplierIds
                .Where(s => supplierCodeTerms.Any(term => !string.IsNullOrEmpty(s.Code) && s.Code.ToLower().Contains(term)))
                .Select(s => s.Id)
                .ToList();
            var productIds = await GetProductIdsForSuppliersAsync(ids, cancellationToken);
            query = productIds.Count == 0
                ? query.Where(_ => false)
                : query.Where(bp => productIds.Contains(bp.ProductId));
        }

        var supplierNameTerms = CleanTerms(filter.SupplierNames);
        if (supplierNameTerms.Count > 0)
        {
            var matchedSupplierIds = await _context.Suppliers
                .AsNoTracking()
                .Where(s => !s.IsDeleted)
                .Select(s => new { s.Id, s.Name })
                .ToListAsync(cancellationToken);

            var ids = matchedSupplierIds
                .Where(s => supplierNameTerms.Any(term => !string.IsNullOrEmpty(s.Name) && s.Name.ToLower().Contains(term)))
                .Select(s => s.Id)
                .ToList();
            var productIds = await GetProductIdsForSuppliersAsync(ids, cancellationToken);
            query = productIds.Count == 0
                ? query.Where(_ => false)
                : query.Where(bp => productIds.Contains(bp.ProductId));
        }

        if (!string.IsNullOrWhiteSpace(filter.Category) && !string.Equals(filter.Category, "all", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(bp => bp.Product != null && bp.Product.Category != null && bp.Product.Category.Name == filter.Category);
        }

        if (!string.IsNullOrWhiteSpace(filter.Status) && !string.Equals(filter.Status, "all", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(bp => bp.Status == filter.Status);
        }

        var projected = query.Select(bp => new
        {
            bp.Id,
            bp.BranchId,
            BranchCode = bp.Branch!.BranchCode,
            BranchName = bp.Branch.Name,
            bp.ProductId,
            ProductName = bp.Product!.Name,
            ProductCode = bp.Product.ProductCode,
            Barcode = bp.Product.Barcode,
            CategoryName = bp.Product.Category != null ? bp.Product.Category.Name : "Khác",
            Unit = bp.Product.BaseUnit,
            MasterRetailPrice = bp.Product.SellingPrice,
            CostPrice = bp.Product.CostPrice ?? 0m,
            MinStock = bp.Product.MinStock,
            RetailPrice = bp.SellingPriceOverride ?? bp.Product.SellingPrice,
            ProductStatus = bp.Status,
            ShelfLocation = bp.ShelfLocation,
            AssignedDate = bp.CreatedAt,
            AllowedSupplierIdsJson = bp.AllowedSupplierIdsJson,
            BranchProcessedSellingPricePerKg = bp.ProcessedSellingPricePerKg
        });

        // Stock alert cần QuantityOnHand từ BranchStocks — lọc sau khi join qty.
        var stockAlert = filter.StockAlertFilter;
        var needsStockFilter = !string.IsNullOrWhiteSpace(stockAlert)
            && !string.Equals(stockAlert, "all", StringComparison.OrdinalIgnoreCase);

        List<Guid> filteredIds;
        int totalCount;
        if (needsStockFilter)
        {
            var allRows = await projected.ToListAsync(cancellationToken);
            var ids = allRows.Select(x => x.ProductId).Distinct().ToList();
            var branchIds = allRows.Select(x => x.BranchId).Distinct().ToList();
            var todayForFilter = DateTime.UtcNow.Date;
            var qtyRows = await _context.BranchStocks
                .AsNoTracking()
                .Where(bs => branchIds.Contains(bs.BranchId) && ids.Contains(bs.ProductId) && !bs.IsDeleted)
                .ToListAsync(cancellationToken);
            var qtyLookup = qtyRows
                .GroupBy(bs => (bs.BranchId, bs.ProductId))
                .ToDictionary(
                    g => g.Key,
                    g => g.Sum(bs => BranchStockAvailability.GetSellableAvailable(bs, todayForFilter)));

            var filtered = allRows.Where(x =>
            {
                qtyLookup.TryGetValue((x.BranchId, x.ProductId), out var qty);
                if (string.Equals(stockAlert, "OUT_OF_STOCK", StringComparison.OrdinalIgnoreCase))
                    return qty == 0;
                if (string.Equals(stockAlert, "LOW", StringComparison.OrdinalIgnoreCase))
                    return qty > 0 && qty <= x.MinStock;
                if (string.Equals(stockAlert, "NORMAL", StringComparison.OrdinalIgnoreCase))
                    return qty > x.MinStock;
                return true;
            }).ToList();

            totalCount = filtered.Count;
            filteredIds = filtered
                .OrderBy(x => x.BranchCode)
                .ThenBy(x => x.ProductCode)
                .Skip((Math.Max(1, pageIndex) - 1) * Math.Clamp(pageSize, 1, 1000))
                .Take(Math.Clamp(pageSize, 1, 1000))
                .Select(x => x.Id)
                .ToList();
        }
        else
        {
            totalCount = await projected.CountAsync(cancellationToken);
            var safePageIndex = Math.Max(1, pageIndex);
            var safePageSize = Math.Clamp(pageSize, 1, 1000);
            filteredIds = await projected
                .OrderBy(x => x.BranchCode)
                .ThenBy(x => x.ProductCode)
                .Skip((safePageIndex - 1) * safePageSize)
                .Take(safePageSize)
                .Select(x => x.Id)
                .ToListAsync(cancellationToken);
        }

        if (filteredIds.Count == 0)
        {
            return (Array.Empty<BranchProductDto>(), totalCount);
        }

        var pagedData = await projected
            .Where(x => filteredIds.Contains(x.Id))
            .ToListAsync(cancellationToken);
        pagedData = pagedData.OrderBy(x => filteredIds.IndexOf(x.Id)).ToList();

        var pagedProductIds = pagedData.Select(x => x.ProductId).Distinct().ToList();
        var pagedBranchIds = pagedData.Select(x => x.BranchId).Distinct().ToList();

        var today = DateTime.UtcNow.Date;
        var stockRows = await _context.BranchStocks
            .AsNoTracking()
            .Where(bs => pagedBranchIds.Contains(bs.BranchId)
                && pagedProductIds.Contains(bs.ProductId)
                && !bs.IsDeleted)
            .ToListAsync(cancellationToken);
        var stockLookup = stockRows
            .GroupBy(bs => (bs.BranchId, bs.ProductId))
            .ToDictionary(
                g => g.Key,
                g => g.Sum(bs => BranchStockAvailability.GetSellableAvailable(bs, today)));

        // WAC theo ĐVT cơ sở: Σ(QOH × LandedCost) / Σ(QOH) — giống Inventory AvgCost.
        var avgCostLookup = stockRows
            .GroupBy(bs => (bs.BranchId, bs.ProductId))
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var physicalQty = g.Sum(bs => bs.QuantityOnHand);
                    if (physicalQty <= 0m) return 0m;
                    return Math.Round(g.Sum(bs => bs.QuantityOnHand * bs.LandedCostPerUnit) / physicalQty, 2);
                });

        // NCC Active đang phục vụ từng chi nhánh trên trang → 1 BranchProduct có thể nhiều dòng nguồn nhập.
        var branchSupplierLinks = await _context.SupplierBranchAssignments
            .AsNoTracking()
            .Where(a =>
                pagedBranchIds.Contains(a.BranchId)
                && !a.IsDeleted
                && a.Status == SupplierStatus.Active)
            .Select(a => new { a.BranchId, a.SupplierId })
            .ToListAsync(cancellationToken);
        var branchSupplierSet = branchSupplierLinks
            .Select(a => (a.BranchId, a.SupplierId))
            .ToHashSet();

        var rawSp = await _context.SupplierProducts
            .Include(sp => sp.Supplier)
            .Where(sp =>
                pagedProductIds.Contains(sp.ProductId)
                && !sp.IsDeleted
                && sp.Status == SupplierProductStatus.Active
                && sp.Supplier != null
                && !sp.Supplier.IsDeleted
                && sp.Supplier.Status == SupplierStatus.Active)
            .OrderBy(sp => sp.AgreedWholesalePrice.HasValue && sp.AgreedWholesalePrice.Value > 0
                ? sp.AgreedWholesalePrice.Value
                : decimal.MaxValue)
            .ThenByDescending(sp => sp.IsPrimarySupplier)
            .ThenBy(sp => sp.CreatedAt)
            .ToListAsync(cancellationToken);
        var spLookup = rawSp.GroupBy(sp => sp.ProductId).ToDictionary(g => g.Key, g => g.ToList());

        var productExtras = await _context.Products
            .AsNoTracking()
            .Include(p => p.Units)
            .Where(p => pagedProductIds.Contains(p.Id))
            .Select(p => new
            {
                p.Id,
                p.ImageUrl,
                p.BaseUnit,
                p.SellingPrice,
                p.AllowRepack,
                p.ProcessedSellingPricePerKg,
                Units = p.Units.Select(u => new
                {
                    u.Id,
                    u.UnitName,
                    u.ConversionRate,
                    u.Price,
                    u.IsBaseUnit,
                    u.IsPurchaseUnit
                }).ToList()
            })
            .ToListAsync(cancellationToken);
        var productExtraLookup = productExtras.ToDictionary(p => p.Id);

        var pagedBranchProductIds = pagedData.Select(x => x.Id).ToList();
        var unitOverrideRows = await _context.BranchProductUnitPrices
            .AsNoTracking()
            .Where(u => pagedBranchProductIds.Contains(u.BranchProductId))
            .Select(u => new { u.BranchProductId, u.ProductUnitId, u.SellingPriceOverride })
            .ToListAsync(cancellationToken);
        var unitOverrideLookup = unitOverrideRows
            .GroupBy(u => u.BranchProductId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyDictionary<Guid, decimal>)g.ToDictionary(x => x.ProductUnitId, x => x.SellingPriceOverride));

        // 1 BranchProduct = 1 dòng; nhiều NCC gộp vào Suppliers[] (không fan-out).
        var dtos = new List<BranchProductDto>();
        foreach (var x in pagedData)
        {
            productExtraLookup.TryGetValue(x.ProductId, out var extra);
            stockLookup.TryGetValue((x.BranchId, x.ProductId), out var currentStock);
            unitOverrideLookup.TryGetValue(x.Id, out var unitOverrides);
            var unitPrices = BuildUnitPrices(
                extra?.Units.Select(u => (u.Id, u.UnitName, u.ConversionRate, u.Price, u.IsBaseUnit)),
                x.Unit,
                x.RetailPrice,
                unitOverrides);
            var catalogPurchaseUnitName = extra?.Units.FirstOrDefault(u => u.IsPurchaseUnit)?.UnitName;

            spLookup.TryGetValue(x.ProductId, out var spList);
            var activeSourcing = (spList ?? new List<SupplierProduct>())
                .Where(sp => branchSupplierSet.Contains((x.BranchId, sp.SupplierId)))
                .ToList();
            var allowedSupplierIds = BranchProduct.ParseAllowedSupplierIds(x.AllowedSupplierIdsJson);
            var sourcing = allowedSupplierIds != null
                ? activeSourcing.Where(sp => allowedSupplierIds.Contains(sp.SupplierId)).ToList()
                : activeSourcing;

            if (supplierCodeTerms.Count > 0)
            {
                var matched = sourcing.Any(sp =>
                {
                    var code = sp.Supplier?.Code?.ToLowerInvariant() ?? string.Empty;
                    return supplierCodeTerms.Any(term => code.Contains(term));
                });
                if (!matched)
                {
                    continue;
                }
            }

            if (supplierNameTerms.Count > 0)
            {
                var matched = sourcing.Any(sp =>
                {
                    var name = sp.Supplier?.Name?.ToLowerInvariant() ?? string.Empty;
                    return supplierNameTerms.Any(term => name.Contains(term));
                });
                if (!matched)
                {
                    continue;
                }
            }

            var stockUnitName = string.IsNullOrWhiteSpace(extra?.BaseUnit)
                ? (string.IsNullOrWhiteSpace(x.Unit) ? "Cái" : x.Unit.Trim())
                : extra!.BaseUnit.Trim();

            // Primary = NCC giá vốn thấp nhất (rawSp đã sort). Scalar Cost/MOQ/Unit lấy từ primary.
            var primarySp = sourcing.FirstOrDefault();
            var moq = primarySp != null && primarySp.MinimumOrderQuantity > 0
                ? (int)primarySp.MinimumOrderQuantity
                : 0;
            var hasSource = sourcing.Count > 0;
            var (inboundUnit, purchaseToStockRate) = ResolveInboundUnitAndRate(
                deliveryUoM: primarySp?.DeliveryUoM,
                purchaseUnitName: catalogPurchaseUnitName,
                baseUnitName: stockUnitName,
                units: extra?.Units.Select(u => (u.UnitName, u.ConversionRate, u.IsPurchaseUnit)));

            var safeRate = purchaseToStockRate > 0m ? purchaseToStockRate : 1m;
            var baseUnitFallbackCost = primarySp?.AgreedWholesalePrice is > 0m
                ? Math.Round(primarySp.AgreedWholesalePrice.Value / safeRate, 2)
                : x.CostPrice;
            var cost = primarySp?.AgreedWholesalePrice is > 0m
                ? primarySp.AgreedWholesalePrice.Value
                : Math.Round(x.CostPrice * safeRate, 2);
            avgCostLookup.TryGetValue((x.BranchId, x.ProductId), out var stockAvgCost);
            var averageCost = stockAvgCost > 0m ? stockAvgCost : baseUnitFallbackCost;

            var supplierSources = sourcing.Select(sp => new BranchProductSupplierSourceDto
            {
                SupplierId = sp.SupplierId,
                SupplierCode = sp.Supplier?.Code ?? string.Empty,
                SupplierName = sp.Supplier?.Name ?? string.Empty,
                CostPrice = MT_GRMS.Application.Suppliers.SupplierProductPricing.ResolveEffectivePurchasePrice(
                    sp.AgreedWholesalePrice,
                    x.CostPrice),
                MinOrderQuantity = sp.MinimumOrderQuantity > 0 ? (int)sp.MinimumOrderQuantity : 0,
                IsPrimarySupplier = sp.IsPrimarySupplier,
                DeliveryUoM = sp.DeliveryUoM
            }).ToList();

            dtos.Add(new BranchProductDto
            {
                Id = x.Id,
                BranchId = x.BranchId,
                BranchCode = x.BranchCode,
                BranchName = x.BranchName,
                ProductId = x.ProductId,
                Name = x.ProductName,
                Sku = x.ProductCode,
                Barcode = x.Barcode ?? string.Empty,
                Category = x.CategoryName,
                Unit = inboundUnit,
                StockUnit = stockUnitName,
                PurchaseToStockRate = purchaseToStockRate,
                ImageUrl = extra?.ImageUrl,
                RetailPrice = x.RetailPrice,
                CostPrice = cost,
                AverageCost = averageCost,
                CurrentStock = currentStock,
                MinStockLevel = x.MinStock,
                SupplierId = primarySp?.SupplierId,
                PrimarySupplierCode = primarySp?.Supplier?.Code ?? string.Empty,
                PrimarySupplierName = primarySp?.Supplier?.Name ?? string.Empty,
                IsPrimarySupplier = primarySp?.IsPrimarySupplier ?? false,
                HasActiveSupplierSource = hasSource,
                LostSupplierSource = !hasSource,
                Suppliers = supplierSources,
                SupplierCount = supplierSources.Count,
                MinOrderQuantity = moq,
                Status = x.ProductStatus,
                ShelfLocation = x.ShelfLocation,
                AssignedDate = x.AssignedDate,
                UnitPrices = unitPrices,
                AllowRepack = extra?.AllowRepack ?? false,
                ProcessedSellingPricePerKg = x.BranchProcessedSellingPricePerKg ?? extra?.ProcessedSellingPricePerKg
            });
        }

        return (dtos, totalCount);
    }

    /// <summary>
    /// Đơn vị nhập (cùng logic màn NCC): DeliveryUoM → đơn vị IsPurchaseUnit → BaseUnit.
    /// </summary>
    private static string ResolveInboundUnit(
        string? deliveryUoM,
        string? purchaseUnitName,
        string? baseUnitName)
    {
        return ResolveInboundUnitAndRate(deliveryUoM, purchaseUnitName, baseUnitName, null).Name;
    }

    /// <summary>
    /// ĐVT nhập + hệ số quy đổi 1 ĐVT nhập = N ĐVT tồn (giống BatchRepository).
    /// </summary>
    private static (string Name, decimal Rate) ResolveInboundUnitAndRate(
        string? deliveryUoM,
        string? purchaseUnitName,
        string? baseUnitName,
        IEnumerable<(string UnitName, decimal ConversionRate, bool IsPurchaseUnit)>? units)
    {
        var stockUnit = string.IsNullOrWhiteSpace(baseUnitName) ? "Cái" : baseUnitName.Trim();
        var unitList = units?.ToList() ?? [];
        var purchase = unitList.FirstOrDefault(u => u.IsPurchaseUnit);

        string inboundName;
        if (!string.IsNullOrWhiteSpace(deliveryUoM))
            inboundName = deliveryUoM.Trim();
        else if (!string.IsNullOrWhiteSpace(purchase.UnitName))
            inboundName = purchase.UnitName.Trim();
        else if (!string.IsNullOrWhiteSpace(purchaseUnitName))
            inboundName = purchaseUnitName.Trim();
        else
            inboundName = stockUnit;

        var matched = unitList.FirstOrDefault(u =>
            string.Equals((u.UnitName ?? string.Empty).Trim(), inboundName, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(matched.UnitName) && matched.ConversionRate > 0)
            return (inboundName, matched.ConversionRate);
        if (!string.IsNullOrWhiteSpace(purchase.UnitName) && purchase.ConversionRate > 0)
            return (inboundName, purchase.ConversionRate);
        return (inboundName, 1m);
    }

    /// <summary>
    /// Giá ĐVT hiệu lực = override CN ?? giá Master ProductUnits.
    /// </summary>
    private static List<BranchProductUnitPriceDto> BuildUnitPrices(
        IEnumerable<(Guid Id, string UnitName, decimal ConversionRate, decimal Price, bool IsBaseUnit)>? units,
        string baseUnitName,
        decimal fallbackBasePrice,
        IReadOnlyDictionary<Guid, decimal>? overridesByUnitId = null)
    {
        var list = units?.ToList() ?? [];
        overridesByUnitId ??= new Dictionary<Guid, decimal>();

        if (list.Count == 0)
        {
            return
            [
                new BranchProductUnitPriceDto
                {
                    UnitName = string.IsNullOrWhiteSpace(baseUnitName) ? "Cái" : baseUnitName,
                    ConversionRate = 1m,
                    Price = fallbackBasePrice,
                    IsBaseUnit = true
                }
            ];
        }

        return list
            .OrderByDescending(u => u.IsBaseUnit)
            .ThenBy(u => u.ConversionRate)
            .Select(u =>
            {
                var rate = u.ConversionRate <= 0 ? 1m : u.ConversionRate;
                var price = overridesByUnitId.TryGetValue(u.Id, out var ov) ? ov : u.Price;
                if (price <= 0 && fallbackBasePrice > 0)
                {
                    price = Math.Round(fallbackBasePrice * rate, 0);
                }

                return new BranchProductUnitPriceDto
                {
                    UnitId = u.Id,
                    UnitName = u.UnitName,
                    ConversionRate = rate,
                    Price = price,
                    IsBaseUnit = u.IsBaseUnit || rate == 1m
                };
            })
            .ToList();
    }

    private async Task<List<Guid>> GetProductIdsForSuppliersAsync(
        List<Guid> supplierIds,
        CancellationToken cancellationToken)
    {
        if (supplierIds.Count == 0)
        {
            return new List<Guid>();
        }

        return await _context.SupplierProducts
            .AsNoTracking()
            .Where(sp => !sp.IsDeleted && supplierIds.Contains(sp.SupplierId))
            .Select(sp => sp.ProductId)
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    private static List<string> CleanTerms(IReadOnlyList<string>? values)
    {
        if (values == null || values.Count == 0) return [];
        return values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim().ToLower())
            .Distinct()
            .ToList();
    }

    private static IQueryable<T> WhereAnyContains<T>(
        IQueryable<T> query,
        Expression<Func<T, string>> selector,
        IReadOnlyList<string> terms,
        bool ignoreCase)
    {
        if (terms.Count == 0) return query;

        var parameter = selector.Parameters[0];
        Expression source = selector.Body;
        if (ignoreCase)
        {
            source = Expression.Call(source, nameof(string.ToLower), Type.EmptyTypes);
        }

        Expression? body = null;
        foreach (var term in terms)
        {
            var contains = Expression.Call(
                source,
                nameof(string.Contains),
                Type.EmptyTypes,
                Expression.Constant(term));
            body = body is null ? contains : Expression.OrElse(body, contains);
        }

        var predicate = Expression.Lambda<Func<T, bool>>(body!, parameter);
        return query.Where(predicate);
    }

    public async Task<IReadOnlyList<BranchSummaryDto>> GetBranchSummariesAsync(
        CancellationToken cancellationToken = default)
    {
        var branches = await _context.Branches
            .AsNoTracking()
            .Where(b => !b.IsDeleted)
            .ToListAsync(cancellationToken);

        var catalog = await _context.BranchProducts
            .AsNoTracking()
            .Where(bp => bp.Product != null)
            .Select(bp => new { bp.BranchId, bp.ProductId })
            .ToListAsync(cancellationToken);

        var stocks = await _context.BranchStocks
            .Include(bs => bs.Product)
            .AsNoTracking()
            .Where(bs => bs.Product != null)
            .ToListAsync(cancellationToken);

        var result = new List<BranchSummaryDto>();

        foreach (var b in branches)
        {
            var productIds = catalog.Where(c => c.BranchId == b.Id).Select(c => c.ProductId).Distinct().ToList();
            var branchStocks = stocks.Where(s => s.BranchId == b.Id).ToList();
            var totalStock = branchStocks.Sum(s => s.QuantityOnHand);
            var lowStockCount = productIds.Count(pid =>
            {
                var qty = branchStocks.Where(s => s.ProductId == pid).Sum(s => s.QuantityOnHand);
                var min = branchStocks.FirstOrDefault(s => s.ProductId == pid)?.Product?.MinStock ?? 0m;
                return qty <= min;
            });

            result.Add(new BranchSummaryDto
            {
                Id = b.Id,
                Code = b.Code,
                Name = b.Name,
                Address = b.Address ?? "Hồ Chí Minh, Việt Nam",
                ManagerName = b.ManagerName ?? "Quản lý chi nhánh",
                Phone = b.Phone ?? "0901 234 567",
                TotalProducts = productIds.Count,
                TotalStock = totalStock,
                LowStockCount = lowStockCount
            });
        }

        return result;
    }

    public async Task<BranchProduct?> GetBranchProductAsync(
        Guid branchId,
        Guid productId,
        CancellationToken cancellationToken = default)
    {
        return await _context.BranchProducts
            .Include(bp => bp.Product)!.ThenInclude(p => p!.Category)
            .Include(bp => bp.Product)!.ThenInclude(p => p!.Units)
            .Include(bp => bp.Branch)
            .Include(bp => bp.UnitPriceOverrides)
            .AsSplitQuery()
            .FirstOrDefaultAsync(bp => bp.BranchId == branchId && bp.ProductId == productId, cancellationToken);
    }

    public async Task AddBranchProductAsync(
        BranchProduct branchProduct,
        CancellationToken cancellationToken = default)
    {
        await _context.BranchProducts.AddAsync(branchProduct, cancellationToken);
    }

    public void RemoveBranchProduct(BranchProduct branchProduct)
    {
        branchProduct.IsDeleted = true;
        branchProduct.UpdatedAt = DateTime.UtcNow;
    }

    public async Task<IReadOnlyList<BranchStock>> GetBranchStocksByProductAsync(
        Guid branchId,
        Guid productId,
        CancellationToken cancellationToken = default)
    {
        return await _context.BranchStocks
            .Include(bs => bs.Product)
            .Include(bs => bs.Branch)
            .Include(bs => bs.Supplier)
            .Where(bs => bs.BranchId == branchId && bs.ProductId == productId)
            .ToListAsync(cancellationToken);
    }

    public async Task SyncBranchStockCatalogAsync(
        Guid branchId,
        Guid productId,
        decimal retailPrice,
        string status,
        string? shelfLocation,
        CancellationToken cancellationToken = default)
    {
        // Catalog metadata is independent from stock quantities. Updating it in bulk avoids
        // using BranchStock.RowVersion loaded earlier in the request and therefore does not
        // overwrite, or spuriously conflict with, a concurrent sale/receipt stock movement.
        await _context.BranchStocks
            .Where(bs => bs.BranchId == branchId && bs.ProductId == productId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(bs => bs.RetailPrice, retailPrice)
                .SetProperty(bs => bs.Status, status)
                .SetProperty(bs => bs.ShelfLocation, shelfLocation)
                .SetProperty(bs => bs.UpdatedAt, DateTime.UtcNow),
                cancellationToken);
    }

    public async Task AddUnitPriceOverrideAsync(BranchProductUnitPrice unitPrice, CancellationToken cancellationToken = default)
    {
        await _context.BranchProductUnitPrices.AddAsync(unitPrice, cancellationToken);
    }

    public async Task AddBranchStockAsync(
        BranchStock branchStock,
        CancellationToken cancellationToken = default)
    {
        await _context.BranchStocks.AddAsync(branchStock, cancellationToken);
    }

    public void RemoveBranchStocks(
        IEnumerable<BranchStock> branchStocks)
    {
        foreach (var bs in branchStocks)
        {
            bs.IsDeleted = true;
            bs.UpdatedAt = DateTime.UtcNow;
        }
    }

    public async Task<SupplierProduct?> GetSupplierProductAsync(
        Guid productId,
        Guid? supplierId,
        CancellationToken cancellationToken = default)
    {
        var query = _context.SupplierProducts
            .Include(sp => sp.Supplier)
            .Where(sp => sp.ProductId == productId && !sp.IsDeleted && sp.Supplier != null && !sp.Supplier.IsDeleted);

        if (supplierId.HasValue && supplierId.Value != Guid.Empty)
        {
            return await query.FirstOrDefaultAsync(sp => sp.SupplierId == supplierId.Value, cancellationToken);
        }

        var matches = await query
            .OrderBy(sp => sp.CreatedAt)
            .ToListAsync(cancellationToken);

        return matches.Count == 1 ? matches[0] : null;
    }

    public async Task<IReadOnlyList<Guid>> GetActiveSupplierIdsForBranchAsync(
        Guid branchId,
        CancellationToken cancellationToken = default)
    {
        return await _context.SupplierBranchAssignments
            .AsNoTracking()
            .Where(a =>
                a.BranchId == branchId
                && !a.IsDeleted
                && a.Status == SupplierStatus.Active
                && a.Supplier != null
                && !a.Supplier.IsDeleted
                && a.Supplier.Status == SupplierStatus.Active)
            .Select(a => a.SupplierId)
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AssignableBranchProductDto>> GetAssignableProductsForBranchAsync(
        Guid branchId,
        CancellationToken cancellationToken = default)
    {
        var supplierIds = await GetActiveSupplierIdsForBranchAsync(branchId, cancellationToken);
        if (supplierIds.Count == 0)
        {
            return Array.Empty<AssignableBranchProductDto>();
        }

        var existingBranchProducts = await _context.BranchProducts
            .AsNoTracking()
            .Where(bp => bp.BranchId == branchId)
            .Select(bp => new { bp.ProductId, bp.AllowedSupplierIdsJson })
            .ToListAsync(cancellationToken);
        var allowedByProduct = existingBranchProducts
            .GroupBy(bp => bp.ProductId)
            .ToDictionary(
                g => g.Key,
                g => BranchProduct.ParseAllowedSupplierIds(g.First().AllowedSupplierIdsJson));

        var supplierProducts = await _context.SupplierProducts
            .AsNoTracking()
            .Include(sp => sp.Product)!.ThenInclude(p => p!.Category)
            .Include(sp => sp.Product)!.ThenInclude(p => p!.Units)
            .Include(sp => sp.Supplier)
            .Where(sp =>
                supplierIds.Contains(sp.SupplierId)
                && !sp.IsDeleted
                && sp.Status == SupplierProductStatus.Active
                && sp.Product != null
                && !sp.Product.IsDeleted
                && sp.Product.Status == ProductStatus.Active
                && sp.Supplier != null
                && !sp.Supplier.IsDeleted
                && sp.Supplier.Status == SupplierStatus.Active)
            .OrderByDescending(sp => sp.IsPrimarySupplier)
            .ThenBy(sp => sp.CreatedAt)
            .ToListAsync(cancellationToken);

        var result = new List<AssignableBranchProductDto>();
        var seenPairs = new HashSet<(Guid ProductId, Guid SupplierId)>();

        foreach (var sp in supplierProducts)
        {
            if (sp.Product == null || !seenPairs.Add((sp.ProductId, sp.SupplierId)))
            {
                continue;
            }

            var isAlreadyAssigned = allowedByProduct.TryGetValue(sp.ProductId, out var allowedIds)
                && (allowedIds == null || allowedIds.Contains(sp.SupplierId));

            var cost = MT_GRMS.Application.Suppliers.SupplierProductPricing.ResolveEffectivePurchasePrice(
                sp.AgreedWholesalePrice,
                sp.Product.CostPrice);
            var selling = sp.Product.SellingPrice;
            var unitPrices = BuildUnitPrices(
                sp.Product.Units.Select(u => (u.Id, u.UnitName, u.ConversionRate, u.Price, u.IsBaseUnit)),
                sp.Product.BaseUnit ?? "Cái",
                selling);

            result.Add(new AssignableBranchProductDto
            {
                ProductId = sp.ProductId,
                Sku = sp.Product.ProductCode,
                Barcode = sp.Product.Barcode ?? string.Empty,
                Name = sp.Product.Name,
                CategoryName = sp.Product.Category?.Name ?? "Khác",
                BaseUoM = sp.Product.BaseUnit ?? "Cái",
                ImageUrl = sp.Product.ImageUrl,
                CostPrice = cost,
                SellingPrice = selling,
                Moq = sp.MinimumOrderQuantity > 0 ? sp.MinimumOrderQuantity : 1,
                SupplierId = sp.SupplierId,
                SupplierCode = sp.Supplier?.Code ?? string.Empty,
                SupplierName = sp.Supplier?.Name ?? string.Empty,
                IsAlreadyAssigned = isAlreadyAssigned,
                UnitPrices = unitPrices
            });
        }

        return result
            .OrderBy(x => x.Name)
            .ThenBy(x => x.SupplierName)
            .ToList();
    }

    public async Task<Product?> GetProductByIdAsync(
        Guid productId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Products
            .Include(p => p.Category)
            .Include(p => p.Units)
            .FirstOrDefaultAsync(p => p.Id == productId, cancellationToken);
    }

    public async Task<Branch?> GetBranchByIdAsync(
        Guid branchId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Branches
            .FirstOrDefaultAsync(b => b.Id == branchId, cancellationToken);
    }
}
