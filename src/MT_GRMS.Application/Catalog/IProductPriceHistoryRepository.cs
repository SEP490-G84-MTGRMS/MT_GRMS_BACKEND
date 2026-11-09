using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MT_GRMS.Domain.Entities.Catalog;

namespace MT_GRMS.Application.Catalog;

public interface IProductPriceHistoryRepository
{
    Task AddAsync(ProductPriceHistory history, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProductPriceHistory>> GetByProductIdAsync(Guid productId, Guid? branchId = null, CancellationToken cancellationToken = default);
}

