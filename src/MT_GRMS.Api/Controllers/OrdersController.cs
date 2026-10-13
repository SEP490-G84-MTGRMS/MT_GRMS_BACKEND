using Microsoft.AspNetCore.Mvc;
using MT_GRMS.Application.PosOrders;

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
}
