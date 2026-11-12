using MT_GRMS.Application.PurchaseOrders.Dtos;

namespace MT_GRMS.Application.PurchaseOrders.Dispatch;

public interface ISupplierDispatchPackageService
{
    Task<SupplierDispatchPackageDto?> CreatePackageForDispatchAsync(
        SplitMatrixPoCommand command,
        SplitMatrixBatchResultDto batchResult,
        CancellationToken cancellationToken = default);

    Task<SupplierDispatchPackageDto?> GetByIdAsync(Guid packageId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SupplierDispatchPackageDto>> ListAsync(
        Guid? supplierId,
        int take = 50,
        CancellationToken cancellationToken = default);

    Task RetryGenerationAsync(Guid packageId, CancellationToken cancellationToken = default);

    Task RetryEmailAsync(Guid packageId, CancellationToken cancellationToken = default);

    Task CancelScheduledAsync(Guid packageId, CancellationToken cancellationToken = default);

    Task<(byte[] Content, string FileName, string ContentType)> DownloadAsync(
        Guid packageId,
        CancellationToken cancellationToken = default);

    /// <summary>Worker: generate Excel và/hoặc gửi email theo trạng thái package.</summary>
    Task ProcessPackageAsync(Guid packageId, CancellationToken cancellationToken = default);
}
