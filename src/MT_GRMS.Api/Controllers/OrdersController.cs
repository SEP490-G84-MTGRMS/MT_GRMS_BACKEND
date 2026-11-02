using Microsoft.AspNetCore.Mvc;
using MT_GRMS.Api.Filters;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.PosOrders;
using MT_GRMS.Application.PosOrders.Dtos;

namespace MT_GRMS.Api.Controllers;

[ApiController]
[Route("api/orders")]
public class OrdersController : BaseApiController
{
    private readonly IOrderService _orderService;

    public OrdersController(IOrderService orderService)
    {
        _orderService = orderService;
    }

    /// <summary>
    /// Tạo đơn hàng POS (hỗ trợ Tiền mặt, Ngân hàng VietQR, và Bán nợ công nợ khách hàng)
    /// </summary>
    [HttpPost]
    [RequireIdempotency]
    public async Task<ActionResult<ApiResponse<OrderResponse>>> CreateOrder(
        [FromBody] OrderCreateRequest request,
        CancellationToken ct)
    {
        var result = await _orderService.CreateOrderAsync(request, ct);
        return Ok(ApiResponse<OrderResponse>.Ok(result, "Tạo đơn hàng POS thành công."));
    }

    /// <summary>
    /// Lấy danh sách đơn hàng POS theo chi nhánh, khách hàng hoặc khoảng thời gian
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<OrderResponse>>>> GetOrders(
        [FromQuery] Guid? branchId,
        [FromQuery] Guid? customerId,
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate,
        CancellationToken ct)
    {
        var results = await _orderService.GetOrdersAsync(branchId, customerId, fromDate, toDate, ct);
        return Ok(ApiResponse<List<OrderResponse>>.Ok(results));
    }

    /// <summary>
    /// Lấy chi tiết đơn hàng POS theo ID
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<OrderResponse>>> GetOrderById(
        Guid id,
        CancellationToken ct)
    {
        var result = await _orderService.GetOrderByIdAsync(id, ct);
        if (result == null)
        {
            return NotFound(ApiResponse<OrderResponse>.Fail("ORDER_NOT_FOUND", "Không tìm thấy đơn hàng."));
        }
        return Ok(ApiResponse<OrderResponse>.Ok(result));
    }
}
