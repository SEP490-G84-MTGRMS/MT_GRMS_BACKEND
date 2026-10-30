using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.SupplierReturns.Dtos;
using MT_GRMS.Domain.Entities.Inventory;
using MT_GRMS.Domain.Entities.Supplier;

namespace MT_GRMS.Application.SupplierReturns;

public interface ISupplierReturnRepository
{
    Task<PagedResult<SupplierReturnListItemDto>> GetPagedAsync(
        string tenantId,
        SupplierReturnFilterQuery query,
        CancellationToken cancellationToken = default);

    Task<SupplierReturn?> GetByIdAsync(
        string tenantId,
        Guid id,
        CancellationToken cancellationToken = default);

    Task<SupplierReturn> AddAsync(
        SupplierReturn entity,
        CancellationToken cancellationToken = default);

    Task UpdateAsync(
        SupplierReturn entity,
        CancellationToken cancellationToken = default);

    Task<string> GenerateCodeAsync(
        string tenantId,
        CancellationToken cancellationToken = default);

    Task<List<EligibleGrnLookupDto>> GetEligibleGrnsAsync(
        string tenantId,
        Guid? branchId,
        Guid? supplierId,
        CancellationToken cancellationToken = default);

    Task<List<GrnReturnableLineDto>> GetGrnReturnableLinesAsync(
        string tenantId,
        Guid grnId,
        CancellationToken cancellationToken = default);

    Task<BranchStock?> GetBranchStockAsync(
        string tenantId,
        Guid branchId,
        Guid productId,
        string? batchCode,
        CancellationToken cancellationToken = default);
}
