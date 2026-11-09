using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Catalog;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Infrastructure.Persistence;

namespace MT_GRMS.Infrastructure.Repositories;

public class ProductPriceHistoryRepository : GenericRepository<ProductPriceHistory>, IProductPriceHistoryRepository
{
    public ProductPriceHistoryRepository(AppDbContext context) : base(context)
    {
    }

    public async Task<IReadOnlyList<ProductPriceHistory>> GetByProductIdAsync(Guid productId, Guid? branchId = null, CancellationToken cancellationToken = default)
    {
        var query = _context.ProductPriceHistories
            .Include(h => h.Branch)
            .Where(h => h.ProductId == productId && !h.IsDeleted);

        if (branchId.HasValue && branchId.Value != Guid.Empty)
        {
            query = query.Where(h => h.BranchId == branchId.Value || h.BranchId == null);
        }

        return await query
            .OrderByDescending(h => h.CreatedAt)
            .ToListAsync(cancellationToken);
    }
}

