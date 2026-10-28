using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Tax;
using MT_GRMS.Application.Tax.Dtos;
using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Api.Controllers;

[Authorize]
[Route("api/tax/household")]
public class HouseholdTaxController : BaseApiController
{
    private readonly IHouseholdTaxService _taxService;

    public HouseholdTaxController(IHouseholdTaxService taxService)
    {
        _taxService = taxService;
    }

    [HttpGet("summary")]
    public async Task<ActionResult<ApiResponse<HouseholdTaxSummaryDto>>> GetSummary(
        [FromQuery] int? year,
        [FromQuery] TaxPeriodType? periodType,
        [FromQuery] int? periodNumber,
        [FromQuery] Guid? branchId,
        CancellationToken ct)
    {
        var filter = new HouseholdTaxFilterDto
        {
            Year = year ?? DateTime.UtcNow.Year,
            PeriodType = periodType ?? TaxPeriodType.Quarter,
            PeriodNumber = periodNumber ?? 1,
            BranchId = branchId
        };

        var result = await _taxService.GetTaxSummaryAsync(filter, ct);
        return Ok(ApiResponse<HouseholdTaxSummaryDto>.Ok(result));
    }

    [HttpGet("books")]
    public async Task<ActionResult<ApiResponse<List<AccountingBookEntryDto>>>> GetBookDetails(
        [FromQuery] int? year,
        [FromQuery] TaxPeriodType? periodType,
        [FromQuery] int? periodNumber,
        [FromQuery] Guid? branchId,
        [FromQuery] AccountingBookType? bookType,
        CancellationToken ct)
    {
        var y = year ?? DateTime.UtcNow.Year;
        var pType = periodType ?? TaxPeriodType.Quarter;
        var pNum = periodNumber ?? 1;

        var result = await _taxService.GetBookDetailsAsync(y, pType, pNum, branchId, bookType, ct);
        return Ok(ApiResponse<List<AccountingBookEntryDto>>.Ok(result));
    }

    [Authorize(Roles = "StoreOwner,ChiefAccountant,ADMIN,SUPER_ADMIN")]
    [HttpPost("snapshots")]
    public async Task<ActionResult<ApiResponse<HouseholdTaxSnapshotDto>>> CreateSnapshot(
        [FromBody] CreateTaxSnapshotRequest request,
        CancellationToken ct)
    {
        var userId = User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? User?.FindFirst("sub")?.Value
            ?? User?.FindFirst("id")?.Value
            ?? User?.Identity?.Name;

        var finalizedBy = User?.Identity?.Name
            ?? User?.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value
            ?? User?.FindFirst("username")?.Value;

        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(finalizedBy))
        {
            return Unauthorized(ApiResponse<HouseholdTaxSnapshotDto>.Fail("UNAUTHORIZED", "Không xác định được danh tính người dùng thực hiện chốt kỳ tính thuế."));
        }

        var result = await _taxService.FinalizeTaxSnapshotAsync(request, finalizedBy, userId, ct);
        return Ok(ApiResponse<HouseholdTaxSnapshotDto>.Ok(result, "Chốt kỳ tính thuế thành công."));
    }

    [HttpGet("snapshots")]
    public async Task<ActionResult<ApiResponse<List<HouseholdTaxSnapshotDto>>>> GetSnapshots(
        [FromQuery] int? year,
        [FromQuery] Guid? branchId,
        CancellationToken ct)
    {
        var result = await _taxService.GetSnapshotsAsync(year, branchId, ct);
        return Ok(ApiResponse<List<HouseholdTaxSnapshotDto>>.Ok(result));
    }

    [HttpGet("snapshots/{id:guid}")]
    public async Task<ActionResult<ApiResponse<HouseholdTaxSnapshotDto>>> GetSnapshotById(
        [FromRoute] Guid id,
        CancellationToken ct)
    {
        var result = await _taxService.GetSnapshotByIdAsync(id, ct);
        if (result == null)
        {
            return NotFound(ApiResponse<HouseholdTaxSnapshotDto>.Fail("SNAPSHOT_NOT_FOUND", "Không tìm thấy snapshot kỳ tính thuế."));
        }

        return Ok(ApiResponse<HouseholdTaxSnapshotDto>.Ok(result));
    }
}
