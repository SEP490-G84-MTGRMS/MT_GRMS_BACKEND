using Microsoft.AspNetCore.Mvc;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.PurchaseOrders;
using MT_GRMS.Application.PurchaseOrders.Dtos;

namespace MT_GRMS.Api.Controllers;

[ApiController]
[Route("api/v1/purchase-orders")]
public class PurchaseOrdersController : BaseApiController
{
    private readonly IPurchaseOrderService _poService;

    public PurchaseOrdersController(IPurchaseOrderService poService)
    {
        _poService = poService;
    }

    /// <summary>
    /// Lấy danh sách các đơn đặt hàng nhập kho PO có phân trang & bộ lọc (REQ-PO-01)
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetPurchaseOrders([FromQuery] PurchaseOrderFilterQuery query, CancellationToken cancellationToken)
    {
        var result = await _poService.GetPagedPurchaseOrdersAsync(query, cancellationToken);
        return Ok(ApiResponse<PagedResult<PurchaseOrderDto>>.Ok(result));
    }

    /// <summary>
    /// Lấy chi tiết một đơn đặt hàng PO theo ID (kèm danh sách mặt hàng đặt mua)
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _poService.GetPurchaseOrderByIdAsync(id, cancellationToken);
        return Ok(ApiResponse<PurchaseOrderDto>.Ok(result));
    }

    /// <summary>
    /// Lấy dữ liệu lưới ma trận đặt hàng đa ngày (Order Matrix Grid Data)
    /// </summary>
    [HttpGet("matrix")]
    public async Task<IActionResult> GetOrderMatrix([FromQuery] OrderMatrixQuery query, CancellationToken cancellationToken)
    {
        var result = await _poService.GetOrderMatrixAsync(query, cancellationToken);
        return Ok(ApiResponse<OrderMatrixResponse>.Ok(result));
    }

    /// <summary>
    /// Lưu bản thảo kế hoạch đặt hàng ma trận (DRAFT)
    /// </summary>
    [HttpPost("matrix/draft")]
    public async Task<IActionResult> SaveMatrixDraft([FromBody] SaveMatrixDraftCommand command, CancellationToken cancellationToken)
    {
        var result = await _poService.SaveMatrixDraftAsync(command, cancellationToken);
        return Ok(ApiResponse<PurchaseOrderDto>.Ok(result, "Lưu bản thảo ma trận đặt hàng thành công."));
    }

    /// <summary>
    /// Chốt ma trận và tự động tách thành các đơn PO độc lập theo từng Ngày giao hàng và Nhà cung cấp (FE-05)
    /// </summary>
    [HttpPost("matrix/split")]
    public async Task<IActionResult> SplitMatrixPo([FromBody] SplitMatrixPoCommand command, CancellationToken cancellationToken)
    {
        var result = await _poService.SplitMatrixPoAsync(command, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<PurchaseOrderDto>>.Ok(result, $"Đã phát hành thành công {result.Count} đơn PO độc lập!"));
    }

    /// <summary>
    /// Cập nhật trạng thái đơn PO (APPROVED, REJECTED, DISPATCHED, CANCELLED)
    /// </summary>
    [HttpPatch("{id:guid}/status")]
    public async Task<IActionResult> UpdateStatus(
        Guid id,
        [FromQuery] string status,
        [FromQuery] string? reason,
        CancellationToken cancellationToken)
    {
        var result = await _poService.UpdatePurchaseOrderStatusAsync(id, status, reason, cancellationToken);
        return Ok(ApiResponse<PurchaseOrderDto>.Ok(result, $"Cập nhật trạng thái đơn PO sang {status} thành công."));
    }
}
