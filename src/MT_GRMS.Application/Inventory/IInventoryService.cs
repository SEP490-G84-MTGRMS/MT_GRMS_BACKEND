using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MT_GRMS.Application.Inventory.Dtos;

namespace MT_GRMS.Application.Inventory;

public interface IInventoryService
{
    Task<InventoryPagedResultDto> GetPagedStockSummaryAsync(
        InventoryFilterQuery query,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<InventoryBatchSummaryDto>> GetProductBatchesAsync(
        Guid branchId,
        Guid productId,
        CancellationToken cancellationToken = default);
}

