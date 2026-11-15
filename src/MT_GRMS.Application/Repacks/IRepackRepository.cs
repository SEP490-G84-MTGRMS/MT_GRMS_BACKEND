using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Entities.Inventory;

namespace MT_GRMS.Application.Repacks;

public interface IRepackRepository
{
    Task<BranchStock?> GetBranchStockAsync(string tenantId, Guid branchStockId, CancellationToken ct = default);
    Task<Product?> GetProductWithUnitsAsync(string tenantId, Guid productId, CancellationToken ct = default);
    Task<decimal> GetActivePackageWeightAsync(string tenantId, Guid branchStockId, CancellationToken ct = default);
    Task<IReadOnlyList<Package>> GetPackagesByBranchStockAsync(string tenantId, Guid branchStockId, CancellationToken ct = default);
    Task<Package?> GetPackageByIdAsync(string tenantId, Guid packageId, CancellationToken ct = default);
    Task<int> CountPackagesCreatedTodayAsync(string tenantId, DateTime utcDate, CancellationToken ct = default);
    Task<int> CountRepackOrdersTodayAsync(string tenantId, DateTime utcDate, CancellationToken ct = default);
    Task AddRepackOrderAsync(RepackOrder order, CancellationToken ct = default);
    Task AddPackagesAsync(IEnumerable<Package> packages, CancellationToken ct = default);
    Task<bool> IsCodeConflictAsync(string tenantId, string? internalCode, string? barcode, CancellationToken ct = default);
}
