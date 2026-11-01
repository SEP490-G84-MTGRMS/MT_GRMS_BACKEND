using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Inventory.Dtos;

namespace MT_GRMS.Application.Inventory;

/// <summary>
/// Service xử lý nghiệp vụ quản lý tồn kho chi nhánh
/// </summary>
public class InventoryService : IInventoryService
{
    private readonly IInventoryRepository _repository;
    private readonly ITenantContext _tenantContext;

    public InventoryService(
        IInventoryRepository repository,
        ITenantContext tenantContext)
    {
        _repository = repository;
        _tenantContext = tenantContext;
    }

    public async Task<InventoryPagedResultDto> GetPagedStockSummaryAsync(
        InventoryFilterQuery query,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.CurrentTenantId ?? string.Empty;
        return await _repository.GetPagedStockSummaryAsync(tenantId, query, cancellationToken);
    }

    public async Task<IReadOnlyList<InventoryBatchSummaryDto>> GetProductBatchesAsync(
        Guid branchId,
        Guid productId,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.CurrentTenantId ?? string.Empty;
        return await _repository.GetProductBatchesAsync(tenantId, branchId, productId, cancellationToken);
    }
}

