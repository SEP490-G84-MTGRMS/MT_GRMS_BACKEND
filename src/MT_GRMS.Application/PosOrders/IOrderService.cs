using MT_GRMS.Application.PosOrders.Dtos;

namespace MT_GRMS.Application.PosOrders;

public interface IOrderService
{
    Task<OrderResponse> CreateOrderAsync(OrderCreateRequest request, CancellationToken ct = default);
    Task<OrderResponse?> GetOrderByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<OrderResponse>> GetOrdersAsync(Guid? branchId = null, Guid? customerId = null, DateTime? fromDate = null, DateTime? toDate = null, CancellationToken ct = default);
}
