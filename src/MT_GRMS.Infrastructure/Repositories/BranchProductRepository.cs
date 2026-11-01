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
            stockQuery = stockQuery.Where(bs =>
                (bs.Product != null && bs.Product.Name.ToLower().Contains(kw)) ||
                (bs.Product != null && bs.Product.ProductCode.ToLower().Contains(kw)) ||
                (bs.Product != null && bs.Product.Barcode.ToLower().Contains(kw)) ||
                (bs.Branch != null && bs.Branch.BranchCode.ToLower().Contains(kw)) ||
                (bs.Branch != null && bs.Branch.Name.ToLower().Contains(kw)));
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

        if (!string.IsNullOrWhiteSpace(filter.Category) && !string.Equals(filter.Category, "all", StringComparison.OrdinalIgnoreCase))
        {
            stockQuery = stockQuery.Where(bs => bs.Product != null && bs.Product.Category != null && bs.Product.Category.Name == filter.Category);
        }

        if (!string.IsNullOrWhiteSpace(filter.Status) && !string.Equals(filter.Status, "all", StringComparison.OrdinalIgnoreCase))
        {
            stockQuery = stockQuery.Where(bs => bs.Product != null && bs.Product.Status == filter.Status);
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
                RetailPrice = bs.Product.SellingPrice,
                CostPrice = bs.Product.CostPrice ?? 0m,
                MinStock = bs.Product.MinStock,
                ProductStatus = bs.Product.Status,
                PrimarySupplierName = bs.Supplier != null ? bs.Supplier.Name : "Nông Sản Tươi Sạch Khải Anh"
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
                g.Key.RetailPrice,
                g.Key.CostPrice,
                g.Key.MinStock,
                g.Key.ProductStatus,
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

        var dtos = pagedData.Select(x => new BranchProductDto
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
            CostPrice = x.CostPrice,
            CurrentStock = x.CurrentStock,
            MinStockLevel = x.MinStock,
            PrimarySupplierName = x.PrimarySupplierName,
            Status = x.ProductStatus,
            ShelfLocation = x.FirstShelfLocation,
            AssignedDate = x.AssignedDate
        }).ToList();

        return (dtos, totalCount);
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
        _context.BranchStocks.RemoveRange(branchStocks);
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
