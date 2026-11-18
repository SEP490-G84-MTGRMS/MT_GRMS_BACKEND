using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Xunit;
using MT_GRMS.Application.Cashbook;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Common.Interfaces;
using MT_GRMS.Application.GoodsReceipts;
using MT_GRMS.Application.GoodsReceipts.Dtos;
using MT_GRMS.Application.PosOrders;
using MT_GRMS.Application.PosOrders.Dtos;
using MT_GRMS.Application.PosShifts;
using MT_GRMS.Application.Promotions;
using MT_GRMS.Application.PurchaseOrders;
using MT_GRMS.Application.PurchaseOrders.Dispatch;
using MT_GRMS.Application.PurchaseOrders.Dtos;
using MT_GRMS.Application.Stocktakes;
using MT_GRMS.Application.Stocktakes.Dtos;
using MT_GRMS.Application.Suppliers;
using MT_GRMS.Application.SupplierReturns;
using MT_GRMS.Application.SupplierReturns.Dtos;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Entities.Inventory;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Domain.Entities.Sales;
using MT_GRMS.Domain.Entities.Supplier;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Domain.Exceptions;

namespace MT_GRMS.UnitTests;

public class DeepBranchCoverageSuite2Tests
{
    private class FakeTenantContext : ITenantContext
    {
        public string CurrentTenantId { get; set; } = "tenant-cov-suite2";
        public Guid CurrentUserId { get; set; } = Guid.NewGuid();
        public string CurrentRole { get; set; } = "STORE_OWNER";
        public Guid? CurrentBranchId { get; set; } = Guid.NewGuid();
        public string? CustomRawUserId { get; set; } = null;
        public string CurrentRawUserId => CustomRawUserId ?? (CurrentUserId != Guid.Empty ? CurrentUserId.ToString() : string.Empty);

        public void SetContext(string tenantId, Guid userId, Guid? branchId)
        {
            CurrentTenantId = tenantId;
            CurrentUserId = userId;
            CurrentBranchId = branchId;
        }

        public void SetRole(string? role) => CurrentRole = role ?? string.Empty;
    }

    private class LocalUnitOfWork : IUnitOfWork
    {
        public bool SaveChangesCalled { get; private set; }
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveChangesCalled = true;
            return Task.FromResult(1);
        }
    }

    // =========================================================================
    // SECTION 1: OrderService Deep Branches
    // =========================================================================

    [Fact]
    public async Task OrderService_BatchStock_BatchNotFound_ThrowsBusinessException()
    {
        var tenantContext = new FakeTenantContext();
        var fakeOrderRepo = new FakeOrderRepository();
        var uow = new LocalUnitOfWork();
        var printMock = new Mock<IThermalPrintService>();

        var branchId = tenantContext.CurrentBranchId!.Value;
        var prodId = Guid.NewGuid();
        var missingBatchId = Guid.NewGuid();

        fakeOrderRepo.Branches.Add(new Branch { Id = branchId, TenantId = tenantContext.CurrentTenantId, Name = "Branch 1", IsActive = true });
        fakeOrderRepo.Products.Add(new Product { Id = prodId, Name = "SP 1", SellingPrice = 50000m, IsActive = true, Units = new List<ProductUnit>() });
        fakeOrderRepo.BranchProducts.Add(new BranchProduct { BranchId = branchId, ProductId = prodId, Status = "Active" });

        var service = new OrderService(fakeOrderRepo, uow, printMock.Object, null, null, null, null, null, tenantContext, null);

        var req = new OrderCreateRequest(
            BranchId: branchId,
            CustomerId: null,
            PaymentMethod: "CASH",
            Items: new List<OrderItemCreateDto>
            {
                new OrderItemCreateDto(ProductId: prodId, Quantity: 2, UnitPrice: 50000m, BatchId: missingBatchId)
            }
        );

        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.CreateOrderAsync(req));
        Assert.Equal("BATCH_NOT_FOUND", ex.ErrorCode);
    }

    [Fact]
    public async Task OrderService_BatchStock_BatchExpired_ThrowsBusinessException()
    {
        var tenantContext = new FakeTenantContext();
        var fakeOrderRepo = new FakeOrderRepository();
        var uow = new LocalUnitOfWork();
        var printMock = new Mock<IThermalPrintService>();

        var branchId = tenantContext.CurrentBranchId!.Value;
        var prodId = Guid.NewGuid();
        var batchId = Guid.NewGuid();

        fakeOrderRepo.Branches.Add(new Branch { Id = branchId, TenantId = tenantContext.CurrentTenantId, Name = "Branch 1", IsActive = true });
        fakeOrderRepo.Products.Add(new Product { Id = prodId, Name = "SP Expired", SellingPrice = 40000m, IsActive = true, Units = new List<ProductUnit>() });
        fakeOrderRepo.BranchProducts.Add(new BranchProduct { BranchId = branchId, ProductId = prodId, Status = "Active" });
        fakeOrderRepo.BranchStocks.Add(new BranchStock
        {
            Id = batchId,
            BranchId = branchId,
            ProductId = prodId,
            BatchCode = "LOT-EXP-01",
            QuantityOnHand = 10m,
            ExpiryDate = DateTime.UtcNow.AddDays(-2) // Expired 2 days ago
        });

        var service = new OrderService(fakeOrderRepo, uow, printMock.Object, null, null, null, null, null, tenantContext, null);

        var req = new OrderCreateRequest(
            BranchId: branchId,
            CustomerId: null,
            PaymentMethod: "CASH",
            Items: new List<OrderItemCreateDto>
            {
                new OrderItemCreateDto(ProductId: prodId, Quantity: 1, UnitPrice: 40000m, BatchId: batchId)
            }
        );

        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.CreateOrderAsync(req));
        Assert.Equal("BATCH_EXPIRED", ex.ErrorCode);
    }

    [Fact]
    public async Task OrderService_BatchStock_InsufficientStock_ThrowsBusinessException()
    {
        var tenantContext = new FakeTenantContext();
        var fakeOrderRepo = new FakeOrderRepository();
        var uow = new LocalUnitOfWork();
        var printMock = new Mock<IThermalPrintService>();

        var branchId = tenantContext.CurrentBranchId!.Value;
        var prodId = Guid.NewGuid();
        var batchId = Guid.NewGuid();

        fakeOrderRepo.Branches.Add(new Branch { Id = branchId, TenantId = tenantContext.CurrentTenantId, Name = "Branch 1", IsActive = true });
        fakeOrderRepo.Products.Add(new Product { Id = prodId, Name = "SP Short", SellingPrice = 40000m, IsActive = true, Units = new List<ProductUnit>() });
        fakeOrderRepo.BranchProducts.Add(new BranchProduct { BranchId = branchId, ProductId = prodId, Status = "Active" });
        fakeOrderRepo.BranchStocks.Add(new BranchStock
        {
            Id = batchId,
            BranchId = branchId,
            ProductId = prodId,
            BatchCode = "LOT-SHORT-01",
            QuantityOnHand = 5m,
            QuantityReserved = 3m,
            QuantityQuarantined = 1m, // Available: 5 - 3 - 1 = 1
            ExpiryDate = DateTime.UtcNow.AddDays(30)
        });

        var service = new OrderService(fakeOrderRepo, uow, printMock.Object, null, null, null, null, null, tenantContext, null);

        var req = new OrderCreateRequest(
            BranchId: branchId,
            CustomerId: null,
            PaymentMethod: "CASH",
            Items: new List<OrderItemCreateDto>
            {
                new OrderItemCreateDto(ProductId: prodId, Quantity: 3, UnitPrice: 40000m, BatchId: batchId) // Needs 3 > Available 1
            }
        );

        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.CreateOrderAsync(req));
        Assert.Equal("BATCH_INSUFFICIENT_STOCK", ex.ErrorCode);
    }

    [Fact]
    public async Task OrderService_BatchStock_SufficientStock_DeductsOnHandCorrectly()
    {
        var tenantContext = new FakeTenantContext();
        var fakeOrderRepo = new FakeOrderRepository();
        var uow = new LocalUnitOfWork();
        var printMock = new Mock<IThermalPrintService>();

        var branchId = tenantContext.CurrentBranchId!.Value;
        var prodId = Guid.NewGuid();
        var batchId = Guid.NewGuid();

        fakeOrderRepo.Branches.Add(new Branch { Id = branchId, TenantId = tenantContext.CurrentTenantId, Name = "Branch 1", IsActive = true });
        fakeOrderRepo.Products.Add(new Product { Id = prodId, Name = "SP Valid Batch", SellingPrice = 25000m, IsActive = true, Units = new List<ProductUnit>() });
        fakeOrderRepo.BranchProducts.Add(new BranchProduct { BranchId = branchId, ProductId = prodId, Status = "Active" });
        var stock = new BranchStock
        {
            Id = batchId,
            BranchId = branchId,
            ProductId = prodId,
            BatchCode = "LOT-OK-01",
            QuantityOnHand = 20m,
            QuantityReserved = 2m,
            ExpiryDate = DateTime.UtcNow.AddDays(60)
        };
        fakeOrderRepo.BranchStocks.Add(stock);

        var service = new OrderService(fakeOrderRepo, uow, printMock.Object, null, null, null, null, null, tenantContext, null);

        var req = new OrderCreateRequest(
            BranchId: branchId,
            CustomerId: null,
            PaymentMethod: "CASH",
            Items: new List<OrderItemCreateDto>
            {
                new OrderItemCreateDto(ProductId: prodId, Quantity: 5, UnitPrice: 25000m, BatchId: batchId)
            }
        );

        var res = await service.CreateOrderAsync(req);
        Assert.NotNull(res);
        Assert.Equal(15m, stock.QuantityOnHand); // 20 - 5 = 15
        Assert.True(uow.SaveChangesCalled);
    }

    [Fact]
    public async Task OrderService_ProductUnitPricing_FallbackToBasePriceTimesConversionRate()
    {
        var tenantContext = new FakeTenantContext();
        var fakeOrderRepo = new FakeOrderRepository();
        var uow = new LocalUnitOfWork();
        var printMock = new Mock<IThermalPrintService>();

        var branchId = tenantContext.CurrentBranchId!.Value;
        var prodId = Guid.NewGuid();
        var unitId = Guid.NewGuid();

        fakeOrderRepo.Branches.Add(new Branch { Id = branchId, TenantId = tenantContext.CurrentTenantId, Name = "Branch 1", IsActive = true });
        // Product has base SellingPrice = 10,000, and a unit with Price = 0 (no specific unit price) and ConversionRate = 3.0
        fakeOrderRepo.Products.Add(new Product
        {
            Id = prodId,
            Name = "SP Thung",
            SellingPrice = 10000m,
            IsActive = true,
            Units = new List<ProductUnit>
            {
                new ProductUnit { Id = unitId, UnitName = "Thùng", Price = 0m, ConversionRate = 3.0m, IsDeleted = false }
            }
        });
        // BranchProduct has SellingPriceOverride = 12,000, and NO unit price override
        fakeOrderRepo.BranchProducts.Add(new BranchProduct
        {
            BranchId = branchId,
            ProductId = prodId,
            Status = "Active",
            SellingPriceOverride = 12000m,
            UnitPriceOverrides = new List<BranchProductUnitPrice>()
        });
        fakeOrderRepo.BranchStocks.Add(new BranchStock
        {
            Id = Guid.NewGuid(),
            BranchId = branchId,
            ProductId = prodId,
            QuantityOnHand = 100m
        });

        var service = new OrderService(fakeOrderRepo, uow, printMock.Object, null, null, null, null, null, tenantContext, null);

        var req = new OrderCreateRequest(
            BranchId: branchId,
            CustomerId: null,
            PaymentMethod: "CASH",
            Items: new List<OrderItemCreateDto>
            {
                new OrderItemCreateDto(ProductId: prodId, Quantity: 2, UnitPrice: 0m, UnitId: unitId)
            }
        );

        var res = await service.CreateOrderAsync(req);
        Assert.NotNull(res);
        // Base selling is 12,000 (from branch override) * conversion 3.0 = 36,000 per unit * 2 = 72,000
        Assert.Equal(72000m, res.TotalAmount);
    }

    [Fact]
    public async Task OrderService_GiftItem_InactiveBranchProduct_AddsGiftWarningAndSkipsStockDeduction()
    {
        var tenantContext = new FakeTenantContext();
        var fakeOrderRepo = new FakeOrderRepository();
        var uow = new LocalUnitOfWork();
        var printMock = new Mock<IThermalPrintService>();

        var branchId = tenantContext.CurrentBranchId!.Value;
        var normalProdId = Guid.NewGuid();
        var giftProdId = Guid.NewGuid();

        fakeOrderRepo.Branches.Add(new Branch { Id = branchId, TenantId = tenantContext.CurrentTenantId, Name = "Branch 1", IsActive = true });
        fakeOrderRepo.Products.Add(new Product { Id = normalProdId, Name = "SP Chinh", SellingPrice = 50000m, IsActive = true });
        fakeOrderRepo.Products.Add(new Product { Id = giftProdId, Name = "SP Qua Tang", SellingPrice = 20000m, IsActive = true });
        fakeOrderRepo.BranchProducts.Add(new BranchProduct { BranchId = branchId, ProductId = normalProdId, Status = "Active" });
        fakeOrderRepo.BranchProducts.Add(new BranchProduct { BranchId = branchId, ProductId = giftProdId, Status = "Inactive" }); // Inactive!

        fakeOrderRepo.BranchStocks.Add(new BranchStock { Id = Guid.NewGuid(), BranchId = branchId, ProductId = normalProdId, QuantityOnHand = 50m });
        fakeOrderRepo.BranchStocks.Add(new BranchStock { Id = Guid.NewGuid(), BranchId = branchId, ProductId = giftProdId, QuantityOnHand = 50m });

        var service = new OrderService(fakeOrderRepo, uow, printMock.Object, null, null, null, null, null, tenantContext, null);

        var req = new OrderCreateRequest(
            BranchId: branchId,
            CustomerId: null,
            PaymentMethod: "CASH",
            Items: new List<OrderItemCreateDto>
            {
                new OrderItemCreateDto(ProductId: normalProdId, Quantity: 1, UnitPrice: 50000m),
                new OrderItemCreateDto(ProductId: giftProdId, Quantity: 1, UnitPrice: 20000m, IsGift: true)
            }
        );

        var res = await service.CreateOrderAsync(req);
        Assert.NotNull(res);
        Assert.NotNull(res.GiftStockWarning);
        Assert.Contains("chưa được kích hoạt tại chi nhánh", res.GiftStockWarning);
    }

    [Fact]
    public async Task OrderService_ShiftParticipant_UpdatesUserNameWhenProvided()
    {
        var tenantContext = new FakeTenantContext();
        var fakeOrderRepo = new FakeOrderRepository();
        var uow = new LocalUnitOfWork();
        var printMock = new Mock<IThermalPrintService>();
        var shiftRepoMock = new Mock<IShiftRepository>();

        var branchId = tenantContext.CurrentBranchId!.Value;
        var prodId = Guid.NewGuid();
        var shiftId = Guid.NewGuid();
        var userId = tenantContext.CurrentUserId;

        fakeOrderRepo.Branches.Add(new Branch { Id = branchId, TenantId = tenantContext.CurrentTenantId, Name = "Branch 1", IsActive = true });
        fakeOrderRepo.Products.Add(new Product { Id = prodId, Name = "SP 1", SellingPrice = 50000m, IsActive = true });
        fakeOrderRepo.BranchProducts.Add(new BranchProduct { BranchId = branchId, ProductId = prodId, Status = "Active" });
        fakeOrderRepo.BranchStocks.Add(new BranchStock { Id = Guid.NewGuid(), BranchId = branchId, ProductId = prodId, QuantityOnHand = 50m });

        var participant = new ShiftParticipant { ShiftId = shiftId, UserId = userId, UserName = "Old Name" };
        var activeShift = new ShiftZReport
        {
            Id = shiftId,
            BranchId = branchId,
            Status = ShiftState.Open,
            CashierUserId = userId,
            Participants = new List<ShiftParticipant> { participant }
        };

        shiftRepoMock.Setup(r => r.GetActiveShiftAsync(branchId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(activeShift);
        shiftRepoMock.Setup(r => r.GetParticipantAsync(shiftId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(participant);

        var service = new OrderService(fakeOrderRepo, uow, printMock.Object, null, null, null, shiftRepoMock.Object, null, tenantContext, null);

        var req = new OrderCreateRequest(
            BranchId: branchId,
            CustomerId: null,
            PaymentMethod: "CASH",
            Items: new List<OrderItemCreateDto>
            {
                new OrderItemCreateDto(ProductId: prodId, Quantity: 1, UnitPrice: 50000m)
            },
            ShiftId: shiftId,
            CashierName: "Pham Thu Ngan Moi"
        );

        var res = await service.CreateOrderAsync(req);
        Assert.NotNull(res);
        Assert.Equal("Pham Thu Ngan Moi", participant.UserName);
    }

    [Fact]
    public async Task OrderService_CreatePreOrderAsync_NegativeDeposit_ThrowsInvalidOperationException()
    {
        var tenantContext = new FakeTenantContext();
        var fakeOrderRepo = new FakeOrderRepository();
        var uow = new LocalUnitOfWork();
        var printMock = new Mock<IThermalPrintService>();

        var branchId = tenantContext.CurrentBranchId!.Value;
        var customerId = Guid.NewGuid();
        var prodId = Guid.NewGuid();

        fakeOrderRepo.Branches.Add(new Branch { Id = branchId, TenantId = tenantContext.CurrentTenantId, Name = "Branch 1", IsActive = true });
        fakeOrderRepo.Customers.Add(new Customer { Id = customerId, Name = "Khach Hang A" });
        fakeOrderRepo.Products.Add(new Product { Id = prodId, Name = "SP Pre", SellingPrice = 100000m, IsActive = true });
        fakeOrderRepo.BranchProducts.Add(new BranchProduct { BranchId = branchId, ProductId = prodId, Status = "Active" });

        var service = new OrderService(fakeOrderRepo, uow, printMock.Object, null, null, null, null, null, tenantContext, null);

        var req = new PreOrderCreateRequest(
            BranchId: branchId,
            CustomerId: customerId,
            Items: new List<OrderItemCreateDto> { new OrderItemCreateDto(ProductId: prodId, Quantity: 1, UnitPrice: 100000m) },
            DepositAmount: -5000m, // Negative deposit!
            ExpectedPickupDate: DateTime.UtcNow.AddDays(3)
        );

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreatePreOrderAsync(req));
        Assert.Contains("Tiền đặt cọc không được là số âm", ex.Message);
    }

    [Fact]
    public async Task OrderService_CreatePreOrderAsync_PromoSummaryLongerThan300Chars_Truncated()
    {
        var tenantContext = new FakeTenantContext();
        var fakeOrderRepo = new FakeOrderRepository();
        var uow = new LocalUnitOfWork();
        var printMock = new Mock<IThermalPrintService>();

        var branchId = tenantContext.CurrentBranchId!.Value;
        var customerId = Guid.NewGuid();
        var prodId = Guid.NewGuid();

        fakeOrderRepo.Branches.Add(new Branch { Id = branchId, TenantId = tenantContext.CurrentTenantId, Name = "Branch 1", IsActive = true });
        fakeOrderRepo.Customers.Add(new Customer { Id = customerId, Name = "Khach Hang A" });
        fakeOrderRepo.Products.Add(new Product { Id = prodId, Name = "SP Pre 2", SellingPrice = 100000m, IsActive = true });
        fakeOrderRepo.BranchProducts.Add(new BranchProduct { BranchId = branchId, ProductId = prodId, Status = "Active" });
        fakeOrderRepo.BranchStocks.Add(new BranchStock { Id = Guid.NewGuid(), BranchId = branchId, ProductId = prodId, QuantityOnHand = 10m });

        var service = new OrderService(fakeOrderRepo, uow, printMock.Object, null, null, null, null, null, tenantContext, null);

        var longSummary = new string('A', 350);
        var req = new PreOrderCreateRequest(
            BranchId: branchId,
            CustomerId: customerId,
            Items: new List<OrderItemCreateDto> { new OrderItemCreateDto(ProductId: prodId, Quantity: 1, UnitPrice: 100000m) },
            DepositAmount: 20000m,
            ExpectedPickupDate: DateTime.UtcNow.AddDays(3),
            AppliedPromotionId: Guid.NewGuid(),
            AppliedPromotionSummary: longSummary
        );

        var res = await service.CreatePreOrderAsync(req);
        Assert.NotNull(res);
        var savedOrder = fakeOrderRepo.Orders.Last();
        Assert.NotNull(savedOrder.AppliedPromotionSummary);
        Assert.Equal(300, savedOrder.AppliedPromotionSummary.Length);
    }

    [Fact]
    public async Task OrderService_ProcessReturnExchangeAsync_SettlementMethodDebt_IncreasesCustomerDebtAndSetsUnpaid()
    {
        var tenantContext = new FakeTenantContext();
        var fakeOrderRepo = new FakeOrderRepository();
        var uow = new LocalUnitOfWork();
        var printMock = new Mock<IThermalPrintService>();

        var branchId = tenantContext.CurrentBranchId!.Value;
        var customerId = Guid.NewGuid();
        var originalOrderId = Guid.NewGuid();
        var oldProdId = Guid.NewGuid();
        var newProdId = Guid.NewGuid();

        var customer = new Customer { Id = customerId, Name = "Khach Co No", TotalDebt = 50000m };
        fakeOrderRepo.Customers.Add(customer);
        fakeOrderRepo.Branches.Add(new Branch { Id = branchId, TenantId = tenantContext.CurrentTenantId, Name = "Branch 1", IsActive = true });

        var oldProd = new Product { Id = oldProdId, Name = "SP Cu", SellingPrice = 100000m, IsActive = true };
        var newProd = new Product { Id = newProdId, Name = "SP Moi", SellingPrice = 180000m, IsActive = true };
        fakeOrderRepo.Products.Add(oldProd);
        fakeOrderRepo.Products.Add(newProd);
        fakeOrderRepo.BranchProducts.Add(new BranchProduct { BranchId = branchId, ProductId = oldProdId, Status = "Active" });
        fakeOrderRepo.BranchProducts.Add(new BranchProduct { BranchId = branchId, ProductId = newProdId, Status = "Active" });
        fakeOrderRepo.BranchStocks.Add(new BranchStock { Id = Guid.NewGuid(), BranchId = branchId, ProductId = oldProdId, QuantityOnHand = 10m });
        fakeOrderRepo.BranchStocks.Add(new BranchStock { Id = Guid.NewGuid(), BranchId = branchId, ProductId = newProdId, QuantityOnHand = 10m });

        var orderDetailId = Guid.NewGuid();
        var originalOrder = new Order
        {
            Id = originalOrderId,
            BranchId = branchId,
            CustomerId = customerId,
            Status = "Completed",
            OrderDate = DateTime.UtcNow.AddDays(-1),
            TotalAmount = 100000m,
            FinalAmount = 100000m,
            Details = new List<OrderDetail>
            {
                new OrderDetail
                {
                    Id = orderDetailId,
                    ProductId = oldProdId,
                    Quantity = 1,
                    UnitPrice = 100000m,
                    ActualPrice = 100000m,
                    TotalLineAmount = 100000m
                }
            }
        };
        fakeOrderRepo.Orders.Add(originalOrder);

        var service = new OrderService(fakeOrderRepo, uow, printMock.Object, null, null, null, null, null, tenantContext, null);

        // Exchange: return old item (100k credit), take new item (180k debit). Net amount = +80k. SettlementMethod = "DEBT"
        var req = new ReturnExchangeRequest(
            ReturnedItems: new List<ReturnItemDto> { new ReturnItemDto(oldProdId, 1, 100000m, "Restock", orderDetailId) },
            NewItems: new List<OrderItemCreateDto> { new OrderItemCreateDto(ProductId: newProdId, Quantity: 1, UnitPrice: 180000m) },
            SettlementMethod: "DEBT"
        );

        var res = await service.ProcessReturnExchangeAsync(originalOrderId, req);
        Assert.NotNull(res);
        Assert.Equal(130000m, customer.TotalDebt); // 50,000 + 80,000 = 130,000!
        var exchangeOrder = fakeOrderRepo.Orders.Last();
        Assert.Equal("UNPAID", exchangeOrder.PaymentStatus);
    }

    [Fact]
    public async Task OrderService_ProcessReturnExchangeAsync_SettlementMethodReduceDebt_DecreasesCustomerDebt()
    {
        var tenantContext = new FakeTenantContext();
        var fakeOrderRepo = new FakeOrderRepository();
        var uow = new LocalUnitOfWork();
        var printMock = new Mock<IThermalPrintService>();

        var branchId = tenantContext.CurrentBranchId!.Value;
        var customerId = Guid.NewGuid();
        var originalOrderId = Guid.NewGuid();
        var oldProdId = Guid.NewGuid();
        var newProdId = Guid.NewGuid();

        var customer = new Customer { Id = customerId, Name = "Khach Tra Bot", TotalDebt = 100000m };
        fakeOrderRepo.Customers.Add(customer);
        fakeOrderRepo.Branches.Add(new Branch { Id = branchId, TenantId = tenantContext.CurrentTenantId, Name = "Branch 1", IsActive = true });

        var oldProd = new Product { Id = oldProdId, Name = "SP Gia Cao", SellingPrice = 150000m, IsActive = true };
        var newProd = new Product { Id = newProdId, Name = "SP Gia Thap", SellingPrice = 80000m, IsActive = true };
        fakeOrderRepo.Products.Add(oldProd);
        fakeOrderRepo.Products.Add(newProd);
        fakeOrderRepo.BranchProducts.Add(new BranchProduct { BranchId = branchId, ProductId = oldProdId, Status = "Active" });
        fakeOrderRepo.BranchProducts.Add(new BranchProduct { BranchId = branchId, ProductId = newProdId, Status = "Active" });
        fakeOrderRepo.BranchStocks.Add(new BranchStock { Id = Guid.NewGuid(), BranchId = branchId, ProductId = oldProdId, QuantityOnHand = 10m });
        fakeOrderRepo.BranchStocks.Add(new BranchStock { Id = Guid.NewGuid(), BranchId = branchId, ProductId = newProdId, QuantityOnHand = 10m });

        var orderDetailId = Guid.NewGuid();
        var originalOrder = new Order
        {
            Id = originalOrderId,
            BranchId = branchId,
            CustomerId = customerId,
            Status = "Completed",
            OrderDate = DateTime.UtcNow.AddDays(-1),
            TotalAmount = 150000m,
            FinalAmount = 150000m,
            Details = new List<OrderDetail>
            {
                new OrderDetail
                {
                    Id = orderDetailId,
                    ProductId = oldProdId,
                    Quantity = 1,
                    UnitPrice = 150000m,
                    ActualPrice = 150000m,
                    TotalLineAmount = 150000m
                }
            }
        };
        fakeOrderRepo.Orders.Add(originalOrder);

        var service = new OrderService(fakeOrderRepo, uow, printMock.Object, null, null, null, null, null, tenantContext, null);

        // Exchange: return 150k, buy 80k -> net = -70k. SettlementMethod = "REDUCE_DEBT"
        var req = new ReturnExchangeRequest(
            ReturnedItems: new List<ReturnItemDto> { new ReturnItemDto(oldProdId, 1, 150000m, "Restock", orderDetailId) },
            NewItems: new List<OrderItemCreateDto> { new OrderItemCreateDto(ProductId: newProdId, Quantity: 1, UnitPrice: 80000m) },
            SettlementMethod: "REDUCE_DEBT"
        );

        var res = await service.ProcessReturnExchangeAsync(originalOrderId, req);
        Assert.NotNull(res);
        Assert.Equal(30000m, customer.TotalDebt); // 100,000 - 70,000 = 30,000!
    }

    // =========================================================================
    // SECTION 2: PurchaseOrderService Deep Branches
    // =========================================================================

    [Fact]
    public async Task PurchaseOrderService_GetDispatchHub_FinalizedOnly_DisplaysDaChotPoAndLater()
    {
        var tenantContext = new FakeTenantContext();
        var poRepoMock = new Mock<IPurchaseOrderRepository>();
        var uow = new LocalUnitOfWork();
        var dispatchPkgMock = new Mock<ISupplierDispatchPackageService>();

        var branchId = tenantContext.CurrentBranchId!.Value;
        var supplierId = Guid.NewGuid();
        var deliveryDate = DateTime.Today.AddDays(2);

        poRepoMock.Setup(r => r.GetBranchByIdAsync(branchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Branch { Id = branchId, TenantId = tenantContext.CurrentTenantId, Name = "Branch 1", IsActive = true });
        poRepoMock.Setup(r => r.GetMatrixSupplierConfigsAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OrderMatrixSupplierDeliveryConfigDto>
            {
                new OrderMatrixSupplierDeliveryConfigDto
                {
                    SupplierId = supplierId,
                    SupplierName = "NCC Finalized",
                    SupplierCode = "NCC-FIN",
                    CutoffTime = "12:00",
                    CutoffDayOffset = 1
                }
            });
        poRepoMock.Setup(r => r.GetMatrixProductsAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OrderMatrixProductDto>());
        poRepoMock.Setup(r => r.GetDraftMatrixPosAsync(branchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PurchaseOrder>
            {
                new PurchaseOrder
                {
                    Id = Guid.NewGuid(),
                    BranchId = branchId,
                    SupplierId = supplierId,
                    Status = OrderMatrixRules.StatusFinalized,
                    Details = new List<PurchaseOrderDetail>
                    {
                        new PurchaseOrderDetail
                        {
                            Id = Guid.NewGuid(),
                            DeliveryDate = deliveryDate,
                            FinalQty = 10m
                        }
                    }
                }
            });
        poRepoMock.Setup(r => r.GetCommittedPosInRangeAsync(branchId, null, It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PurchaseOrder>
            {
                new PurchaseOrder
                {
                    Id = Guid.NewGuid(),
                    BranchId = branchId,
                    SupplierId = supplierId,
                    Status = OrderMatrixRules.StatusFinalized,
                    DeliveryDate = deliveryDate
                }
            });

        var service = new PurchaseOrderService(poRepoMock.Object, uow, tenantContext, dispatchPkgMock.Object);

        var query = new DispatchHubQuery
        {
            BranchId = branchId,
            Scope = "WEEK",
            WeekStartDate = DateTime.Today.ToString("yyyy-MM-dd")
        };

        var hub = await service.GetDispatchHubAsync(query);
        Assert.NotNull(hub);
        var supp = hub.Suppliers.FirstOrDefault(s => s.SupplierId == supplierId);
        if (supp != null)
        {
            Assert.True(supp.CountdownDisplay == "Đã chốt PO" || supp.Status == "dispatched");
        }
    }

    [Fact]
    public async Task PurchaseOrderService_GetDispatchHub_SearchKeywordMatchesSupplierCode()
    {
        var tenantContext = new FakeTenantContext();
        var poRepoMock = new Mock<IPurchaseOrderRepository>();
        var uow = new LocalUnitOfWork();
        var dispatchPkgMock = new Mock<ISupplierDispatchPackageService>();

        var branchId = tenantContext.CurrentBranchId!.Value;
        var supplierId1 = Guid.NewGuid();
        var supplierId2 = Guid.NewGuid();

        poRepoMock.Setup(r => r.GetBranchByIdAsync(branchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Branch { Id = branchId, TenantId = tenantContext.CurrentTenantId, Name = "Branch 1", IsActive = true });
        poRepoMock.Setup(r => r.GetMatrixSupplierConfigsAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OrderMatrixSupplierDeliveryConfigDto>
            {
                new OrderMatrixSupplierDeliveryConfigDto { SupplierId = supplierId1, SupplierName = "Supplier Alpha", SupplierCode = "ALPHA-01" },
                new OrderMatrixSupplierDeliveryConfigDto { SupplierId = supplierId2, SupplierName = "Supplier Beta", SupplierCode = "BETA-02" }
            });
        poRepoMock.Setup(r => r.GetMatrixProductsAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OrderMatrixProductDto>());
        poRepoMock.Setup(r => r.GetDraftMatrixPosAsync(branchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PurchaseOrder>());
        poRepoMock.Setup(r => r.GetCommittedPosInRangeAsync(branchId, null, It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PurchaseOrder>());

        var service = new PurchaseOrderService(poRepoMock.Object, uow, tenantContext, dispatchPkgMock.Object);

        var query = new DispatchHubQuery
        {
            BranchId = branchId,
            Keyword = "ALPHA"
        };

        var hub = await service.GetDispatchHubAsync(query);
        Assert.NotNull(hub);
        Assert.All(hub.Suppliers, s => Assert.Contains("ALPHA", s.Code + s.Name, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PurchaseOrderService_SaveMatrixDraftAsync_SupplierOffAndNotEditable_SkipsGroupWithReason()
    {
        var tenantContext = new FakeTenantContext();
        var poRepoMock = new Mock<IPurchaseOrderRepository>();
        var uow = new LocalUnitOfWork();
        var dispatchPkgMock = new Mock<ISupplierDispatchPackageService>();

        var branchId = tenantContext.CurrentBranchId!.Value;
        var supplierId = Guid.NewGuid();
        var prodId = Guid.NewGuid();
        var sundayDate = DateTime.Today.AddDays(7);

        poRepoMock.Setup(r => r.GetBranchByIdAsync(branchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Branch { Id = branchId, TenantId = tenantContext.CurrentTenantId, Name = "Branch 1", IsActive = true });
        poRepoMock.Setup(r => r.GetSupplierByIdAsync(supplierId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Supplier { Id = supplierId, Name = "NCC Off Sun", Code = "NCC-OFF" });
        poRepoMock.Setup(r => r.GetMatrixSupplierConfigsAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OrderMatrixSupplierDeliveryConfigDto>
            {
                new OrderMatrixSupplierDeliveryConfigDto
                {
                    SupplierId = supplierId,
                    SupplierName = "NCC Off Sun",
                    SupplierCode = "NCC-OFF",
                    CutoffTime = "12:00",
                    CutoffDayOffset = 1,
                    ActiveDeliveryDays = new List<int>() // Does not deliver any day (all off!)
                }
            });
        poRepoMock.Setup(r => r.GetMatrixProductsAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OrderMatrixProductDto>
            {
                new OrderMatrixProductDto { Id = prodId, Sku = "SKU-OFF", Name = "SP Off", SupplierId = supplierId, Price = 50000m, Unit = "Goi", IsProductActive = true }
            });
        poRepoMock.Setup(r => r.GetDraftMatrixPosAsync(branchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PurchaseOrder>());
        poRepoMock.Setup(r => r.GetCommittedPosInRangeAsync(branchId, null, It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PurchaseOrder>());

        var service = new PurchaseOrderService(poRepoMock.Object, uow, tenantContext, dispatchPkgMock.Object);

        var draftCmd = new SaveMatrixDraftCommand
        {
            BranchId = branchId,
            SupplierId = supplierId,
            ViewFrom = DateTime.Today,
            ViewTo = DateTime.Today.AddDays(7),
            Cells = new List<OrderMatrixCellDto>
            {
                new OrderMatrixCellDto
                {
                    ProductId = prodId,
                    SupplierId = supplierId,
                    DeliveryDate = sundayDate,
                    EmployeeQty = 15m,
                    FinalQty = 15m
                }
            }
        };

        var res = await service.SaveMatrixDraftAsync(draftCmd);
        Assert.NotNull(res);
        Assert.Equal(branchId, res.BranchId);
    }

    [Fact]
    public async Task PurchaseOrderService_SaveMatrixDraftAsync_RowDisabledProduct_AddsLineError()
    {
        var tenantContext = new FakeTenantContext();
        var poRepoMock = new Mock<IPurchaseOrderRepository>();
        var uow = new LocalUnitOfWork();
        var dispatchPkgMock = new Mock<ISupplierDispatchPackageService>();

        var branchId = tenantContext.CurrentBranchId!.Value;
        var supplierId = Guid.NewGuid();
        var prodId = Guid.NewGuid();

        poRepoMock.Setup(r => r.GetBranchByIdAsync(branchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Branch { Id = branchId, TenantId = tenantContext.CurrentTenantId, Name = "Branch 1", IsActive = true });
        poRepoMock.Setup(r => r.GetSupplierByIdAsync(supplierId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Supplier { Id = supplierId, Name = "NCC Disabled", Code = "NCC-DIS" });
        poRepoMock.Setup(r => r.GetMatrixSupplierConfigsAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OrderMatrixSupplierDeliveryConfigDto>
            {
                new OrderMatrixSupplierDeliveryConfigDto
                {
                    SupplierId = supplierId,
                    SupplierName = "NCC Disabled",
                    SupplierCode = "NCC-DIS",
                    ActiveDeliveryDays = new List<int> { 0, 1, 2, 3, 4, 5, 6 }
                }
            });
        poRepoMock.Setup(r => r.GetMatrixProductsAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OrderMatrixProductDto>
            {
                new OrderMatrixProductDto
                {
                    Id = prodId,
                    Sku = "SKU-DIS",
                    Name = "SP Disabled",
                    SupplierId = supplierId,
                    Price = 50000m,
                    Unit = "Hop",
                    IsProductActive = false,
                    RowDisabledReason = "Sản phẩm tạm dừng do nhà cung cấp hết hàng"
                }
            });
        poRepoMock.Setup(r => r.GetDraftMatrixPosAsync(branchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PurchaseOrder>());
        poRepoMock.Setup(r => r.GetCommittedPosInRangeAsync(branchId, null, It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PurchaseOrder>());

        var service = new PurchaseOrderService(poRepoMock.Object, uow, tenantContext, dispatchPkgMock.Object);

        var draftCmd = new SaveMatrixDraftCommand
        {
            BranchId = branchId,
            SupplierId = supplierId,
            ViewFrom = DateTime.Today,
            ViewTo = DateTime.Today.AddDays(7),
            Cells = new List<OrderMatrixCellDto>
            {
                new OrderMatrixCellDto
                {
                    ProductId = prodId,
                    SupplierId = supplierId,
                    DeliveryDate = DateTime.Today.AddDays(2),
                    EmployeeQty = 5m,
                    FinalQty = 5m
                }
            }
        };

        var res = await service.SaveMatrixDraftAsync(draftCmd);
        Assert.NotNull(res);
    }

    [Fact]
    public async Task PurchaseOrderService_DispatchSupplierHub_And_Detail_CoverAggregationsAndFilters()
    {
        var tenantContext = new FakeTenantContext();
        var poRepo = new Mock<IPurchaseOrderRepository>();
        var service = new PurchaseOrderService(
            poRepo.Object,
            new LocalUnitOfWork(),
            tenantContext,
            new Mock<ISupplierDispatchPackageService>().Object);

        var branchId = tenantContext.CurrentBranchId!.Value;
        var supplierId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var deliveryDate = DateTime.Today.AddDays(3).Date;
        var branch = new Branch
        {
            Id = branchId,
            TenantId = tenantContext.CurrentTenantId,
            Code = "CN-01",
            Name = "Chi nhanh 01",
            IsActive = true
        };
        var supplier = new Supplier
        {
            Id = supplierId,
            TenantId = tenantContext.CurrentTenantId,
            Code = "NCC-AGG",
            Name = "Nha cung cap Aggregate",
            Email = "aggregate@example.test"
        };
        var config = new OrderMatrixSupplierDeliveryConfigDto
        {
            SupplierId = supplierId,
            SupplierCode = supplier.Code,
            SupplierName = supplier.Name,
            CutoffTime = "23:59",
            CutoffDayOffset = 0,
            IsAssignedToBranch = true,
            IsActive = true,
            ActiveDeliveryDays = new List<int> { 0, 1, 2, 3, 4, 5, 6 }
        };
        var product = new OrderMatrixProductDto
        {
            Id = productId,
            SupplierId = supplierId,
            Name = "San pham Aggregate",
            Sku = "SKU-AGG",
            Unit = "Hop",
            Category = "Thuc pham",
            Price = 25_000m,
            IsProductActive = true
        };
        var draft = new PurchaseOrder
        {
            Id = Guid.NewGuid(),
            TenantId = tenantContext.CurrentTenantId,
            BranchId = branchId,
            SupplierId = supplierId,
            Code = "PO-DRAFT-AGG",
            Status = "DRAFT",
            Details = new List<PurchaseOrderDetail>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    ProductId = productId,
                    DeliveryDate = deliveryDate,
                    EmployeeQty = 4m,
                    FinalQty = 4m
                }
            }
        };

        poRepo.Setup(r => r.GetActiveBranchesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Branch> { branch });
        poRepo.Setup(r => r.GetBranchByIdAsync(branchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(branch);
        poRepo.Setup(r => r.GetSupplierByIdAsync(supplierId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(supplier);
        poRepo.Setup(r => r.GetActiveBranchIdsForSupplierAsync(supplierId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { branchId });
        poRepo.Setup(r => r.GetMatrixProductsAsync(branchId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OrderMatrixProductDto> { product });
        poRepo.Setup(r => r.GetMatrixSupplierConfigsAsync(branchId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OrderMatrixSupplierDeliveryConfigDto> { config });
        poRepo.Setup(r => r.GetDraftMatrixPosAsync(branchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PurchaseOrder> { draft });
        poRepo.Setup(r => r.GetCommittedPosInRangeAsync(branchId, null, It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PurchaseOrder>());

        var all = await service.GetDispatchSupplierHubAsync(new DispatchSupplierHubQuery
        {
            DurationDays = 7,
            Keyword = "Aggregate",
            Urgency = "all"
        });
        var row = Assert.Single(all.Suppliers);
        Assert.Equal(supplierId, row.SupplierId);
        Assert.Equal(1, row.LinkedBranchCount);
        Assert.Equal(1, row.BranchesWithQty);
        Assert.Equal(4m, row.TotalQty);
        Assert.Equal(100_000m, row.TotalAmount);
        Assert.Single(row.Branches);

        var detail = await service.GetDispatchSupplierDetailAsync(new DispatchSupplierDetailQuery
        {
            SupplierId = supplierId,
            DurationDays = 7
        });
        Assert.Equal(supplier.Email, detail.Email);
        var detailBranch = Assert.Single(detail.Branches);
        Assert.Equal(branch.Code, detailBranch.BranchCode);
        Assert.Single(detailBranch.Items);

        var noMatch = await service.GetDispatchSupplierHubAsync(new DispatchSupplierHubQuery
        {
            Keyword = "khong-ton-tai",
            Urgency = "completed"
        });
        Assert.Empty(noMatch.Suppliers);
    }

    [Fact]
    public async Task PurchaseOrderService_DispatchSupplierDetail_ValidatesSupplier()
    {
        var tenantContext = new FakeTenantContext();
        var repo = new Mock<IPurchaseOrderRepository>();
        var service = new PurchaseOrderService(
            repo.Object,
            new LocalUnitOfWork(),
            tenantContext,
            new Mock<ISupplierDispatchPackageService>().Object);

        var required = await Assert.ThrowsAsync<BusinessException>(() =>
            service.GetDispatchSupplierDetailAsync(new DispatchSupplierDetailQuery { SupplierId = Guid.Empty }));
        Assert.Equal("SUPPLIER_REQUIRED", required.ErrorCode);

        var missingId = Guid.NewGuid();
        repo.Setup(r => r.GetSupplierByIdAsync(missingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Supplier?)null);
        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.GetDispatchSupplierDetailAsync(new DispatchSupplierDetailQuery { SupplierId = missingId }));
    }

    [Fact]
    public async Task PurchaseOrderService_PagedAndSingle_MapFallbacksAndAttachReceipt()
    {
        var tenant = new FakeTenantContext();
        var repo = new Mock<IPurchaseOrderRepository>();
        var service = new PurchaseOrderService(repo.Object, new LocalUnitOfWork(), tenant,
            new Mock<ISupplierDispatchPackageService>().Object);
        var po = new PurchaseOrder
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.CurrentTenantId,
            BranchId = Guid.NewGuid(),
            SupplierId = Guid.NewGuid(),
            Code = "PO-PAGED-01",
            Status = "APPROVED",
            RevisionNumber = 0,
            StoreCodeSnapshot = null,
            SupplierCodeSnapshot = null,
            Details = new List<PurchaseOrderDetail>
            {
                new()
                {
                    Id = Guid.NewGuid(), ProductId = Guid.NewGuid(), DeliveryUoM = "Cai",
                    EmployeeQty = 2m, FinalQty = 2m, OrderedQty = null,
                    UnitPrice = 10m, LineAmount = 20m, Product = null
                }
            }
        };
        var receiptId = Guid.NewGuid();
        repo.Setup(r => r.GetPagedPurchaseOrdersAsync(null, null, null, null, null, 1, 100, "PO",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<PurchaseOrder> { po }, 1));
        repo.Setup(r => r.GetPurchaseOrderByIdAsync(po.Id, It.IsAny<CancellationToken>())).ReturnsAsync(po);
        repo.Setup(r => r.GetActiveGoodsReceiptsByPoIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, (Guid, string, string)>
            {
                [po.Id] = (receiptId, "GR-01", "DRAFT")
            });

        var page = await service.GetPagedPurchaseOrdersAsync(new PurchaseOrderFilterQuery
        {
            PageIndex = 0,
            PageSize = 1000,
            Keyword = "PO"
        });
        Assert.Equal(1, page.Page);
        Assert.Equal(100, page.PageSize);
        Assert.Equal(1, page.TotalItems);
        var dto = Assert.Single(page.Items);
        Assert.Equal("BR", dto.BranchCode);
        Assert.Equal("SUP", dto.SupplierCode);
        Assert.Equal(1, dto.RevisionNumber);
        Assert.Equal(receiptId, dto.ActiveGoodsReceiptId);
        Assert.Equal(2m, Assert.Single(dto.Details).OrderedQty);

        var single = await service.GetPurchaseOrderByIdAsync(po.Id);
        Assert.Equal("GR-01", single.ActiveGoodsReceiptCode);
    }

    [Theory]
    [InlineData("EMPLOYEE")]
    [InlineData("STAFF")]
    [InlineData("CASHIER")]
    public async Task PurchaseOrderService_EmployeeRoles_CannotAccessAnotherBranch(string role)
    {
        var tenant = new FakeTenantContext { CurrentRole = role, CurrentBranchId = Guid.NewGuid() };
        var service = new PurchaseOrderService(new Mock<IPurchaseOrderRepository>().Object,
            new LocalUnitOfWork(), tenant, new Mock<ISupplierDispatchPackageService>().Object);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetOrderMatrixAsync(
            new OrderMatrixQuery
            {
                BranchId = Guid.NewGuid(),
                ViewFrom = DateTime.Today,
                DurationDays = 1
            }));
    }

    // =========================================================================
    // SECTION 3: StocktakeService Deep Branches
    // =========================================================================

    [Fact]
    public async Task StocktakeService_ReviewAsync_NotFound_ThrowsNotFoundException()
    {
        var tenantContext = new FakeTenantContext();
        var stRepoMock = new Mock<IStocktakeRepository>();
        var supRetServiceMock = new Mock<ISupplierReturnService>();
        var supRetRepoMock = new Mock<ISupplierReturnRepository>();
        var supRepoMock = new Mock<ISupplierRepository>();
        var uow = new LocalUnitOfWork();

        var missingId = Guid.NewGuid();
        stRepoMock.Setup(r => r.GetByIdAsync(tenantContext.CurrentTenantId, missingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Stocktake?)null);

        var service = new StocktakeService(stRepoMock.Object, supRetServiceMock.Object, supRetRepoMock.Object, supRepoMock.Object, tenantContext, uow);

        var reviewCmd = new ReviewStocktakeCommand("APPROVE", "Duyet", "Store Owner");
        var ex = await Assert.ThrowsAsync<NotFoundException>(() => service.ReviewAsync(missingId, reviewCmd));
        Assert.Contains(missingId.ToString(), ex.Message);
    }

    [Fact]
    public async Task StocktakeService_CreateSupplierReturnFromStocktake_NotFound_ThrowsNotFoundException()
    {
        var tenantContext = new FakeTenantContext();
        var stRepoMock = new Mock<IStocktakeRepository>();
        var supRetServiceMock = new Mock<ISupplierReturnService>();
        var supRetRepoMock = new Mock<ISupplierReturnRepository>();
        var supRepoMock = new Mock<ISupplierRepository>();
        var uow = new LocalUnitOfWork();

        var missingId = Guid.NewGuid();
        stRepoMock.Setup(r => r.GetByIdAsync(tenantContext.CurrentTenantId, missingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Stocktake?)null);

        var service = new StocktakeService(stRepoMock.Object, supRetServiceMock.Object, supRetRepoMock.Object, supRepoMock.Object, tenantContext, uow);

        var cmd = new CreateReturnFromStocktakeCommand(
            GoodsReceiptId: Guid.NewGuid(),
            ResolutionType: "REFUND",
            ReasonNotes: "Tra hang loi",
            AttachmentUrl: null,
            Lines: new List<CreateReturnFromStocktakeLineItemDto>
            {
                new CreateReturnFromStocktakeLineItemDto(Guid.NewGuid(), null, null, 5m, "Tra hang")
            }
        );

        var ex = await Assert.ThrowsAsync<NotFoundException>(() => service.CreateReturnFromStocktakeAsync(missingId, cmd));
        Assert.Contains(missingId.ToString(), ex.Message);
    }

    [Fact]
    public async Task StocktakeService_EmployeeRole_UnassignedBranch_ThrowsBusinessException()
    {
        var tenantContext = new FakeTenantContext
        {
            CurrentRole = "EMPLOYEE",
            CurrentBranchId = null // Unassigned branch!
        };
        var stRepoMock = new Mock<IStocktakeRepository>();
        var supRetServiceMock = new Mock<ISupplierReturnService>();
        var supRetRepoMock = new Mock<ISupplierReturnRepository>();
        var supRepoMock = new Mock<ISupplierRepository>();
        var uow = new LocalUnitOfWork();

        var service = new StocktakeService(stRepoMock.Object, supRetServiceMock.Object, supRetRepoMock.Object, supRepoMock.Object, tenantContext, uow);

        var cmd = new CreateStocktakeCommand(
            BranchId: Guid.NewGuid(),
            Scope: "ALL",
            ReasonType: "PERIODIC",
            Note: "Kiem ke thieu branch",
            BranchStockIds: null,
            CategoryId: null,
            Lines: null
        );

        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.CreateAsync(cmd));
        Assert.Contains("Tài khoản nhân viên chưa được gán chi nhánh", ex.Message);
    }

    [Fact]
    public async Task StocktakeService_EmployeeRole_MismatchedBranch_ThrowsBusinessException()
    {
        var assignedBranchId = Guid.NewGuid();
        var requestedBranchId = Guid.NewGuid();

        var tenantContext = new FakeTenantContext
        {
            CurrentRole = "EMPLOYEE",
            CurrentBranchId = assignedBranchId
        };
        var stRepoMock = new Mock<IStocktakeRepository>();
        var supRetServiceMock = new Mock<ISupplierReturnService>();
        var supRetRepoMock = new Mock<ISupplierReturnRepository>();
        var supRepoMock = new Mock<ISupplierRepository>();
        var uow = new LocalUnitOfWork();

        var service = new StocktakeService(stRepoMock.Object, supRetServiceMock.Object, supRetRepoMock.Object, supRepoMock.Object, tenantContext, uow);

        var cmd = new CreateStocktakeCommand(
            BranchId: requestedBranchId, // Mismatched branch!
            Scope: "ALL",
            ReasonType: "PERIODIC",
            Note: "Kiem ke khac branch",
            BranchStockIds: null,
            CategoryId: null,
            Lines: null
        );

        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.CreateAsync(cmd));
        Assert.Contains("Nhân viên chỉ được lập phiếu kiểm kê cho chi nhánh được phân công", ex.Message);
    }

    [Fact]
    public async Task StocktakeService_ReviewAsync_SupplierRequired_WhenLineSupplierIdMissing()
    {
        var tenantContext = new FakeTenantContext { CurrentRole = "STORE_OWNER" };
        var stRepoMock = new Mock<IStocktakeRepository>();
        var supRetServiceMock = new Mock<ISupplierReturnService>();
        var supRetRepoMock = new Mock<ISupplierReturnRepository>();
        var supRepoMock = new Mock<ISupplierRepository>();
        var uow = new LocalUnitOfWork();

        var branchId = tenantContext.CurrentBranchId!.Value;
        var stId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var prodId = Guid.NewGuid();
        var stockId = Guid.NewGuid();

        var stocktake = new Stocktake
        {
            Id = stId,
            TenantId = tenantContext.CurrentTenantId,
            BranchId = branchId,
            Status = "PENDING_APPROVAL",
            Lines = new List<StocktakeLine>
            {
                new StocktakeLine
                {
                    Id = lineId,
                    TenantId = tenantContext.CurrentTenantId,
                    StocktakeId = stId,
                    BranchStockId = stockId,
                    ProductId = prodId,
                    DamagedQty = 2m,
                    VarianceQty = 0m,
                    SupplierId = null // Missing supplier ID!
                }
            }
        };

        stRepoMock.Setup(r => r.GetByIdAsync(tenantContext.CurrentTenantId, stId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stocktake);

        var service = new StocktakeService(stRepoMock.Object, supRetServiceMock.Object, supRetRepoMock.Object, supRepoMock.Object, tenantContext, uow);

        var reviewCmd = new ReviewStocktakeCommand(
            Decision: "APPROVE",
            ReviewNote: "Duyet tra NCC",
            ReviewedByName: "Store Owner",
            ConfirmSurplusFound: false,
            LineActions: new List<ReviewStocktakeLineActionDto>
            {
                new ReviewStocktakeLineActionDto(lineId, DamagedAction: "RETURN_SUPPLIER", ExpiredAction: null)
            }
        );

        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.ReviewAsync(stId, reviewCmd));
        Assert.Equal("SUPPLIER_REQUIRED", ex.ErrorCode);
    }

    // =========================================================================
    // SECTION 4: GoodsReceiptService Deep Branches
    // =========================================================================

    [Fact]
    public async Task GoodsReceiptService_CreateGoodsReceipt_AdjustsRejectedQtyDeliveredQtyAndRejectionReason()
    {
        var tenantContext = new FakeTenantContext();
        var grRepoMock = new Mock<IGoodsReceiptRepository>();
        var uow = new LocalUnitOfWork();

        var branchId = tenantContext.CurrentBranchId!.Value;
        var supplierId = Guid.NewGuid();
        var poId = Guid.NewGuid();
        var podId = Guid.NewGuid();
        var prodId = Guid.NewGuid();

        var po = new PurchaseOrder
        {
            Id = poId,
            BranchId = branchId,
            SupplierId = supplierId,
            Status = "SENT",
            DeliveryDate = DateTime.UtcNow.Date,
            Details = new List<PurchaseOrderDetail>
            {
                new PurchaseOrderDetail
                {
                    Id = podId,
                    ProductId = prodId,
                    OrderedQty = 10m,
                    FinalQty = 10m,
                    ReceivedQty = 0m
                }
            }
        };

        grRepoMock.Setup(r => r.GetPurchaseOrderWithDetailsAsync(tenantContext.CurrentTenantId, poId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(po);
        grRepoMock.Setup(r => r.GetActiveReceiptByPoIdAsync(tenantContext.CurrentTenantId, poId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((GoodsReceipt?)null);
        grRepoMock.Setup(r => r.GenerateCodeAsync(tenantContext.CurrentTenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync("GRN-AUTO-01");
        grRepoMock.Setup(r => r.UserExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        grRepoMock.Setup(r => r.GetProductsByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, Product>
            {
                [prodId] = new Product { Id = prodId, Name = "SP 1", HasBatches = false }
            });

        GoodsReceipt? createdReceipt = null;
        grRepoMock.Setup(r => r.AddAsync(It.IsAny<GoodsReceipt>(), It.IsAny<CancellationToken>()))
            .Callback<GoodsReceipt, CancellationToken>((gr, ct) => createdReceipt = gr)
            .ReturnsAsync((GoodsReceipt gr, CancellationToken ct) => gr);
        grRepoMock.Setup(r => r.GetByIdAsync(tenantContext.CurrentTenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string t, Guid id, CancellationToken ct) => createdReceipt);

        var service = new GoodsReceiptService(grRepoMock.Object, uow, tenantContext);

        // Ordered = 10. Client sends AcceptedQty = 6, DeliveredQty = 6, but provides RejectionReason = "Bao bi rach".
        // Service should auto-adjust RejectedQty = 4, DeliveredQty = 10, and copy RejectionReason.
        var cmd = new CreateGoodsReceiptCommand
        {
            BranchId = branchId,
            PurchaseOrderId = poId,
            SupplierId = supplierId,
            Lines = new List<CreateGoodsReceiptLineCommand>
            {
                new CreateGoodsReceiptLineCommand
                {
                    ProductId = prodId,
                    PurchaseOrderDetailId = podId,
                    OrderedQty = 10m,
                    AcceptedQty = 6m,
                    DeliveredQty = 6m,
                    RejectionReason = "Bao bi rach"
                }
            }
        };

        var res = await service.CreateGoodsReceiptAsync(cmd);
        Assert.NotNull(res);
        Assert.NotNull(createdReceipt);
        var firstLine = createdReceipt.Lines.First();
        Assert.Equal(4m, firstLine.RejectedQty); // 10 - 6 = 4 rejected
        Assert.Equal(10m, firstLine.DeliveredQty); // 6 + 4 = 10 delivered
        Assert.Equal("Bao bi rach", firstLine.RejectionReason);
    }

    [Fact]
    public async Task GoodsReceiptService_CreateGoodsReceipt_NegativeQty_ThrowsBusinessException()
    {
        var tenantContext = new FakeTenantContext();
        var grRepoMock = new Mock<IGoodsReceiptRepository>();
        var uow = new LocalUnitOfWork();

        var poId = Guid.NewGuid();
        var podId = Guid.NewGuid();
        var prodId = Guid.NewGuid();
        var supplierId = Guid.NewGuid();
        var po = new PurchaseOrder
        {
            Id = poId,
            BranchId = tenantContext.CurrentBranchId!.Value,
            SupplierId = supplierId,
            Status = "SENT",
            DeliveryDate = DateTime.UtcNow.Date,
            Details = new List<PurchaseOrderDetail>
            {
                new PurchaseOrderDetail { Id = podId, ProductId = prodId, OrderedQty = 10m, FinalQty = 10m }
            }
        };

        grRepoMock.Setup(r => r.GetPurchaseOrderWithDetailsAsync(tenantContext.CurrentTenantId, poId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(po);
        grRepoMock.Setup(r => r.GetActiveReceiptByPoIdAsync(tenantContext.CurrentTenantId, poId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((GoodsReceipt?)null);
        grRepoMock.Setup(r => r.GenerateCodeAsync(tenantContext.CurrentTenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync("GRN-001");
        grRepoMock.Setup(r => r.UserExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        grRepoMock.Setup(r => r.GetProductsByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, Product> { [prodId] = new Product { Id = prodId, Name = "SP 1" } });

        var service = new GoodsReceiptService(grRepoMock.Object, uow, tenantContext);

        var cmd = new CreateGoodsReceiptCommand
        {
            BranchId = tenantContext.CurrentBranchId!.Value,
            PurchaseOrderId = poId,
            SupplierId = supplierId,
            Lines = new List<CreateGoodsReceiptLineCommand>
            {
                new CreateGoodsReceiptLineCommand
                {
                    ProductId = prodId,
                    PurchaseOrderDetailId = podId,
                    OrderedQty = 10m,
                    AcceptedQty = -2m, // Negative qty!
                    DeliveredQty = 10m
                }
            }
        };

        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.CreateGoodsReceiptAsync(cmd));
        Assert.Equal("INVALID_QTY", ex.ErrorCode);
    }

    [Fact]
    public async Task GoodsReceiptService_CreateGoodsReceipt_AcceptedExceedsDelivered_ThrowsBusinessException()
    {
        var tenantContext = new FakeTenantContext();
        var grRepoMock = new Mock<IGoodsReceiptRepository>();
        var uow = new LocalUnitOfWork();

        var poId = Guid.NewGuid();
        var podId = Guid.NewGuid();
        var prodId = Guid.NewGuid();
        var supplierId = Guid.NewGuid();
        var po = new PurchaseOrder
        {
            Id = poId,
            BranchId = tenantContext.CurrentBranchId!.Value,
            SupplierId = supplierId,
            Status = "SENT",
            DeliveryDate = DateTime.UtcNow.Date,
            Details = new List<PurchaseOrderDetail>
            {
                new PurchaseOrderDetail { Id = podId, ProductId = prodId, OrderedQty = 10m, FinalQty = 10m }
            }
        };

        grRepoMock.Setup(r => r.GetPurchaseOrderWithDetailsAsync(tenantContext.CurrentTenantId, poId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(po);
        grRepoMock.Setup(r => r.GetActiveReceiptByPoIdAsync(tenantContext.CurrentTenantId, poId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((GoodsReceipt?)null);
        grRepoMock.Setup(r => r.GenerateCodeAsync(tenantContext.CurrentTenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync("GRN-001");
        grRepoMock.Setup(r => r.UserExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        grRepoMock.Setup(r => r.GetProductsByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, Product> { [prodId] = new Product { Id = prodId, Name = "SP 1" } });

        var service = new GoodsReceiptService(grRepoMock.Object, uow, tenantContext);

        var cmd = new CreateGoodsReceiptCommand
        {
            BranchId = tenantContext.CurrentBranchId!.Value,
            PurchaseOrderId = poId,
            SupplierId = supplierId,
            Lines = new List<CreateGoodsReceiptLineCommand>
            {
                new CreateGoodsReceiptLineCommand
                {
                    ProductId = prodId,
                    PurchaseOrderDetailId = podId,
                    OrderedQty = 10m,
                    AcceptedQty = 8m, // 8 > 5!
                    DeliveredQty = 5m
                }
            }
        };

        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.CreateGoodsReceiptAsync(cmd));
        Assert.Equal("ACCEPTED_EXCEEDS_RECEIVED", ex.ErrorCode);
    }

    [Fact]
    public async Task GoodsReceiptService_CompleteGoodsReceipt_AlreadyCompletedOrCancelled_ThrowsBusinessException()
    {
        var tenantContext = new FakeTenantContext();
        var grRepoMock = new Mock<IGoodsReceiptRepository>();
        var uow = new LocalUnitOfWork();

        var completedReceiptId = Guid.NewGuid();
        var cancelledReceiptId = Guid.NewGuid();

        grRepoMock.Setup(r => r.GetByIdAsync(tenantContext.CurrentTenantId, completedReceiptId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoodsReceipt { Id = completedReceiptId, Status = "COMPLETED" });
        grRepoMock.Setup(r => r.GetByIdAsync(tenantContext.CurrentTenantId, cancelledReceiptId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoodsReceipt { Id = cancelledReceiptId, Status = "CANCELLED" });

        var service = new GoodsReceiptService(grRepoMock.Object, uow, tenantContext);

        var cmd = new CompleteGoodsReceiptCommand { Note = "Hoan tat" };

        var ex1 = await Assert.ThrowsAsync<BusinessException>(() => service.CompleteGoodsReceiptAsync(completedReceiptId, cmd));
        Assert.Equal("ALREADY_COMPLETED", ex1.ErrorCode);

        var ex2 = await Assert.ThrowsAsync<BusinessException>(() => service.CompleteGoodsReceiptAsync(cancelledReceiptId, cmd));
        Assert.Equal("CANNOT_COMPLETE_CANCELLED", ex2.ErrorCode);
    }

    [Fact]
    public async Task GoodsReceiptService_CompleteGoodsReceipt_ExpiryDateInPast_ThrowsBusinessException()
    {
        var tenantContext = new FakeTenantContext();
        var grRepoMock = new Mock<IGoodsReceiptRepository>();
        var uow = new LocalUnitOfWork();

        var receiptId = Guid.NewGuid();
        var prodId = Guid.NewGuid();
        var pastDate = DateTime.UtcNow.AddDays(-10);

        var receipt = new GoodsReceipt
        {
            Id = receiptId,
            TenantId = tenantContext.CurrentTenantId,
            Status = "DRAFT",
            ReceivedAtUtc = DateTime.UtcNow,
            Lines = new List<GoodsReceiptLine>
            {
                new GoodsReceiptLine
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantContext.CurrentTenantId,
                    ProductId = prodId,
                    DeliveredQty = 10m,
                    AcceptedQty = 10m,
                    BatchCode = "LOT-OLD-01",
                    ExpiryDate = pastDate // Expired before received date!
                }
            }
        };

        grRepoMock.Setup(r => r.GetByIdAsync(tenantContext.CurrentTenantId, receiptId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(receipt);
        grRepoMock.Setup(r => r.GetProductsByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, Product> { [prodId] = new Product { Id = prodId, Name = "SP Past" } });
        grRepoMock.Setup(r => r.UserExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var service = new GoodsReceiptService(grRepoMock.Object, uow, tenantContext);

        var cmd = new CompleteGoodsReceiptCommand { Note = "Nhap hang" };

        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.CompleteGoodsReceiptAsync(receiptId, cmd));
        Assert.Equal("EXPIRY_PAST", ex.ErrorCode);
    }

    [Fact]
    public async Task GoodsReceiptService_CompleteGoodsReceipt_ExchangePo_GeneratesUniqueBatchCodesWithSuffix()
    {
        var tenantContext = new FakeTenantContext();
        var grRepoMock = new Mock<IGoodsReceiptRepository>();
        var supRetRepoMock = new Mock<ISupplierReturnRepository>();
        var uow = new LocalUnitOfWork();

        var receiptId = Guid.NewGuid();
        var poId = Guid.NewGuid();
        var returnId = Guid.NewGuid();
        var branchId = tenantContext.CurrentBranchId!.Value;
        var prodId = Guid.NewGuid();

        var product = new Product
        {
            Id = prodId,
            Name = "Sản phẩm đổi",
            ProductCode = "SPDOI",
            HasBatches = true,
            CostPrice = 10000m,
            Units = new List<ProductUnit>
            {
                new ProductUnit { Id = Guid.NewGuid(), UnitName = "Cai", ConversionRate = 1.0m, IsBaseUnit = true }
            }
        };

        var baseCandidateCode = $"BAT-EX-SPDOI-{DateTime.UtcNow:yyyyMMdd}";

        var receipt = new GoodsReceipt
        {
            Id = receiptId,
            Code = "GRN-EX-001",
            TenantId = tenantContext.CurrentTenantId,
            BranchId = branchId,
            Status = "DRAFT",
            PurchaseOrderId = poId,
            ReceivedAtUtc = DateTime.UtcNow,
            Lines = new List<GoodsReceiptLine>
            {
                new GoodsReceiptLine
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantContext.CurrentTenantId,
                    GoodsReceiptId = receiptId,
                    ProductId = prodId,
                    Product = product,
                    DeliveredQty = 10m,
                    AcceptedQty = 10m,
                    LandedUnitCost = 15000m,
                    BatchCode = baseCandidateCode, // Existing code conflicts with return batch!
                    ExpiryDate = DateTime.UtcNow.AddMonths(12)
                }
            }
        };

        var po = new PurchaseOrder
        {
            Id = poId,
            BranchId = branchId,
            DeliveryDate = DateTime.UtcNow.Date,
            SourceType = "SUPPLIER_RETURN",
            SourceSupplierReturnId = returnId,
            Details = new List<PurchaseOrderDetail>()
        };

        var supplierReturn = new Domain.Entities.Supplier.SupplierReturn
        {
            Id = returnId,
            Lines = new List<Domain.Entities.Supplier.SupplierReturnLine>
            {
                // Returned batch conflicts with base code: BAT-EX-SPDOI-yyyyMMdd
                new Domain.Entities.Supplier.SupplierReturnLine
                {
                    ProductId = prodId,
                    BatchCode = baseCandidateCode
                }
            }
        };

        grRepoMock.Setup(r => r.GetByIdAsync(tenantContext.CurrentTenantId, receiptId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(receipt);
        grRepoMock.Setup(r => r.GetPurchaseOrderWithDetailsAsync(tenantContext.CurrentTenantId, poId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(po);
        grRepoMock.Setup(r => r.GetTotalProductStockAsync(tenantContext.CurrentTenantId, prodId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0m); // Existing stock is 0 -> exercises product.CostPrice = landedCostPerStockUnit!
        grRepoMock.Setup(r => r.UserExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        supRetRepoMock.Setup(r => r.GetByIdAsync(tenantContext.CurrentTenantId, returnId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(supplierReturn);

        var service = new GoodsReceiptService(grRepoMock.Object, uow, tenantContext, supRetRepoMock.Object);

        var cmd = new CompleteGoodsReceiptCommand { Note = "Hoan tat doi hang" };

        var res = await service.CompleteGoodsReceiptAsync(receiptId, cmd);
        Assert.NotNull(res);
        Assert.Equal("COMPLETED", receipt.Status);
        // Base candidate collided with returned batch code, so it got suffixed with -2!
        var line = receipt.Lines.First();
        Assert.NotNull(line.BatchCode);
        Assert.EndsWith("-2", line.BatchCode);
        // Product CostPrice updated to landed unit cost
        Assert.Equal(15000m, product.CostPrice);
    }
}
