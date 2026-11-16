using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MT_GRMS.Application.PosOrders.Dtos;

namespace MT_GRMS.Application.PosOrders;

public interface IOrderService
{
    /// <summary>
    /// Tạo đơn hàng bán lẻ POS, tự động trừ kho BranchStocks theo FEFO, cập nhật công nợ và tích điểm
    /// </summary>
    Task<OrderResponse> CreateOrderAsync(OrderCreateRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy chi tiết đơn hàng theo ID
    /// </summary>
    Task<OrderResponse?> GetOrderByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy danh sách đơn hàng có phân trang và bộ lọc
    /// </summary>
    Task<(IReadOnlyList<OrderResponse> Items, int TotalCount)> GetOrdersAsync(OrderFilterRequest filter, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy danh sách đơn hàng theo chi nhánh, khách hàng hoặc khoảng thời gian
    /// </summary>
    Task<List<OrderResponse>> GetOrdersAsync(Guid? branchId = null, Guid? customerId = null, DateTime? fromDate = null, DateTime? toDate = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// In hóa đơn nhiệt POS-58 (khổ in 43mm siêu thị mini / tạp hóa) gửi thẳng qua Windows Print Spooler RAW
    /// </summary>
    Task<bool> PrintOrderInvoiceAsync(Guid orderId, PrintOrderRequest? printRequest = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy luồng byte nhị phân ESC/POS phục vụ kiểm thử hoặc in từ client
    /// </summary>
    Task<byte[]> GetOrderReceiptEscPosBytesAsync(Guid orderId, decimal? cashReceived = null, string? cashierName = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tạo đơn đặt trước giữ kho cho khách hàng, nhận tiền cọc và giữ tồn kho FEFO
    /// </summary>
    Task<OrderResponse> CreatePreOrderAsync(PreOrderCreateRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Hoàn tất đơn đặt trước khi khách đến nhận hàng tại quầy (thanh toán nốt số tiền còn lại)
    /// </summary>
    Task<OrderResponse> FulfillPreOrderAsync(Guid orderId, PreOrderFulfillRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Hủy đơn hàng hoặc đơn đặt trước, tự động hoàn trả tồn kho về BranchStocks
    /// </summary>
    Task<OrderResponse> CancelOrderAsync(Guid orderId, CancelOrderRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Xử lý đổi / trả hàng chuẩn KiotViet: hoàn kho hàng trả, trừ kho FEFO hàng mới, bù trừ công nợ và dòng tiền
    /// </summary>
    Task<OrderResponse> ProcessReturnExchangeAsync(Guid orderId, ReturnExchangeRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy thống kê tổng hợp số lượng đơn, doanh thu ca / ngày, tiền mặt vs QR, và đơn đặt trước
    /// </summary>
    Task<OrderSummaryResponse> GetOrderSummaryAsync(Guid? branchId, DateTime? fromDate, DateTime? toDate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Xác nhận thanh toán thành công cho đơn hàng đang ở trạng thái chờ thanh toán (PendingPayment -> Completed/PAID)
    /// </summary>
    Task<OrderResponse> ConfirmOrderPaymentAsync(Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy thống kê các đơn hàng Chủ tiệm tự bán (không gắn ca) chưa chốt vào Sổ quỹ cuối ngày
    /// </summary>
    Task<OwnerUnsettledSummaryResponse> GetOwnerUnsettledSummaryAsync(Guid? branchId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Chốt doanh thu cuối ngày cho Chủ tiệm: gộp toàn bộ các đơn bán lẻ không gắn ca thành 1 bút toán Két sắt + 1 bút toán Ngân hàng
    /// </summary>
    Task<OwnerDailySettlementResponse> SettleOwnerDailyOrdersAsync(SettleOwnerDailyRequest request, CancellationToken cancellationToken = default);
}
