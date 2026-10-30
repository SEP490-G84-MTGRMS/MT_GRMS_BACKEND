using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Inventory.Dtos;
using MT_GRMS.Domain.Entities.Inventory;

namespace MT_GRMS.Application.Inventory;

public interface IInventoryRepository
{
    Task<InventoryPagedResultDto> GetPagedStockSummaryAsync(
        string tenantId,
        InventoryFilterQuery query,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<InventoryBatchSummaryDto>> GetProductBatchesAsync(
        string tenantId,
        Guid branchId,
        Guid productId,
        CancellationToken cancellationToken = default);
}

