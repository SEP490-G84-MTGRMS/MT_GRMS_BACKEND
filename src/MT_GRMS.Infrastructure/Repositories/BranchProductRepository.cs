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
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Entities.Inventory;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Domain.Entities.Supplier;
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
        var stockQuery = _context.BranchStocks
            .Include(bs => bs.Product)
                .ThenInclude(p => p!.Category)
            .Include(bs => bs.Branch)
            .Include(bs => bs.Supplier)
            .AsNoTracking();

        if (branchId != Guid.Empty)
        {
            stockQuery = stockQuery.Where(bs => bs.BranchId == branchId);
        }

        if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
        {
            var kw = filter.SearchTerm.Trim().ToLower();

            var matchingSupplierIds = await _context.Suppliers
                .AsNoTracking()
                .Where(s => !s.IsDeleted && (s.Code.ToLower().Contains(kw) || s.Name.ToLower().Contains(kw)))
                .Select(s => s.Id)
                .ToListAsync(cancellationToken);

            var primarySupplierMap = await GetProductPrimarySupplierMapAsync(cancellationToken);
            var matchingProductIdsFromSP = primarySupplierMap
                .Where(kv => matchingSupplierIds.Contains(kv.Value))
                .Select(kv => kv.Key)
                .ToList();

            stockQuery = stockQuery.Where(bs =>
                (bs.Product != null && bs.Product.Name.ToLower().Contains(kw)) ||
                (bs.Product != null && bs.Product.ProductCode.ToLower().Contains(kw)) ||
                (bs.Product != null && bs.Product.Barcode.ToLower().Contains(kw)) ||
                (bs.Branch != null && bs.Branch.BranchCode.ToLower().Contains(kw)) ||
                (bs.Branch != null && bs.Branch.Name.ToLower().Contains(kw)) ||
                (bs.SupplierId.HasValue && matchingSupplierIds.Contains(bs.SupplierId.Value)) ||
                (!bs.SupplierId.HasValue && matchingProductIdsFromSP.Contains(bs.ProductId)));
        }

        var skuTerms = CleanTerms(filter.Skus);
        if (skuTerms.Count > 0)
        {
            stockQuery = WhereAnyContains(stockQuery, bs => bs.Product!.ProductCode, skuTerms, true);
        }

        var nameTerms = CleanTerms(filter.Names);
        if (nameTerms.Count > 0)
        {
            stockQuery = WhereAnyContains(stockQuery, bs => bs.Product!.Name, nameTerms, true);
        }

        var branchCodeTerms = CleanTerms(filter.BranchCodes);
        if (branchCodeTerms.Count > 0)
        {
            stockQuery = WhereAnyContains(stockQuery, bs => bs.Branch!.BranchCode, branchCodeTerms, true);
        }
        else if (!string.IsNullOrWhiteSpace(filter.BranchCode) && !string.Equals(filter.BranchCode, "all", StringComparison.OrdinalIgnoreCase))
        {
            var code = filter.BranchCode.Trim().ToLower();
            stockQuery = stockQuery.Where(bs => bs.Branch != null && bs.Branch.BranchCode.ToLower().Contains(code));
        }

        var branchNameTerms = CleanTerms(filter.BranchNames);
        if (branchNameTerms.Count > 0)
        {
            stockQuery = WhereAnyContains(stockQuery, bs => bs.Branch!.Name, branchNameTerms, true);
        }

        var supplierCodeTerms = CleanTerms(filter.SupplierCodes);
        if (supplierCodeTerms.Count > 0)
        {
            var allSuppliers = await _context.Suppliers
                .AsNoTracking()
                .Where(s => !s.IsDeleted)
                .Select(s => new { s.Id, s.Code })
                .ToListAsync(cancellationToken);

            var matchedSupplierIds = allSuppliers
                .Where(s => supplierCodeTerms.Any(term => !string.IsNullOrEmpty(s.Code) && s.Code.ToLower().Contains(term)))
                .Select(s => s.Id)
                .ToList();

            var primarySupplierMap = await GetProductPrimarySupplierMapAsync(cancellationToken);
            var matchedProductIdsFromSP = primarySupplierMap
                .Where(kv => matchedSupplierIds.Contains(kv.Value))
                .Select(kv => kv.Key)
                .ToList();

            stockQuery = FilterStocksBySupplier(
                stockQuery,
                matchedSupplierIds,
                matchedProductIdsFromSP);
        }

        var supplierNameTerms = CleanTerms(filter.SupplierNames);
        if (supplierNameTerms.Count > 0)
        {
            var allSuppliers = await _context.Suppliers
                .AsNoTracking()
                .Where(s => !s.IsDeleted)
                .Select(s => new { s.Id, s.Name })
                .ToListAsync(cancellationToken);

            var matchedSupplierIds = allSuppliers
                .Where(s => supplierNameTerms.Any(term => !string.IsNullOrEmpty(s.Name) && s.Name.ToLower().Contains(term)))
                .Select(s => s.Id)
                .ToList();

            var primarySupplierMap = await GetProductPrimarySupplierMapAsync(cancellationToken);
            var matchedProductIdsFromSP = primarySupplierMap
                .Where(kv => matchedSupplierIds.Contains(kv.Value))
                .Select(kv => kv.Key)
                .ToList();

            stockQuery = FilterStocksBySupplier(
                stockQuery,
                matchedSupplierIds,
                matchedProductIdsFromSP);
        }

        if (!string.IsNullOrWhiteSpace(filter.Category) && !string.Equals(filter.Category, "all", StringComparison.OrdinalIgnoreCase))
        {
            stockQuery = stockQuery.Where(bs => bs.Product != null && bs.Product.Category != null && bs.Product.Category.Name == filter.Category);
        }

        if (!string.IsNullOrWhiteSpace(filter.Status) && !string.Equals(filter.Status, "all", StringComparison.OrdinalIgnoreCase))
        {
            stockQuery = stockQuery.Where(bs => bs.Status == filter.Status || (string.IsNullOrEmpty(bs.Status) && bs.Product != null && bs.Product.Status == filter.Status));
        }

        // Nhóm theo từng cặp (BranchId, ProductId)
        var grouped = stockQuery
            .Where(bs => bs.Product != null && bs.Branch != null)
            .GroupBy(bs => new
            {
                bs.BranchId,
                bs.ProductId,
                BranchCode = bs.Branch!.BranchCode,
                BranchName = bs.Branch!.Name,
                ProductName = bs.Product!.Name,
                ProductCode = bs.Product.ProductCode,
                Barcode = bs.Product.Barcode,
                CategoryName = bs.Product.Category != null ? bs.Product.Category.Name : "Khác",
                Unit = bs.Product.BaseUnit,
                MasterRetailPrice = bs.Product.SellingPrice,
                CostPrice = bs.Product.CostPrice ?? 0m,
                MinStock = bs.Product.MinStock,
                MasterProductStatus = bs.Product.Status,
                DirectSupplierId = bs.SupplierId,
                PrimarySupplierCode = bs.Supplier != null ? bs.Supplier.Code : string.Empty,
                PrimarySupplierName = bs.Supplier != null ? bs.Supplier.Name : string.Empty
            })
            .Select(g => new
            {
                g.Key.BranchId,
                g.Key.ProductId,
                g.Key.BranchCode,
                g.Key.BranchName,
                g.Key.ProductName,
                g.Key.ProductCode,
                g.Key.Barcode,
                g.Key.CategoryName,
                g.Key.Unit,
                RetailPrice = g.Max(x => x.RetailPrice) ?? g.Key.MasterRetailPrice,
                g.Key.CostPrice,
                g.Key.MinStock,
                ProductStatus = g.Any(x => x.Status == "Active") ? "Active" : (g.First().Status ?? g.Key.MasterProductStatus),
                DirectSupplierId = g.Key.DirectSupplierId,
                g.Key.PrimarySupplierCode,
                g.Key.PrimarySupplierName,
                FirstStockId = g.First().Id,
                FirstShelfLocation = g.First().ShelfLocation,
                AssignedDate = g.Min(x => x.CreatedAt),
                CurrentStock = g.Sum(x => x.QuantityOnHand)
            });

        // Áp dụng bộ lọc cảnh báo tồn kho
        var stockAlert = filter.StockAlertFilter;
        if (!string.IsNullOrWhiteSpace(stockAlert) && !string.Equals(stockAlert, "all", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(stockAlert, "OUT_OF_STOCK", StringComparison.OrdinalIgnoreCase))
            {
                grouped = grouped.Where(x => x.CurrentStock == 0);
            }
            else if (string.Equals(stockAlert, "LOW", StringComparison.OrdinalIgnoreCase))
            {
                grouped = grouped.Where(x => x.CurrentStock > 0 && x.CurrentStock <= x.MinStock);
            }
            else if (string.Equals(stockAlert, "NORMAL", StringComparison.OrdinalIgnoreCase))
            {
                grouped = grouped.Where(x => x.CurrentStock > x.MinStock);
            }
        }

        var totalCount = await grouped.CountAsync(cancellationToken);

        var safePageIndex = Math.Max(1, pageIndex);
        var safePageSize = Math.Clamp(pageSize, 1, 100);

        var pagedData = await grouped
            .OrderBy(x => x.ProductCode)
            .Skip((safePageIndex - 1) * safePageSize)
            .Take(safePageSize)
            .ToListAsync(cancellationToken);

        // Bổ sung thông tin Nhà cung cấp, MOQ và Giá vốn NCC từ SupplierProducts
        var pagedProductIds = pagedData.Select(x => x.ProductId).Distinct().ToList();
        var rawSp = await _context.SupplierProducts
            .Include(sp => sp.Supplier)
            .Where(sp => pagedProductIds.Contains(sp.ProductId) && !sp.IsDeleted && sp.Supplier != null && !sp.Supplier.IsDeleted)
            .OrderByDescending(sp => sp.IsPrimarySupplier)
            .ThenBy(sp => sp.CreatedAt)
            .ToListAsync(cancellationToken);

        var spLookup = rawSp
            .GroupBy(sp => sp.ProductId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var dtos = pagedData.Select(x =>
        {
            spLookup.TryGetValue(x.ProductId, out var spList);
            var matchedSp = (x.DirectSupplierId.HasValue && spList != null)
                ? (spList.FirstOrDefault(s => s.SupplierId == x.DirectSupplierId.Value) ?? spList.FirstOrDefault())
                : spList?.FirstOrDefault();

            var finalCode = !string.IsNullOrWhiteSpace(x.PrimarySupplierCode)
                ? x.PrimarySupplierCode
                : (matchedSp?.Supplier?.Code ?? string.Empty);

            var finalName = !string.IsNullOrWhiteSpace(x.PrimarySupplierName)
                ? x.PrimarySupplierName
                : (matchedSp?.Supplier?.Name ?? string.Empty);

            var finalCostPrice = (matchedSp != null && matchedSp.AgreedWholesalePrice > 0)
                ? matchedSp.AgreedWholesalePrice
                : x.CostPrice;

            var finalMoq = (matchedSp != null && matchedSp.MinimumOrderQuantity > 0)
                ? (int)matchedSp.MinimumOrderQuantity
                : 10;

            var finalSupplierId = x.DirectSupplierId ?? matchedSp?.SupplierId;

            return new BranchProductDto
            {
                Id = x.FirstStockId,
                BranchId = x.BranchId,
                BranchCode = x.BranchCode,
                BranchName = x.BranchName,
                ProductId = x.ProductId,
                Name = x.ProductName,
                Sku = x.ProductCode,
                Barcode = x.Barcode,
                Category = x.CategoryName,
                Unit = x.Unit,
                RetailPrice = x.RetailPrice,
                CostPrice = finalCostPrice,
                CurrentStock = x.CurrentStock,
                MinStockLevel = x.MinStock,
                SupplierId = finalSupplierId,
                PrimarySupplierCode = finalCode,
                PrimarySupplierName = finalName,
                MinOrderQuantity = finalMoq,
                Status = x.ProductStatus,
                ShelfLocation = x.FirstShelfLocation,
                AssignedDate = x.AssignedDate
            };
        }).ToList();

        return (dtos, totalCount);
    }

    private async Task<Dictionary<Guid, Guid>> GetProductPrimarySupplierMapAsync(CancellationToken cancellationToken)
    {
        var raw = await _context.SupplierProducts
            .AsNoTracking()
            .Where(sp => !sp.IsDeleted && sp.Supplier != null && !sp.Supplier.IsDeleted)
            .OrderByDescending(sp => sp.IsPrimarySupplier)
            .ThenBy(sp => sp.CreatedAt)
            .Select(sp => new { sp.ProductId, sp.SupplierId })
            .ToListAsync(cancellationToken);

        return raw
            .GroupBy(sp => sp.ProductId)
            .ToDictionary(g => g.Key, g => g.First().SupplierId);
    }

    private static IQueryable<BranchStock> FilterStocksBySupplier(
        IQueryable<BranchStock> stockQuery,
        List<Guid> matchedSupplierIds,
        List<Guid> matchedProductIdsFromSP)
    {
        if (matchedSupplierIds.Count == 0)
        {
            return stockQuery.Where(_ => false);
        }

        return stockQuery.Where(bs =>
            (bs.SupplierId.HasValue && matchedSupplierIds.Contains(bs.SupplierId.Value))
            || (!bs.SupplierId.HasValue && matchedProductIdsFromSP.Contains(bs.ProductId)));
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

        var stocks = await _context.BranchStocks
            .Include(bs => bs.Product)
            .AsNoTracking()
            .Where(bs => bs.Product != null && !bs.IsDeleted)
            .ToListAsync(cancellationToken);

        var result = new List<BranchSummaryDto>();

        foreach (var b in branches)
        {
            var branchStocks = stocks.Where(s => s.BranchId == b.Id).ToList();

            var distinctProducts = branchStocks
                .GroupBy(s => s.ProductId)
                .Select(g => new
                {
                    ProductId = g.Key,
                    TotalStock = g.Sum(x => x.QuantityOnHand),
                    MinStock = g.First().Product?.MinStock ?? 0m
                })
                .ToList();

            var totalStock = branchStocks.Sum(s => s.QuantityOnHand);
            var lowStockCount = distinctProducts.Count(p => p.TotalStock <= p.MinStock);

            result.Add(new BranchSummaryDto
            {
                Id = b.Id,
                Code = b.Code,
                Name = b.Name,
                Address = b.Address ?? "Hồ Chí Minh, Việt Nam",
                ManagerName = b.ManagerName ?? "Quản lý chi nhánh",
                Phone = b.Phone ?? "0901 234 567",
                TotalProducts = distinctProducts.Count,
                TotalStock = totalStock,
                LowStockCount = lowStockCount
            });
        }

        return result;
    }

    public async Task<BranchStock?> GetBranchStockAsync(
        Guid branchId,
        Guid productId,
        CancellationToken cancellationToken = default)
    {
        return await _context.BranchStocks
            .Include(bs => bs.Product)
            .Include(bs => bs.Branch)
            .FirstOrDefaultAsync(bs => bs.BranchId == branchId && bs.ProductId == productId, cancellationToken);
    }

    public async Task<IReadOnlyList<BranchStock>> GetBranchStocksByProductAsync(
        Guid branchId,
        Guid productId,
        CancellationToken cancellationToken = default)
    {
        return await _context.BranchStocks
            .Include(bs => bs.Product)
            .Include(bs => bs.Branch)
            .Where(bs => bs.BranchId == branchId && bs.ProductId == productId)
            .ToListAsync(cancellationToken);
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
            var matched = await query.FirstOrDefaultAsync(sp => sp.SupplierId == supplierId.Value, cancellationToken);
            if (matched != null) return matched;
        }

        return await query
            .OrderByDescending(sp => sp.IsPrimarySupplier)
            .ThenBy(sp => sp.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<Product?> GetProductByIdAsync(
        Guid productId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Products
            .Include(p => p.Category)
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
