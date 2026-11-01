using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Inventory;
using MT_GRMS.Application.Inventory.Dtos;

namespace MT_GRMS.Api.Controllers;

[ApiController]
[Route("api/v1/inventory")]
public class InventoryController : BaseApiController
{
    private readonly IInventoryService _inventoryService;

    public InventoryController(IInventoryService inventoryService)
    {
        _inventoryService = inventoryService;
    }

    /// <summary>
    /// Lấy danh sách tồn kho tổng hợp theo chi nhánh, sản phẩm, 4 chỉ số tồn và KPI (REQ-01, REQ-02, REQ-03)
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetInventorySummary(
        [FromQuery] InventoryFilterQuery query,
        CancellationToken cancellationToken)
    {
        var result = await _inventoryService.GetPagedStockSummaryAsync(query, cancellationToken);
        return Ok(ApiResponse<InventoryPagedResultDto>.Ok(result));
    }

    /// <summary>
    /// Lấy chi tiết các lô hàng FEFO của một sản phẩm tại chi nhánh (REQ-04)
    /// </summary>
    [HttpGet("{productId:guid}/batches")]
    public async Task<IActionResult> GetProductBatches(
        Guid productId,
        [FromQuery] Guid branchId,
        CancellationToken cancellationToken)
    {
        var result = await _inventoryService.GetProductBatchesAsync(branchId, productId, cancellationToken);
        return Ok(ApiResponse<System.Collections.Generic.IReadOnlyList<InventoryBatchSummaryDto>>.Ok(result));
    }
}
