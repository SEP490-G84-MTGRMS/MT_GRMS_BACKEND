using MT_GRMS.Application.Common;

namespace MT_GRMS.Application.Transfers;

public interface ITransferService
{
    Task<PagedResult<TransferDto>> GetTransfersAsync(TransferFilterParams filter, CancellationToken ct);
    Task<TransferDto> GetTransferAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<TransferStockBatchDto>> GetStockBatchesAsync(Guid branchId, CancellationToken ct);
    Task<TransferDto> CreateTransferAsync(CreateTransferRequest request, CancellationToken ct);
    Task<TransferDto> DispatchTransferAsync(Guid id, CancellationToken ct);
    Task<TransferDto> ReceiveTransferAsync(Guid id, ReceiveTransferRequest request, CancellationToken ct);
    Task<TransferDto> CancelTransferAsync(Guid id, CancelTransferRequest request, CancellationToken ct);
}