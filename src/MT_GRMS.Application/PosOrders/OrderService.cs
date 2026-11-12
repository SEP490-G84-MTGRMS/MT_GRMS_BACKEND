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
    private readonly PosShifts.IShiftService? _shiftService;
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
        ITenantContext? tenantContext = null,
        PosShifts.IShiftService? shiftService = null)
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
        _shiftService = shiftService;
    }

    public OrderService(
        IOrderRepository orderRepository,
        IUnitOfWork unitOfWork,
        IThermalPrintService thermalPrintService,
        IValidator<OrderCreateRequest>? validator)
        : this(orderRepository, unitOfWork, thermalPrintService, null, null, validator, null, null, null, null)
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

        // 1b. Kiểm tra ca bán hàng của chi nhánh (REQ-SHIFT-SHARED-03 & REQ-OWNER-SHIFTLESS-04)
        bool isOwnerOrAdmin = IsOwnerOrAdminCaller();
        ShiftZReport? activeShift = null;
        if (_shiftRepository != null)
        {
            activeShift = await _shiftRepository.GetActiveShiftAsync(branchId, cancellationToken: cancellationToken);
            if (!isOwnerOrAdmin)
            {
                if (_tenantContext != null
                    && !string.IsNullOrWhiteSpace(_tenantContext.CurrentRole)
                    && activeShift == null
                    && !request.ShiftId.HasValue)
                {
                    throw new BusinessException(
                        "SHIFT_NOT_OPEN",
                        "Chi nhánh chưa mở ca bán hàng. Thu ngân vui lòng mở ca trước khi thanh toán.");
                }

                if (activeShift != null && activeShift.Status == ShiftState.Closing)
                {
                    throw new BusinessException(
                        "SHIFT_CLOSING_LOCKDOWN",
                        "Ca bán hàng của chi nhánh đang trong trạng thái kiểm đếm chốt ca (Closing). Không thể tạo đơn mới.");
                }
            }
            if (request.ShiftId.HasValue)
            {
                if (activeShift == null || activeShift.Id != request.ShiftId.Value || activeShift.Status != ShiftState.Open)
                {
                    if (activeShift != null && activeShift.Id == request.ShiftId.Value && activeShift.Status == ShiftState.Closing)
                    {
                        throw new BusinessException(
                            "SHIFT_CLOSING_LOCKDOWN",
                            "Ca bán hàng được chọn đang trong trạng thái kiểm đếm chốt ca (Closing). Không thể tạo đơn mới.");
                    }

                    throw new BusinessException(
                        "SHIFT_CLOSED_OR_INVALID",
                        "Ca bán hàng được chọn đã kết thúc hoặc không tồn tại trên chi nhánh hiện tại. Vui lòng kiểm tra lại chế độ bán hàng.");
                }
            }
        }

        // 1c. Kiểm tra tính đủ điều kiện trực ca của nhân viên thu ngân (REQ-STRICT-SCHEDULE-VALIDATION)
        if (!isOwnerOrAdmin && _shiftService != null && _tenantContext != null)
        {
            var cashierId = _tenantContext.CurrentUserId;
            if (cashierId != Guid.Empty)
            {
                var eligibility = await _shiftService.CheckCashierShiftEligibilityAsync(
                    branchId,
                    cashierId,
                    _tenantContext.CurrentRole,
                    cancellationToken);

                if (!eligibility.IsAllowed)
                {
                    throw new BusinessException(
                        "EMPLOYEE_NOT_SCHEDULED",
                        eligibility.Reason ?? "Bạn không có lịch làm việc trong ca hiện tại của chi nhánh.");
                }

                if (activeShift != null)
                {
                    await _shiftService.RecordParticipantAsync(
                        activeShift.Id,
                        cashierId,
                        eligibility.EmployeeName,
                        eligibility.EmployeeName,
                        cancellationToken);
                }
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

        // REQ-BRANCH-SCOPE-02: Chặn sản phẩm bán không thuộc danh mục Active của chi nhánh
        foreach (var item in request.Items.Where(i => !i.IsGift))
        {
            var prod = products[item.ProductId];
            if (!branchProducts.TryGetValue(item.ProductId, out var bp) ||
                !string.Equals(bp.Status, "Active", StringComparison.OrdinalIgnoreCase))
            {
                throw new BusinessException("PRODUCT_NOT_IN_BRANCH", $"Sản phẩm '{prod.Name}' chưa được kích hoạt hoặc không thuộc danh mục bán của chi nhánh.");
            }
        }

        if (!request.Items.Any(i => !i.IsGift))
        {
            throw new ValidationException("Đơn hàng bắt buộc phải có ít nhất 1 mặt hàng mua trả tiền (không thể chỉ gồm quà tặng khuyến mãi).");
        }

        // 4. Trừ tồn kho BranchStocks theo FEFO: ưu tiên mặt hàng khách mua chính (!IsGift) trước, quà tặng (IsGift) trừ trên tồn dư (REQ-POS-11, REQ-ORD-BOGO-02)
        var stocksCache = new Dictionary<Guid, IReadOnlyList<Domain.Entities.Inventory.BranchStock>>();
        async Task<IReadOnlyList<Domain.Entities.Inventory.BranchStock>> GetStocksCachedAsync(Guid pId)
        {
            if (!stocksCache.TryGetValue(pId, out var list))
            {
                list = await _orderRepository.GetBranchStocksForProductFefoAsync(branchId, pId, cancellationToken);
                stocksCache[pId] = list;
            }
            return list;
        }

        decimal GetConversionRate(OrderItemCreateDto item)
        {
            var prod = products[item.ProductId];
            if (item.UnitId.HasValue && item.UnitId.Value != Guid.Empty)
            {
                var matchedUnit = prod.Units?.FirstOrDefault(u => u.Id == item.UnitId.Value && !u.IsDeleted);
                if (matchedUnit != null && matchedUnit.ConversionRate > 0)
                {
                    return matchedUnit.ConversionRate;
                }
            }
            if (!string.IsNullOrWhiteSpace(item.UnitName) && prod.Units != null)
            {
                var matchedByName = prod.Units.FirstOrDefault(u => string.Equals(u.UnitName, item.UnitName, StringComparison.OrdinalIgnoreCase) && !u.IsDeleted);
                if (matchedByName != null && matchedByName.ConversionRate > 0)
                {
                    return matchedByName.ConversionRate;
                }
            }
            return 1.00m;
        }

        var productRequiredBaseQty = new Dictionary<Guid, decimal>();
        foreach (var item in request.Items.Where(i => !i.IsGift))
        {
            var baseQty = item.Quantity * GetConversionRate(item);
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

            var branchStocks = await GetStocksCachedAsync(pId);

            var totalOnHand = branchStocks.Where(s => s.QuantityOnHand > 0).Sum(s => s.QuantityOnHand);
            if (totalOnHand < requiredBaseQty)
            {
                throw new InvalidOperationException($"Sản phẩm '{prod.Name}' không đủ tồn kho tại chi nhánh (Hiện còn: {totalOnHand:0.##}, Cần bán: {requiredBaseQty:0.##}).");
            }

            var remainingToDeduct = requiredBaseQty;
            foreach (var stock in branchStocks)
            {
                if (remainingToDeduct <= 0) break;
                if (stock.QuantityOnHand <= 0) continue;

                var deduct = Math.Min(stock.QuantityOnHand, remainingToDeduct);
                stock.QuantityOnHand -= deduct;
                stock.UpdatedAt = DateTime.UtcNow;
                remainingToDeduct -= deduct;
            }
        }

        // 5. Tính toán chi tiết đơn hàng (Phân giải giá BranchProduct & xử lý dòng quà tặng IsGift = 0đ) (REQ-POS-09, REQ-POS-10, REQ-ORD-BOGO-02)
        decimal totalAmount = 0m;
        decimal itemDiscountTotal = 0m;
        decimal giftEquivalenceTotal = 0m;
        var giftWarnings = new List<string>();
        var orderDetails = new List<OrderDetail>();
        int lineNumber = 1;

        foreach (var item in request.Items)
        {
            var prod = products[item.ProductId];
            branchProducts.TryGetValue(item.ProductId, out var bp);

            decimal resolvedUnitPrice;
            if (item.UnitId.HasValue && item.UnitId.Value != Guid.Empty)
            {
                var matchedUnit = prod.Units?.FirstOrDefault(u => u.Id == item.UnitId.Value && !u.IsDeleted);
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
                resolvedUnitPrice = (bp?.SellingPriceOverride > 0) ? bp.SellingPriceOverride.Value : prod.SellingPrice;
            }

            if (item.IsGift)
            {
                var hasActiveBp = bp != null && string.Equals(bp.Status, "Active", StringComparison.OrdinalIgnoreCase);
                if (!hasActiveBp)
                {
                    giftWarnings.Add($"Quà tặng '{prod.Name}' chưa được kích hoạt tại chi nhánh nên tạm thời không áp dụng");
                    continue;
                }

                var rate = GetConversionRate(item);
                var giftStocks = await GetStocksCachedAsync(item.ProductId);
                var remainingBaseStock = giftStocks.Where(s => s.QuantityOnHand > 0).Sum(s => s.QuantityOnHand);
                var availableGiftUnits = Math.Floor(remainingBaseStock / rate);
                var actualGiftQty = Math.Min(item.Quantity, Math.Max(0m, availableGiftUnits));

                if (actualGiftQty < item.Quantity)
                {
                    giftWarnings.Add(actualGiftQty <= 0m
                        ? $"Quà tặng '{prod.Name}' tạm hết tồn kho (0/{item.Quantity:0.##})"
                        : $"Quà tặng '{prod.Name}' chỉ xuất được {actualGiftQty:0.##}/{item.Quantity:0.##} do giới hạn tồn kho");
                }

                if (actualGiftQty <= 0m)
                {
                    continue;
                }

                var remainingGiftBaseToDeduct = actualGiftQty * rate;
                foreach (var stock in giftStocks)
                {
                    if (remainingGiftBaseToDeduct <= 0) break;
                    if (stock.QuantityOnHand <= 0) continue;

                    var deduct = Math.Min(stock.QuantityOnHand, remainingGiftBaseToDeduct);
                    stock.QuantityOnHand -= deduct;
                    stock.UpdatedAt = DateTime.UtcNow;
                    remainingGiftBaseToDeduct -= deduct;
                }

                var refGiftValue = resolvedUnitPrice * actualGiftQty;
                giftEquivalenceTotal += refGiftValue;

                var giftConvRate = GetConversionRate(item);
                var giftUnitName = !string.IsNullOrWhiteSpace(item.UnitName)
                    ? item.UnitName
                    : (item.UnitId.HasValue && item.UnitId.Value != Guid.Empty
                        ? prod.Units?.FirstOrDefault(u => u.Id == item.UnitId.Value && !u.IsDeleted)?.UnitName ?? prod.BaseUnit
                        : prod.BaseUnit);

                orderDetails.Add(new OrderDetail
                {
                    Id = Guid.NewGuid(),
                    ProductId = item.ProductId,
                    PromotionId = item.PromotionId ?? request.AppliedPromotionId,
                    LineNumber = lineNumber++,
                    Qty = actualGiftQty,
                    UnitPrice = resolvedUnitPrice,
                    DiscountAmount = refGiftValue,
                    ActualPrice = 0m,
                    CostPriceAtSale = (prod.CostPrice ?? 0m) * giftConvRate,
                    IsGift = true,
                    AppliedVatRate = prod.VatRate > 0 ? prod.VatRate : 0.01m,
                    VatAmount = 0m,
                    ProductName = !string.IsNullOrWhiteSpace(item.ProductName) ? item.ProductName : prod.Name,
                    ProductUnitId = item.UnitId,
                    UnitName = giftUnitName,
                    ConversionRate = giftConvRate,
                    Product = prod
                });
                continue;
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

            var itemConvRate = GetConversionRate(item);
            var itemUnitName = !string.IsNullOrWhiteSpace(item.UnitName)
                ? item.UnitName
                : (item.UnitId.HasValue && item.UnitId.Value != Guid.Empty
                    ? prod.Units?.FirstOrDefault(u => u.Id == item.UnitId.Value && !u.IsDeleted)?.UnitName ?? prod.BaseUnit
                    : prod.BaseUnit);

            var detail = new OrderDetail
            {
                Id = Guid.NewGuid(),
                ProductId = item.ProductId,
                PromotionId = item.PromotionId,
                LineNumber = lineNumber++,
                Qty = item.Quantity,
                UnitPrice = effectiveUnitPrice,
                DiscountAmount = itemDiscount,
                ActualPrice = actualPrice,
                CostPriceAtSale = (prod.CostPrice ?? 0m) * itemConvRate, // Chốt cứng giá vốn COGS (FE-10)
                IsGift = false,
                AppliedVatRate = vatRate,
                VatAmount = vatAmount,
                ProductName = !string.IsNullOrWhiteSpace(item.ProductName) ? item.ProductName : prod.Name,
                ProductUnitId = item.UnitId,
                UnitName = itemUnitName,
                ConversionRate = itemConvRate,
                Product = prod
            };
            orderDetails.Add(detail);
        }

        decimal orderDiscount = Math.Max(0m, request.DiscountAmount ?? 0m);
        decimal totalDiscount = Math.Min(totalAmount, itemDiscountTotal + orderDiscount);
        decimal finalAmount = Math.Max(0, totalAmount - totalDiscount);
        string? giftStockWarning = giftWarnings.Count > 0 ? string.Join("; ", giftWarnings) : null;

        // Phân bổ chiết khấu cấp hóa đơn (orderDiscount) vào từng dòng hàng bán (không áp dụng quà tặng)
        // để chốt đúng ActualPrice phục vụ đổi/trả hàng và hạch toán COGS/doanh thu thuần theo chuẩn KiotViet/Sapo
        decimal nonGiftSubtotal = orderDetails.Where(d => !d.IsGift).Sum(d => d.UnitPrice * d.Qty - d.DiscountAmount);
        if (orderDiscount > 0 && nonGiftSubtotal > 0)
        {
            decimal remainingToAllocate = orderDiscount;
            var nonGiftItems = orderDetails.Where(d => !d.IsGift).ToList();
            for (int i = 0; i < nonGiftItems.Count; i++)
            {
                var d = nonGiftItems[i];
                decimal lineBase = (d.UnitPrice * d.Qty) - d.DiscountAmount;
                decimal allocated;
                if (i == nonGiftItems.Count - 1)
                {
                    allocated = Math.Min(remainingToAllocate, lineBase);
                }
                else
                {
                    allocated = Math.Min(remainingToAllocate, Math.Round(orderDiscount * (lineBase / nonGiftSubtotal), 2));
                    remainingToAllocate -= allocated;
                }

                d.ActualPrice = d.Qty > 0 ? Math.Max(0, (lineBase - allocated) / d.Qty) : d.UnitPrice;
                d.VatAmount = (d.ActualPrice * d.Qty) * d.AppliedVatRate;
            }
        }

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

            var roleUpper = (request.CreatorRole ?? string.Empty).Trim().ToUpperInvariant();
            bool isManagerRole = roleUpper == "STOREOWNER" || roleUpper == "MANAGER" || roleUpper == "ADMIN";
            bool isManagerBypass = !string.IsNullOrWhiteSpace(request.ManagerBypassCode) &&
                                  (isManagerRole ||
                                   request.ManagerBypassCode.Trim().StartsWith("BYPASS", StringComparison.OrdinalIgnoreCase) ||
                                   request.ManagerBypassCode.Trim().StartsWith("MGR-", StringComparison.OrdinalIgnoreCase));

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
            paymentStatus = request.IsPendingPayment ? "PENDING_PAYMENT" : "PAID";
        }

        if (request.IsPendingPayment && paymentMethodUpper != "CREDIT" && paymentMethodUpper != "DEBT")
        {
            paymentStatus = "PENDING_PAYMENT";
        }

        // 7. Tích điểm thưởng cho khách hàng (Khách lẻ KHÔNG tích điểm, tuân thủ cấu hình LoyaltySetting)
        bool isWalkIn = customer == null
            || string.Equals(customer.CustomerCode, "KHACHLE", StringComparison.OrdinalIgnoreCase)
            || string.Equals(customer.Name?.Trim(), "Khách lẻ", StringComparison.OrdinalIgnoreCase)
            || string.Equals(customer.Name?.Trim(), "Khách hàng lẻ", StringComparison.OrdinalIgnoreCase);

        bool isLoyaltyEnabled = true;
        if (!isWalkIn && customer != null)
        {
            decimal earningRate = 10000m;
            int earningPoints = 1;
            if (_promotionRepository != null)
            {
                var loyaltySetting = await _promotionRepository.GetLoyaltySettingAsync(customer.TenantId, cancellationToken);
                if (loyaltySetting != null)
                {
                    isLoyaltyEnabled = loyaltySetting.IsEnabled;
                    if (isLoyaltyEnabled)
                    {
                        earningRate = loyaltySetting.EarningRateAmount > 0 ? loyaltySetting.EarningRateAmount : 10000m;
                        earningPoints = loyaltySetting.EarningPoints > 0 ? loyaltySetting.EarningPoints : 1;
                    }
                }
            }

            if (isLoyaltyEnabled)
            {
                if (request.PointsRedeemed.HasValue && request.PointsRedeemed.Value > 0)
                {
                    int pointsToDeduct = Math.Min(customer.RewardPoints, request.PointsRedeemed.Value);
                    customer.RewardPoints -= pointsToDeduct;
                }

                if (!request.IsPendingPayment)
                {
                    int earnedPoints = (int)(finalAmount / earningRate) * earningPoints;
                    customer.RewardPoints += earnedPoints;
                    customer.TotalSpending += finalAmount;
                }
                customer.UpdatedAt = DateTime.UtcNow;
            }
        }

        // 8. Lưu Order Entity
        Guid? shiftId = request.ShiftId ?? (isOwnerOrAdmin ? null : activeShift?.Id);
        Guid? creatorId = null;
        if (_tenantContext != null && _tenantContext.CurrentUserId != Guid.Empty)
        {
            creatorId = _tenantContext.CurrentUserId;
        }

        string? resolvedCashierName = !string.IsNullOrWhiteSpace(request.CashierName)
            ? request.CashierName.Trim()
            : (_tenantContext?.CurrentRole == "STORE_OWNER" ? "Chủ cửa hàng" : "Thu ngân POS");

        string? normalizedVoucherCode = !string.IsNullOrWhiteSpace(request.VoucherCode)
            ? request.VoucherCode.Trim().ToUpperInvariant()
            : null;
        decimal recordedPromoDiscount = Math.Max(0m, request.PromotionDiscountAmount ?? 0m);
        decimal recordedVoucherDiscount = Math.Max(0m, request.VoucherDiscountAmount ?? 0m);
        decimal recordedPointsDiscount = (!isWalkIn && isLoyaltyEnabled) ? Math.Max(0m, request.PointsDiscountAmount ?? 0m) : 0m;
        int recordedPointsRedeemed = (!isWalkIn && isLoyaltyEnabled) ? Math.Max(0, request.PointsRedeemed ?? 0) : 0;
        string? combinedPromoSummary = !string.IsNullOrWhiteSpace(request.AppliedPromotionSummary)
            ? request.AppliedPromotionSummary.Trim()
            : null;
        if (combinedPromoSummary != null && combinedPromoSummary.Length > 300)
        {
            combinedPromoSummary = combinedPromoSummary.Substring(0, 300);
        }

        bool hadGiftRequest = request.Items.Any(i => i.IsGift);
        bool hasDeliveredGift = orderDetails.Any(d => d.IsGift);
        Guid? effectiveAppliedPromoId = (hadGiftRequest && !hasDeliveredGift && recordedPromoDiscount <= 0m)
            ? null
            : request.AppliedPromotionId;
        string? effectivePromoSummary = (hadGiftRequest && !hasDeliveredGift && recordedPromoDiscount <= 0m)
            ? null
            : combinedPromoSummary;

        string orderCode = debtInvoiceResponse != null ? debtInvoiceResponse.OrderCode : $"HD{DateTime.UtcNow:yyMMddHHmmss}{Random.Shared.Next(100, 999)}";
        var orderBusinessDate = activeShift?.BusinessDate ?? BusinessDateResolver.Resolve(DateTime.UtcNow, new TimeSpan(6, 0, 0));
        var order = new Order
        {
            Id = debtInvoiceResponse != null ? debtInvoiceResponse.OrderId : Guid.NewGuid(),
            TenantId = branch.TenantId ?? (_tenantContext?.CurrentTenantId ?? string.Empty),
            BranchId = branchId,
            CustomerId = request.CustomerId,
            ShiftId = shiftId,
            OrderCode = orderCode,
            CashierName = resolvedCashierName,
            CreatedBy = creatorId,
            OrderDate = DateTime.UtcNow,
            BusinessDate = orderBusinessDate,
            TotalAmount = totalAmount,
            DiscountAmount = totalDiscount,
            FinalAmount = finalAmount,
            DepositAmount = request.IsPendingPayment ? 0m : ((paymentMethodUpper == "CASH") ? cashTendered : (paymentMethodUpper == "CREDIT" ? 0m : finalAmount)),
            PaymentMethod = paymentMethodUpper,
            PaymentStatus = paymentStatus,
            Status = request.IsPendingPayment ? "PendingPayment" : "Completed",
            Note = request.Note,
            AppliedPromotionId = effectiveAppliedPromoId,
            AppliedPromotionSummary = effectivePromoSummary,
            PromotionDiscountAmount = recordedPromoDiscount,
            AppliedVoucherCode = normalizedVoucherCode,
            VoucherDiscountAmount = recordedVoucherDiscount,
            PointsRedeemed = recordedPointsRedeemed,
            PointsDiscountAmount = recordedPointsDiscount,
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

        // Lưu ý (FE-17 Consolidated):
        // - Đơn hàng thuộc Ca thu ngân (shiftId.HasValue) sẽ được gộp doanh thu vào Sổ quỹ khi Chốt ca / Xác nhận bàn giao ca.
        // - Đơn hàng của Chủ cửa hàng bán lẻ trực tiếp (!shiftId.HasValue) sẽ được gộp khi Chủ bấm "Chốt doanh thu cuối ngày" (SettleOwnerDailyOrdersAsync).

        await _orderRepository.AddAsync(order, cancellationToken);

        // Ghi nhận nhân viên tham gia ca bán hàng chung (REQ-SHIFT-SHARED-02 — không gán Chủ cửa hàng bán không mở ca)
        if (shiftId.HasValue && activeShift != null && _shiftRepository != null)
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

        // 9. Đổi mã voucher (Redeem) nguyên tử và đồng bộ UsedCount / TotalDiscountGiven của chương trình KM & Voucher (REQ-ORD-PROMO-02)
        if (_promotionRepository != null)
        {
            bool voucherRedeemed = false;
            if (!string.IsNullOrWhiteSpace(normalizedVoucherCode))
            {
                voucherRedeemed = await _promotionRepository.RedeemVoucherCodeAtomicAsync(
                    normalizedVoucherCode,
                    order.Id,
                    order.OrderCode,
                    customer?.Id,
                    customer?.Name,
                    customer?.Phone,
                    cancellationToken
                );
            }

            decimal promoBudgetConsumed = recordedPromoDiscount + giftEquivalenceTotal;
            if (order.AppliedPromotionId.HasValue || (voucherRedeemed && recordedVoucherDiscount > 0m))
            {
                await _promotionRepository.RecordPromotionUsageAsync(
                    order.AppliedPromotionId,
                    promoBudgetConsumed,
                    voucherRedeemed ? normalizedVoucherCode : null,
                    voucherRedeemed ? recordedVoucherDiscount : 0m,
                    order.TenantId,
                    cancellationToken
                );
            }
        }

        return MapToOrderResponse(order, cashTendered, changeAmount, debtInvoiceResponse, giftStockWarning);
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
        if (request == null)
        {
            throw new ValidationException("Dữ liệu đơn đặt trước không được để trống.");
        }

        if (!string.IsNullOrWhiteSpace(request.VoucherCode))
        {
            throw new ValidationException("Đơn đặt trước không hỗ trợ áp dụng mã giảm giá (Voucher) dùng 1 lần.");
        }

        if (request.BranchId == Guid.Empty)
        {
            throw new InvalidOperationException("Chi nhánh (BranchId) không được để trống.");
        }

        if (request.Items == null || request.Items.Count == 0)
        {
            throw new ValidationException("Đơn đặt trước phải có ít nhất một mặt hàng.");
        }

        if (!request.Items.Any(i => !i.IsGift))
        {
            throw new ValidationException("Đơn đặt trước phải có ít nhất một mặt hàng mua (không phải quà tặng).");
        }

        if (request.CustomerId == Guid.Empty)
        {
            throw new InvalidOperationException("Đơn đặt trước bắt buộc phải có thông tin khách hàng định danh.");
        }

        if (request.ExpectedPickupDate == default ||
            (request.ExpectedPickupDate.Kind == DateTimeKind.Utc && request.ExpectedPickupDate <= DateTime.UtcNow.AddMinutes(-1)) ||
            (request.ExpectedPickupDate.Kind == DateTimeKind.Local && request.ExpectedPickupDate.ToUniversalTime() <= DateTime.UtcNow.AddMinutes(-1)) ||
            (request.ExpectedPickupDate.Kind == DateTimeKind.Unspecified && request.ExpectedPickupDate <= DateTime.UtcNow.AddHours(-14)))
        {
            throw new InvalidOperationException("Thời gian hẹn nhận hàng phải sau thời điểm hiện tại.");
        }

        if (string.IsNullOrWhiteSpace(request.PaymentMethod))
        {
            throw new InvalidOperationException("Phương thức thu cọc không được để trống.");
        }

        string preOrderPaymentMethod = request.PaymentMethod.Trim().ToUpperInvariant();
        if (preOrderPaymentMethod is not ("CASH" or "VIETQR" or "TRANSFER" or "BANK"))
        {
            throw new InvalidOperationException($"Phương thức thanh toán '{request.PaymentMethod}' không được hỗ trợ cho đơn đặt trước.");
        }

        if (request.Note != null && request.Note.Trim().Length > 500)
        {
            throw new InvalidOperationException("Ghi chú đơn đặt trước tối đa 500 ký tự.");
        }

        if (request.DiscountAmount.HasValue && request.DiscountAmount.Value < 0)
        {
            throw new InvalidOperationException("Chiết khấu đơn đặt trước không được âm.");
        }

        if (request.DepositAmount < 0)
        {
            throw new InvalidOperationException("Tiền đặt cọc không được là số âm.");
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

        bool isWalkInCustomer = string.Equals(customer.CustomerCode, "KHACHLE", StringComparison.OrdinalIgnoreCase)
            || string.Equals(customer.Name?.Trim(), "Khách lẻ", StringComparison.OrdinalIgnoreCase)
            || string.Equals(customer.Name?.Trim(), "Khách hàng lẻ", StringComparison.OrdinalIgnoreCase);
        if (isWalkInCustomer)
        {
            throw new InvalidOperationException("Đơn đặt trước bắt buộc phải chọn khách hàng định danh (không áp dụng cho Khách lẻ).");
        }

        var productIds = request.Items.Select(i => i.ProductId).Distinct().ToList();
        var products = await _orderRepository.GetProductsByIdsAsync(productIds, cancellationToken);
        var branchProducts = await _orderRepository.GetBranchProductsByProductIdsAsync(request.BranchId, productIds, cancellationToken);

        foreach (var item in request.Items)
        {
            if (item.ProductId == Guid.Empty)
            {
                throw new InvalidOperationException("Mã sản phẩm không được để trống.");
            }
            if (item.Quantity <= 0)
            {
                throw new InvalidOperationException("Số lượng sản phẩm đặt trước phải lớn hơn 0.");
            }
            if (item.UnitPrice < 0)
            {
                throw new InvalidOperationException("Đơn giá sản phẩm đặt trước không được âm.");
            }
            if (item.DiscountAmount.HasValue && item.DiscountAmount.Value < 0)
            {
                throw new InvalidOperationException("Giảm giá sản phẩm không được âm.");
            }
            if (item.ConversionRate < 0)
            {
                throw new InvalidOperationException("Hệ số quy đổi đơn vị sản phẩm không hợp lệ.");
            }
            if (!products.TryGetValue(item.ProductId, out _))
            {
                throw new InvalidOperationException($"Sản phẩm với ID '{item.ProductId}' không tồn tại.");
            }
        }

        // REQ-BRANCH-SCOPE-02: Chặn sản phẩm đặt trước không thuộc danh mục Active của chi nhánh
        foreach (var item in request.Items.Where(i => !i.IsGift))
        {
            var prod = products[item.ProductId];
            if (!branchProducts.TryGetValue(item.ProductId, out var bp) ||
                !string.Equals(bp.Status, "Active", StringComparison.OrdinalIgnoreCase))
            {
                throw new BusinessException("PRODUCT_NOT_IN_BRANCH", $"Sản phẩm '{prod.Name}' chưa được kích hoạt hoặc không thuộc danh mục bán của chi nhánh.");
            }
        }

        decimal ResolvePreOrderConversionRate(OrderItemCreateDto item)
        {
            if (item.ConversionRate > 0 && item.ConversionRate != 1.00m)
            {
                return item.ConversionRate;
            }
            var prod = products[item.ProductId];
            if (item.UnitId.HasValue && item.UnitId.Value != Guid.Empty)
            {
                var matchedUnit = prod.Units?.FirstOrDefault(u => u.Id == item.UnitId.Value && !u.IsDeleted);
                if (matchedUnit != null && matchedUnit.ConversionRate > 0)
                {
                    return matchedUnit.ConversionRate;
                }
            }
            if (!string.IsNullOrWhiteSpace(item.UnitName) && prod.Units != null)
            {
                var matchedByName = prod.Units.FirstOrDefault(u => string.Equals(u.UnitName, item.UnitName, StringComparison.OrdinalIgnoreCase) && !u.IsDeleted);
                if (matchedByName != null && matchedByName.ConversionRate > 0)
                {
                    return matchedByName.ConversionRate;
                }
            }
            return item.ConversionRate > 0 ? item.ConversionRate : 1.00m;
        }

        string ResolvePreOrderUnitName(OrderItemCreateDto item, Product prod)
        {
            if (!string.IsNullOrWhiteSpace(item.UnitName)) return item.UnitName;
            if (item.UnitId.HasValue && item.UnitId.Value != Guid.Empty)
            {
                var matchedUnit = prod.Units?.FirstOrDefault(u => u.Id == item.UnitId.Value && !u.IsDeleted);
                if (!string.IsNullOrWhiteSpace(matchedUnit?.UnitName)) return matchedUnit.UnitName;
            }
            return prod.BaseUnit ?? "Cái";
        }

        Order? createdOrder = null;
        string? createdGiftWarning = null;

        await _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var stockCache = new Dictionary<Guid, IReadOnlyList<Domain.Entities.Inventory.BranchStock>>();
            async Task<IReadOnlyList<Domain.Entities.Inventory.BranchStock>> GetPreOrderStocksCachedAsync(Guid productId)
            {
                if (!stockCache.TryGetValue(productId, out var stocks))
                {
                    stocks = await _orderRepository.GetBranchStocksForProductFefoAsync(request.BranchId, productId, ct);
                    stockCache[productId] = stocks;
                }
                return stocks;
            }

            // 1. Trừ/giữ tồn kho FEFO ưu tiên hàng mua trả tiền trước (!IsGift) theo tổng số lượng cơ sở quy đổi
            var paidItems = request.Items.Where(i => !i.IsGift).ToList();
            var productRequiredBaseQty = new Dictionary<Guid, decimal>();
            foreach (var item in paidItems)
            {
                var rate = ResolvePreOrderConversionRate(item);
                var baseQty = item.Quantity * rate;
                if (productRequiredBaseQty.ContainsKey(item.ProductId))
                    productRequiredBaseQty[item.ProductId] += baseQty;
                else
                    productRequiredBaseQty[item.ProductId] = baseQty;
            }

            foreach (var kvp in productRequiredBaseQty)
            {
                var pId = kvp.Key;
                var requiredBaseQty = kvp.Value;
                var prod = products[pId];

                var branchStocks = await GetPreOrderStocksCachedAsync(pId);
                var totalOnHand = branchStocks.Where(s => s.QuantityOnHand > 0).Sum(s => s.QuantityOnHand);
                if (totalOnHand < requiredBaseQty)
                {
                    throw new InvalidOperationException($"Sản phẩm '{prod.Name}' không đủ tồn kho để đặt trước (Hiện còn: {totalOnHand:0.##}, Cần đặt: {requiredBaseQty:0.##}).");
                }

                var remainingToDeduct = requiredBaseQty;
                foreach (var stock in branchStocks)
                {
                    if (remainingToDeduct <= 0) break;
                    if (stock.QuantityOnHand <= 0) continue;

                    var deduct = Math.Min(stock.QuantityOnHand, remainingToDeduct);
                    stock.QuantityOnHand -= deduct;
                    stock.UpdatedAt = DateTime.UtcNow;
                    remainingToDeduct -= deduct;
                }
            }

            // 2. Tạo chi tiết đơn hàng (OrderDetails) độc lập theo từng dòng request.Items
            var orderDetails = new List<OrderDetail>();
            decimal totalAmount = 0m;
            decimal itemDiscountTotal = 0m;
            decimal giftEquivalenceTotal = 0m;
            var giftWarnings = new List<string>();
            int lineNumber = 1;

            foreach (var item in request.Items)
            {
                var prod = products[item.ProductId];
                branchProducts.TryGetValue(item.ProductId, out var bp);
                var rate = ResolvePreOrderConversionRate(item);
                var unitName = ResolvePreOrderUnitName(item, prod);

                decimal resolvedUnitPrice;
                if (item.UnitId.HasValue && item.UnitId.Value != Guid.Empty)
                {
                    var matchedUnit = prod.Units?.FirstOrDefault(u => u.Id == item.UnitId.Value && !u.IsDeleted);
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
                        resolvedUnitPrice = baseSelling * rate;
                    }
                }
                else
                {
                    resolvedUnitPrice = (bp?.SellingPriceOverride > 0) ? bp.SellingPriceOverride.Value : prod.SellingPrice;
                }

                if (item.IsGift)
                {
                    var hasActiveBp = bp != null && string.Equals(bp.Status, "Active", StringComparison.OrdinalIgnoreCase);
                    if (!hasActiveBp)
                    {
                        giftWarnings.Add($"Quà tặng '{prod.Name}' chưa được kích hoạt tại chi nhánh nên tạm thời không áp dụng");
                        continue;
                    }

                    var giftStocks = await GetPreOrderStocksCachedAsync(item.ProductId);
                    var remainingBaseStock = giftStocks.Where(s => s.QuantityOnHand > 0).Sum(s => s.QuantityOnHand);
                    var availableGiftUnits = Math.Floor(remainingBaseStock / rate);
                    var actualGiftQty = Math.Min(item.Quantity, Math.Max(0m, availableGiftUnits));

                    if (actualGiftQty < item.Quantity)
                    {
                        giftWarnings.Add(actualGiftQty <= 0m
                            ? $"Quà tặng '{prod.Name}' tạm hết tồn kho để giữ cho đơn đặt trước (0/{item.Quantity:0.##})"
                            : $"Quà tặng '{prod.Name}' chỉ giữ được {actualGiftQty:0.##}/{item.Quantity:0.##} do giới hạn tồn kho");
                    }

                    if (actualGiftQty <= 0m) continue;

                    var remainingGiftBaseToDeduct = actualGiftQty * rate;
                    foreach (var stock in giftStocks)
                    {
                        if (remainingGiftBaseToDeduct <= 0) break;
                        if (stock.QuantityOnHand <= 0) continue;

                        var deduct = Math.Min(stock.QuantityOnHand, remainingGiftBaseToDeduct);
                        stock.QuantityOnHand -= deduct;
                        stock.UpdatedAt = DateTime.UtcNow;
                        remainingGiftBaseToDeduct -= deduct;
                    }

                    var refGiftValue = resolvedUnitPrice * actualGiftQty;
                    giftEquivalenceTotal += refGiftValue;

                    orderDetails.Add(new OrderDetail
                    {
                        Id = Guid.NewGuid(),
                        ProductId = item.ProductId,
                        PromotionId = item.PromotionId ?? request.AppliedPromotionId,
                        LineNumber = lineNumber++,
                        ProductName = !string.IsNullOrWhiteSpace(item.ProductName) ? item.ProductName : prod.Name,
                        Qty = actualGiftQty,
                        UnitPrice = resolvedUnitPrice,
                        DiscountAmount = refGiftValue,
                        ActualPrice = 0m,
                        CostPriceAtSale = (prod.CostPrice ?? 0m) * rate,
                        IsGift = true,
                        AppliedVatRate = prod.VatRate > 0 ? prod.VatRate : 0.01m,
                        VatAmount = 0m,
                        ProductUnitId = item.UnitId,
                        UnitName = unitName,
                        ConversionRate = rate,
                        Product = prod
                    });
                    continue;
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
                decimal vatRate = prod.VatRate > 0 ? prod.VatRate : 0.01m;
                decimal vatAmount = (actualPrice * item.Quantity) * vatRate;

                totalAmount += lineTotal;
                itemDiscountTotal += itemDiscount;

                orderDetails.Add(new OrderDetail
                {
                    Id = Guid.NewGuid(),
                    ProductId = item.ProductId,
                    PromotionId = item.PromotionId,
                    LineNumber = lineNumber++,
                    ProductName = !string.IsNullOrWhiteSpace(item.ProductName) ? item.ProductName : prod.Name,
                    Qty = item.Quantity,
                    UnitPrice = effectiveUnitPrice,
                    DiscountAmount = itemDiscount,
                    ActualPrice = actualPrice,
                    CostPriceAtSale = (prod.CostPrice ?? 0m) * rate,
                    IsGift = false,
                    AppliedVatRate = vatRate,
                    VatAmount = vatAmount,
                    ProductUnitId = item.UnitId,
                    UnitName = unitName,
                    ConversionRate = rate,
                    Product = prod
                });
            }

            decimal orderDiscount = Math.Clamp(request.DiscountAmount ?? 0m, 0m, Math.Max(0m, totalAmount - itemDiscountTotal));
            decimal totalDiscount = Math.Min(totalAmount, itemDiscountTotal + orderDiscount);
            decimal finalAmount = Math.Max(0, totalAmount - totalDiscount);
            string? giftStockWarning = giftWarnings.Count > 0 ? string.Join("; ", giftWarnings) : null;

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

            bool isOwnerOrAdminPreOrder = IsOwnerOrAdminCaller();
            Guid? preOrderShiftId = request.ShiftId ?? (!isOwnerOrAdminPreOrder && _shiftRepository != null
                ? (await _shiftRepository.GetActiveShiftAsync(request.BranchId, cancellationToken: cancellationToken))?.Id
                : null);
            var preOrderShift = preOrderShiftId.HasValue && _shiftRepository != null
                ? await _shiftRepository.GetByIdAsync(preOrderShiftId.Value, cancellationToken)
                : null;

            string? preOrderCashierName = !string.IsNullOrWhiteSpace(request.CashierName)
                ? request.CashierName.Trim()
                : (_tenantContext?.CurrentRole == "STORE_OWNER" ? "Chủ cửa hàng" : "Thu ngân POS");

            bool hadPreOrderGiftRequest = request.Items.Any(i => i.IsGift);
            bool hasDeliveredPreOrderGift = orderDetails.Any(d => d.IsGift);
            Guid? effectivePromoId = (hadPreOrderGiftRequest && !hasDeliveredPreOrderGift && orderDiscount <= 0m)
                ? null
                : request.AppliedPromotionId;
            string? effectivePromoSummary = (hadPreOrderGiftRequest && !hasDeliveredPreOrderGift && orderDiscount <= 0m)
                ? null
                : (!string.IsNullOrWhiteSpace(request.AppliedPromotionSummary) ? request.AppliedPromotionSummary.Trim() : null);
            if (effectivePromoSummary != null && effectivePromoSummary.Length > 300)
            {
                effectivePromoSummary = effectivePromoSummary.Substring(0, 300);
            }

            var preOrderBusinessDate = preOrderShift != null
                ? preOrderShift.BusinessDate
                : BusinessDateResolver.Resolve(DateTime.UtcNow, new TimeSpan(6, 0, 0));
            var order = new Order
            {
                Id = Guid.NewGuid(),
                TenantId = branch.TenantId ?? (_tenantContext?.CurrentTenantId ?? string.Empty),
                BranchId = request.BranchId,
                CustomerId = request.CustomerId,
                ShiftId = preOrderShiftId,
                OrderCode = "PO" + DateTime.UtcNow.ToString("yyMMddHHmmssfff"),
                CashierName = preOrderCashierName,
                CreatedBy = _tenantContext != null && _tenantContext.CurrentUserId != Guid.Empty ? _tenantContext.CurrentUserId : null,
                OrderDate = DateTime.UtcNow,
                BusinessDate = preOrderBusinessDate,
                TotalAmount = totalAmount,
                DiscountAmount = totalDiscount,
                FinalAmount = finalAmount,
                DepositAmount = request.DepositAmount,
                PaymentMethod = preOrderPaymentMethod,
                PaymentStatus = request.DepositAmount >= finalAmount ? "PAID" : "PARTIALLY_PAID",
                Status = "PendingPickup",
                Note = $"Hẹn nhận: {request.ExpectedPickupDate:dd/MM/yyyy HH:mm}. {request.Note}".Trim(),
                AppliedPromotionId = effectivePromoId,
                AppliedPromotionSummary = effectivePromoSummary,
                PromotionDiscountAmount = orderDiscount,
                Details = orderDetails,
                Branch = branch,
                Customer = customer
            };

            foreach (var d in orderDetails)
            {
                d.OrderId = order.Id;
            }

            await _orderRepository.AddAsync(order, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            if (_promotionRepository != null && order.AppliedPromotionId.HasValue)
            {
                await _promotionRepository.RecordPromotionUsageAsync(
                    order.AppliedPromotionId,
                    orderDiscount + giftEquivalenceTotal,
                    null,
                    0m,
                    order.TenantId,
                    ct
                );
            }

            createdOrder = order;
            createdGiftWarning = giftStockWarning;
        }, cancellationToken);

        return MapToOrderResponse(createdOrder!, request.DepositAmount, 0m, null, createdGiftWarning);
    }

    public async Task<OrderResponse> FulfillPreOrderAsync(Guid orderId, PreOrderFulfillRequest request, CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            throw new InvalidOperationException("Dữ liệu bàn giao đơn đặt trước không hợp lệ.");
        }

        if (string.IsNullOrWhiteSpace(request.PaymentMethod))
        {
            throw new InvalidOperationException("Phương thức thanh toán bàn giao không được để trống.");
        }

        string fulfillPaymentMethod = request.PaymentMethod.Trim().ToUpperInvariant();
        if (fulfillPaymentMethod is not ("CASH" or "VIETQR" or "TRANSFER" or "BANK"))
        {
            throw new InvalidOperationException($"Phương thức thanh toán '{request.PaymentMethod}' không được hỗ trợ khi bàn giao đơn đặt trước.");
        }

        if (request.Note != null && request.Note.Trim().Length > 500)
        {
            throw new InvalidOperationException("Ghi chú xuất hàng tối đa 500 ký tự.");
        }

        if (request.AmountPaid < 0)
        {
            throw new InvalidOperationException("Số tiền khách trả không được âm.");
        }

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

        string? fulfillingCashierName = !string.IsNullOrWhiteSpace(request.CashierName)
            ? request.CashierName.Trim()
            : (_tenantContext?.CurrentRole == "STORE_OWNER" ? "Chủ cửa hàng" : "Thu ngân POS");

        order.Status = "Completed";
        order.PaymentStatus = "PAID";
        order.PaymentMethod = fulfillPaymentMethod;
        order.DepositAmount = order.FinalAmount;
        order.FulfilledByCashierName = fulfillingCashierName;
        order.FulfilledAtUtc = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(request.Note))
        {
            order.Note = $"{order.Note} | Xuất hàng: {request.Note}".Trim();
        }

        // Tự động ghi nhận khoản thu phần còn lại (tách Két sắt chi nhánh nếu Chủ cửa hàng giao hàng không mở ca)
        bool isOwnerOrAdminFulfill = IsOwnerOrAdminCaller();
        if (remainingAmount > 0)
        {
            if (isOwnerOrAdminFulfill && !request.ShiftId.HasValue)
            {
                if (_cashbookService != null && string.Equals(request.PaymentMethod, "CASH", StringComparison.OrdinalIgnoreCase))
                {
                    var safe = await _cashbookService.GetOrCreateSafeAsync(order.BranchId, cancellationToken);
                    await _cashbookService.ReceiveCashAsync(
                        safe.Id,
                        FinanceAccountType.Safe,
                        remainingAmount,
                        "PREORDER_PICKUP",
                        order.Id,
                        order.OrderCode,
                        $"Thu tiền còn lại giao hàng đơn đặt trước '{order.OrderCode}' (Chủ cửa hàng)",
                        cancellationToken
                    );
                }
            }
            else if (_shiftRepository != null)
            {
                var activeShift = request.ShiftId.HasValue && request.ShiftId.Value != Guid.Empty
                    ? await _shiftRepository.GetByIdAsync(request.ShiftId.Value, cancellationToken)
                    : await _shiftRepository.GetActiveShiftAsync(order.BranchId, cancellationToken: cancellationToken);

                if (activeShift != null)
                {
                    if (string.Equals(request.PaymentMethod, "CASH", StringComparison.OrdinalIgnoreCase))
                    {
                        var movement = new ShiftCashMovement
                        {
                            Id = Guid.NewGuid(),
                            TenantId = order.TenantId,
                            ShiftId = activeShift.Id,
                            MovementType = CashMovementType.IN,
                            Category = CashMovementCategory.PREORDER_PICKUP,
                            Amount = remainingAmount,
                            Reason = $"Thu tiền còn lại giao hàng đơn đặt trước '{order.OrderCode}'",
                            CreatedByName = fulfillingCashierName,
                            OccurredAtUtc = DateTime.UtcNow,
                            Status = CashMovementStatus.Recorded,
                            IsConfirmedByOwner = true
                        };
                        await _shiftRepository.AddCashMovementAsync(movement, cancellationToken);
                        activeShift.TotalCashIn += remainingAmount;
                        activeShift.ExpectedCash += remainingAmount;
                    }
                    else
                    {
                        activeShift.TotalBankRevenue += remainingAmount;
                    }
                }
            }
        }

        // Tích điểm thưởng cho khách hàng (Khách lẻ KHÔNG tích điểm, tuân thủ cấu hình LoyaltySetting)
        if (order.CustomerId.HasValue && order.CustomerId.Value != Guid.Empty)
        {
            var customer = await _orderRepository.GetCustomerByIdAsync(order.CustomerId.Value, cancellationToken);
            bool isWalkInCust = customer == null
                || string.Equals(customer.CustomerCode, "KHACHLE", StringComparison.OrdinalIgnoreCase)
                || string.Equals(customer.Name?.Trim(), "Khách lẻ", StringComparison.OrdinalIgnoreCase)
                || string.Equals(customer.Name?.Trim(), "Khách hàng lẻ", StringComparison.OrdinalIgnoreCase);

            if (!isWalkInCust && customer != null)
            {
                decimal earningRate = 10000m;
                int earningPoints = 1;
                bool isLoyaltyEnabled = true;
                if (_promotionRepository != null)
                {
                    var loyaltySetting = await _promotionRepository.GetLoyaltySettingAsync(customer.TenantId, cancellationToken);
                    if (loyaltySetting != null)
                    {
                        isLoyaltyEnabled = loyaltySetting.IsEnabled;
                        if (isLoyaltyEnabled)
                        {
                            earningRate = loyaltySetting.EarningRateAmount > 0 ? loyaltySetting.EarningRateAmount : 10000m;
                            earningPoints = loyaltySetting.EarningPoints > 0 ? loyaltySetting.EarningPoints : 1;
                        }
                    }
                }
                if (isLoyaltyEnabled)
                {
                    int pointsEarned = (int)(order.FinalAmount / earningRate) * earningPoints;
                    customer.RewardPoints += pointsEarned;
                    customer.TotalSpending += order.FinalAmount;
                    customer.UpdatedAt = DateTime.UtcNow;
                }
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

        var effectiveRole = !string.IsNullOrWhiteSpace(request.UserRole) ? request.UserRole : _tenantContext?.CurrentRole;
        bool isStoreOwnerOrAdmin = string.Equals(effectiveRole, "STORE_OWNER", StringComparison.OrdinalIgnoreCase)
            || string.Equals(effectiveRole, "StoreOwner", StringComparison.OrdinalIgnoreCase)
            || string.Equals(effectiveRole, "ADMIN", StringComparison.OrdinalIgnoreCase)
            || string.Equals(effectiveRole, "Admin", StringComparison.OrdinalIgnoreCase);

        if (order.Status == "Completed" && !isStoreOwnerOrAdmin)
        {
            throw new UnauthorizedAccessException("Chỉ Chủ cửa hàng (STORE_OWNER) mới có quyền hủy hóa đơn đã thanh toán. Nhân viên không được phép tự ý hủy.");
        }

        // Hoàn trả tồn kho về BranchStocks (quy đổi về ĐVT cơ sở)
        foreach (var detail in order.Details)
        {
            var stock = await _orderRepository.GetBranchStockAsync(order.BranchId, detail.ProductId, null, cancellationToken);
            if (stock != null)
            {
                var restoreQty = detail.Qty * (detail.ConversionRate > 0m ? detail.ConversionRate : 1.00m);
                stock.QuantityOnHand += restoreQty;
                stock.UpdatedAt = DateTime.UtcNow;
            }
        }

        // Nếu là đơn bán nợ thì hoàn nợ cho khách, hoặc hoàn điểm đã quy đổi nếu hủy đơn
        if (order.CustomerId.HasValue)
        {
            var customer = await _orderRepository.GetCustomerByIdAsync(order.CustomerId.Value, cancellationToken);
            if (customer != null)
            {
                if (order.PaymentMethod == "DEBT" && order.Status != "PendingPayment")
                {
                    customer.TotalDebt = Math.Max(0, customer.TotalDebt - order.FinalAmount);
                }
                if (order.PointsRedeemed > 0)
                {
                    customer.RewardPoints += order.PointsRedeemed;
                }
                customer.UpdatedAt = DateTime.UtcNow;
            }
        }

        bool wasPendingPayment = string.Equals(order.Status, "PendingPayment", StringComparison.OrdinalIgnoreCase);
        order.Status = "Cancelled";
        order.PaymentStatus = wasPendingPayment ? "CANCELLED" : "REFUNDED";
        order.Note = $"Đã hủy: {request.Reason}. {request.Note} | Trước đó: {order.Note}".Trim();

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (_promotionRepository != null && (order.AppliedPromotionId.HasValue || !string.IsNullOrWhiteSpace(order.AppliedVoucherCode)))
        {
            decimal giftBudget = order.Details.Where(d => d.IsGift).Sum(d => d.DiscountAmount);
            await _promotionRepository.RestoreOrderPromotionsOnCancelAsync(
                order.AppliedPromotionId,
                order.PromotionDiscountAmount + giftBudget,
                order.AppliedVoucherCode,
                order.VoucherDiscountAmount,
                order.TenantId,
                cancellationToken
            );
        }

        return MapToOrderResponse(order, 0m, 0m, null);
    }

    public async Task<OrderResponse> ProcessReturnExchangeAsync(Guid orderId, ReturnExchangeRequest request, CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            throw new InvalidOperationException("Dữ liệu đổi/trả hàng không hợp lệ.");
        }

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

        if (string.IsNullOrWhiteSpace(request.SettlementMethod))
        {
            throw new InvalidOperationException("Phương thức bù trừ không được để trống.");
        }

        string settlementMethodUpper = request.SettlementMethod.Trim().ToUpperInvariant();
        if (settlementMethodUpper is not ("CASH" or "VIETQR" or "TRANSFER" or "BANK" or "DEBT" or "REDUCE_DEBT"))
        {
            throw new InvalidOperationException($"Phương thức bù trừ '{request.SettlementMethod}' không hợp lệ.");
        }

        if (request.Note != null && request.Note.Trim().Length > 500)
        {
            throw new InvalidOperationException("Ghi chú đổi/trả hàng tối đa 500 ký tự.");
        }

        if (string.Equals(request.Reason?.Trim(), "OTHER", StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(request.Note))
        {
            throw new InvalidOperationException("Khi chọn lý do khác, bắt buộc nhập ghi chú chi tiết.");
        }

        // Tiền kiểm tra NewItems: dữ liệu hợp lệ, sản phẩm tồn tại và thuộc danh mục Active của chi nhánh đơn gốc (REQ-BRANCH-SCOPE-02)
        Dictionary<Guid, Product>? newProducts = null;
        decimal totalNewAmount = 0m;
        if (request.NewItems != null && request.NewItems.Count > 0)
        {
            var newProductIds = request.NewItems.Select(i => i.ProductId).Distinct().ToList();
            newProducts = await _orderRepository.GetProductsByIdsAsync(newProductIds, cancellationToken);
            var newBranchProducts = await _orderRepository.GetBranchProductsByProductIdsAsync(originalOrder.BranchId, newProductIds, cancellationToken);

            foreach (var newItem in request.NewItems)
            {
                if (newItem.Quantity <= 0)
                {
                    throw new InvalidOperationException("Số lượng sản phẩm đổi mới phải lớn hơn 0.");
                }

                if (newItem.UnitPrice < 0)
                {
                    throw new InvalidOperationException("Đơn giá sản phẩm đổi mới không được âm.");
                }

                if (newItem.DiscountAmount.HasValue && newItem.DiscountAmount.Value < 0)
                {
                    throw new InvalidOperationException("Chiết khấu sản phẩm đổi mới không được âm.");
                }

                if (newItem.ConversionRate < 0)
                {
                    throw new InvalidOperationException("Hệ số quy đổi đơn vị sản phẩm đổi mới không hợp lệ.");
                }

                if (!newProducts.TryGetValue(newItem.ProductId, out var nProd))
                {
                    throw new InvalidOperationException($"Sản phẩm đổi mới với ID '{newItem.ProductId}' không tồn tại.");
                }

                if (!newBranchProducts.TryGetValue(newItem.ProductId, out var bp) ||
                    !string.Equals(bp.Status, "Active", StringComparison.OrdinalIgnoreCase))
                {
                    throw new BusinessException("PRODUCT_NOT_IN_BRANCH", $"Sản phẩm '{nProd.Name}' chưa được kích hoạt hoặc không thuộc danh mục bán của chi nhánh.");
                }

                decimal lineTotal = newItem.Quantity * newItem.UnitPrice;
                decimal itemDiscount = newItem.DiscountAmount ?? 0m;
                if (itemDiscount > lineTotal)
                {
                    throw new InvalidOperationException($"Chiết khấu sản phẩm '{nProd.Name}' không được vượt quá thành tiền.");
                }
                totalNewAmount += lineTotal - itemDiscount;
            }
        }

        // 1. Tiền kiểm tra toàn bộ mặt hàng khách trả trước khi ghi kho
        decimal totalReturnAmount = 0m;
        var resolvedReturns = new List<(ReturnItemDto RetItem, OrderDetail OrigDetail)>();
        var returnedQtyByDetailId = new Dictionary<Guid, decimal>();
        if (request.ReturnedItems != null && request.ReturnedItems.Count > 0)
        {
            foreach (var retItem in request.ReturnedItems)
            {
                if (retItem.Quantity <= 0)
                {
                    throw new InvalidOperationException("Số lượng trả phải lớn hơn 0.");
                }

                if (retItem.UnitPrice < 0)
                {
                    throw new InvalidOperationException("Đơn giá hàng trả không được âm.");
                }

                if (!string.IsNullOrWhiteSpace(retItem.Condition) &&
                    !string.Equals(retItem.Condition.Trim(), "Restock", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(retItem.Condition.Trim(), "Damaged", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Tình trạng hàng trả '{retItem.Condition}' không hợp lệ (chỉ hỗ trợ Restock hoặc Damaged).");
                }

                OrderDetail? origDetail = null;
                if (retItem.OrderDetailId.HasValue && retItem.OrderDetailId.Value != Guid.Empty)
                {
                    origDetail = originalOrder.Details.FirstOrDefault(d => d.Id == retItem.OrderDetailId.Value);
                }
                origDetail ??= originalOrder.Details.FirstOrDefault(d => d.ProductId == retItem.ProductId && d.IsGift == retItem.IsGift)
                            ?? originalOrder.Details.FirstOrDefault(d => d.ProductId == retItem.ProductId);

                if (origDetail == null)
                {
                    throw new InvalidOperationException($"Sản phẩm với ID '{retItem.ProductId}' không có trong hóa đơn gốc '{originalOrder.OrderCode}'.");
                }

                decimal cumulativeReturnQty = returnedQtyByDetailId.GetValueOrDefault(origDetail.Id) + retItem.Quantity;
                if (cumulativeReturnQty > origDetail.Qty)
                {
                    throw new InvalidOperationException($"Số lượng trả ({cumulativeReturnQty}) vượt quá số lượng đã mua ({origDetail.Qty}) của sản phẩm '{origDetail.ProductName}'.");
                }
                returnedQtyByDetailId[origDetail.Id] = cumulativeReturnQty;

                // Khóa cứng hoàn tiền 0đ đối với dòng quà tặng khuyến mãi (REQ-ORD-BOGO-03)
                decimal refundableUnitPrice = origDetail.IsGift
                    ? 0m
                    : (origDetail.ActualPrice > 0m ? Math.Min(Math.Max(0m, retItem.UnitPrice), origDetail.ActualPrice) : Math.Max(0m, retItem.UnitPrice));

                totalReturnAmount += retItem.Quantity * refundableUnitPrice;
                resolvedReturns.Add((retItem, origDetail));
            }
        }

        // Kiểm tra quy tắc Đơn Đặt Trước (PreOrder): chỉ cho đổi sang sản phẩm tương đương trở lên, chặn hoàn tiền trừ khi lỗi NSX
        decimal netAmount = totalNewAmount - totalReturnAmount;
        bool isPreOrderOrder = originalOrder.OrderCode.StartsWith("PO", StringComparison.OrdinalIgnoreCase) ||
                               originalOrder.OrderCode.StartsWith("PRE-", StringComparison.OrdinalIgnoreCase);
        if (isPreOrderOrder &&
            (request.NewItems == null || request.NewItems.Count == 0 || netAmount < 0m) &&
            !string.Equals(request.Reason?.Trim(), "DEFECTIVE", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Đơn đặt trước chỉ hỗ trợ đổi sang sản phẩm khác có giá trị tương đương trở lên, không hỗ trợ hoàn tiền trừ khi sản phẩm lỗi do Nhà Sản Xuất.");
        }

        // 2. Tiền kiểm tra tồn kho FEFO các mặt hàng đổi mới trước khi mutate kho
        var fefoDeductions = new List<(decimal RequiredQty, IReadOnlyList<Domain.Entities.Inventory.BranchStock> Stocks)>();
        if (request.NewItems != null && request.NewItems.Count > 0 && newProducts != null)
        {
            var itemGroups = request.NewItems.GroupBy(i => i.ProductId);
            foreach (var group in itemGroups)
            {
                var pId = group.Key;
                var prod = newProducts[pId];
                var requiredQty = group.Sum(x => x.Quantity * (x.ConversionRate > 0m ? x.ConversionRate : (prod.Units?.FirstOrDefault(u => u.Id == x.UnitId)?.ConversionRate ?? 1.00m)));

                var branchStocks = await _orderRepository.GetBranchStocksForProductFefoAsync(originalOrder.BranchId, pId, cancellationToken);
                var totalOnHand = branchStocks.Sum(s => s.QuantityOnHand);
                if (totalOnHand < requiredQty)
                {
                    throw new InvalidOperationException($"Sản phẩm '{prod.Name}' không đủ tồn kho tại chi nhánh để đổi (Hiện còn: {totalOnHand:0.##}, Cần: {requiredQty:0.##}).");
                }

                fefoDeductions.Add((requiredQty, branchStocks));
            }
        }

        // 3. Kiểm tra công nợ & két quầy trước khi ghi kho
        Customer? customer = null;
        if (originalOrder.CustomerId.HasValue)
        {
            customer = await _orderRepository.GetCustomerByIdAsync(originalOrder.CustomerId.Value, cancellationToken);
        }

        if (netAmount > 0 && settlementMethodUpper == "DEBT" && customer == null)
        {
            throw new InvalidOperationException("Bán nợ bù trừ bắt buộc phải có thông tin khách hàng.");
        }

        if (netAmount < 0 && (settlementMethodUpper == "REDUCE_DEBT" || settlementMethodUpper == "DEBT") && customer == null)
        {
            throw new InvalidOperationException("Trừ nợ khách hàng bắt buộc phải có thông tin khách hàng.");
        }

        Domain.Entities.Finance.ShiftZReport? currentOpenShift = null;
        if (!IsOwnerOrAdminCaller() && _shiftRepository != null)
        {
            currentOpenShift = await _shiftRepository.GetActiveShiftAsync(originalOrder.BranchId, cancellationToken: cancellationToken);
        }

        if (netAmount < 0 &&
            settlementMethodUpper != "REDUCE_DEBT" &&
            settlementMethodUpper != "DEBT" &&
            !IsOwnerOrAdminCaller() &&
            currentOpenShift != null)
        {
            decimal previewRefundAmount = Math.Abs(netAmount);
            if (currentOpenShift.ExpectedCash < previewRefundAmount)
            {
                throw new InvalidOperationException($"Số dư tiền mặt trong két quầy không đủ để hoàn trả (Hiện có: {currentOpenShift.ExpectedCash:N0} đ, Cần hoàn: {previewRefundAmount:N0} đ). Vui lòng nạp thêm tiền vào két ca.");
            }
        }

        // Hoàn lại kho BranchStock cho hàng trả Restock
        foreach (var (retItem, origDetail) in resolvedReturns)
        {
            if (!string.Equals(retItem.Condition?.Trim(), "Damaged", StringComparison.OrdinalIgnoreCase))
            {
                var stock = await _orderRepository.GetBranchStockAsync(originalOrder.BranchId, retItem.ProductId, null, cancellationToken);
                if (stock != null)
                {
                    var convRate = origDetail.ConversionRate > 0m ? origDetail.ConversionRate : 1.00m;
                    stock.QuantityOnHand += retItem.Quantity * convRate;
                    stock.UpdatedAt = DateTime.UtcNow;
                }
            }
        }

        // Trừ kho FEFO cho hàng đổi mới & build chi tiết hóa đơn mới
        var newOrderDetails = new List<OrderDetail>();
        if (request.NewItems != null && request.NewItems.Count > 0 && newProducts != null)
        {
            foreach (var (requiredQty, branchStocks) in fefoDeductions)
            {
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
                var prod = newProducts[item.ProductId];
                decimal lineTotal = item.Quantity * item.UnitPrice;
                decimal itemDiscount = item.DiscountAmount ?? 0m;
                decimal actualPrice = item.Quantity > 0 ? Math.Max(0, (lineTotal - itemDiscount) / item.Quantity) : item.UnitPrice;
                decimal vatRate = prod.VatRate > 0 ? prod.VatRate : 0.01m;
                decimal vatAmount = (actualPrice * item.Quantity) * vatRate;

                var rate = item.ConversionRate > 0m ? item.ConversionRate : (prod.Units?.FirstOrDefault(u => u.Id == item.UnitId)?.ConversionRate ?? 1.00m);
                var uName = !string.IsNullOrWhiteSpace(item.UnitName) ? item.UnitName : (prod.Units?.FirstOrDefault(u => u.Id == item.UnitId)?.UnitName ?? prod.BaseUnit);

                newOrderDetails.Add(new OrderDetail
                {
                    Id = Guid.NewGuid(),
                    ProductId = item.ProductId,
                    LineNumber = line++,
                    Qty = item.Quantity,
                    UnitPrice = item.UnitPrice,
                    DiscountAmount = itemDiscount,
                    ActualPrice = actualPrice,
                    CostPriceAtSale = (prod.CostPrice ?? 0m) * rate,
                    AppliedVatRate = vatRate,
                    VatAmount = vatAmount,
                    ProductName = prod.Name,
                    ProductUnitId = item.UnitId,
                    UnitName = uName,
                    ConversionRate = rate,
                    Product = prod
                });
            }
        }

        // Ghi nhận thanh toán / hoàn tiền / công nợ
        string paymentStatus = "PAID";
        decimal cashTendered = 0m;
        decimal changeAmount = 0m;
        string? exchangeCashierName = originalOrder.CashierName ?? (_tenantContext?.CurrentRole == "STORE_OWNER" ? "Chủ cửa hàng" : "Thu ngân POS");

        if (netAmount > 0)
        {
            if (settlementMethodUpper == "DEBT")
            {
                customer!.TotalDebt += netAmount;
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
                customer!.TotalDebt = Math.Max(0, customer.TotalDebt - refundAmount);
                customer.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                changeAmount = refundAmount;

                // Tự động kiểm tra két và ghi nhận phiếu chi hoàn tiền (Chủ cửa hàng chi từ Két sắt chi nhánh, không đụng két ca thu ngân)
                if (IsOwnerOrAdminCaller())
                {
                    if (_cashbookService != null)
                    {
                        var safe = await _cashbookService.GetOrCreateSafeAsync(originalOrder.BranchId, cancellationToken);
                        await _cashbookService.DisburseCashAsync(
                            safe.Id,
                            FinanceAccountType.Safe,
                            refundAmount,
                            "RETURN_REFUND",
                            originalOrder.Id,
                            originalOrder.OrderCode,
                            $"Hoàn tiền mặt đổi/trả đơn '{originalOrder.OrderCode}' (Chủ cửa hàng)",
                            cancellationToken
                        );
                    }
                }
                else if (currentOpenShift != null && _shiftRepository != null)
                {
                    var movement = new ShiftCashMovement
                    {
                        Id = Guid.NewGuid(),
                        TenantId = originalOrder.TenantId,
                        ShiftId = currentOpenShift.Id,
                        MovementType = CashMovementType.OUT,
                        Category = CashMovementCategory.RETURN_REFUND,
                        Amount = refundAmount,
                        Reason = $"Hoàn tiền mặt đổi/trả đơn '{originalOrder.OrderCode}'. Lý do: {request.Reason ?? request.Note ?? "Khách trả hàng"}",
                        CreatedByName = exchangeCashierName,
                        OccurredAtUtc = DateTime.UtcNow,
                        Status = CashMovementStatus.Recorded,
                        IsConfirmedByOwner = true
                    };
                    await _shiftRepository.AddCashMovementAsync(movement, cancellationToken);
                    currentOpenShift.TotalCashOut += refundAmount;
                    currentOpenShift.ExpectedCash -= refundAmount;
                }
            }
        }

        // 4. Sinh đơn mới liên kết (gắn vào ca đang mở nếu nhân viên thực hiện đổi trả sau khi ca cũ đã đóng)
        Guid? currentActiveShiftId = currentOpenShift?.Id;

        string exchangeCode = $"DT{DateTime.UtcNow:yyMMddHHmmss}{Random.Shared.Next(100, 999)}";
        var exchangeOrder = new Order
        {
            Id = Guid.NewGuid(),
            BranchId = originalOrder.BranchId,
            ShiftId = currentActiveShiftId,
            CustomerId = originalOrder.CustomerId,
            OrderCode = exchangeCode,
            CashierName = exchangeCashierName,
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
            .Where(o => o.PaymentMethod == "CASH" && o.Status != "Cancelled" && o.Status != "PendingPayment")
            .Sum(o => o.Status == "Completed" ? o.FinalAmount : o.DepositAmount);

        decimal transferRevenue = orders
            .Where(o => (o.PaymentMethod == "VIETQR" || o.PaymentMethod == "TRANSFER" || o.PaymentMethod == "BANK") && o.Status != "Cancelled" && o.Status != "PendingPayment")
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

    public async Task<OrderResponse> ConfirmOrderPaymentAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetOrderWithDetailsAsync(orderId, cancellationToken);
        if (order == null)
        {
            throw new InvalidOperationException($"Không tìm thấy đơn hàng với ID '{orderId}'.");
        }

        if (order.Status == "Cancelled")
        {
            throw new InvalidOperationException($"Đơn hàng '{order.OrderCode}' đã bị hủy, không thể xác nhận thanh toán.");
        }

        if (order.Status == "Completed" && order.PaymentStatus == "PAID")
        {
            return MapToOrderResponse(order, order.FinalAmount, 0m, null);
        }

        bool wasPendingPayment = string.Equals(order.Status, "PendingPayment", StringComparison.OrdinalIgnoreCase);
        order.Status = "Completed";
        order.PaymentStatus = "PAID";
        order.DepositAmount = order.FinalAmount;
        order.UpdatedAt = DateTime.UtcNow;

        if (wasPendingPayment && order.CustomerId.HasValue && order.FinalAmount > 0m)
        {
            var customer = await _orderRepository.GetCustomerByIdAsync(order.CustomerId.Value, cancellationToken);
            if (customer != null)
            {
                bool isLoyaltyEnabled = true;
                decimal earningRate = 10000m;
                int earningPoints = 1;
                if (_promotionRepository != null)
                {
                    var loyaltySetting = await _promotionRepository.GetLoyaltySettingAsync(order.TenantId, cancellationToken);
                    if (loyaltySetting != null)
                    {
                        isLoyaltyEnabled = loyaltySetting.IsEnabled;
                        if (isLoyaltyEnabled)
                        {
                            earningRate = loyaltySetting.EarningRateAmount > 0 ? loyaltySetting.EarningRateAmount : 10000m;
                            earningPoints = loyaltySetting.EarningPoints > 0 ? loyaltySetting.EarningPoints : 1;
                        }
                    }
                }
                if (isLoyaltyEnabled)
                {
                    int pointsEarned = (int)(order.FinalAmount / earningRate) * earningPoints;
                    customer.RewardPoints += pointsEarned;
                }
                customer.TotalSpending += order.FinalAmount;
                customer.UpdatedAt = DateTime.UtcNow;
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return MapToOrderResponse(order, order.FinalAmount, 0m, null);
    }

    public async Task<OwnerUnsettledSummaryResponse> GetOwnerUnsettledSummaryAsync(Guid? branchId = null, CancellationToken cancellationToken = default)
    {
        var orders = await _orderRepository.GetAllOrdersForSummaryAsync(branchId, null, null, cancellationToken);
        var unsettled = orders
            .Where(o => o.ShiftId == null
                && o.Status == "Completed"
                && o.PaymentMethod != "DEBT"
                && (o.Note == null || !o.Note.Contains("[CHỐT CUỐI NGÀY:")))
            .OrderByDescending(o => o.OrderDate)
            .ToList();

        var cashOrders = unsettled.Where(o => o.PaymentMethod == "CASH").ToList();
        var bankOrders = unsettled.Where(o => o.PaymentMethod == "VIETQR" || o.PaymentMethod == "BANK" || o.PaymentMethod == "TRANSFER").ToList();

        decimal cashRevenue = cashOrders.Sum(o => o.FinalAmount);
        decimal bankRevenue = bankOrders.Sum(o => o.FinalAmount);

        return new OwnerUnsettledSummaryResponse(
            UnsettledOrdersCount: unsettled.Count,
            CashRevenue: cashRevenue,
            BankRevenue: bankRevenue,
            TotalRevenue: cashRevenue + bankRevenue,
            EarliestOrderTimeUtc: unsettled.Count > 0 ? unsettled.Min(o => o.OrderDate) : null,
            LatestOrderTimeUtc: unsettled.Count > 0 ? unsettled.Max(o => o.OrderDate) : null
        );
    }

    public async Task<OwnerDailySettlementResponse> SettleOwnerDailyOrdersAsync(SettleOwnerDailyRequest request, CancellationToken cancellationToken = default)
    {
        if (!IsOwnerOrAdminCaller())
        {
            throw new UnauthorizedAccessException("Chỉ Chủ cửa hàng (STORE_OWNER) mới có quyền thực hiện chốt doanh thu bán lẻ cuối ngày.");
        }

        var orders = await _orderRepository.GetAllOrdersForSummaryAsync(request.BranchId, null, null, cancellationToken);
        var unsettled = orders
            .Where(o => o.ShiftId == null
                && o.Status == "Completed"
                && o.PaymentMethod != "DEBT"
                && (o.Note == null || !o.Note.Contains("[CHỐT CUỐI NGÀY:")))
            .ToList();

        if (unsettled.Count == 0)
        {
            throw new InvalidOperationException("Hiện không có đơn hàng bán lẻ trực tiếp nào của Chủ cửa hàng chờ kết toán cuối ngày.");
        }

        string settlementCode = $"EOD-{DateTime.UtcNow:yyyyMMdd-HHmmss}";
        string noteSuffix = !string.IsNullOrWhiteSpace(request.Note) ? $" - Ghi chú: {request.Note.Trim()}" : "";

        decimal totalCashSettled = 0m;
        decimal totalBankSettled = 0m;

        foreach (var branchGroup in unsettled.GroupBy(o => o.BranchId))
        {
            var bId = branchGroup.Key;
            var branchCashOrders = branchGroup.Where(o => o.PaymentMethod == "CASH").ToList();
            var branchBankOrders = branchGroup.Where(o => o.PaymentMethod == "VIETQR" || o.PaymentMethod == "BANK" || o.PaymentMethod == "TRANSFER").ToList();

            decimal branchCash = branchCashOrders.Sum(o => o.FinalAmount);
            decimal branchBank = branchBankOrders.Sum(o => o.FinalAmount);

            if (_cashbookService != null)
            {
                if (branchCash > 0m)
                {
                    var safe = await _cashbookService.GetOrCreateSafeAsync(bId, cancellationToken);
                    await _cashbookService.ReceiveCashAsync(
                        safe.Id,
                        FinanceAccountType.Safe,
                        branchCash,
                        "POS_SALE",
                        null,
                        settlementCode,
                        $"Chốt doanh thu bán lẻ cuối ngày của Chủ cửa hàng ({branchCashOrders.Count} đơn tiền mặt){noteSuffix}",
                        cancellationToken
                    );
                }

                if (branchBank > 0m)
                {
                    var bank = await _cashbookService.GetOrCreateBankAccountAsync(bId, ct: cancellationToken);
                    await _cashbookService.ReceiveCashAsync(
                        bank.Id,
                        FinanceAccountType.Bank,
                        branchBank,
                        "POS_SALE",
                        null,
                        $"{settlementCode}-BANK",
                        $"Chốt doanh thu CK/VietQR cuối ngày của Chủ cửa hàng ({branchBankOrders.Count} đơn CK){noteSuffix}",
                        cancellationToken
                    );
                }
            }

            totalCashSettled += branchCash;
            totalBankSettled += branchBank;
        }

        foreach (var ord in unsettled)
        {
            ord.Note = string.IsNullOrWhiteSpace(ord.Note)
                ? $"[CHỐT CUỐI NGÀY: {settlementCode}]"
                : $"{ord.Note} | [CHỐT CUỐI NGÀY: {settlementCode}]";
            ord.UpdatedAt = DateTime.UtcNow;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new OwnerDailySettlementResponse(
            SettledOrdersCount: unsettled.Count,
            CashSettledAmount: totalCashSettled,
            BankSettledAmount: totalBankSettled,
            TotalSettledAmount: totalCashSettled + totalBankSettled,
            SettlementCode: settlementCode,
            SettledAtUtc: DateTime.UtcNow
        );
    }

    private static OrderResponse MapToOrderResponse(
        Order order,
        decimal cashTendered,
        decimal changeAmount,
        CustomerDebtInvoiceResponse? debtInvoice = null,
        string? giftStockWarning = null)
    {
        var items = order.Details.Select(d => new OrderItemResponse(
            Id: d.Id,
            ProductId: d.ProductId,
            ProductName: !string.IsNullOrWhiteSpace(d.ProductName) ? d.ProductName : (d.Product?.Name ?? "Sản phẩm"),
            Barcode: d.Product?.Barcode,
            UnitName: !string.IsNullOrWhiteSpace(d.UnitName) ? d.UnitName : d.Product?.BaseUnit,
            Quantity: d.Qty,
            UnitPrice: d.UnitPrice,
            DiscountAmount: d.DiscountAmount,
            ActualPrice: d.ActualPrice,
            CostPriceAtSale: d.CostPriceAtSale,
            TotalLineAmount: d.ActualPrice * d.Qty,
            AppliedVatRate: d.AppliedVatRate,
            VatAmount: d.VatAmount,
            BatchCode: null,
            IsGift: d.IsGift,
            PromotionId: d.PromotionId
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
            ShiftId: order.ShiftId,
            CashierName: order.CashierName ?? (order.Shift != null ? "Thu ngân POS" : null),
            FulfilledByCashierName: order.FulfilledByCashierName,
            FulfilledAtUtc: order.FulfilledAtUtc,
            AppliedPromotionId: order.AppliedPromotionId,
            AppliedPromotionSummary: order.AppliedPromotionSummary,
            PromotionDiscountAmount: order.PromotionDiscountAmount,
            AppliedVoucherCode: order.AppliedVoucherCode,
            VoucherDiscountAmount: order.VoucherDiscountAmount,
            PointsRedeemed: order.PointsRedeemed,
            PointsDiscountAmount: order.PointsDiscountAmount,
            GiftStockWarning: giftStockWarning
        );
    }

    private bool IsOwnerOrAdminCaller()
    {
        var role = _tenantContext?.CurrentRole;
        return string.Equals(role, "STORE_OWNER", StringComparison.OrdinalIgnoreCase)
            || string.Equals(role, "ADMIN", StringComparison.OrdinalIgnoreCase);
    }
}
