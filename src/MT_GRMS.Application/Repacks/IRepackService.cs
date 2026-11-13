using System;
using System.Threading;
using System.Threading.Tasks;
using MT_GRMS.Application.Repacks.Dtos;

namespace MT_GRMS.Application.Repacks;

public interface IRepackService
{
    Task<BranchStockPackageSummaryDto> GetBranchStockPackagesAsync(Guid branchStockId, CancellationToken ct = default);
    Task<RepackOrderDto> CreateRepackOrderAsync(CreateRepackOrderRequest request, CancellationToken ct = default);
    Task<PackageDto> UpdatePackagePriceAsync(Guid packageId, UpdatePackagePriceRequest request, CancellationToken ct = default);
    Task<PackageDto> QuarantinePackageAsync(Guid packageId, CancellationToken ct = default);
    Task<PackageDto> ReleasePackageAsync(Guid packageId, CancellationToken ct = default);
    Task<PackageDto> VoidPackageAsync(Guid packageId, VoidPackageRequest request, CancellationToken ct = default);
}
