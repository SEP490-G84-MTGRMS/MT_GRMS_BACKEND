using System;
using System.Threading;
using System.Threading.Tasks;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Stocktakes.Dtos;
using MT_GRMS.Application.SupplierReturns.Dtos;

namespace MT_GRMS.Application.Stocktakes;

public interface IStocktakeService
{
    Task<PagedResult<StocktakeListItemDto>> GetPagedAsync(
        StocktakeFilterQuery query,
        CancellationToken cancellationToken = default);

    Task<StocktakeDetailDto> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<StocktakeDetailDto> CreateAsync(
        CreateStocktakeCommand command,
        CancellationToken cancellationToken = default);

    Task<StocktakeDetailDto> UpdateAsync(
        Guid id,
        UpdateStocktakeCommand command,
        CancellationToken cancellationToken = default);

    Task<StocktakeDetailDto> SubmitAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<StocktakeDetailDto> ReviewAsync(
        Guid id,
        ReviewStocktakeCommand command,
        CancellationToken cancellationToken = default);

    Task<SupplierReturnDetailDto> CreateReturnFromStocktakeAsync(
        Guid stocktakeId,
        CreateReturnFromStocktakeCommand command,
        CancellationToken cancellationToken = default);
}

