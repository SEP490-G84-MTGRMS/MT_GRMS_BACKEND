using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Repacks;
using MT_GRMS.Application.Repacks.Dtos;

namespace MT_GRMS.Api.Controllers;

[ApiController]
[Route("api")]
[Authorize]
public class RepackOrdersController : BaseApiController
{
    private readonly IRepackService _repackService;

    public RepackOrdersController(IRepackService repackService)
    {
        _repackService = repackService;
    }

    [HttpGet("branch-stocks/{branchStockId:guid}/packages")]
    public async Task<IActionResult> GetPackages(Guid branchStockId, CancellationToken cancellationToken)
    {
        var result = await _repackService.GetBranchStockPackagesAsync(branchStockId, cancellationToken);
        return Ok(ApiResponse<BranchStockPackageSummaryDto>.Ok(result));
    }

    [HttpPost("repack-orders")]
    public async Task<IActionResult> Create([FromBody] CreateRepackOrderRequest request, CancellationToken cancellationToken)
    {
        var result = await _repackService.CreateRepackOrderAsync(request, cancellationToken);
        return Ok(ApiResponse<RepackOrderDto>.Ok(result, "Chế biến khay thành công."));
    }

    [HttpPatch("packages/{id:guid}/price")]
    public async Task<IActionResult> UpdatePrice(Guid id, [FromBody] UpdatePackagePriceRequest request, CancellationToken cancellationToken)
    {
        var result = await _repackService.UpdatePackagePriceAsync(id, request, cancellationToken);
        return Ok(ApiResponse<PackageDto>.Ok(result, "Đã cập nhật giá khay."));
    }

    [HttpPost("packages/{id:guid}/quarantine")]
    public async Task<IActionResult> Quarantine(Guid id, CancellationToken cancellationToken)
    {
        var result = await _repackService.QuarantinePackageAsync(id, cancellationToken);
        return Ok(ApiResponse<PackageDto>.Ok(result, "Đã cách ly khay."));
    }

    [HttpPost("packages/{id:guid}/release")]
    public async Task<IActionResult> Release(Guid id, CancellationToken cancellationToken)
    {
        var result = await _repackService.ReleasePackageAsync(id, cancellationToken);
        return Ok(ApiResponse<PackageDto>.Ok(result, "Đã bỏ cách ly khay."));
    }

    [HttpPost("packages/{id:guid}/void")]
    public async Task<IActionResult> Void(Guid id, [FromBody] VoidPackageRequest request, CancellationToken cancellationToken)
    {
        var result = await _repackService.VoidPackageAsync(id, request, cancellationToken);
        return Ok(ApiResponse<PackageDto>.Ok(result, "Đã hủy khay."));
    }
}
