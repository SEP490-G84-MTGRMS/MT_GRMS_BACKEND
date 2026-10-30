using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MT_GRMS.Application.Batches.Dtos;

namespace MT_GRMS.Application.Batches;

public interface IBatchService
{
    Task<BatchPagedResultDto> GetPagedBatchesAsync(BatchFilterQuery query, CancellationToken cancellationToken = default);
    Task<BatchListItemDto> GetBatchByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<BatchResponse> CreateBatchAsync(BatchCreateRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ExpiryAlertDto>> GetNearExpiryBatchesAsync(int daysThreshold = 30, Guid? branchId = null, CancellationToken cancellationToken = default);
}

