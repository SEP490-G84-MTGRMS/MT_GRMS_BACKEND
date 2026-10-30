using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.GoodsReceipts;
using MT_GRMS.Application.GoodsReceipts.Dtos;

namespace MT_GRMS.Api.Controllers;

[ApiController]
[Route("api/v1/goods-receipts")]
public class GoodsReceiptsController : BaseApiController
{
    private readonly IGoodsReceiptService _goodsReceiptService;

    public GoodsReceiptsController(IGoodsReceiptService goodsReceiptService)
    {
        _goodsReceiptService = goodsReceiptService;
    }

    /// <summary>
    /// Lấy danh sách phiếu nhập kho GRN có phân trang & lọc (GRN List - GR-10)
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetGoodsReceipts([FromQuery] GoodsReceiptFilterQuery query, CancellationToken cancellationToken)
    {
        var result = await _goodsReceiptService.GetGoodsReceiptsAsync(query, cancellationToken);
        return Ok(ApiResponse<PagedResult<GoodsReceiptDto>>.Ok(result));
    }

    /// <summary>
    /// Lấy chi tiết một phiếu nhập kho GRN theo ID (GR-10)
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _goodsReceiptService.GetGoodsReceiptByIdAsync(id, cancellationToken);
        return Ok(ApiResponse<GoodsReceiptDto>.Ok(result));
    }

    /// <summary>
    /// Tra cứu danh sách PO đủ điều kiện để lập phiếu nhập kho (GR-01, Mục 4)
    /// </summary>
    [HttpGet("po-lookup")]
    public async Task<IActionResult> GetPoLookup([FromQuery] Guid? branchId, CancellationToken cancellationToken)
    {
        var result = await _goodsReceiptService.GetPoLookupAsync(branchId, cancellationToken);
        return Ok(ApiResponse<List<PurchaseOrderLookupDto>>.Ok(result));
    }

    /// <summary>
    /// Lấy chi tiết mặt hàng từ PO nguồn để điền tự động vào phiếu nhập kho (GR-01)
    /// </summary>
    [HttpGet("po-details/{poId:guid}")]
    public async Task<IActionResult> GetPoReceiptDetails(Guid poId, CancellationToken cancellationToken)
    {
        var result = await _goodsReceiptService.GetPoReceiptDetailsAsync(poId, cancellationToken);
        return Ok(ApiResponse<PoReceiptDetailsDto>.Ok(result));
    }

    /// <summary>
    /// Tạo mới phiếu nhập kho (GR-01, GR-07 Lưu nháp hoặc Hoàn tất ngay)
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> CreateGoodsReceipt([FromBody] CreateGoodsReceiptCommand command, CancellationToken cancellationToken)
    {
        var result = await _goodsReceiptService.CreateGoodsReceiptAsync(command, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, ApiResponse<GoodsReceiptDto>.Ok(result));
    }

    /// <summary>
    /// Hoàn tất phiếu nhập kho: cập nhật tồn kho chi nhánh & chuyển trạng thái PO sang COMPLETED (GR-08)
    /// </summary>
    [HttpPost("{id:guid}/complete")]
    public async Task<IActionResult> CompleteGoodsReceipt(Guid id, [FromBody] CompleteGoodsReceiptCommand command, CancellationToken cancellationToken)
    {
        var result = await _goodsReceiptService.CompleteGoodsReceiptAsync(id, command, cancellationToken);
        return Ok(ApiResponse<GoodsReceiptDto>.Ok(result));
    }

    /// <summary>
    /// Hủy phiếu nhập kho DRAFT và giải phóng đơn PO (GR-09, Mục 11)
    /// </summary>
    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> CancelGoodsReceipt(Guid id, [FromBody] CancelGoodsReceiptCommand command, CancellationToken cancellationToken)
    {
        var result = await _goodsReceiptService.CancelGoodsReceiptAsync(id, command, cancellationToken);
        return Ok(ApiResponse<GoodsReceiptDto>.Ok(result));
    }
}
