using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MT_GRMS.Api.Filters;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.PosOrders;
using MT_GRMS.Application.PosOrders.Dtos;

namespace MT_GRMS.Api.Controllers;

[ApiController]
[Route("api/orders")]
[Authorize]
public class OrdersController : BaseApiController
{
    private readonly IOrderService _orderService;

    public OrdersController(IOrderService orderService)
    {
        _orderService = orderService;
    }

    /// <summary>
    /// Tạo đơn hàng bán lẻ POS (hỗ trợ Tiền mặt, Ngân hàng VietQR, và Bán nợ công nợ khách hàng)
    /// Tự động trừ kho FEFO, chốt cứng giá vốn COGS và ghi nhận Sổ quỹ
    /// </summary>
    [HttpPost]
    [RequireIdempotency]
    public async Task<ActionResult<ApiResponse<OrderResponse>>> CreateOrder([FromBody] OrderCreateRequest request, CancellationToken cancellationToken)
    {
        var response = await _orderService.CreateOrderAsync(request, cancellationToken);
        return Ok(ApiResponse<OrderResponse>.Ok(response, "Tạo đơn hàng POS thành công."));
    }

    /// <summary>
    /// Lấy thông tin chi tiết một đơn hàng theo ID
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<OrderResponse>>> GetOrderById([FromRoute] Guid id, CancellationToken cancellationToken)
    {
        var order = await _orderService.GetOrderByIdAsync(id, cancellationToken);
        if (order == null)
        {
            return NotFound(ApiResponse<OrderResponse>.Fail("ORDER_NOT_FOUND", $"Không tìm thấy đơn hàng với ID '{id}'."));
        }
        return Ok(ApiResponse<OrderResponse>.Ok(order));
    }

    /// <summary>
    /// Danh sách đơn hàng bán lẻ có phân trang và bộ lọc theo chi nhánh, ngày, trạng thái
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetOrders([FromQuery] OrderFilterRequest filter, CancellationToken cancellationToken)
    {
        var (items, totalCount) = await _orderService.GetOrdersAsync(filter, cancellationToken);
        return Ok(new
        {
            items,
            totalCount,
            pageNumber = filter.PageNumber,
            pageSize = filter.PageSize
        });
    }

    /// <summary>
    /// Tạo đơn đặt trước giữ kho cho khách hàng, nhận tiền đặt cọc
    /// </summary>
    [HttpPost("pre-orders")]
    public async Task<IActionResult> CreatePreOrder([FromBody] PreOrderCreateRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _orderService.CreatePreOrderAsync(request, cancellationToken);
            return CreatedAtAction(nameof(GetOrderById), new { id = response.Id }, response);
        }
        catch (FluentValidation.ValidationException valEx)
        {
            return BadRequest(new { message = "Dữ liệu đơn đặt trước không hợp lệ.", errors = valEx.Errors });
        }
        catch (UnauthorizedAccessException authEx)
        {
            return StatusCode(403, new { message = authEx.Message });
        }
        catch (InvalidOperationException opEx)
        {
            return BadRequest(new { message = opEx.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Lỗi xử lý đơn đặt trước.", detail = ex.Message });
        }
    }

    /// <summary>
    /// Hoàn tất giao hàng đơn đặt trước khi khách đến quầy nhận hàng (thanh toán nốt số tiền còn lại)
    /// </summary>
    [HttpPut("{id:guid}/fulfill")]
    public async Task<IActionResult> FulfillPreOrder([FromRoute] Guid id, [FromBody] PreOrderFulfillRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _orderService.FulfillPreOrderAsync(id, request, cancellationToken);
            return Ok(response);
        }
        catch (UnauthorizedAccessException authEx)
        {
            return StatusCode(403, new { message = authEx.Message });
        }
        catch (InvalidOperationException opEx)
        {
            return BadRequest(new { message = opEx.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Lỗi hoàn tất đơn đặt trước.", detail = ex.Message });
        }
    }

    /// <summary>
    /// Hủy đơn hàng hoặc đơn đặt trước, tự động hoàn trả tồn kho về BranchStocks
    /// </summary>
    [HttpPut("{id:guid}/cancel")]
    public async Task<IActionResult> CancelOrder([FromRoute] Guid id, [FromBody] CancelOrderRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var userRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value 
                           ?? User.FindFirst("role")?.Value 
                           ?? request.UserRole;
            var effectiveRequest = request with { UserRole = userRole };
            var response = await _orderService.CancelOrderAsync(id, effectiveRequest, cancellationToken);
            return Ok(response);
        }
        catch (UnauthorizedAccessException authEx)
        {
            return StatusCode(403, new { message = authEx.Message });
        }
        catch (InvalidOperationException opEx)
        {
            return BadRequest(new { message = opEx.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Lỗi khi hủy đơn hàng.", detail = ex.Message });
        }
    }

    /// <summary>
    /// Xử lý Đổi / Trả hàng chuẩn KiotViet: hoàn kho hàng trả, trừ kho hàng đổi mới (FEFO) và bù trừ chênh lệch
    /// </summary>
    [HttpPost("{id:guid}/return-exchange")]
    public async Task<IActionResult> ReturnExchange([FromRoute] Guid id, [FromBody] ReturnExchangeRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _orderService.ProcessReturnExchangeAsync(id, request, cancellationToken);
            return Ok(response);
        }
        catch (UnauthorizedAccessException authEx)
        {
            return StatusCode(403, new { message = authEx.Message });
        }
        catch (InvalidOperationException opEx)
        {
            return BadRequest(new { message = opEx.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Lỗi khi xử lý đổi trả hàng.", detail = ex.Message });
        }
    }

    /// <summary>
    /// Lấy thống kê tổng hợp số lượng đơn, doanh thu ca / ngày, tiền mặt vs QR, và đơn đặt trước
    /// </summary>
    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary(
        [FromQuery] Guid? branchId,
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate,
        CancellationToken cancellationToken)
    {
        try
        {
            var summary = await _orderService.GetOrderSummaryAsync(branchId, fromDate, toDate, cancellationToken);
            return Ok(summary);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Lỗi thống kê doanh thu.", detail = ex.Message });
        }
    }

    /// <summary>
    /// In hóa đơn nhiệt siêu thị mini POS-58 (khổ in 43mm) 1-Click trực tiếp qua Windows Spooler RAW
    /// </summary>
    [HttpPost("{id:guid}/print")]
    public async Task<IActionResult> PrintOrder([FromRoute] Guid id, [FromBody] PrintOrderRequest? printRequest, CancellationToken cancellationToken)
    {
        try
        {
            bool printed = await _orderService.PrintOrderInvoiceAsync(id, printRequest, cancellationToken);
            if (!printed)
            {
                return Ok(new
                {
                    success = false,
                    message = "Lệnh in đã được gửi nhưng máy in POS-58 chưa sẵn sàng hoặc không kết nối cổng USB. Vui lòng kiểm tra cáp in!"
                });
            }

            return Ok(new
            {
                success = true,
                message = "Đã gửi lệnh in hóa đơn siêu thị mini POS-58 thành công!"
            });
        }
        catch (UnauthorizedAccessException authEx)
        {
            return StatusCode(403, new { message = authEx.Message });
        }
        catch (InvalidOperationException opEx)
        {
            return BadRequest(new { message = opEx.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Lỗi hệ thống khi in hóa đơn.", detail = ex.Message });
        }
    }

    /// <summary>
    /// Lấy mảng byte ESC/POS nhị phân (khổ in 43mm) của hóa đơn để kiểm thử hoặc in client-side
    /// </summary>
    [HttpGet("{id:guid}/receipt-bytes")]
    public async Task<IActionResult> GetReceiptBytes([FromRoute] Guid id, [FromQuery] decimal? cashReceived, [FromQuery] string? cashierName, CancellationToken cancellationToken)
    {
        try
        {
            byte[] bytes = await _orderService.GetOrderReceiptEscPosBytesAsync(id, cashReceived, cashierName, cancellationToken);
            return File(bytes, "application/octet-stream", $"invoice_{id}.bin");
        }
        catch (UnauthorizedAccessException authEx)
        {
            return StatusCode(403, new { message = authEx.Message });
        }
        catch (InvalidOperationException opEx)
        {
            return BadRequest(new { message = opEx.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Lỗi lấy luồng in ESC/POS.", detail = ex.Message });
        }
    }
}
