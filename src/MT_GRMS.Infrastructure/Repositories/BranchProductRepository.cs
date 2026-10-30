using System;
using System.Collections.Generic;
using System.Linq;
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

    public async Task<(IReadOnlyList<BranchProductDto> Items, int TotalCount)> GetPagedBranchProductsAsync(
        Guid branchId,
        string? searchTerm,
        string? category,
        string? status,
        string? stockAlertFilter,
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

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var kw = searchTerm.Trim().ToLower();
            stockQuery = stockQuery.Where(bs =>
                (bs.Product != null && bs.Product.Name.ToLower().Contains(kw)) ||
                (bs.Product != null && bs.Product.ProductCode.ToLower().Contains(kw)) ||
                (bs.Product != null && bs.Product.Barcode.ToLower().Contains(kw)));
        }

        if (!string.IsNullOrWhiteSpace(category) && !string.Equals(category, "all", StringComparison.OrdinalIgnoreCase))
        {
            stockQuery = stockQuery.Where(bs => bs.Product != null && bs.Product.Category != null && bs.Product.Category.Name == category);
        }

        if (!string.IsNullOrWhiteSpace(status) && !string.Equals(status, "all", StringComparison.OrdinalIgnoreCase))
        {
            stockQuery = stockQuery.Where(bs => bs.Product != null && bs.Product.Status == status);
        }

        // Nhóm theo từng cặp (BranchId, ProductId)
        var grouped = stockQuery
            .Where(bs => bs.Product != null && bs.Branch != null)
            .GroupBy(bs => new
            {
                bs.BranchId,
                bs.ProductId,
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
        if (!string.IsNullOrWhiteSpace(stockAlertFilter) && !string.Equals(stockAlertFilter, "all", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(stockAlertFilter, "OUT_OF_STOCK", StringComparison.OrdinalIgnoreCase))
            {
                grouped = grouped.Where(x => x.CurrentStock == 0);
            }
            else if (string.Equals(stockAlertFilter, "LOW", StringComparison.OrdinalIgnoreCase))
            {
                grouped = grouped.Where(x => x.CurrentStock > 0 && x.CurrentStock <= x.MinStock);
            }
            else if (string.Equals(stockAlertFilter, "NORMAL", StringComparison.OrdinalIgnoreCase))
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
