namespace MT_GRMS.Application.Transfers;

public interface ITransferRequestService
{
    Task<IReadOnlyList<TransferAvailableProductDto>> GetAvailableProductsAsync(Guid? toBranchId, CancellationToken ct);
    Task<TransferAllocationPreviewDto> PreviewAllocationAsync(CreateTransferRequestGroupRequest request, CancellationToken ct);
    Task<TransferRequestGroupDto> CreateAndAllocateAsync(CreateTransferRequestGroupRequest request, CancellationToken ct);
    Task<TransferRequestGroupDto> GetAsync(Guid id, CancellationToken ct);
}
