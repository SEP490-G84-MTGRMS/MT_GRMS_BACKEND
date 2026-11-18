namespace MT_GRMS.Application.Transfers;

public interface ITransferRequestService
{
    Task<TransferAllocationPreviewDto> PreviewAllocationAsync(CreateTransferRequestGroupRequest request, CancellationToken ct);
    Task<TransferRequestGroupDto> CreateAndAllocateAsync(CreateTransferRequestGroupRequest request, CancellationToken ct);
    Task<TransferRequestGroupDto> GetAsync(Guid id, CancellationToken ct);
}
