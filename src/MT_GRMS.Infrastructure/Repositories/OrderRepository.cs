using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.PosOrders;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Entities.Inventory;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Domain.Entities.Sales;
using MT_GRMS.Infrastructure.Persistence;

namespace MT_GRMS.Infrastructure.Repositories;

public class OrderRepository : GenericRepository<Order>, IOrderRepository
{
    public OrderRepository(AppDbContext context) : base(context)
    {
    }

    public async Task<Branch?> GetBranchByIdAsync(Guid branchId, CancellationToken cancellationToken = default)
    {
        return await _context.Branches.FirstOrDefaultAsync(b => b.Id == branchId && !b.IsDeleted, cancellationToken);
    }

    public async Task<Customer?> GetCustomerByIdAsync(Guid customerId, CancellationToken cancellationToken = default)
    {
        return await _context.Customers.FirstOrDefaultAsync(c => c.Id == customerId && !c.IsDeleted, cancellationToken);
    }

    public async Task<Dictionary<Guid, Product>> GetProductsByIdsAsync(IEnumerable<Guid> productIds, CancellationToken cancellationToken = default)
    {
        var idList = productIds.Distinct().ToList();
        return await _context.Products
            .Include(p => p.Units)
            .Where(p => idList.Contains(p.Id) && !p.IsDeleted)
            .ToDictionaryAsync(p => p.Id, cancellationToken);
    }

    public async Task<Dictionary<Guid, BranchProduct>> GetBranchProductsByProductIdsAsync(Guid branchId, IEnumerable<Guid> productIds, CancellationToken cancellationToken = default)
    {
        var idList = productIds.Distinct().ToList();
        return await _context.BranchProducts
            .Include(bp => bp.UnitPriceOverrides)
            .Where(bp => bp.BranchId == branchId && idList.Contains(bp.ProductId) && !bp.IsDeleted)
            .ToDictionaryAsync(bp => bp.ProductId, cancellationToken);
    }

    public async Task<IReadOnlyList<BranchStock>> GetBranchStocksForProductFefoAsync(Guid branchId, Guid productId, CancellationToken cancellationToken = default)
    {
        if (_context.Database.IsNpgsql())
        {
            return await _context.BranchStocks
                .FromSqlInterpolated($@"
                    SELECT * FROM ""BranchStocks""
                    WHERE ""BranchId"" = {branchId}
                      AND ""ProductId"" = {productId}
                      AND ""QuantityOnHand"" > 0
                      AND (""ExpiryDate"" IS NULL OR ""ExpiryDate""::date >= CURRENT_DATE)
                      AND NOT ""IsDeleted""
                    ORDER BY ""ExpiryDate"" ASC NULLS LAST, ""ManufactureDate"" ASC NULLS LAST
                    FOR UPDATE")
                .ToListAsync(cancellationToken);
        }

        return await _context.BranchStocks
            .Where(s => s.BranchId == branchId && s.ProductId == productId && s.QuantityOnHand > 0 && !s.IsDeleted
                && (!s.ExpiryDate.HasValue || s.ExpiryDate.Value.Date >= DateTime.UtcNow.Date))
            .OrderBy(s => s.ExpiryDate ?? DateTime.MaxValue)
            .ThenBy(s => s.ManufactureDate ?? DateTime.MaxValue)
            .ToListAsync(cancellationToken);
    }

    public async Task<BranchStock?> GetBranchStockByIdAsync(Guid branchId, Guid productId, Guid batchId, CancellationToken cancellationToken = default)
    {
        if (_context.Database.IsNpgsql())
        {
            return await _context.BranchStocks
                .FromSqlInterpolated($@"
                    SELECT * FROM ""BranchStocks""
                    WHERE ""Id"" = {batchId}
                      AND ""BranchId"" = {branchId}
                      AND ""ProductId"" = {productId}
                      AND NOT ""IsDeleted""
                    FOR UPDATE")
                .FirstOrDefaultAsync(cancellationToken);
        }

        return await _context.BranchStocks.FirstOrDefaultAsync(
            s => s.Id == batchId && s.BranchId == branchId && s.ProductId == productId && !s.IsDeleted,
            cancellationToken);
    }

    public async Task<Order?> GetOrderWithDetailsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Include(o => o.Branch)
            .Include(o => o.Customer)
            .Include(o => o.Details)
                .ThenInclude(d => d.Product)
            .FirstOrDefaultAsync(o => o.Id == id && !o.IsDeleted, cancellationToken);
    }

    public async Task<IReadOnlyList<Order>> GetOrdersWithFilterAsync(
        Guid? branchId,
        Guid? customerId,
        string? paymentMethod,
        string? status,
        DateTime? fromDate,
        DateTime? toDate,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = BuildFilterQuery(branchId, customerId, paymentMethod, status, fromDate, toDate);

        return await query
            .OrderByDescending(o => o.OrderDate)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> CountOrdersWithFilterAsync(
        Guid? branchId,
        Guid? customerId,
        string? paymentMethod,
        string? status,
        DateTime? fromDate,
        DateTime? toDate,
        CancellationToken cancellationToken = default)
    {
        var query = BuildFilterQuery(branchId, customerId, paymentMethod, status, fromDate, toDate);
        return await query.CountAsync(cancellationToken);
    }

    public async Task<BranchStock?> GetBranchStockAsync(Guid branchId, Guid productId, string? batchCode, CancellationToken cancellationToken = default)
    {
        var query = _context.BranchStocks
            .Where(s => s.BranchId == branchId && s.ProductId == productId && !s.IsDeleted);

        if (!string.IsNullOrWhiteSpace(batchCode))
        {
            query = query.Where(s => s.BatchCode == batchCode);
        }

        return await query.FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Order>> GetAllOrdersForSummaryAsync(Guid? branchId, DateTime? fromDate, DateTime? toDate, CancellationToken cancellationToken = default)
    {
        var query = _dbSet.Where(o => !o.IsDeleted).AsQueryable();

        if (branchId.HasValue && branchId.Value != Guid.Empty)
        {
            query = query.Where(o => o.BranchId == branchId.Value);
        }

        if (fromDate.HasValue)
        {
            query = query.Where(o => o.OrderDate >= fromDate.Value);
        }

        if (toDate.HasValue)
        {
            query = query.Where(o => o.OrderDate <= toDate.Value);
        }

        return await query.ToListAsync(cancellationToken);
    }

    private IQueryable<Order> BuildFilterQuery(
        Guid? branchId,
        Guid? customerId,
        string? paymentMethod,
        string? status,
        DateTime? fromDate,
        DateTime? toDate)
    {
        var query = _dbSet
            .Include(o => o.Branch)
            .Include(o => o.Customer)
            .Include(o => o.Details)
                .ThenInclude(d => d.Product)
            .Where(o => !o.IsDeleted)
            .AsQueryable();

        if (branchId.HasValue && branchId.Value != Guid.Empty)
        {
            query = query.Where(o => o.BranchId == branchId.Value);
        }

        if (customerId.HasValue && customerId.Value != Guid.Empty)
        {
            query = query.Where(o => o.CustomerId == customerId.Value);
        }

        if (!string.IsNullOrWhiteSpace(paymentMethod))
        {
            query = query.Where(o => o.PaymentMethod.ToUpper() == paymentMethod.ToUpper());
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(o => o.Status.ToUpper() == status.ToUpper());
        }

        if (fromDate.HasValue)
        {
            query = query.Where(o => o.OrderDate >= fromDate.Value);
        }

        if (toDate.HasValue)
        {
            query = query.Where(o => o.OrderDate <= toDate.Value);
        }

        return query;
    }
}
