using MT_GRMS.Domain.Entities.Inventory;

namespace MT_GRMS.Application.Transfers;

public interface ITransferRepository
{
    Task<(IReadOnlyList<Transfer> Items, int TotalItems)> GetPagedAsync(
        string tenantId,
        Guid? restrictToBranchId,
        TransferFilterParams filter,
        CancellationToken ct = default);

    Task<Transfer?> GetTransferForUpdateAsync(Guid id, CancellationToken ct = default);

    Task<BranchStock?> GetBranchStockByIdForUpdateAsync(Guid branchStockId, CancellationToken ct = default);

    Task<IReadOnlyList<BranchStock>> GetBranchStocksForUpdateAsync(
        string tenantId,
        Guid branchId,
        Guid productId,
        CancellationToken ct = default);

    Task<IReadOnlyList<BranchStock>> GetBranchStocksByProductForUpdateAsync(
        string tenantId,
        Guid productId,
        Guid excludeBranchId,
        CancellationToken ct = default);
}
