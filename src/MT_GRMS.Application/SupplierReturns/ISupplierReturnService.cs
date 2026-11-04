using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.SupplierReturns.Dtos;

namespace MT_GRMS.Application.SupplierReturns;

public interface ISupplierReturnService
{
    Task<PagedResult<SupplierReturnListItemDto>> GetPagedAsync(
        SupplierReturnFilterQuery query,
        CancellationToken cancellationToken = default);

    Task<SupplierReturnDetailDto> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<List<EligibleGrnLookupDto>> GetEligibleGrnsAsync(
        Guid? branchId,
        Guid? supplierId,
        CancellationToken cancellationToken = default);

    Task<List<GrnReturnableLineDto>> GetGrnReturnableLinesAsync(
        Guid grnId,
        CancellationToken cancellationToken = default);

    Task<SupplierReturnDetailDto> CreateAsync(
        CreateSupplierReturnCommand command,
        CancellationToken cancellationToken = default);

    Task<SupplierReturnDetailDto> SubmitAsync(
        Guid id,
        string? notes = null,
        CancellationToken cancellationToken = default);

    Task<SupplierReturnDetailDto> RecordSupplierResponseAsync(
        Guid id,
        RecordSupplierResponseCommand command,
        CancellationToken cancellationToken = default);

    Task<SupplierReturnDetailDto> CompleteHandoverAsync(
        Guid id,
        CompleteHandoverCommand command,
        CancellationToken cancellationToken = default);

    Task<SupplierReturnDetailDto> CancelAsync(
        Guid id,
        CancelSupplierReturnCommand command,
        CancellationToken cancellationToken = default);
}
