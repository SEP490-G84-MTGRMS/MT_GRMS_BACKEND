using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentValidation;
using MT_GRMS.Application.Cashbook;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Debt;
using MT_GRMS.Application.Debt.Dtos;
using MT_GRMS.Application.PosOrders.Dtos;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Entities.Sales;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Domain.Exceptions;

namespace MT_GRMS.Application.PosOrders;

public class OrderService : IOrderService
{
    private readonly IOrderRepository _orderRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IThermalPrintService _thermalPrintService;
    private readonly ICustomerDebtService? _debtService;
    private readonly ICashbookService? _cashbookService;
    private readonly IValidator<OrderCreateRequest>? _validator;
    private readonly PosShifts.IShiftRepository? _shiftRepository;
    private readonly Promotions.IPromotionRepository? _promotionRepository;
    private readonly ITenantContext? _tenantContext;

    private static readonly Guid DefaultTillId = Guid.Parse("01a19664-6210-7b12-a9eb-6fa16ca5b9db");
    private static readonly Guid DefaultBankId = Guid.Parse("01a19664-6741-7f3d-9be7-cb4c7b876559");

    public OrderService(
        IOrderRepository orderRepository,
        IUnitOfWork unitOfWork,
        IThermalPrintService thermalPrintService,
        ICustomerDebtService? debtService = null,
        ICashbookService? cashbookService = null,
        IValidator<OrderCreateRequest>? validator = null,
        PosShifts.IShiftRepository? shiftRepository = null,
        Promotions.IPromotionRepository? promotionRepository = null,
        ITenantContext? tenantContext = null)
    {
        _orderRepository = orderRepository;
        _unitOfWork = unitOfWork;
        _thermalPrintService = thermalPrintService;
        _debtService = debtService;
        _cashbookService = cashbookService;
        _validator = validator;
        _shiftRepository = shiftRepository;
        _promotionRepository = promotionRepository;
        _tenantContext = tenantContext;
    }

    public OrderService(
        IOrderRepository orderRepository,
        IUnitOfWork unitOfWork,
        IThermalPrintService thermalPrintService,
        IValidator<OrderCreateRequest>? validator)
        : this(orderRepository, unitOfWork, thermalPrintService, null, null, validator, null, null, null)
    {
    }

    public async Task<OrderResponse> CreateOrderAsync(OrderCreateRequest request, CancellationToken cancellationToken = default)
    {
        if (_validator != null)
        {
            var valResult = await _validator.ValidateAsync(request, cancellationToken);
            if (!valResult.IsValid)
            {
                throw new ValidationException(valResult.Errors);
            }
        }

        if (request.Items == null || request.Items.Count == 0)
        {
            throw new ValidationException("Giỏ hàng không được để trống.");
        }

        // 1. Kiểm tra chi nhánh
        var branchId = request.BranchId.HasValue && request.BranchId != Guid.Empty
            ? request.BranchId.Value
            : Guid.Parse("cd5e8d1b-b08b-448d-beff-b85ea5873d52");

        var branch = await _orderRepository.GetBranchByIdAsync(branchId, cancellationToken);
        if (branch == null)
        {
            throw new InvalidOperationException($"Chi nhánh với ID '{branchId}' không tồn tại trong hệ thống.");
        }

        // 1b. Kiểm tra ca bán hàng của chi nhánh (REQ-SHIFT-SHARED-03)
        ShiftZReport? activeShift = null;
        if (_shiftRepository != null)
        {
            activeShift = await _shiftRepository.GetActiveShiftAsync(branchId, cancellationToken: cancellationToken);
            if (activeShift != null && activeShift.Status == ShiftState.Closing)
            {
                throw new BusinessException(
                    "SHIFT_CLOSING_LOCKDOWN",
                    "Ca bán hàng của chi nhánh đang trong trạng thái kiểm đếm chốt ca (Closing). Không thể tạo đơn mới.");
            }
        }

        // 2. Kiểm tra khách hàng (nếu có)
        Customer? customer = null;
        if (request.CustomerId.HasValue && request.CustomerId.Value != Guid.Empty)
        {
            customer = await _orderRepository.GetCustomerByIdAsync(request.CustomerId.Value, cancellationToken);
            if (customer == null)
            {
                throw new InvalidOperationException($"Khách hàng với ID '{request.CustomerId.Value}' không tồn tại.");
            }
        }

        // 3. Tải danh mục sản phẩm tương ứng và cấu hình giá chi nhánh (BranchProducts)
        var productIds = request.Items.Select(i => i.ProductId).Distinct();
        var products = await _orderRepository.GetProductsByIdsAsync(productIds, cancellationToken);
        var branchProducts = await _orderRepository.GetBranchProductsByProductIdsAsync(branchId, productIds, cancellationToken);

        foreach (var item in request.Items)
        {
            if (!products.TryGetValue(item.ProductId, out _))
            {
                throw new InvalidOperationException($"Sản phẩm với ID '{item.ProductId}' không tồn tại.");
            }
        }

        // 4. Trừ tồn kho BranchStocks theo FEFO (Hạn dùng sớm nhất trừ trước) quy đổi theo ĐVT (REQ-POS-11)
        var productRequiredBaseQty = new Dictionary<Guid, decimal>();
        foreach (var item in request.Items)
        {
            decimal rate = 1.00m;
            if (item.UnitId.HasValue && item.UnitId.Value != Guid.Empty)
            {
                var prod = products[item.ProductId];
                var matchedUnit = prod.Units?.FirstOrDefault(u => u.Id == item.UnitId.Value && !u.IsDeleted);
                if (matchedUnit != null && matchedUnit.ConversionRate > 0)
                {
                    rate = matchedUnit.ConversionRate;
                }
            }
            var baseQty = item.Quantity * rate;
            if (productRequiredBaseQty.ContainsKey(item.ProductId))
            {
                productRequiredBaseQty[item.ProductId] += baseQty;
            }
            else
            {
                productRequiredBaseQty[item.ProductId] = baseQty;
            }
        }

        foreach (var kvp in productRequiredBaseQty)
        {
            var pId = kvp.Key;
            var requiredBaseQty = kvp.Value;
            var prod = products[pId];

            var branchStocks = await _orderRepository.GetBranchStocksForProductFefoAsync(branchId, pId, cancellationToken);

            var totalOnHand = branchStocks.Sum(s => s.QuantityOnHand);
            if (totalOnHand < requiredBaseQty)
            {
                throw new InvalidOperationException($"Sản phẩm '{prod.Name}' không đủ tồn kho tại chi nhánh (Hiện còn: {totalOnHand:0.##}, Cần bán: {requiredBaseQty:0.##}).");
            }

            var remainingToDeduct = requiredBaseQty;
            foreach (var stock in branchStocks)
            {
                if (remainingToDeduct <= 0) break;

                var deduct = Math.Min(stock.QuantityOnHand, remainingToDeduct);
                stock.QuantityOnHand -= deduct;
                stock.UpdatedAt = DateTime.UtcNow;
                remainingToDeduct -= deduct;
            }
        }

        // 5. Tính toán chi tiết đơn hàng (Phân giải giá BranchProduct & BranchProductUnitPrice) (REQ-POS-09, REQ-POS-10)
        decimal totalAmount = 0m;
        decimal itemDiscountTotal = 0m;
        var orderDetails = new List<OrderDetail>();
        int lineNumber = 1;

        foreach (var item in request.Items)
        {
            var prod = products[item.ProductId];
            branchProducts.TryGetValue(item.ProductId, out var bp);

            decimal resolvedUnitPrice;
            string? unitName = null;

            if (item.UnitId.HasValue && item.UnitId.Value != Guid.Empty)
            {
                var matchedUnit = prod.Units?.FirstOrDefault(u => u.Id == item.UnitId.Value && !u.IsDeleted);
                if (matchedUnit != null)
                {
                    unitName = matchedUnit.UnitName;
                }

                var unitOverride = bp?.UnitPriceOverrides?.FirstOrDefault(o => o.ProductUnitId == item.UnitId.Value && !o.IsDeleted);
                if (unitOverride != null && unitOverride.SellingPriceOverride > 0)
                {
                    resolvedUnitPrice = unitOverride.SellingPriceOverride;
                }
                else if (matchedUnit != null && matchedUnit.Price > 0)
                {
                    resolvedUnitPrice = matchedUnit.Price;
                }
                else
                {
                    var baseSelling = (bp?.SellingPriceOverride > 0) ? bp.SellingPriceOverride.Value : prod.SellingPrice;
                    var conversion = matchedUnit?.ConversionRate > 0 ? matchedUnit.ConversionRate : 1.00m;
                    resolvedUnitPrice = baseSelling * conversion;
                }
            }
            else
            {
                if (bp?.SellingPriceOverride > 0)
                {
                    resolvedUnitPrice = bp.SellingPriceOverride.Value;
                }
                else
                {
                    resolvedUnitPrice = prod.SellingPrice;
                }
                unitName = prod.BaseUnit;
            }

            decimal effectiveUnitPrice;
            if (item.UnitId.HasValue && item.UnitId.Value != Guid.Empty)
            {
                effectiveUnitPrice = (item.UnitPrice > 0 && item.UnitPrice != prod.SellingPrice) ? item.UnitPrice : resolvedUnitPrice;
            }
            else if (bp?.SellingPriceOverride > 0 && (item.UnitPrice <= 0 || item.UnitPrice == prod.SellingPrice))
            {
                effectiveUnitPrice = bp.SellingPriceOverride.Value;
            }
            else
            {
                effectiveUnitPrice = item.UnitPrice > 0 ? item.UnitPrice : resolvedUnitPrice;
            }

            decimal lineTotal = item.Quantity * effectiveUnitPrice;
            decimal itemDiscount = item.DiscountAmount ?? 0m;
            decimal actualPrice = item.Quantity > 0 ? Math.Max(0, (lineTotal - itemDiscount) / item.Quantity) : effectiveUnitPrice;

            // Tính VAT theo tỷ lệ của sản phẩm (hoặc mặc định 1% cho tạp hóa)
            decimal vatRate = prod.VatRate > 0 ? prod.VatRate : 0.01m;
            decimal vatAmount = (actualPrice * item.Quantity) * vatRate;

            totalAmount += lineTotal;
            itemDiscountTotal += itemDiscount;

            var detail = new OrderDetail
            {
                Id = Guid.NewGuid(),
                ProductId = item.ProductId,
                LineNumber = lineNumber++,
                Qty = item.Quantity,
                UnitPrice = effectiveUnitPrice,
                DiscountAmount = itemDiscount,
                ActualPrice = actualPrice,
                CostPriceAtSale = prod.CostPrice ?? 0m, // Chốt cứng giá vốn COGS (FE-10)
                AppliedVatRate = vatRate,
                VatAmount = vatAmount,
                ProductName = !string.IsNullOrWhiteSpace(item.ProductName) ? item.ProductName : prod.Name,
                Product = prod
            };
            orderDetails.Add(detail);
        }

        decimal orderDiscount = request.DiscountAmount ?? 0m;
        decimal totalDiscount = itemDiscountTotal + orderDiscount;
        decimal finalAmount = Math.Max(0, totalAmount - totalDiscount);

        // 6. Xử lý phương thức thanh toán & Bán nợ
        string paymentMethodUpper = string.IsNullOrWhiteSpace(request.PaymentMethod) ? "CASH" : request.PaymentMethod.Trim().ToUpperInvariant();
        decimal cashTendered = request.CashTendered ?? finalAmount;
        decimal changeAmount = 0m;
        string paymentStatus = "PAID";
        CustomerDebtInvoiceResponse? debtInvoiceResponse = null;

        if (paymentMethodUpper == "CREDIT" || paymentMethodUpper == "DEBT")
        {
            if (customer == null)
            {
                throw new InvalidOperationException("Bán nợ (ghi sổ) bắt buộc phải chọn thông tin khách hàng.");
            }

            bool isManagerBypass = !string.IsNullOrWhiteSpace(request.ManagerBypassCode) &&
                                  (request.ManagerBypassCode.Trim().Equals("MGR-BYPASS-2026", StringComparison.OrdinalIgnoreCase) ||
                                   request.ManagerBypassCode.Trim().StartsWith("BYPASS", StringComparison.OrdinalIgnoreCase));

            if (!isManagerBypass)
            {
                if (!string.IsNullOrWhiteSpace(request.CreatorRole) &&
                    (request.CreatorRole.Equals("CASHIER", StringComparison.OrdinalIgnoreCase) ||
                     request.CreatorRole.Equals("STAFF", StringComparison.OrdinalIgnoreCase)))
                {
                    throw new UnauthorizedAccessException("Chỉ Chủ cửa hàng hoặc Quản trị viên mới có quyền bán nợ / ghi nợ đơn hàng. Nhân viên thu ngân không được phép thực hiện chức năng này.");
                }

                if (!customer.AllowDebt)
                {
                    throw new InvalidOperationException($"Khách hàng '{customer.Name}' chưa được kích hoạt quyền mua nợ.");
                }
                if (customer.IsBlockedForDebt)
                {
                    throw new InvalidOperationException($"Khách hàng '{customer.Name}' đang bị khóa nợ do nợ quá hạn.");
                }
            }

            paymentStatus = "UNPAID";
            cashTendered = 0m;
            changeAmount = 0m;

            if (_debtService != null)
            {
                debtInvoiceResponse = await _debtService.RecordSaleDebtAsync(new RecordSaleDebtRequest(
                    CustomerId: customer.Id,
                    OrderId: Guid.NewGuid(), // Will be updated
                    OrderCode: $"HD{DateTime.UtcNow:yyMMddHHmmss}{Random.Shared.Next(100, 999)}",
                    BranchId: branchId,
                    DebtAmount: finalAmount,
                    ManagerBypassCode: request.ManagerBypassCode,
                    Notes: request.Note ?? $"Bán nợ POS đơn"
                ), cancellationToken);
            }
            else
            {
                customer.TotalDebt += finalAmount;
            }
            customer.UpdatedAt = DateTime.UtcNow;
        }
        else if (paymentMethodUpper == "CASH")
        {
            if (cashTendered < finalAmount)
            {
                throw new InvalidOperationException($"Tiền khách đưa ({cashTendered:#,##0} đ) không đủ để thanh toán tổng tiền ({finalAmount:#,##0} đ).");
            }
            changeAmount = cashTendered - finalAmount;
            paymentStatus = "PAID";
        }
        else
        {
            // VIETQR / BANK_TRANSFER / BANK / CARD
            cashTendered = finalAmount;
            changeAmount = 0m;
            paymentStatus = "PAID";
        }

        // 7. Tích điểm thưởng cho khách hàng
        if (customer != null)
        {
            decimal earningRate = 10000m;
            int earningPoints = 1;
            if (_promotionRepository != null)
            {
                var loyaltySetting = await _promotionRepository.GetLoyaltySettingAsync(customer.TenantId, cancellationToken);
                if (loyaltySetting != null && loyaltySetting.IsEnabled)
                {
                    earningRate = loyaltySetting.EarningRateAmount > 0 ? loyaltySetting.EarningRateAmount : 10000m;
                    earningPoints = loyaltySetting.EarningPoints > 0 ? loyaltySetting.EarningPoints : 1;
                }
            }

            if (request.PointsRedeemed.HasValue && request.PointsRedeemed.Value > 0)
            {
                int pointsToDeduct = Math.Min(customer.RewardPoints, request.PointsRedeemed.Value);
                customer.RewardPoints -= pointsToDeduct;
            }

            int earnedPoints = (int)(finalAmount / earningRate) * earningPoints;
            customer.RewardPoints += earnedPoints;
            customer.TotalSpending += finalAmount;
            customer.UpdatedAt = DateTime.UtcNow;
        }

        // 8. Lưu Order Entity
        Guid? shiftId = request.ShiftId ?? activeShift?.Id;
        Guid? creatorId = null;
        if (_tenantContext != null && _tenantContext.CurrentUserId != Guid.Empty)
        {
            creatorId = _tenantContext.CurrentUserId;
        }

        string orderCode = debtInvoiceResponse != null ? debtInvoiceResponse.OrderCode : $"HD{DateTime.UtcNow:yyMMddHHmmss}{Random.Shared.Next(100, 999)}";
        var order = new Order
        {
            Id = debtInvoiceResponse != null ? debtInvoiceResponse.OrderId : Guid.NewGuid(),
            BranchId = branchId,
            CustomerId = request.CustomerId,
            ShiftId = shiftId,
            OrderCode = orderCode,
            CreatedBy = creatorId,
            OrderDate = DateTime.UtcNow,
            TotalAmount = totalAmount,
            DiscountAmount = totalDiscount,
            FinalAmount = finalAmount,
            DepositAmount = (paymentMethodUpper == "CASH") ? cashTendered : (paymentMethodUpper == "CREDIT" ? 0m : finalAmount),
            PaymentMethod = paymentMethodUpper,
            PaymentStatus = paymentStatus,
            Status = "Completed",
            Note = request.Note,
            Details = orderDetails,
            Branch = branch,
            Customer = customer,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        foreach (var d in orderDetails)
        {
            d.OrderId = order.Id;
        }

        // Đồng bộ Sổ quỹ (FE-17)
        if (_cashbookService != null)
        {
            if (paymentMethodUpper == "CASH")
            {
                var till = await _cashbookService.GetOrCreateTillAsync(order.BranchId, "Két quầy chính", cancellationToken);
                await _cashbookService.ReceiveCashAsync(
                    till.Id,
                    FinanceAccountType.Till,
                    finalAmount,
                    "POS_SALE",
                    order.Id,
                    orderCode,
                    $"Doanh thu bán hàng POS (Tiền mặt) đơn {orderCode}",
                    cancellationToken
                );
            }
            else if (paymentMethodUpper != "CREDIT" && paymentMethodUpper != "DEBT")
            {
                var accounts = await _cashbookService.GetAccountsAsync(order.BranchId, cancellationToken);
                var bank = accounts.FirstOrDefault(a => string.Equals(a.AccountType, "Bank", StringComparison.OrdinalIgnoreCase));
                var bankId = bank?.Id ?? DefaultBankId;
                await _cashbookService.ReceiveCashAsync(
                    bankId,
                    FinanceAccountType.Bank,
                    finalAmount,
                    "POS_SALE",
                    order.Id,
                    orderCode,
                    $"Doanh thu bán hàng POS ({paymentMethodUpper}) đơn {orderCode}",
                    cancellationToken
                );
            }
        }

        await _orderRepository.AddAsync(order, cancellationToken);

        // Ghi nhận nhân viên tham gia ca bán hàng chung (REQ-SHIFT-SHARED-02)
        if (activeShift != null && _shiftRepository != null)
        {
            var cashierId = creatorId ?? (activeShift.CashierUserId != Guid.Empty ? activeShift.CashierUserId : Guid.Empty);
            if (cashierId != Guid.Empty)
            {
                var participant = await _shiftRepository.GetParticipantAsync(activeShift.Id, cashierId, cancellationToken);
                if (participant == null)
                {
                    await _shiftRepository.AddParticipantAsync(new ShiftParticipant
                    {
                        TenantId = branch.TenantId ?? (_tenantContext?.CurrentTenantId ?? string.Empty),
                        ShiftId = activeShift.Id,
                        UserId = cashierId,
                        UserName = request.CashierName ?? (_tenantContext?.CurrentRole == "STORE_OWNER" ? "Chủ cửa hàng" : "Thu ngân"),
                        FullName = request.CashierName ?? (_tenantContext?.CurrentRole == "STORE_OWNER" ? "Chủ cửa hàng" : "Thu ngân"),
                        JoinedAtUtc = DateTime.UtcNow,
                        LastActiveAtUtc = DateTime.UtcNow
                    }, cancellationToken);
                }
                else
                {
                    participant.LastActiveAtUtc = DateTime.UtcNow;
                    if (!string.IsNullOrWhiteSpace(request.CashierName))
                    {
                        participant.UserName = request.CashierName;
                    }
                }
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // 9. Đổi mã voucher (Redeem) nguyên tử nếu có áp dụng
        if (!string.IsNullOrWhiteSpace(request.VoucherCode) && _promotionRepository != null)
        {
            await _promotionRepository.RedeemVoucherCodeAtomicAsync(
                request.VoucherCode.Trim().ToUpper(),
                order.Id,
                order.OrderCode,
                customer?.Id,
                customer?.Name,
                customer?.Phone,
                cancellationToken
            );
        }

        return MapToOrderResponse(order, cashTendered, changeAmount, debtInvoiceResponse);
    }

    public async Task<OrderResponse?> GetOrderByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetOrderWithDetailsAsync(id, cancellationToken);
        if (order == null) return null;

        decimal cash = order.DepositAmount;
        decimal change = order.PaymentMethod == "CASH" ? Math.Max(0, cash - order.FinalAmount) : 0m;
        return MapToOrderResponse(order, cash, change, null);
    }

    public async Task<(IReadOnlyList<OrderResponse> Items, int TotalCount)> GetOrdersAsync(OrderFilterRequest filter, CancellationToken cancellationToken = default)
    {
        var orders = await _orderRepository.GetOrdersWithFilterAsync(
            filter.BranchId,
            filter.CustomerId,
            filter.PaymentMethod,
            filter.Status,
            filter.FromDate,
            filter.ToDate,
            filter.PageNumber,
            filter.PageSize,
            cancellationToken);

        int totalCount = await _orderRepository.CountOrdersWithFilterAsync(
            filter.BranchId,
            filter.CustomerId,
            filter.PaymentMethod,
            filter.Status,
            filter.FromDate,
            filter.ToDate,
            cancellationToken);

        var responses = orders.Select(o =>
        {
            decimal cash = o.DepositAmount;
            decimal change = o.PaymentMethod == "CASH" ? Math.Max(0, cash - o.FinalAmount) : 0m;
            return MapToOrderResponse(o, cash, change, null);
        }).ToList();

        return (responses, totalCount);
    }

    public async Task<List<OrderResponse>> GetOrdersAsync(Guid? branchId = null, Guid? customerId = null, DateTime? fromDate = null, DateTime? toDate = null, CancellationToken cancellationToken = default)
    {
        var (items, _) = await GetOrdersAsync(new OrderFilterRequest
        {
            BranchId = branchId,
            CustomerId = customerId,
            FromDate = fromDate,
            ToDate = toDate,
            PageSize = 1000
        }, cancellationToken);
        return items.ToList();
    }

    public async Task<bool> PrintOrderInvoiceAsync(Guid orderId, PrintOrderRequest? printRequest = null, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetOrderWithDetailsAsync(orderId, cancellationToken);
        if (order == null)
        {
            throw new InvalidOperationException($"Không tìm thấy đơn hàng với ID '{orderId}'.");
        }

        string printer = string.IsNullOrWhiteSpace(printRequest?.PrinterName) ? "POS-58" : printRequest.PrinterName;
        decimal? cash = printRequest?.CashTendered ?? (order.PaymentMethod == "CASH" ? order.DepositAmount : null);
        decimal? change = cash.HasValue ? Math.Max(0, cash.Value - order.FinalAmount) : null;

        return _thermalPrintService.PrintOrderInvoice(order, printer, cash, change, printRequest?.CashierName);
    }

    public async Task<byte[]> GetOrderReceiptEscPosBytesAsync(Guid orderId, decimal? cashReceived = null, string? cashierName = null, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetOrderWithDetailsAsync(orderId, cancellationToken);
        if (order == null)
        {
            throw new InvalidOperationException($"Không tìm thấy đơn hàng với ID '{orderId}'.");
        }

        decimal? cash = cashReceived ?? (order.PaymentMethod == "CASH" ? order.DepositAmount : null);
        decimal? change = cash.HasValue ? Math.Max(0, cash.Value - order.FinalAmount) : null;

        return _thermalPrintService.BuildEscPosReceipt(order, cash, change, cashierName);
    }

    public async Task<OrderResponse> CreatePreOrderAsync(PreOrderCreateRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Items == null || request.Items.Count == 0)
        {
            throw new ValidationException("Đơn đặt trước phải có ít nhất một mặt hàng.");
        }

        if (request.CustomerId == Guid.Empty)
        {
            throw new InvalidOperationException("Đơn đặt trước bắt buộc phải có thông tin khách hàng định danh.");
        }

        var branch = await _orderRepository.GetBranchByIdAsync(request.BranchId, cancellationToken);
        if (branch == null)
        {
            throw new InvalidOperationException($"Chi nhánh với ID '{request.BranchId}' không tồn tại.");
        }

        var customer = await _orderRepository.GetCustomerByIdAsync(request.CustomerId, cancellationToken);
        if (customer == null)
        {
            throw new InvalidOperationException($"Khách hàng với ID '{request.CustomerId}' không tồn tại.");
        }

        var productIds = request.Items.Select(i => i.ProductId).Distinct();
        var products = await _orderRepository.GetProductsByIdsAsync(productIds, cancellationToken);

        foreach (var item in request.Items)
        {
            if (!products.TryGetValue(item.ProductId, out _))
            {
                throw new InvalidOperationException($"Sản phẩm với ID '{item.ProductId}' không tồn tại.");
            }
        }

        // Trừ/giữ tồn kho FEFO
        var itemGroups = request.Items.GroupBy(i => i.ProductId);
        var orderDetails = new List<OrderDetail>();

        decimal totalAmount = 0m;
        decimal totalDiscount = 0m;

        foreach (var group in itemGroups)
        {
            var pId = group.Key;
            var requiredQty = group.Sum(x => x.Quantity);
            var prod = products[pId];

            var branchStocks = await _orderRepository.GetBranchStocksForProductFefoAsync(request.BranchId, pId, cancellationToken);
            var totalOnHand = branchStocks.Sum(s => s.QuantityOnHand);
            if (totalOnHand < requiredQty)
            {
                throw new InvalidOperationException($"Sản phẩm '{prod.Name}' không đủ tồn kho để đặt trước (Hiện còn: {totalOnHand:0.##}, Cần đặt: {requiredQty:0.##}).");
            }

            var remainingToDeduct = requiredQty;
            foreach (var stock in branchStocks)
            {
                if (remainingToDeduct <= 0) break;
                var deduct = Math.Min(stock.QuantityOnHand, remainingToDeduct);
                stock.QuantityOnHand -= deduct;
                remainingToDeduct -= deduct;

                var reqItem = group.First();
                var itemDiscount = reqItem.DiscountAmount ?? 0m;
                var discountPerUnit = itemDiscount > 0 && reqItem.Quantity > 0
                    ? itemDiscount / reqItem.Quantity
                    : 0m;
                var actualUnitPrice = Math.Max(0, reqItem.UnitPrice - discountPerUnit);

                int lineNumber = orderDetails.Count + 1;
                decimal vatRate = prod.VatRate > 0 ? prod.VatRate : 0.01m;
                decimal vatAmount = (actualUnitPrice * deduct) * vatRate;

                var detail = new OrderDetail
                {
                    Id = Guid.NewGuid(),
                    ProductId = pId,
                    LineNumber = lineNumber,
                    ProductName = prod.Name,
                    Qty = deduct,
                    UnitPrice = reqItem.UnitPrice,
                    DiscountAmount = discountPerUnit * deduct,
                    ActualPrice = actualUnitPrice,
                    CostPriceAtSale = prod.CostPrice ?? 0m,
                    AppliedVatRate = vatRate,
                    VatAmount = vatAmount,
                    Product = prod
                };

                orderDetails.Add(detail);
                totalAmount += reqItem.UnitPrice * deduct;
                totalDiscount += discountPerUnit * deduct;
            }
        }

        decimal finalAmount = Math.Max(0, totalAmount - totalDiscount);

        if (request.DepositAmount < 0)
        {
            throw new InvalidOperationException("Tiền đặt cọc không được là số âm.");
        }

        if (request.DepositAmount > finalAmount)
        {
            throw new InvalidOperationException($"Tiền đặt cọc ({request.DepositAmount:N0} đ) không được lớn hơn tổng giá trị đơn hàng ({finalAmount:N0} đ).");
        }

        decimal minDeposit = Math.Round(finalAmount * 0.10m, 2);
        if (request.DepositAmount < minDeposit)
        {
            throw new InvalidOperationException($"Tiền đặt cọc tối thiểu phải bằng 10% tổng giá trị đơn hàng (Tối thiểu: {minDeposit:N0} đ, Thực tế: {request.DepositAmount:N0} đ).");
        }

        Guid? preOrderShiftId = _shiftRepository != null
            ? (await _shiftRepository.GetActiveShiftAsync(request.BranchId, cancellationToken: cancellationToken))?.Id
            : null;

        var order = new Order
        {
            Id = Guid.NewGuid(),
            BranchId = request.BranchId,
            CustomerId = request.CustomerId,
            ShiftId = preOrderShiftId,
            OrderCode = "PO" + DateTime.UtcNow.ToString("yyMMddHHmmssfff"),
            OrderDate = DateTime.UtcNow,
            TotalAmount = totalAmount,
            DiscountAmount = totalDiscount,
            FinalAmount = finalAmount,
            DepositAmount = request.DepositAmount,
            PaymentMethod = request.PaymentMethod,
            PaymentStatus = request.DepositAmount >= finalAmount ? "PAID" : "PARTIALLY_PAID",
            Status = "PendingPickup",
            Note = $"Hẹn nhận: {request.ExpectedPickupDate:dd/MM/yyyy HH:mm}. {request.Note}".Trim(),
            Details = orderDetails
        };

        await _orderRepository.AddAsync(order, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return MapToOrderResponse(order, request.DepositAmount, 0m, null);
    }

    public async Task<OrderResponse> FulfillPreOrderAsync(Guid orderId, PreOrderFulfillRequest request, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetOrderWithDetailsAsync(orderId, cancellationToken);
        if (order == null)
        {
            throw new InvalidOperationException($"Không tìm thấy đơn hàng với ID '{orderId}'.");
        }

        if (order.Status != "PendingPickup")
        {
            throw new InvalidOperationException($"Đơn hàng '{order.OrderCode}' không ở trạng thái Chờ nhận hàng (trạng thái hiện tại: {order.Status}).");
        }

        decimal remainingAmount = Math.Max(0, order.FinalAmount - order.DepositAmount);
        if (request.AmountPaid < remainingAmount)
        {
            throw new InvalidOperationException($"Số tiền khách trả ({request.AmountPaid:N0} đ) không đủ để thanh toán phần còn lại của đơn hàng ({remainingAmount:N0} đ).");
        }

        order.Status = "Completed";
        order.PaymentStatus = "PAID";
        order.PaymentMethod = request.PaymentMethod;
        order.DepositAmount = order.FinalAmount;
        if (!string.IsNullOrWhiteSpace(request.Note))
        {
            order.Note = $"{order.Note} | Xuất hàng: {request.Note}".Trim();
        }

        // Tích điểm thưởng cho khách hàng (10.000đ = 1 điểm)
        if (order.CustomerId.HasValue && order.CustomerId.Value != Guid.Empty)
        {
            var customer = await _orderRepository.GetCustomerByIdAsync(order.CustomerId.Value, cancellationToken);
            if (customer != null)
            {
                decimal earningRate = 10000m;
                int earningPoints = 1;
                if (_promotionRepository != null)
                {
                    var loyaltySetting = await _promotionRepository.GetLoyaltySettingAsync(customer.TenantId, cancellationToken);
                    if (loyaltySetting != null && loyaltySetting.IsEnabled)
                    {
                        earningRate = loyaltySetting.EarningRateAmount > 0 ? loyaltySetting.EarningRateAmount : 10000m;
                        earningPoints = loyaltySetting.EarningPoints > 0 ? loyaltySetting.EarningPoints : 1;
                    }
                }
                int pointsEarned = (int)(order.FinalAmount / earningRate) * earningPoints;
                customer.RewardPoints += pointsEarned;
                customer.TotalSpending += order.FinalAmount;
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        decimal changeAmount = Math.Max(0, request.AmountPaid - remainingAmount);
        return MapToOrderResponse(order, request.AmountPaid, changeAmount, null);
    }

    public async Task<OrderResponse> CancelOrderAsync(Guid orderId, CancelOrderRequest request, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetOrderWithDetailsAsync(orderId, cancellationToken);
        if (order == null)
        {
            throw new InvalidOperationException($"Không tìm thấy đơn hàng với ID '{orderId}'.");
        }

        if (order.Status == "Cancelled")
        {
            throw new InvalidOperationException($"Đơn hàng '{order.OrderCode}' đã bị hủy trước đó.");
        }

        if (order.Status == "Completed" && (string.Equals(request.UserRole, "CASHIER", StringComparison.OrdinalIgnoreCase) || string.Equals(request.UserRole, "EMPLOYEE", StringComparison.OrdinalIgnoreCase) || (request.UserRole != null && !string.Equals(request.UserRole, "STORE_OWNER", StringComparison.OrdinalIgnoreCase))))
        {
            throw new UnauthorizedAccessException("Chỉ Chủ cửa hàng (STORE_OWNER) mới có quyền hủy hóa đơn đã thanh toán. Nhân viên không được phép tự ý hủy.");
        }

        // Hoàn trả tồn kho về BranchStocks
        foreach (var detail in order.Details)
        {
            var stock = await _orderRepository.GetBranchStockAsync(order.BranchId, detail.ProductId, null, cancellationToken);
            if (stock != null)
            {
                stock.QuantityOnHand += detail.Qty;
            }
        }

        // Nếu là đơn bán nợ thì hoàn nợ cho khách
        if (order.PaymentMethod == "DEBT" && order.CustomerId.HasValue)
        {
            var customer = await _orderRepository.GetCustomerByIdAsync(order.CustomerId.Value, cancellationToken);
            if (customer != null)
            {
                customer.TotalDebt = Math.Max(0, customer.TotalDebt - order.FinalAmount);
            }
        }

        order.Status = "Cancelled";
        order.PaymentStatus = "REFUNDED";
        order.Note = $"Đã hủy: {request.Reason}. {request.Note} | Trước đó: {order.Note}".Trim();

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return MapToOrderResponse(order, 0m, 0m, null);
    }

    public async Task<OrderResponse> ProcessReturnExchangeAsync(Guid orderId, ReturnExchangeRequest request, CancellationToken cancellationToken = default)
    {
        var originalOrder = await _orderRepository.GetOrderWithDetailsAsync(orderId, cancellationToken);
        if (originalOrder == null)
        {
            throw new InvalidOperationException($"Không tìm thấy đơn hàng với ID '{orderId}'.");
        }

        if (originalOrder.Status != "Completed")
        {
            throw new InvalidOperationException($"Chỉ có thể đổi/trả cho đơn hàng đã hoàn thành (Trạng thái hiện tại: {originalOrder.Status}).");
        }

        if ((request.ReturnedItems == null || request.ReturnedItems.Count == 0) &&
            (request.NewItems == null || request.NewItems.Count == 0))
        {
            throw new InvalidOperationException("Yêu cầu đổi trả phải có ít nhất một mặt hàng trả hoặc mặt hàng đổi mới.");
        }

        // 1. Kiểm tra và hoàn kho các mặt hàng khách trả
        decimal totalReturnAmount = 0m;
        if (request.ReturnedItems != null && request.ReturnedItems.Count > 0)
        {
            foreach (var retItem in request.ReturnedItems)
            {
                if (retItem.Quantity <= 0)
                {
                    throw new InvalidOperationException("Số lượng trả phải lớn hơn 0.");
                }

                var origDetail = originalOrder.Details.FirstOrDefault(d => d.ProductId == retItem.ProductId);
                if (origDetail == null)
                {
                    throw new InvalidOperationException($"Sản phẩm với ID '{retItem.ProductId}' không có trong hóa đơn gốc '{originalOrder.OrderCode}'.");
                }

                if (retItem.Quantity > origDetail.Qty)
                {
                    throw new InvalidOperationException($"Số lượng trả ({retItem.Quantity}) vượt quá số lượng đã mua ({origDetail.Qty}) của sản phẩm '{origDetail.ProductName}'.");
                }

                totalReturnAmount += retItem.Quantity * retItem.UnitPrice;

                // Hoàn lại kho BranchStock
                var stock = await _orderRepository.GetBranchStockAsync(originalOrder.BranchId, retItem.ProductId, null, cancellationToken);
                if (stock != null)
                {
                    stock.QuantityOnHand += retItem.Quantity;
                    stock.UpdatedAt = DateTime.UtcNow;
                }
            }
        }

        // 2. Kiểm tra và trừ kho FEFO các mặt hàng khách đổi mới (nếu có)
        decimal totalNewAmount = 0m;
        var newOrderDetails = new List<OrderDetail>();
        if (request.NewItems != null && request.NewItems.Count > 0)
        {
            var productIds = request.NewItems.Select(i => i.ProductId).Distinct();
            var products = await _orderRepository.GetProductsByIdsAsync(productIds, cancellationToken);

            foreach (var newItem in request.NewItems)
            {
                if (!products.TryGetValue(newItem.ProductId, out _))
                {
                    throw new InvalidOperationException($"Sản phẩm đổi mới với ID '{newItem.ProductId}' không tồn tại.");
                }
            }

            var itemGroups = request.NewItems.GroupBy(i => i.ProductId);
            foreach (var group in itemGroups)
            {
                var pId = group.Key;
                var requiredQty = group.Sum(x => x.Quantity);
                var prod = products[pId];

                var branchStocks = await _orderRepository.GetBranchStocksForProductFefoAsync(originalOrder.BranchId, pId, cancellationToken);
                var totalOnHand = branchStocks.Sum(s => s.QuantityOnHand);
                if (totalOnHand < requiredQty)
                {
                    throw new InvalidOperationException($"Sản phẩm '{prod.Name}' không đủ tồn kho tại chi nhánh để đổi (Hiện còn: {totalOnHand:0.##}, Cần: {requiredQty:0.##}).");
                }

                var remainingToDeduct = requiredQty;
                foreach (var stock in branchStocks)
                {
                    if (remainingToDeduct <= 0) break;
                    var deduct = Math.Min(stock.QuantityOnHand, remainingToDeduct);
                    stock.QuantityOnHand -= deduct;
                    stock.UpdatedAt = DateTime.UtcNow;
                    remainingToDeduct -= deduct;
                }
            }

            int line = 1;
            foreach (var item in request.NewItems)
            {
                var prod = products[item.ProductId];
                decimal lineTotal = item.Quantity * item.UnitPrice;
                decimal itemDiscount = item.DiscountAmount ?? 0m;
                decimal actualPrice = item.Quantity > 0 ? Math.Max(0, (lineTotal - itemDiscount) / item.Quantity) : item.UnitPrice;
                decimal vatRate = prod.VatRate > 0 ? prod.VatRate : 0.01m;
                decimal vatAmount = (actualPrice * item.Quantity) * vatRate;

                totalNewAmount += lineTotal - itemDiscount;

                newOrderDetails.Add(new OrderDetail
                {
                    Id = Guid.NewGuid(),
                    ProductId = item.ProductId,
                    LineNumber = line++,
                    Qty = item.Quantity,
                    UnitPrice = item.UnitPrice,
                    DiscountAmount = itemDiscount,
                    ActualPrice = actualPrice,
                    CostPriceAtSale = prod.CostPrice ?? 0m,
                    AppliedVatRate = vatRate,
                    VatAmount = vatAmount,
                    ProductName = prod.Name,
                    Product = prod
                });
            }
        }

        // 3. Tính bù trừ chênh lệch
        decimal netAmount = totalNewAmount - totalReturnAmount;
        Customer? customer = null;
        if (originalOrder.CustomerId.HasValue)
        {
            customer = await _orderRepository.GetCustomerByIdAsync(originalOrder.CustomerId.Value, cancellationToken);
        }

        string settlementMethodUpper = request.SettlementMethod.ToUpper();
        string paymentStatus = "PAID";
        decimal cashTendered = 0m;
        decimal changeAmount = 0m;

        if (netAmount > 0)
        {
            if (settlementMethodUpper == "DEBT")
            {
                if (customer == null) throw new InvalidOperationException("Bán nợ bù trừ bắt buộc phải có thông tin khách hàng.");
                customer.TotalDebt += netAmount;
                customer.UpdatedAt = DateTime.UtcNow;
                paymentStatus = "UNPAID";
            }
            else
            {
                cashTendered = netAmount;
            }
        }
        else if (netAmount < 0)
        {
            decimal refundAmount = Math.Abs(netAmount);
            if (settlementMethodUpper == "REDUCE_DEBT" || settlementMethodUpper == "DEBT")
            {
                if (customer != null)
                {
                    customer.TotalDebt = Math.Max(0, customer.TotalDebt - refundAmount);
                    customer.UpdatedAt = DateTime.UtcNow;
                }
            }
            else
            {
                changeAmount = refundAmount;
            }
        }

        // 4. Sinh đơn mới liên kết
        string exchangeCode = $"DT{DateTime.UtcNow:yyMMddHHmmss}{Random.Shared.Next(100, 999)}";
        var exchangeOrder = new Order
        {
            Id = Guid.NewGuid(),
            BranchId = originalOrder.BranchId,
            CustomerId = originalOrder.CustomerId,
            OrderCode = exchangeCode,
            OrderDate = DateTime.UtcNow,
            TotalAmount = totalNewAmount,
            DiscountAmount = 0m,
            FinalAmount = Math.Max(0, netAmount),
            DepositAmount = netAmount > 0 ? netAmount : 0m,
            PaymentMethod = settlementMethodUpper,
            PaymentStatus = paymentStatus,
            Status = "Completed",
            Note = $"Đổi/Trả từ đơn '{originalOrder.OrderCode}'. Tiền hàng trả: {totalReturnAmount:N0} đ. Tiền hàng mới: {totalNewAmount:N0} đ. Chênh lệch: {netAmount:N0} đ. {request.Note}".Trim(),
            Details = newOrderDetails,
            Branch = originalOrder.Branch,
            Customer = customer,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        foreach (var d in newOrderDetails)
        {
            d.OrderId = exchangeOrder.Id;
        }

        await _orderRepository.AddAsync(exchangeOrder, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return MapToOrderResponse(exchangeOrder, cashTendered, changeAmount, null);
    }

    public async Task<OrderSummaryResponse> GetOrderSummaryAsync(Guid? branchId, DateTime? fromDate, DateTime? toDate, CancellationToken cancellationToken = default)
    {
        var orders = await _orderRepository.GetAllOrdersForSummaryAsync(branchId, fromDate, toDate, cancellationToken);

        int totalOrders = orders.Count;
        int completedCount = orders.Count(o => o.Status == "Completed");
        int pendingPreOrders = orders.Count(o => o.Status == "PendingPickup");
        int cancelledCount = orders.Count(o => o.Status == "Cancelled");

        // Doanh thu = Hóa đơn Completed (FinalAmount) + Tiền cọc đơn PreOrder (DepositAmount)
        decimal completedRevenue = orders.Where(o => o.Status == "Completed").Sum(o => o.FinalAmount);
        decimal preOrderDepositRevenue = orders.Where(o => o.Status == "PendingPickup").Sum(o => o.DepositAmount);
        decimal totalRevenue = completedRevenue + preOrderDepositRevenue;

        decimal cashRevenue = orders
            .Where(o => o.PaymentMethod == "CASH" && o.Status != "Cancelled")
            .Sum(o => o.Status == "Completed" ? o.FinalAmount : o.DepositAmount);

        decimal transferRevenue = orders
            .Where(o => (o.PaymentMethod == "VIETQR" || o.PaymentMethod == "TRANSFER" || o.PaymentMethod == "BANK") && o.Status != "Cancelled")
            .Sum(o => o.Status == "Completed" ? o.FinalAmount : o.DepositAmount);

        return new OrderSummaryResponse(
            TotalOrders: totalOrders,
            TotalRevenue: totalRevenue,
            CashRevenue: cashRevenue,
            TransferRevenue: transferRevenue,
            PendingPreOrdersCount: pendingPreOrders,
            CompletedOrdersCount: completedCount,
            CancelledOrdersCount: cancelledCount
        );
    }

    private static OrderResponse MapToOrderResponse(Order order, decimal cashTendered, decimal changeAmount, CustomerDebtInvoiceResponse? debtInvoice = null)
    {
        var items = order.Details.Select(d => new OrderItemResponse(
            Id: d.Id,
            ProductId: d.ProductId,
            ProductName: !string.IsNullOrWhiteSpace(d.ProductName) ? d.ProductName : (d.Product?.Name ?? "Sản phẩm"),
            Barcode: d.Product?.Barcode,
            UnitName: d.Product?.BaseUnit,
            Quantity: d.Qty,
            UnitPrice: d.UnitPrice,
            DiscountAmount: d.DiscountAmount,
            ActualPrice: d.ActualPrice,
            CostPriceAtSale: d.CostPriceAtSale,
            TotalLineAmount: d.ActualPrice * d.Qty,
            AppliedVatRate: d.AppliedVatRate,
            VatAmount: d.VatAmount,
            BatchCode: null
        )).ToList();

        return new OrderResponse(
            Id: order.Id,
            OrderCode: order.OrderCode,
            BranchId: order.BranchId,
            BranchName: order.Branch?.Name,
            CustomerId: order.CustomerId,
            CustomerName: order.Customer?.Name,
            OrderDate: order.OrderDate,
            TotalAmount: order.TotalAmount,
            DiscountAmount: order.DiscountAmount,
            FinalAmount: order.FinalAmount,
            CashTendered: cashTendered,
            ChangeAmount: changeAmount,
            PaymentMethod: order.PaymentMethod,
            PaymentStatus: order.PaymentStatus,
            Status: order.Status,
            Items: items,
            CustomerPhone: order.Customer?.Phone,
            DepositAmount: order.DepositAmount,
            Note: order.Note,
            DebtInvoice: debtInvoice,
            ShiftId: order.ShiftId
        );
    }
}
