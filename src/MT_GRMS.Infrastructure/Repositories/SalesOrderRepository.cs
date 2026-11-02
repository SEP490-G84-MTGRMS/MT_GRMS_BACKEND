using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.PosOrders;
using MT_GRMS.Domain.Entities.Sales;
using MT_GRMS.Infrastructure.Persistence;

namespace MT_GRMS.Infrastructure.Repositories;

public class SalesOrderRepository : ISalesOrderRepository
{
    private readonly AppDbContext _context;

    public SalesOrderRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<Order?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _context.Orders
            .Include(o => o.Details)
                .ThenInclude(d => d.Product)
            .Include(o => o.Customer)
            .FirstOrDefaultAsync(o => o.Id == id, ct);
    }

    public async Task<List<Order>> GetListAsync(Guid? branchId = null, Guid? customerId = null, DateTime? fromDate = null, DateTime? toDate = null, CancellationToken ct = default)
    {
        var query = _context.Orders
            .Include(o => o.Details)
                .ThenInclude(d => d.Product)
            .Include(o => o.Customer)
            .AsQueryable();

        if (branchId.HasValue)
        {
            query = query.Where(o => o.BranchId == branchId.Value);
        }

        if (customerId.HasValue)
        {
            query = query.Where(o => o.CustomerId == customerId.Value);
        }

        if (fromDate.HasValue)
        {
            query = query.Where(o => o.CreatedAt >= fromDate.Value);
        }

        if (toDate.HasValue)
        {
            query = query.Where(o => o.CreatedAt <= toDate.Value);
        }

        return await query.OrderByDescending(o => o.CreatedAt).ToListAsync(ct);
    }

    public async Task AddAsync(Order order, CancellationToken ct = default)
    {
        await _context.Orders.AddAsync(order, ct);
    }

    public async Task<Guid> GetDefaultBranchIdAsync(CancellationToken ct = default)
    {
        var branch = await _context.Branches.FirstOrDefaultAsync(ct);
        if (branch != null) return branch.Id;

        return Guid.Parse("cd5e8d1b-b08b-448d-beff-b85ea5873d52");
    }

    public async Task<Dictionary<Guid, decimal>> GetProductCostPricesAsync(List<Guid> productIds, CancellationToken ct = default)
    {
        if (productIds == null || !productIds.Any())
            return new Dictionary<Guid, decimal>();

        return await _context.Products
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.CostPrice ?? 0m, ct);
    }
}
