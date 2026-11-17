using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using MT_GRMS.Api.Controllers;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.PosOrders;
using MT_GRMS.Application.PosOrders.Dtos;
using MT_GRMS.Domain.Exceptions;
using Xunit;

namespace MT_GRMS.UnitTests.Controllers;

public class OrdersControllerTests
{
    private readonly Mock<IOrderService> _mockOrderService;
    private readonly OrdersController _controller;

    public OrdersControllerTests()
    {
        _mockOrderService = new Mock<IOrderService>();
        _controller = new OrdersController(_mockOrderService.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                    {
                        new Claim(ClaimTypes.Role, "ADMIN"),
                        new Claim(ClaimTypes.Name, "TestAdmin")
                    }, "TestAuth"))
                }
            }
        };
    }

    private static OrderResponse CreateTestOrder(Guid id, string orderCode = "ORD-001")
    {
        return new OrderResponse(
            Id: id,
            OrderCode: orderCode,
            BranchId: Guid.NewGuid(),
            BranchName: "Branch 1",
            CustomerId: null,
            CustomerName: "Khach le",
            OrderDate: DateTime.UtcNow,
            TotalAmount: 100_000m,
            DiscountAmount: 0m,
            FinalAmount: 100_000m,
            CashTendered: 100_000m,
            ChangeAmount: 0m,
            PaymentMethod: "Cash",
            PaymentStatus: "PAID",
            Status: "COMPLETED",
            Items: new List<OrderItemResponse>());
    }

    [Fact]
    public async Task CreateOrder_ShouldReturnOk()
    {
        var request = new OrderCreateRequest(
            Guid.NewGuid(), null, "Cash", new List<OrderItemCreateDto>(), 100_000m);
        var order = CreateTestOrder(Guid.NewGuid());

        _mockOrderService
            .Setup(s => s.CreateOrderAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        var result = await _controller.CreateOrder(request, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var apiRes = Assert.IsType<ApiResponse<OrderResponse>>(okResult.Value);
        Assert.True(apiRes.Success);
        Assert.Equal("ORD-001", apiRes.Data?.OrderCode);
    }

    [Fact]
    public async Task GetOrderById_ShouldHandleFound_AndNotFound()
    {
        var id = Guid.NewGuid();
        var order = CreateTestOrder(id);

        _mockOrderService
            .Setup(s => s.GetOrderByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        var resFound = await _controller.GetOrderById(id, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(resFound.Result);
        var apiRes = Assert.IsType<ApiResponse<OrderResponse>>(ok.Value);
        Assert.True(apiRes.Success);

        _mockOrderService
            .Setup(s => s.GetOrderByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrderResponse?)null);

        var resNotFound = await _controller.GetOrderById(id, CancellationToken.None);
        Assert.IsType<NotFoundObjectResult>(resNotFound.Result);
    }

    [Fact]
    public async Task GetOrders_ShouldReturnOk()
    {
        var filter = new OrderFilterRequest { PageNumber = 1, PageSize = 20 };
        var items = new List<OrderResponse> { CreateTestOrder(Guid.NewGuid()) };

        _mockOrderService
            .Setup(s => s.GetOrdersAsync(filter, It.IsAny<CancellationToken>()))
            .ReturnsAsync((items, 1));

        var result = await _controller.GetOrders(filter, CancellationToken.None);
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task CreatePreOrder_ShouldHandleSuccess_AndExceptions()
    {
        var request = new PreOrderCreateRequest(Guid.NewGuid(), Guid.NewGuid(), new List<OrderItemCreateDto>(), 50_000m, DateTime.UtcNow.AddDays(1), "Cash");
        var order = CreateTestOrder(Guid.NewGuid(), "PRE-001");

        _mockOrderService
            .Setup(s => s.CreatePreOrderAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        var res = await _controller.CreatePreOrder(request, CancellationToken.None);
        Assert.IsType<CreatedAtActionResult>(res);

        // ValidationException
        _mockOrderService
            .Setup(s => s.CreatePreOrderAsync(request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FluentValidation.ValidationException("Validation error"));
        var resVal = await _controller.CreatePreOrder(request, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(resVal);

        // BusinessException
        _mockOrderService
            .Setup(s => s.CreatePreOrderAsync(request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BusinessException("ERR", "Business error"));
        var resBiz = await _controller.CreatePreOrder(request, CancellationToken.None);
        Assert.IsType<UnprocessableEntityObjectResult>(resBiz);
    }

    [Fact]
    public async Task FulfillPreOrder_ShouldHandleSuccess_AndExceptions()
    {
        var id = Guid.NewGuid();
        var request = new PreOrderFulfillRequest("Cash", 50_000m);
        var order = CreateTestOrder(id);

        _mockOrderService
            .Setup(s => s.FulfillPreOrderAsync(id, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        var res = await _controller.FulfillPreOrder(id, request, CancellationToken.None);
        Assert.IsType<OkObjectResult>(res);

        // BusinessException
        _mockOrderService
            .Setup(s => s.FulfillPreOrderAsync(id, request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BusinessException("ERR", "Business error"));
        var resBiz = await _controller.FulfillPreOrder(id, request, CancellationToken.None);
        Assert.IsType<UnprocessableEntityObjectResult>(resBiz);
    }

    [Fact]
    public async Task CancelOrder_ShouldHandleSuccess_AndExceptions()
    {
        var id = Guid.NewGuid();
        var request = new CancelOrderRequest("Khach doi y");
        var order = CreateTestOrder(id);

        _mockOrderService
            .Setup(s => s.CancelOrderAsync(id, It.IsAny<CancelOrderRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        var res = await _controller.CancelOrder(id, request, CancellationToken.None);
        Assert.IsType<OkObjectResult>(res);

        // BusinessException
        _mockOrderService
            .Setup(s => s.CancelOrderAsync(id, It.IsAny<CancelOrderRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BusinessException("ERR", "Cannot cancel"));
        var resBiz = await _controller.CancelOrder(id, request, CancellationToken.None);
        Assert.IsType<UnprocessableEntityObjectResult>(resBiz);
    }

    [Fact]
    public async Task ReturnExchange_ShouldHandleSuccess_AndExceptions()
    {
        var id = Guid.NewGuid();
        var request = new ReturnExchangeRequest(new List<ReturnItemDto>(), new List<OrderItemCreateDto>(), "Cash", "Reason", "Notes");
        var order = CreateTestOrder(id);

        _mockOrderService
            .Setup(s => s.ProcessReturnExchangeAsync(id, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        var res = await _controller.ReturnExchange(id, request, CancellationToken.None);
        Assert.IsType<OkObjectResult>(res);

        // BusinessException
        _mockOrderService
            .Setup(s => s.ProcessReturnExchangeAsync(id, request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BusinessException("ERR", "Return error"));
        var resBiz = await _controller.ReturnExchange(id, request, CancellationToken.None);
        Assert.IsType<UnprocessableEntityObjectResult>(resBiz);
    }

    [Fact]
    public async Task GetSummary_ShouldReturnOk_AndHandleException()
    {
        var summary = new OrderSummaryResponse(10, 1_000_000m, 600_000m, 400_000m, 1, 9, 0);
        _mockOrderService
            .Setup(s => s.GetOrderSummaryAsync(It.IsAny<Guid?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(summary);

        var res = await _controller.GetSummary(null, null, null, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(res);
        Assert.Equal(summary, ok.Value);

        // Exception
        _mockOrderService
            .Setup(s => s.GetOrderSummaryAsync(It.IsAny<Guid?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Database error"));
        var resErr = await _controller.GetSummary(null, null, null, CancellationToken.None);
        var objRes = Assert.IsType<ObjectResult>(resErr);
        Assert.Equal(500, objRes.StatusCode);
    }

    [Fact]
    public async Task PrintOrder_ShouldHandleSuccess_NotPrinted_AndExceptions()
    {
        var id = Guid.NewGuid();
        var printReq = new PrintOrderRequest("POS-58");

        // Success
        _mockOrderService
            .Setup(s => s.PrintOrderInvoiceAsync(id, printReq, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var resOk = await _controller.PrintOrder(id, printReq, CancellationToken.None);
        Assert.IsType<OkObjectResult>(resOk);

        // Not printed (printer offline)
        _mockOrderService
            .Setup(s => s.PrintOrderInvoiceAsync(id, printReq, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var resOffline = await _controller.PrintOrder(id, printReq, CancellationToken.None);
        Assert.IsType<OkObjectResult>(resOffline);

        // UnauthorizedAccessException
        _mockOrderService
            .Setup(s => s.PrintOrderInvoiceAsync(id, printReq, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedAccessException("No access"));
        var resUnauth = await _controller.PrintOrder(id, printReq, CancellationToken.None);
        Assert.Equal(403, (resUnauth as ObjectResult)?.StatusCode);

        // BusinessException
        _mockOrderService
            .Setup(s => s.PrintOrderInvoiceAsync(id, printReq, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BusinessException("ERR", "Business error"));
        var resBiz = await _controller.PrintOrder(id, printReq, CancellationToken.None);
        Assert.IsType<UnprocessableEntityObjectResult>(resBiz);

        // InvalidOperationException
        _mockOrderService
            .Setup(s => s.PrintOrderInvoiceAsync(id, printReq, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Invalid operation"));
        var resInvalid = await _controller.PrintOrder(id, printReq, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(resInvalid);

        // Generic Exception
        _mockOrderService
            .Setup(s => s.PrintOrderInvoiceAsync(id, printReq, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("System crash"));
        var resErr = await _controller.PrintOrder(id, printReq, CancellationToken.None);
        Assert.Equal(500, (resErr as ObjectResult)?.StatusCode);
    }

    [Fact]
    public async Task ConfirmOrderPayment_ShouldHandleSuccess_InvalidOperation_AndException()
    {
        var id = Guid.NewGuid();
        var order = CreateTestOrder(id);

        _mockOrderService
            .Setup(s => s.ConfirmOrderPaymentAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);
        var resOk = await _controller.ConfirmOrderPayment(id, CancellationToken.None);
        Assert.IsType<OkObjectResult>(resOk);

        // InvalidOperationException
        _mockOrderService
            .Setup(s => s.ConfirmOrderPaymentAsync(id, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Already paid"));
        var resInvalid = await _controller.ConfirmOrderPayment(id, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(resInvalid);

        // Exception
        _mockOrderService
            .Setup(s => s.ConfirmOrderPaymentAsync(id, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Error"));
        var resErr = await _controller.ConfirmOrderPayment(id, CancellationToken.None);
        Assert.Equal(500, (resErr as ObjectResult)?.StatusCode);
    }

    [Fact]
    public async Task GetOwnerUnsettledSummary_ShouldReturnOk_AndHandleException()
    {
        var summary = new OwnerUnsettledSummaryResponse(5, 500_000m, 200_000m, 700_000m, DateTime.UtcNow, DateTime.UtcNow);
        _mockOrderService
            .Setup(s => s.GetOwnerUnsettledSummaryAsync(It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(summary);

        var res = await _controller.GetOwnerUnsettledSummary(null, CancellationToken.None);
        Assert.IsType<OkObjectResult>(res);

        // Exception
        _mockOrderService
            .Setup(s => s.GetOwnerUnsettledSummaryAsync(It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Fail"));
        var resErr = await _controller.GetOwnerUnsettledSummary(null, CancellationToken.None);
        Assert.Equal(500, (resErr as ObjectResult)?.StatusCode);
    }

    [Fact]
    public async Task SettleOwnerDailyOrders_ShouldHandleSuccess_AndExceptions()
    {
        var req = new SettleOwnerDailyRequest(null, "Settle daily");
        var resObj = new OwnerDailySettlementResponse(5, 500_000m, 200_000m, 700_000m, "STL-01", DateTime.UtcNow);

        _mockOrderService
            .Setup(s => s.SettleOwnerDailyOrdersAsync(req, It.IsAny<CancellationToken>()))
            .ReturnsAsync(resObj);
        var resOk = await _controller.SettleOwnerDailyOrders(req, CancellationToken.None);
        Assert.IsType<OkObjectResult>(resOk);

        // UnauthorizedAccessException
        _mockOrderService
            .Setup(s => s.SettleOwnerDailyOrdersAsync(req, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedAccessException("Forbidden"));
        var resForbid = await _controller.SettleOwnerDailyOrders(req, CancellationToken.None);
        Assert.Equal(403, (resForbid as ObjectResult)?.StatusCode);

        // BusinessException
        _mockOrderService
            .Setup(s => s.SettleOwnerDailyOrdersAsync(req, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BusinessException("ERR", "Business error"));
        var resBiz = await _controller.SettleOwnerDailyOrders(req, CancellationToken.None);
        Assert.IsType<UnprocessableEntityObjectResult>(resBiz);

        // InvalidOperationException
        _mockOrderService
            .Setup(s => s.SettleOwnerDailyOrdersAsync(req, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Invalid"));
        var resInvalid = await _controller.SettleOwnerDailyOrders(req, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(resInvalid);

        // Exception
        _mockOrderService
            .Setup(s => s.SettleOwnerDailyOrdersAsync(req, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Crash"));
        var resErr = await _controller.SettleOwnerDailyOrders(req, CancellationToken.None);
        Assert.Equal(500, (resErr as ObjectResult)?.StatusCode);
    }

    [Fact]
    public async Task GetReceiptBytes_ShouldHandleSuccess_AndExceptions()
    {
        var id = Guid.NewGuid();
        var bytes = new byte[] { 0x1B, 0x40, 0x0A };

        _mockOrderService
            .Setup(s => s.GetOrderReceiptEscPosBytesAsync(id, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(bytes);
        var resOk = await _controller.GetReceiptBytes(id, null, null, CancellationToken.None);
        Assert.IsType<FileContentResult>(resOk);

        // UnauthorizedAccessException
        _mockOrderService
            .Setup(s => s.GetOrderReceiptEscPosBytesAsync(id, null, null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedAccessException("No access"));
        var resUnauth = await _controller.GetReceiptBytes(id, null, null, CancellationToken.None);
        Assert.Equal(403, (resUnauth as ObjectResult)?.StatusCode);

        // BusinessException
        _mockOrderService
            .Setup(s => s.GetOrderReceiptEscPosBytesAsync(id, null, null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BusinessException("ERR", "Biz error"));
        var resBiz = await _controller.GetReceiptBytes(id, null, null, CancellationToken.None);
        Assert.IsType<UnprocessableEntityObjectResult>(resBiz);

        // InvalidOperationException
        _mockOrderService
            .Setup(s => s.GetOrderReceiptEscPosBytesAsync(id, null, null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Not found"));
        var resInvalid = await _controller.GetReceiptBytes(id, null, null, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(resInvalid);

        // Exception
        _mockOrderService
            .Setup(s => s.GetOrderReceiptEscPosBytesAsync(id, null, null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Server error"));
        var resErr = await _controller.GetReceiptBytes(id, null, null, CancellationToken.None);
        Assert.Equal(500, (resErr as ObjectResult)?.StatusCode);
    }
}
