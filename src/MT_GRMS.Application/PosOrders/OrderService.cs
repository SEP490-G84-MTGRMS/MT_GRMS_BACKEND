using MT_GRMS.Application.Cashbook;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Debt;
using MT_GRMS.Application.Debt.Dtos;
using MT_GRMS.Application.PosOrders.Dtos;
using MT_GRMS.Domain.Entities.Sales;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Domain.Exceptions;

namespace MT_GRMS.Application.PosOrders;

/// <summary>
/// Service xử lý đơn hàng POS & Bán nợ công nợ khách hàng (FE-01 + FE-08)
/// Tự động kiểm tra tín dụng, liên kết Sổ quỹ và ghi nhận doanh thu tính thuế
/// </summary>
public class OrderService : IOrderService
{
    private readonly ISalesOrderRepository _orderRepo;
    private readonly ICustomerDebtService _debtService;
    private readonly ICashbookService _cashbookService;
    private readonly ITenantContext _tenantContext;
    private readonly IUnitOfWork _unitOfWork;

    private static readonly Guid DefaultTillId = Guid.Parse("01a19664-6210-7b12-a9eb-6fa16ca5b9db");
    private static readonly Guid DefaultBankId = Guid.Parse("01a19664-6741-7f3d-9be7-cb4c7b876559");
    private static readonly Guid DefaultBranchId = Guid.Parse("cd5e8d1b-b08b-448d-beff-b85ea5873d52");

    public OrderService(
        ISalesOrderRepository orderRepo,
        ICustomerDebtService debtService,
        ICashbookService cashbookService,
        ITenantContext tenantContext,
        IUnitOfWork unitOfWork)
    {
        _orderRepo = orderRepo;
        _debtService = debtService;
        _cashbookService = cashbookService;
        _tenantContext = tenantContext;
        _unitOfWork = unitOfWork;
    }

    public async Task<OrderResponse> CreateOrderAsync(OrderCreateRequest request, CancellationToken ct = default)
    {
        if (request.Items == null || !request.Items.Any())
        {
            throw new BusinessException("EMPTY_CART", "Đơn hàng phải có ít nhất một mặt hàng.");
        }

        var defaultBranchId = await _orderRepo.GetDefaultBranchIdAsync(ct);
        var branchId = request.BranchId.HasValue && request.BranchId != Guid.Empty
            ? request.BranchId.Value
            : defaultBranchId;
        var now = DateTime.UtcNow;
        var orderCode = "HD-" + now.ToString("yyyyMMddHHmmssfff");
        var paymentMethod = (request.PaymentMethod ?? "CASH").Trim().ToUpperInvariant();

        var order = new Order
        {
            Id = Guid.NewGuid(),
            OrderCode = orderCode,
            BranchId = branchId,
            CustomerId = request.CustomerId,
            OrderDate = now,
            DiscountAmount = Math.Max(0, request.DiscountAmount),
            PaymentMethod = paymentMethod,
            Status = "Completed"
        };

        var lineDetails = new List<OrderDetail>();
        int lineNumber = 1;
        decimal rawTotal = 0m;

        var productIds = request.Items.Select(i => i.ProductId).Distinct().ToList();
        var productCostDict = await _orderRepo.GetProductCostPricesAsync(productIds, ct);

        foreach (var item in request.Items)
        {
            if (item.Quantity <= 0)
            {
                throw new BusinessException("INVALID_QUANTITY", $"Số lượng mặt hàng {item.ProductName} phải lớn hơn 0.");
            }
            if (item.UnitPrice < 0)
            {
                throw new BusinessException("INVALID_PRICE", $"Đơn giá mặt hàng {item.ProductName} không hợp lệ.");
            }

            var lineDiscount = Math.Max(0, item.DiscountAmount);
            var actualPrice = Math.Max(0, item.UnitPrice - lineDiscount);
            var lineTotal = item.UnitPrice * item.Quantity;
            rawTotal += lineTotal;

            // Thuế GTGT 1% (hoặc giảm 0.8% nếu được hỗ trợ)
            var vatRate = 0.01m;
            var vatAmount = Math.Round(actualPrice * item.Quantity * vatRate, 2);
            var costPrice = productCostDict.GetValueOrDefault(item.ProductId, 0m);

            var detail = new OrderDetail
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                ProductId = item.ProductId,
                LineNumber = lineNumber++,
                Qty = item.Quantity,
                UnitPrice = item.UnitPrice,
                DiscountAmount = lineDiscount,
                ActualPrice = actualPrice,
                CostPriceAtSale = costPrice, // Chốt cứng giá vốn tại thời điểm bán (FE-10)
                AppliedVatRate = vatRate,
                VatAmount = vatAmount,
                ProductName = item.ProductName
            };

            lineDetails.Add(detail);
        }

        order.TotalAmount = rawTotal;
        order.FinalAmount = Math.Max(0, rawTotal - order.DiscountAmount);
        order.Details = lineDetails;

        CustomerDebtInvoiceResponse? debtInvoiceResponse = null;

        // XỬ LÝ THEO HÌNH THỨC THANH TOÁN
        if (paymentMethod == "CREDIT" || paymentMethod == "DEBT")
        {
            if (!request.CustomerId.HasValue)
            {
                throw new BusinessException("CUSTOMER_REQUIRED", "Bán nợ bắt buộc phải định danh khách hàng.");
            }

            // Gọi Engine kiểm tra hạn mức & ghi nhận nợ
            debtInvoiceResponse = await _debtService.RecordSaleDebtAsync(new RecordSaleDebtRequest(
                CustomerId: request.CustomerId.Value,
                OrderId: order.Id,
                OrderCode: order.OrderCode,
                BranchId: order.BranchId,
                DebtAmount: order.FinalAmount,
                ManagerBypassCode: request.ManagerBypassCode,
                Notes: request.Note ?? $"Bán nợ POS đơn {order.OrderCode}"
            ), ct);

            order.PaymentStatus = "DEBT";
            order.PaymentMethod = "CREDIT";
        }
        else if (paymentMethod == "BANK" || paymentMethod == "VIETQR")
        {
            order.PaymentStatus = "PAID";
            order.PaymentMethod = "BANK";

            // Tự động ghi nhận phiếu thu Sổ quỹ ngân hàng
            await _cashbookService.ReceiveCashAsync(
                accountId: DefaultBankId,
                accountType: FinanceAccountType.Bank,
                amount: order.FinalAmount,
                sourceType: "POS_SALE",
                sourceId: order.Id,
                referenceCode: order.OrderCode,
                description: $"Doanh thu bán hàng POS (Ngân hàng) đơn {order.OrderCode}",
                ct: ct
            );
        }
        else // Mặc định CASH
        {
            order.PaymentStatus = "PAID";
            order.PaymentMethod = "CASH";

            // Tự động ghi nhận phiếu thu Sổ quỹ két quầy
            await _cashbookService.ReceiveCashAsync(
                accountId: DefaultTillId,
                accountType: FinanceAccountType.Till,
                amount: order.FinalAmount,
                sourceType: "POS_SALE",
                sourceId: order.Id,
                referenceCode: order.OrderCode,
                description: $"Doanh thu bán hàng POS (Tiền mặt) đơn {order.OrderCode}",
                ct: ct
            );
        }

        await _orderRepo.AddAsync(order, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return MapToOrderResponse(order, debtInvoiceResponse);
    }

    public async Task<OrderResponse?> GetOrderByIdAsync(Guid id, CancellationToken ct = default)
    {
        var order = await _orderRepo.GetByIdAsync(id, ct);
        if (order == null) return null;
        return MapToOrderResponse(order, null);
    }

    public async Task<List<OrderResponse>> GetOrdersAsync(Guid? branchId = null, Guid? customerId = null, DateTime? fromDate = null, DateTime? toDate = null, CancellationToken ct = default)
    {
        var orders = await _orderRepo.GetListAsync(branchId, customerId, fromDate, toDate, ct);
        return orders.Select(o => MapToOrderResponse(o, null)).ToList();
    }

    private static OrderResponse MapToOrderResponse(Order order, CustomerDebtInvoiceResponse? debtInvoice)
    {
        return new OrderResponse(
            Id: order.Id,
            OrderCode: order.OrderCode,
            BranchId: order.BranchId,
            CustomerId: order.CustomerId,
            CustomerName: order.Customer?.Name,
            TotalAmount: order.TotalAmount,
            DiscountAmount: order.DiscountAmount,
            FinalAmount: order.FinalAmount,
            PaymentMethod: order.PaymentMethod,
            PaymentStatus: order.PaymentStatus,
            Status: order.Status,
            OrderDate: order.OrderDate,
            Items: order.Details.Select(d => new OrderItemResponse(
                Id: d.Id,
                ProductId: d.ProductId,
                ProductName: d.Product?.ProductName ?? d.ProductName,
                UnitPrice: d.UnitPrice,
                Quantity: d.Qty,
                DiscountAmount: d.DiscountAmount,
                ActualPrice: d.ActualPrice,
                CostPriceAtSale: d.CostPriceAtSale,
                TotalLineAmount: d.ActualPrice * d.Qty,
                AppliedVatRate: d.AppliedVatRate,
                VatAmount: d.VatAmount
            )).ToList(),
            DebtInvoice: debtInvoice
        );
    }
}
