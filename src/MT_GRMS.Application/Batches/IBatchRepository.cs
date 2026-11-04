using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MT_GRMS.Application.Batches.Dtos;
using MT_GRMS.Domain.Entities.Inventory;

namespace MT_GRMS.Application.Batches;

public interface IBatchRepository
{
    Task<BatchPagedResultDto> GetPagedBatchesAsync(string tenantId, BatchFilterQuery query, CancellationToken cancellationToken = default);
    Task<BatchListItemDto?> GetBatchByIdAsync(string tenantId, Guid id, CancellationToken cancellationToken = default);
    Task<Batch> AddBatchAsync(Batch batch, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ExpiryAlertDto>> GetNearExpiryBatchesAsync(string tenantId, int daysThreshold, Guid? branchId, CancellationToken cancellationToken = default);
}

