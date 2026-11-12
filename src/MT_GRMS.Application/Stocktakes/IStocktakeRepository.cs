using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Stocktakes.Dtos;
using MT_GRMS.Domain.Entities.Inventory;

namespace MT_GRMS.Application.Stocktakes;

public interface IStocktakeRepository
{
    Task<PagedResult<StocktakeListItemDto>> GetPagedAsync(
        string tenantId,
        StocktakeFilterQuery query,
        CancellationToken cancellationToken = default);

    Task<Stocktake?> GetByIdAsync(
        string tenantId,
        Guid id,
        CancellationToken cancellationToken = default);

    Task<StocktakeDetailDto?> GetDetailDtoByIdAsync(
        string tenantId,
        Guid id,
        CancellationToken cancellationToken = default);

    Task<Stocktake> AddAsync(
        Stocktake entity,
        CancellationToken cancellationToken = default);

    Task UpdateAsync(
        Stocktake entity,
        CancellationToken cancellationToken = default);

    Task<string> GenerateCodeAsync(
        string tenantId,
        CancellationToken cancellationToken = default);

    Task<List<BranchStock>> GetBranchStocksForScopeAsync(
        string tenantId,
        Guid branchId,
        string scope,
        List<Guid>? branchStockIds,
        Guid? categoryId,
        CancellationToken cancellationToken = default);

    Task<Dictionary<Guid, (Guid GrnId, Guid GrnLineId, string GrnCode, Guid SupplierId)>> GetGrnTraceByStockIdsAsync(
        string tenantId,
        List<Guid> branchStockIds,
        CancellationToken cancellationToken = default);

    Task<BranchStock?> GetBranchStockByIdAsync(
        string tenantId,
        Guid branchStockId,
        CancellationToken cancellationToken = default);
}

