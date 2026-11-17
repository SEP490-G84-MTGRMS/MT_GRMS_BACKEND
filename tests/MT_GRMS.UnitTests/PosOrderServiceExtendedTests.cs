using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentValidation;
using Moq;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.PosOrders;
using MT_GRMS.Application.PosOrders.Dtos;
using MT_GRMS.Application.PosShifts;
using MT_GRMS.Application.PosShifts.Dtos;
using MT_GRMS.Application.Promotions;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Entities.Inventory;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Domain.Entities.Sales;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Domain.Exceptions;
using MT_GRMS.Infrastructure.Services.Printer;
using Xunit;

namespace MT_GRMS.UnitTests;

public class PosOrderServiceExtendedTests
{
    private readonly Mock<IOrderRepository> _orderRepoMock = new();
    private readonly Mock<IUnitOfWork> _uowMock = new();
    private readonly Mock<ITenantContext> _tenantContextMock = new();
    private readonly Mock<IShiftRepository> _shiftRepoMock = new();
    private readonly Mock<IShiftService> _shiftServiceMock = new();
    private readonly Mock<IPromotionRepository> _promotionRepoMock = new();
    private readonly ThermalPrintService _printService = new();

    private readonly Guid _branchId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private readonly Guid _customerId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private readonly Guid _productId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    public PosOrderServiceExtendedTests()
    {
        _tenantContextMock.Setup(t => t.CurrentTenantId).Returns("tenant-01");
        _tenantContextMock.Setup(t => t.CurrentUserId).Returns(Guid.NewGuid());
        _tenantContextMock.Setup(t => t.CurrentRole).Returns("CASHIER");

        _orderRepoMock.Setup(r => r.GetBranchByIdAsync(_branchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Branch { Id = _branchId, TenantId = "tenant-01", Name = "Branch 1", IsActive = true });

        var testProd = new Product
        {
            Id = _productId,
            TenantId = "tenant-01",
            ProductCode = "P01",
            Name = "Test Product",
            SellingPrice = 10000m,
            IsActive = true
        };

        _orderRepoMock.Setup(r => r.GetProductsByIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, Product> { { _productId, testProd } });

        _orderRepoMock.Setup(r => r.GetBranchProductsByProductIdsAsync(_branchId, It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, BranchProduct>
            {
                { _productId, new BranchProduct { BranchId = _branchId, ProductId = _productId, Status = "Active" } }
            });

        _orderRepoMock.Setup(r => r.GetBranchStocksForProductFefoAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BranchStock>
            {
                new() { Id = Guid.NewGuid(), BranchId = _branchId, ProductId = _productId, QuantityOnHand = 100m }
            });

        _shiftRepoMock.Setup(s => s.GetActiveShiftAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ShiftZReport { Id = Guid.NewGuid(), BranchId = _branchId, Status = ShiftState.Open });

        _shiftServiceMock.Setup(s => s.CheckCashierShiftEligibilityAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CashierShiftEligibilityDto(true, false, "CASHIER", "Cashier 01", null, null));

        _orderRepoMock.Setup(r => r.GetCustomerByIdAsync(_customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Customer { Id = _customerId, CustomerCode = "CUST01", Name = "Nguyen Van A", AllowDebt = true, TenantId = "tenant-01" });

        _orderRepoMock.Setup(r => r.GetOrderWithDetailsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => new Order
            {
                Id = id,
                BranchId = _branchId,
                TenantId = "tenant-01",
                OrderCode = "ORD001",
                Status = "Completed",
                Details = new List<OrderDetail>
                {
                    new OrderDetail
                    {
                        Id = Guid.NewGuid(),
                        OrderId = id,
                        ProductId = _productId,
                        Quantity = 5,
                        UnitPrice = 10000m,
                        ActualPrice = 10000m,
                        IsGift = false
                    }
                }
            });
    }

    private OrderService CreateService()
    {
        return new OrderService(
            _orderRepoMock.Object,
            _uowMock.Object,
            _printService,
            debtService: null,
            cashbookService: null,
            validator: null,
            shiftRepository: _shiftRepoMock.Object,
            tenantContext: _tenantContextMock.Object,
            shiftService: _shiftServiceMock.Object,
            promotionRepository: _promotionRepoMock.Object);
    }

    [Fact]
    public async Task CreateOrderAsync_EmptyCart_ThrowsValidationException()
    {
        var service = CreateService();
        var req = new OrderCreateRequest(
            BranchId: _branchId,
            CustomerId: null,
            PaymentMethod: "CASH",
            Items: new List<OrderItemCreateDto>()
        );

        var ex = await Assert.ThrowsAsync<ValidationException>(() => service.CreateOrderAsync(req));
        Assert.Contains("Giỏ hàng không được để trống", ex.Message);
    }

    [Fact]
    public async Task CreateOrderAsync_CartOnlyGifts_ThrowsValidationException()
    {
        var service = CreateService();
        var req = new OrderCreateRequest(
            BranchId: _branchId,
            CustomerId: null,
            PaymentMethod: "CASH",
            Items: new List<OrderItemCreateDto>
            {
                new(_productId, 1, 0m, IsGift: true)
            }
        );

        var ex = await Assert.ThrowsAsync<ValidationException>(() => service.CreateOrderAsync(req));
        Assert.Contains("bắt buộc phải có ít nhất 1 mặt hàng mua trả tiền", ex.Message);
    }

    [Fact]
    public async Task CreateOrderAsync_ShiftClosing_ThrowsShiftClosingLockdown()
    {
        var shiftId = Guid.NewGuid();
        _shiftRepoMock.Setup(s => s.GetActiveShiftAsync(_branchId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ShiftZReport { Id = shiftId, BranchId = _branchId, Status = ShiftState.Closing });

        var service = CreateService();
        var req = new OrderCreateRequest(
            BranchId: _branchId,
            CustomerId: null,
            PaymentMethod: "CASH",
            Items: new List<OrderItemCreateDto> { new(_productId, 1, 10000m) }
        );

        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.CreateOrderAsync(req));
        Assert.Equal("SHIFT_CLOSING_LOCKDOWN", ex.ErrorCode);
    }

    [Fact]
    public async Task CreateOrderAsync_ShiftClosedOrInvalid_ThrowsBusinessException()
    {
        var shiftId = Guid.NewGuid();
        _shiftRepoMock.Setup(s => s.GetActiveShiftAsync(_branchId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ShiftZReport { Id = shiftId, BranchId = _branchId, Status = ShiftState.Closed });

        var service = CreateService();
        var req = new OrderCreateRequest(
            BranchId: _branchId,
            CustomerId: null,
            PaymentMethod: "CASH",
            Items: new List<OrderItemCreateDto> { new(_productId, 1, 10000m) },
            ShiftId: Guid.NewGuid() // different shift ID
        );

        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.CreateOrderAsync(req));
        Assert.Equal("SHIFT_CLOSED_OR_INVALID", ex.ErrorCode);
    }

    [Fact]
    public async Task CreateOrderAsync_DebtWithoutCustomer_ThrowsInvalidOperation()
    {
        _shiftRepoMock.Setup(s => s.GetActiveShiftAsync(_branchId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ShiftZReport { Id = Guid.NewGuid(), BranchId = _branchId, Status = ShiftState.Open });

        _orderRepoMock.Setup(r => r.GetBranchStockAsync(_branchId, _productId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BranchStock { Id = Guid.NewGuid(), BranchId = _branchId, ProductId = _productId, QuantityOnHand = 10m });

        var service = CreateService();
        var req = new OrderCreateRequest(
            BranchId: _branchId,
            CustomerId: null,
            PaymentMethod: "DEBT",
            Items: new List<OrderItemCreateDto> { new(_productId, 1, 10000m) }
        );

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateOrderAsync(req));
        Assert.Contains("Bán nợ (ghi sổ) bắt buộc phải chọn thông tin khách hàng", ex.Message);
    }

    [Fact]
    public async Task CreateOrderAsync_CustomerNotAllowedDebt_ThrowsInvalidOperation()
    {
        _tenantContextMock.Setup(t => t.CurrentRole).Returns("ACCOUNTANT");
        _shiftRepoMock.Setup(s => s.GetActiveShiftAsync(_branchId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ShiftZReport { Id = Guid.NewGuid(), BranchId = _branchId, Status = ShiftState.Open });

        _orderRepoMock.Setup(r => r.GetBranchStockAsync(_branchId, _productId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BranchStock { Id = Guid.NewGuid(), BranchId = _branchId, ProductId = _productId, QuantityOnHand = 10m });

        _orderRepoMock.Setup(r => r.GetCustomerByIdAsync(_customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Customer { Id = _customerId, Name = "Khách A", AllowDebt = false });

        var service = CreateService();
        var req = new OrderCreateRequest(
            BranchId: _branchId,
            CustomerId: _customerId,
            PaymentMethod: "DEBT",
            Items: new List<OrderItemCreateDto> { new(_productId, 1, 10000m) }
        );

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateOrderAsync(req));
        Assert.Contains("chưa được kích hoạt quyền mua nợ", ex.Message);
    }

    [Fact]
    public async Task CreateOrderAsync_CashierNotAllowedDebt_ThrowsUnauthorizedAccessException()
    {
        _tenantContextMock.Setup(t => t.CurrentRole).Returns("CASHIER");
        _shiftRepoMock.Setup(s => s.GetActiveShiftAsync(_branchId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ShiftZReport { Id = Guid.NewGuid(), BranchId = _branchId, Status = ShiftState.Open });

        _orderRepoMock.Setup(r => r.GetCustomerByIdAsync(_customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Customer { Id = _customerId, Name = "Khách A", AllowDebt = true });

        var service = CreateService();
        var req = new OrderCreateRequest(
            BranchId: _branchId,
            CustomerId: _customerId,
            PaymentMethod: "DEBT",
            Items: new List<OrderItemCreateDto> { new(_productId, 1, 10000m) }
        );

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.CreateOrderAsync(req));
        Assert.Contains("Chỉ Chủ cửa hàng hoặc Quản trị viên mới có quyền bán nợ", ex.Message);
    }

    [Fact]
    public async Task CreatePreOrderAsync_NullData_ThrowsValidationException()
    {
        var service = CreateService();
        await Assert.ThrowsAsync<ValidationException>(() => service.CreatePreOrderAsync(null!));
    }

    [Fact]
    public async Task CreatePreOrderAsync_EmptyProductId_ThrowsInvalidOperation()
    {
        var service = CreateService();
        var req = new PreOrderCreateRequest(
            BranchId: _branchId,
            CustomerId: _customerId,
            ExpectedPickupDate: DateTime.UtcNow.AddDays(1),
            DepositAmount: 0m,
            PaymentMethod: "CASH",
            Items: new List<OrderItemCreateDto> { new(Guid.Empty, 1, 10000m) }
        );

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreatePreOrderAsync(req));
        Assert.Contains("Mã sản phẩm không được để trống", ex.Message);
    }

    [Fact]
    public async Task CreatePreOrderAsync_NegativeDiscount_ThrowsInvalidOperation()
    {
        var service = CreateService();
        var req = new PreOrderCreateRequest(
            BranchId: _branchId,
            CustomerId: _customerId,
            ExpectedPickupDate: DateTime.UtcNow.AddDays(1),
            DepositAmount: 0m,
            PaymentMethod: "CASH",
            Items: new List<OrderItemCreateDto> { new(_productId, 1, 10000m, DiscountAmount: -100m) }
        );

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreatePreOrderAsync(req));
        Assert.Contains("Giảm giá sản phẩm không được âm", ex.Message);
    }

    [Fact]
    public async Task ProcessReturnExchangeAsync_NegativeDiscountOnNewItem_ThrowsInvalidOperation()
    {
        var service = CreateService();
        var dummyOrderId = Guid.NewGuid();
        var req = new ReturnExchangeRequest(
            ReturnedItems: new List<ReturnItemDto> { new(_productId, 1, 10000m) },
            NewItems: new List<OrderItemCreateDto> { new(_productId, 1, 10000m, DiscountAmount: -500m) },
            SettlementMethod: "CASH",
            Reason: "Test"
        );

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ProcessReturnExchangeAsync(dummyOrderId, req));
        Assert.Contains("Chiết khấu sản phẩm đổi mới không được âm", ex.Message);
    }

    [Fact]
    public async Task ProcessReturnExchangeAsync_InvalidConversionRate_ThrowsInvalidOperation()
    {
        var service = CreateService();
        var dummyOrderId = Guid.NewGuid();
        var req = new ReturnExchangeRequest(
            ReturnedItems: new List<ReturnItemDto> { new(_productId, 1, 10000m) },
            NewItems: new List<OrderItemCreateDto> { new(_productId, 1, 10000m, ConversionRate: -1m) },
            SettlementMethod: "CASH",
            Reason: "Test"
        );

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ProcessReturnExchangeAsync(dummyOrderId, req));
        Assert.Contains("Hệ số quy đổi", ex.Message);
    }

    [Fact]
    public async Task ProcessReturnExchangeAsync_NewProductNotFound_ThrowsInvalidOperation()
    {
        var service = CreateService();
        var dummyOrderId = Guid.NewGuid();
        var unknownProdId = Guid.NewGuid();
        var req = new ReturnExchangeRequest(
            ReturnedItems: new List<ReturnItemDto> { new(_productId, 1, 10000m) },
            NewItems: new List<OrderItemCreateDto> { new(unknownProdId, 1, 10000m) },
            SettlementMethod: "CASH",
            Reason: "Test"
        );

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ProcessReturnExchangeAsync(dummyOrderId, req));
        Assert.Contains("không tồn tại", ex.Message);
    }

    [Fact]
    public async Task CreateOrderAsync_ShiftInClosingState_ThrowsBusinessException()
    {
        var service = CreateService();
        var shiftId = Guid.NewGuid();
        var closingShift = new ShiftZReport
        {
            Id = shiftId,
            TenantId = "tenant-01",
            BranchId = _branchId,
            Status = ShiftState.Closing
        };
        _shiftRepoMock.Setup(s => s.GetActiveShiftAsync(_branchId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(closingShift);

        var req = new OrderCreateRequest(
            BranchId: _branchId,
            ShiftId: shiftId,
            CustomerId: null,
            Items: new List<OrderItemCreateDto> { new(_productId, 1, 10000m) },
            PaymentMethod: "CASH"
        );

        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.CreateOrderAsync(req));
        Assert.Equal("SHIFT_CLOSING_LOCKDOWN", ex.ErrorCode);
    }

    [Fact]
    public async Task CreateOrderAsync_CashierNotEligible_ThrowsBusinessException()
    {
        var service = CreateService();
        var shiftId = Guid.NewGuid();
        var openShift = new ShiftZReport
        {
            Id = shiftId,
            TenantId = "tenant-01",
            BranchId = _branchId,
            Status = ShiftState.Open
        };
        _shiftRepoMock.Setup(s => s.GetActiveShiftAsync(_branchId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(openShift);

        _shiftServiceMock.Setup(s => s.CheckCashierShiftEligibilityAsync(
                _branchId, It.IsAny<Guid>(), "CASHIER", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CashierShiftEligibilityDto(
                false, false, "CASHIER", "Staff A", "Không có lịch làm việc", null, false, false, null));

        var req = new OrderCreateRequest(
            BranchId: _branchId,
            ShiftId: shiftId,
            CustomerId: null,
            Items: new List<OrderItemCreateDto> { new(_productId, 1, 10000m) },
            PaymentMethod: "CASH"
        );

        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.CreateOrderAsync(req));
        Assert.Equal("EMPLOYEE_NOT_SCHEDULED", ex.ErrorCode);
    }

    [Fact]
    public async Task ProcessReturnExchangeAsync_DiscountExceedsLineAmount_ThrowsInvalidOperation()
    {
        var service = CreateService();
        var dummyOrderId = Guid.NewGuid();
        var req = new ReturnExchangeRequest(
            ReturnedItems: new List<ReturnItemDto> { new(_productId, 1, 10000m) },
            NewItems: new List<OrderItemCreateDto> { new(_productId, 1, 10000m, DiscountAmount: 15000m) },
            SettlementMethod: "CASH",
            Reason: "Test"
        );

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ProcessReturnExchangeAsync(dummyOrderId, req));
        Assert.Contains("không được vượt quá thành tiền", ex.Message);
    }

    [Fact]
    public async Task ProcessReturnExchangeAsync_NegativeReturnUnitPrice_ThrowsInvalidOperation()
    {
        var service = CreateService();
        var dummyOrderId = Guid.NewGuid();
        var req = new ReturnExchangeRequest(
            ReturnedItems: new List<ReturnItemDto> { new(_productId, 1, -5000m) },
            NewItems: new List<OrderItemCreateDto> { new(_productId, 1, 10000m) },
            SettlementMethod: "CASH",
            Reason: "Test"
        );

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ProcessReturnExchangeAsync(dummyOrderId, req));
        Assert.Contains("Đơn giá hàng trả không được âm", ex.Message);
    }
}
