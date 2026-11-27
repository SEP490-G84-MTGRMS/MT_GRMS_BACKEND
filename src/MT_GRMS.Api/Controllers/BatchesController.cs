using Microsoft.AspNetCore.Authorization;
using MT_GRMS.Domain.Constants;
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using MT_GRMS.Application.Batches;
using MT_GRMS.Application.Batches.Dtos;
using MT_GRMS.Application.Common;

namespace MT_GRMS.Api.Controllers;

[ApiController]
[Route("api/batches")]
[Authorize(Policy = "TenantOwnerOrAssignedBranchStaff")]
public class BatchesController : BaseApiController
{
    private readonly IBatchService _batchService;

    public BatchesController(IBatchService batchService)
    {
        _batchService = batchService;
    }

    /// <summary>
    /// Lấy danh sách lô hàng theo bộ lọc, phân trang và chỉ số rủi ro hạn sử dụng FEFO (REQ-01, REQ-02, REQ-03)
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetBatches([FromQuery] BatchFilterQuery query, CancellationToken cancellationToken)
    {
        var result = await _batchService.GetPagedBatchesAsync(query, cancellationToken);
        return Ok(ApiResponse<BatchPagedResultDto>.Ok(result));
    }

    /// <summary>
    /// Lấy chi tiết thông tin một lô hàng theo ID (REQ-04)
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _batchService.GetBatchByIdAsync(id, cancellationToken);
        return Ok(ApiResponse<BatchListItemDto>.Ok(result));
    }

    /// <summary>
    /// Lấy danh sách cảnh báo lô cận hạn theo NearExpiryThresholdDays từng SP (fallback daysThreshold).
    /// </summary>
    [HttpGet("alerts")]
    public async Task<IActionResult> GetExpiryAlerts(
        [FromQuery] int daysThreshold = 30,
        [FromQuery] Guid? branchId = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _batchService.GetNearExpiryBatchesAsync(daysThreshold, branchId, cancellationToken);
        return Ok(ApiResponse<System.Collections.Generic.IReadOnlyList<ExpiryAlertDto>>.Ok(result));
    }

    /// <summary>
    /// Deprecated — tồn live chỉ qua BranchStocks (nhập kho GRN). Endpoint giữ để client cũ nhận lỗi rõ.
    /// </summary>
    [HttpPost]
    [Obsolete("Use Goods Receipt complete to increase BranchStocks.")]
    public async Task<IActionResult> CreateBatch([FromBody] BatchCreateRequest request, CancellationToken cancellationToken)
    {
        var result = await _batchService.CreateBatchAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, ApiResponse<BatchResponse>.Ok(result, "Tạo lô hàng thành công!"));
    }

    [HttpPatch("{id:guid}/fefo-discount")]
    [Authorize(Roles = SystemRoles.StoreOwner)]
    public async Task<IActionResult> ApplyFefoDiscount(Guid id, [FromBody] ApplyFefoDiscountRequest request, CancellationToken cancellationToken)
    {
        var result = await _batchService.ApplyFefoDiscountAsync(id, request, cancellationToken);
        return Ok(ApiResponse<FefoDiscountResponse>.Ok(result, "Đã cập nhật giảm giá FEFO cho lô hàng."));
    }
}

