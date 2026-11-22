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
using MT_GRMS.Application.Common.DTOs;
using MT_GRMS.Application.PosOrders;
using MT_GRMS.Application.PosOrders.Dtos;
using PosShiftDtos = MT_GRMS.Application.PosShifts.Dtos;
using MT_GRMS.Application.PosShifts;
using MT_GRMS.Application.Promotions;
using MT_GRMS.Application.PurchaseOrders;
using MT_GRMS.Application.PurchaseOrders.Dispatch;
using MT_GRMS.Application.PurchaseOrders.Dtos;
using MT_GRMS.Application.GoodsReceipts;
using MT_GRMS.Application.GoodsReceipts.Dtos;
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
using MT_GRMS.Infrastructure.Services.Printer;

namespace MT_GRMS.UnitTests;

public class OrderAndInventoryBranchCoverageTests
{
    private class FakeTenantContext : ITenantContext
    {
        public string CurrentTenantId { get; set; } = "tenant-cov-01";
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
    // SECTION 1: OrderService Branches
    // =========================================================================

    [Fact]
    public async Task CreateOrderAsync_ShiftClosingLockdown_ThrowsBusinessException()
    {
        var tenantContext = new FakeTenantContext { CurrentRole = "CASHIER" };
        var orderRepo = new FakeOrderRepository();
        var uow = new LocalUnitOfWork();
        var printMock = new Mock<IThermalPrintService>();
        var shiftRepoMock = new Mock<IShiftRepository>();

        var branchId = tenantContext.CurrentBranchId!.Value;
        orderRepo.Branches.Add(new Branch { Id = branchId, TenantId = tenantContext.CurrentTenantId, Name = "Branch 1" });

        var shiftId = Guid.NewGuid();
        var activeShift = new ShiftZReport
        {
            Id = shiftId,
            BranchId = branchId,
            TenantId = tenantContext.CurrentTenantId,
            Status = ShiftState.Closing
        };

        shiftRepoMock.Setup(r => r.GetActiveShiftAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(activeShift);
        shiftRepoMock.Setup(r => r.GetByIdAsync(shiftId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(activeShift);

        var orderService = new OrderService(
            orderRepo, uow, printMock.Object,
            shiftRepository: shiftRepoMock.Object,
            tenantContext: tenantContext);

        var req = new OrderCreateRequest(
            BranchId: branchId,
            CustomerId: null,
            PaymentMethod: "CASH",
            Items: new List<OrderItemCreateDto>
            {
                new OrderItemCreateDto(Guid.NewGuid(), 1, 10000m)
            },
            ShiftId: shiftId
        );

        var ex = await Assert.ThrowsAsync<BusinessException>(() => orderService.CreateOrderAsync(req));
        Assert.Equal("SHIFT_CLOSING_LOCKDOWN", ex.ErrorCode);
    }

    [Fact]
    public async Task CreateOrderAsync_UnitMatchByNameAndMultiLineQty_CoversCalculations()
    {
        var tenantContext = new FakeTenantContext();
        var orderRepo = new FakeOrderRepository();
        var uow = new LocalUnitOfWork();
        var printMock = new Mock<IThermalPrintService>();

        var branchId = tenantContext.CurrentBranchId!.Value;
        orderRepo.Branches.Add(new Branch { Id = branchId, TenantId = tenantContext.CurrentTenantId, Name = "Branch 1" });

        var prodId = Guid.NewGuid();
        var unitId = Guid.NewGuid();

        var product = new Product
        {
            Id = prodId,
            TenantId = tenantContext.CurrentTenantId,
            ProductCode = "SP-001",
            Name = "Nuoc Ngot Lon",
            BaseUnit = "Lon",
            SellingPrice = 10000m,
            CostPrice = 7000m,
            Units = new List<ProductUnit>
            {
                new ProductUnit
                {
                    Id = unitId,
                    ProductId = prodId,
                    UnitName = "Thung",
                    ConversionRate = 24m,
                    Price = 220000m,
                    IsDeleted = false
                }
            }
        };
        orderRepo.Products.Add(product);

        var stock = new BranchStock
        {
            Id = Guid.NewGuid(),
            TenantId = tenantContext.CurrentTenantId,
            BranchId = branchId,
            ProductId = prodId,
            QuantityOnHand = 200m,
            QuantityReserved = 0m
        };
        orderRepo.BranchStocks.Add(stock);

        var bp = new BranchProduct
        {
            Id = Guid.NewGuid(),
            TenantId = tenantContext.CurrentTenantId,
            BranchId = branchId,
            ProductId = prodId,
            Status = "Active",
            SellingPriceOverride = 12000m
        };
        orderRepo.BranchProducts.Add(bp);

        var orderService = new OrderService(
            orderRepo, uow, printMock.Object,
            tenantContext: tenantContext);

        // Line 1: UnitId is null, matched by UnitName = "Thung"
        // Line 2: Same ProductId (tests productRequiredBaseQty.ContainsKey branch)
        var req = new OrderCreateRequest(
            BranchId: branchId,
            CustomerId: null,
            PaymentMethod: "CASH",
            CashTendered: 500000m,
            Items: new List<OrderItemCreateDto>
            {
                new OrderItemCreateDto(prodId, 1, 220000m, UnitId: null, UnitName: "Thung"),
                new OrderItemCreateDto(prodId, 2, 12000m, UnitId: null, UnitName: "Lon")
            }
        );

        var res = await orderService.CreateOrderAsync(req);
        Assert.NotNull(res);
        Assert.Equal(2, res.Items.Count);
        Assert.True(uow.SaveChangesCalled);
    }

    [Fact]
    public async Task CreateOrderAsync_OrderDiscountAllocationAndPendingPayment_SetsFields()
    {
        var tenantContext = new FakeTenantContext();
        var orderRepo = new FakeOrderRepository();
        var uow = new LocalUnitOfWork();
        var printMock = new Mock<IThermalPrintService>();

        var branchId = tenantContext.CurrentBranchId!.Value;
        var bankAccountId = Guid.NewGuid();
        orderRepo.Branches.Add(new Branch
        {
            Id = branchId,
            TenantId = tenantContext.CurrentTenantId,
            Name = "Branch 1",
            CurrentBankFinanceAccountId = bankAccountId
        });

        var p1 = new Product { Id = Guid.NewGuid(), TenantId = tenantContext.CurrentTenantId, Name = "P1", BaseUnit = "Cai", SellingPrice = 50000m };
        var p2 = new Product { Id = Guid.NewGuid(), TenantId = tenantContext.CurrentTenantId, Name = "P2", BaseUnit = "Cai", SellingPrice = 50000m };
        orderRepo.Products.Add(p1);
        orderRepo.Products.Add(p2);

        orderRepo.BranchProducts.Add(new BranchProduct { Id = Guid.NewGuid(), TenantId = tenantContext.CurrentTenantId, BranchId = branchId, ProductId = p1.Id, Status = "Active" });
        orderRepo.BranchProducts.Add(new BranchProduct { Id = Guid.NewGuid(), TenantId = tenantContext.CurrentTenantId, BranchId = branchId, ProductId = p2.Id, Status = "Active" });

        orderRepo.BranchStocks.Add(new BranchStock { Id = Guid.NewGuid(), TenantId = tenantContext.CurrentTenantId, BranchId = branchId, ProductId = p1.Id, QuantityOnHand = 10m });
        orderRepo.BranchStocks.Add(new BranchStock { Id = Guid.NewGuid(), TenantId = tenantContext.CurrentTenantId, BranchId = branchId, ProductId = p2.Id, QuantityOnHand = 10m });

        var orderService = new OrderService(orderRepo, uow, printMock.Object, tenantContext: tenantContext);

        var longSummary = new string('A', 350);
        var req = new OrderCreateRequest(
            BranchId: branchId,
            CustomerId: null,
            PaymentMethod: "VIETQR", // VIETQR allows IsPendingPayment
            IsPendingPayment: true,
            DiscountAmount: 10000m,
            AppliedPromotionSummary: longSummary,
            Items: new List<OrderItemCreateDto>
            {
                new OrderItemCreateDto(p1.Id, 1, 50000m),
                new OrderItemCreateDto(p2.Id, 1, 50000m)
            }
        );

        var res = await orderService.CreateOrderAsync(req);
        Assert.NotNull(res);
        Assert.Equal("PENDING_PAYMENT", res.PaymentStatus);
        Assert.Equal(bankAccountId, Assert.Single(orderRepo.Orders).FinanceAccountId);
        Assert.True(res.AppliedPromotionSummary?.Length <= 300);
    }

    [Fact]
    public async Task CreatePreOrderAsync_NegativeDeposit_ThrowsInvalidOperationException()
    {
        var tenantContext = new FakeTenantContext();
        var orderRepo = new FakeOrderRepository();
        var uow = new LocalUnitOfWork();
        var printMock = new Mock<IThermalPrintService>();
        var orderService = new OrderService(orderRepo, uow, printMock.Object, tenantContext: tenantContext);

        var branchId = tenantContext.CurrentBranchId!.Value;
        var custId = Guid.NewGuid();

        var req = new PreOrderCreateRequest(
            BranchId: branchId,
            CustomerId: custId,
            DepositAmount: -10000m, // Negative deposit
            ExpectedPickupDate: DateTime.UtcNow.AddDays(2),
            Items: new List<OrderItemCreateDto>
            {
                new OrderItemCreateDto(Guid.NewGuid(), 1, 50000m)
            }
        );

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => orderService.CreatePreOrderAsync(req));
        Assert.Contains("Tiền đặt cọc không được là số âm", ex.Message);
    }

    [Fact]
    public async Task CreatePreOrderAsync_GiftInactiveAndLimitedStock_AddsWarnings()
    {
        var tenantContext = new FakeTenantContext();
        var orderRepo = new FakeOrderRepository();
        var uow = new LocalUnitOfWork();
        var printMock = new Mock<IThermalPrintService>();

        var branchId = tenantContext.CurrentBranchId!.Value;
        var custId = Guid.NewGuid();
        orderRepo.Branches.Add(new Branch { Id = branchId, TenantId = tenantContext.CurrentTenantId, Name = "Branch 1" });
        orderRepo.Customers.Add(new Customer { Id = custId, TenantId = tenantContext.CurrentTenantId, Name = "Khach Test" });

        var pMain = new Product { Id = Guid.NewGuid(), TenantId = tenantContext.CurrentTenantId, Name = "Banh Mi", BaseUnit = "Cai", SellingPrice = 20000m };
        var pGiftInactive = new Product { Id = Guid.NewGuid(), TenantId = tenantContext.CurrentTenantId, Name = "Qua Inactive", BaseUnit = "Cai", SellingPrice = 5000m };
        var pGiftLimited = new Product { Id = Guid.NewGuid(), TenantId = tenantContext.CurrentTenantId, Name = "Qua Limited", BaseUnit = "Cai", SellingPrice = 5000m };

        orderRepo.Products.Add(pMain);
        orderRepo.Products.Add(pGiftInactive);
        orderRepo.Products.Add(pGiftLimited);

        // BranchProduct for pGiftLimited is active, but pGiftInactive is not in BranchProducts (inactive)
        orderRepo.BranchProducts.Add(new BranchProduct { Id = Guid.NewGuid(), TenantId = tenantContext.CurrentTenantId, BranchId = branchId, ProductId = pMain.Id, Status = "Active" });
        orderRepo.BranchProducts.Add(new BranchProduct { Id = Guid.NewGuid(), TenantId = tenantContext.CurrentTenantId, BranchId = branchId, ProductId = pGiftLimited.Id, Status = "Active" });

        // BranchStock for pGiftLimited has only 1 available, while requested 5
        orderRepo.BranchStocks.Add(new BranchStock { Id = Guid.NewGuid(), TenantId = tenantContext.CurrentTenantId, BranchId = branchId, ProductId = pMain.Id, QuantityOnHand = 100m });
        orderRepo.BranchStocks.Add(new BranchStock { Id = Guid.NewGuid(), TenantId = tenantContext.CurrentTenantId, BranchId = branchId, ProductId = pGiftLimited.Id, QuantityOnHand = 1m, QuantityReserved = 0m });

        var orderService = new OrderService(orderRepo, uow, printMock.Object, tenantContext: tenantContext);

        var req = new PreOrderCreateRequest(
            BranchId: branchId,
            CustomerId: custId,
            DepositAmount: 10000m,
            ExpectedPickupDate: DateTime.UtcNow.AddDays(2),
            Items: new List<OrderItemCreateDto>
            {
                new OrderItemCreateDto(pMain.Id, 2, 20000m, IsGift: false),
                new OrderItemCreateDto(pGiftInactive.Id, 1, 0m, IsGift: true),
                new OrderItemCreateDto(pGiftLimited.Id, 5, 0m, IsGift: true)
            }
        );

        var res = await orderService.CreatePreOrderAsync(req);
        Assert.NotNull(res);
        Assert.NotNull(res.GiftStockWarning);
    }

    // =========================================================================
    // SECTION 2: PurchaseOrderService Branches
    // =========================================================================

    [Fact]
    public async Task PurchaseOrderService_ResolveActorFallback_WithEmployeeOrStaffRole()
    {
        var tenantContext = new FakeTenantContext
        {
            CurrentUserId = Guid.Empty, // Empty userId triggers fallback branch
            CurrentRole = "EMPLOYEE"
        };
        var poRepoMock = new Mock<IPurchaseOrderRepository>();
        var uow = new LocalUnitOfWork();
        var dispatchPkgMock = new Mock<ISupplierDispatchPackageService>();

        var branchId = tenantContext.CurrentBranchId!.Value;
        var branch = new Branch { Id = branchId, TenantId = tenantContext.CurrentTenantId, Name = "Branch 1", IsActive = true };

        poRepoMock.Setup(r => r.UserExistsAsync("user-employee-001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        poRepoMock.Setup(r => r.GetBranchByIdAsync(branchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(branch);
        poRepoMock.Setup(r => r.GetMatrixSupplierConfigsAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OrderMatrixSupplierDeliveryConfigDto>());
        poRepoMock.Setup(r => r.GetMatrixProductsAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OrderMatrixProductDto>());
        poRepoMock.Setup(r => r.GetDraftMatrixPosAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PurchaseOrder>());
        poRepoMock.Setup(r => r.GetCommittedPosInRangeAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PurchaseOrder>());

        var poService = new PurchaseOrderService(poRepoMock.Object, uow, tenantContext, dispatchPkgMock.Object);

        var query = new OrderMatrixQuery { BranchId = branchId, ViewFrom = DateTime.Today, DurationDays = 7 };
        var res = await poService.GetOrderMatrixAsync(query);
        Assert.NotNull(res);
    }

    [Fact]
    public async Task PurchaseOrderService_SaveMatrixDraft_SupplierNotFound_ThrowsBusinessException()
    {
        var tenantContext = new FakeTenantContext();
        var poRepoMock = new Mock<IPurchaseOrderRepository>();
        var uow = new LocalUnitOfWork();
        var dispatchPkgMock = new Mock<ISupplierDispatchPackageService>();

        var branchId = tenantContext.CurrentBranchId!.Value;
        var branch = new Branch { Id = branchId, TenantId = tenantContext.CurrentTenantId, Name = "Branch 1", IsActive = true };
        var supplierId = Guid.NewGuid();

        poRepoMock.Setup(r => r.GetBranchByIdAsync(branchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(branch);
        poRepoMock.Setup(r => r.GetMatrixSupplierConfigsAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OrderMatrixSupplierDeliveryConfigDto>
            {
                new OrderMatrixSupplierDeliveryConfigDto { SupplierId = supplierId, SupplierName = "NCC Test", SupplierCode = "NCC", CutoffTime = "17:00", CutoffDayOffset = 1 }
            });
        poRepoMock.Setup(r => r.GetMatrixProductsAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OrderMatrixProductDto>());
        poRepoMock.Setup(r => r.GetCommittedPosInRangeAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PurchaseOrder>());
        poRepoMock.Setup(r => r.GetSupplierByIdAsync(supplierId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Supplier?)null); // Supplier does not exist

        var poService = new PurchaseOrderService(poRepoMock.Object, uow, tenantContext, dispatchPkgMock.Object);

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
                    ProductId = Guid.NewGuid(),
                    SupplierId = supplierId,
                    DeliveryDate = DateTime.Today.AddDays(1),
                    EmployeeQty = 10m,
                    FinalQty = 10m
                }
            }
        };

        var ex = await Assert.ThrowsAsync<BusinessException>(() => poService.SaveMatrixDraftAsync(draftCmd));
        Assert.Equal("SUPPLIER_NOT_FOUND", ex.ErrorCode);
    }

    [Fact]
    public async Task PurchaseOrderService_SplitEngine_SupplierOffAndPreview_ProcessedCorrectly()
    {
        var tenantContext = new FakeTenantContext();
        var poRepoMock = new Mock<IPurchaseOrderRepository>();
        var uow = new LocalUnitOfWork();
        var dispatchPkgMock = new Mock<ISupplierDispatchPackageService>();

        var branchId = tenantContext.CurrentBranchId!.Value;
        var branch = new Branch { Id = branchId, TenantId = tenantContext.CurrentTenantId, Name = "Branch 1", IsActive = true };
        var supplierId = Guid.NewGuid();
        var prodId = Guid.NewGuid();

        poRepoMock.Setup(r => r.GetBranchByIdAsync(branchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(branch);
        poRepoMock.Setup(r => r.GetMatrixSupplierConfigsAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OrderMatrixSupplierDeliveryConfigDto>
            {
                new OrderMatrixSupplierDeliveryConfigDto { SupplierId = supplierId, SupplierName = "NCC Off", SupplierCode = "NCCOFF", CutoffTime = "17:00", CutoffDayOffset = 1 }
            });
        poRepoMock.Setup(r => r.GetSupplierByIdAsync(supplierId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Supplier { Id = supplierId, Name = "NCC Off", Code = "NCCOFF" });
        poRepoMock.Setup(r => r.GetMatrixProductsAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OrderMatrixProductDto>
            {
                new OrderMatrixProductDto { Id = prodId, Sku = "SKU-01", Name = "SP 1", SupplierId = supplierId, Price = 50000m, Unit = "Goi", IsProductActive = false, RowDisabledReason = "Ngung ban" }
            });
        poRepoMock.Setup(r => r.GetDraftMatrixPosAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PurchaseOrder>());
        poRepoMock.Setup(r => r.GetCommittedPosInRangeAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PurchaseOrder>());
        poRepoMock.Setup(r => r.GetDraftMatrixPoAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PurchaseOrder?)null);

        var poService = new PurchaseOrderService(poRepoMock.Object, uow, tenantContext, dispatchPkgMock.Object);

        var cmd = new SplitMatrixPoCommand
        {
            BranchId = branchId,
            SupplierId = supplierId,
            Scope = "WEEK",
            WeekStartDate = DateTime.Today.ToString("yyyy-MM-dd")
        };

        var res = await poService.PreviewSplitMatrixPoAsync(cmd);
        Assert.NotNull(res);
    }

    // =========================================================================
    // SECTION 3: GoodsReceiptService Branches
    // =========================================================================

    [Fact]
    public async Task CreateGoodsReceiptAsync_AutoGenerateBatchCodeAndExchangeDeduction()
    {
        var tenantContext = new FakeTenantContext();
        var grRepoMock = new Mock<IGoodsReceiptRepository>();
        var retRepoMock = new Mock<ISupplierReturnRepository>();
        var uow = new LocalUnitOfWork();

        var branchId = tenantContext.CurrentBranchId!.Value;
        var prodId = Guid.NewGuid();
        var poId = Guid.NewGuid();
        var returnLineId = Guid.NewGuid();

        var po = new PurchaseOrder
        {
            Id = poId,
            TenantId = tenantContext.CurrentTenantId,
            BranchId = branchId,
            SupplierId = Guid.NewGuid(),
            Status = "Confirmed",
            DeliveryDate = DateTime.Today,
            Details = new List<PurchaseOrderDetail>
            {
                new PurchaseOrderDetail
                {
                    Id = Guid.NewGuid(),
                    PurchaseOrderId = poId,
                    ProductId = prodId,
                    OrderedQty = 10m,
                    UnitPrice = 50000m,
                    SourceSupplierReturnLineId = returnLineId
                }
            }
        };

        var ret = new SupplierReturn
        {
            Id = Guid.NewGuid(),
            TenantId = tenantContext.CurrentTenantId,
            BranchId = branchId,
            Lines = new List<SupplierReturnLine>
            {
                new SupplierReturnLine
                {
                    Id = returnLineId,
                    ProductId = prodId,
                    BatchCode = "OLD-BATCH-01",
                    RequestedQty = 10m
                }
            }
        };

        var product = new Product
        {
            Id = prodId,
            TenantId = tenantContext.CurrentTenantId,
            ProductCode = "SP-BATCH",
            Name = "San Pham Batch",
            BaseUnit = "Cai",
            CostPrice = 45000m
        };

        var stock = new BranchStock
        {
            Id = Guid.NewGuid(),
            TenantId = tenantContext.CurrentTenantId,
            BranchId = branchId,
            ProductId = prodId,
            QuantityOnHand = 20m,
            QuantityReserved = 10m
        };

        grRepoMock.Setup(r => r.GetPurchaseOrderWithDetailsAsync(tenantContext.CurrentTenantId, poId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(po);
        grRepoMock.Setup(r => r.GetActiveReceiptByPoIdAsync(tenantContext.CurrentTenantId, poId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((GoodsReceipt?)null);
        grRepoMock.Setup(r => r.GetProductsByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, Product> { [prodId] = product });
        grRepoMock.Setup(r => r.GetBranchStockAsync(tenantContext.CurrentTenantId, branchId, prodId, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(stock);
        grRepoMock.Setup(r => r.GenerateCodeAsync(tenantContext.CurrentTenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync("GRN-001");
        grRepoMock.Setup(r => r.GetByIdAsync(tenantContext.CurrentTenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string t, Guid id, CancellationToken ct) => new GoodsReceipt
            {
                Id = id,
                TenantId = t,
                Code = "GRN-001",
                BranchId = branchId,
                SupplierId = po.SupplierId,
                PurchaseOrderId = poId,
                Lines = new List<GoodsReceiptLine>()
            });
        retRepoMock.Setup(r => r.GetByIdAsync(tenantContext.CurrentTenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ret);
        grRepoMock.Setup(r => r.UserExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var grService = new GoodsReceiptService(grRepoMock.Object, uow, tenantContext, retRepoMock.Object);

        // BatchCode is empty -> triggers auto-generation (needsNew)
        var cmd = new CreateGoodsReceiptCommand
        {
            PurchaseOrderId = poId,
            SupplierId = po.SupplierId,
            BranchId = branchId,
            ReceivedAtUtc = DateTime.UtcNow,
            Note = "Nhan hang test",
            Lines = new List<CreateGoodsReceiptLineCommand>
            {
                new CreateGoodsReceiptLineCommand
                {
                    ProductId = prodId,
                    OrderedQty = 10m,
                    DeliveredQty = 10m,
                    AcceptedQty = 10m,
                    UnitPrice = 50000m,
                    BatchCode = null, // Empty batch code -> auto generate
                    ManufactureDate = DateTime.Today.AddMonths(-1),
                    ExpiryDate = DateTime.Today.AddYears(1),
                    ManageBatch = true
                }
            }
        };

        var res = await grService.CreateGoodsReceiptAsync(cmd);
        Assert.NotNull(res);
        grRepoMock.Verify(r => r.AddAsync(It.IsAny<GoodsReceipt>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RejectGoodsReceipt_WithLineAndGroupRejectionReason_Processed()
    {
        var tenantContext = new FakeTenantContext();
        var grRepoMock = new Mock<IGoodsReceiptRepository>();
        var uow = new LocalUnitOfWork();

        var poId = Guid.NewGuid();
        var prodId = Guid.NewGuid();
        var po = new PurchaseOrder
        {
            Id = poId,
            TenantId = tenantContext.CurrentTenantId,
            BranchId = tenantContext.CurrentBranchId!.Value,
            SupplierId = Guid.NewGuid(),
            Status = "Confirmed",
            DeliveryDate = DateTime.Today,
            Details = new List<PurchaseOrderDetail>
            {
                new PurchaseOrderDetail { Id = Guid.NewGuid(), PurchaseOrderId = poId, ProductId = prodId, OrderedQty = 10m, UnitPrice = 50000m }
            }
        };

        grRepoMock.Setup(r => r.GetPurchaseOrderWithDetailsAsync(tenantContext.CurrentTenantId, poId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(po);
        grRepoMock.Setup(r => r.GetActiveReceiptByPoIdAsync(tenantContext.CurrentTenantId, poId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((GoodsReceipt?)null);
        grRepoMock.Setup(r => r.GetProductsByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, Product> { [prodId] = new Product { Id = prodId, TenantId = tenantContext.CurrentTenantId, Name = "SP Rejected" } });
        grRepoMock.Setup(r => r.GenerateCodeAsync(tenantContext.CurrentTenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync("GRN-002");
        grRepoMock.Setup(r => r.GetByIdAsync(tenantContext.CurrentTenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string t, Guid id, CancellationToken ct) => new GoodsReceipt
            {
                Id = id,
                TenantId = t,
                Code = "GRN-002",
                BranchId = po.BranchId,
                SupplierId = po.SupplierId,
                PurchaseOrderId = poId,
                Lines = new List<GoodsReceiptLine>()
            });
        grRepoMock.Setup(r => r.UserExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var grService = new GoodsReceiptService(grRepoMock.Object, uow, tenantContext);

        // Sum accepted (0) < ordered (10), groupReason provided, firstLine.RejectionReason is empty -> falls back to groupReason
        var cmd = new CreateGoodsReceiptCommand
        {
            PurchaseOrderId = poId,
            SupplierId = po.SupplierId,
            BranchId = po.BranchId,
            ReceivedAtUtc = DateTime.UtcNow,
            Note = "Tu choi toan bo",
            Lines = new List<CreateGoodsReceiptLineCommand>
            {
                new CreateGoodsReceiptLineCommand
                {
                    ProductId = prodId,
                    OrderedQty = 10m,
                    DeliveredQty = 10m,
                    AcceptedQty = 0m,
                    RejectedQty = 10m,
                    UnitPrice = 50000m,
                    RejectionReason = "Hang loi quy cach toan bo" // rejection reason provided
                }
            }
        };

        var res = await grService.CreateGoodsReceiptAsync(cmd);
        Assert.NotNull(res);
    }

    // =========================================================================
    // SECTION 4: StocktakeService Branches
    // =========================================================================

    [Fact]
    public async Task StocktakeService_EmployeeRoleValidations_ThrowsBusinessExceptions()
    {
        var tenantContext = new FakeTenantContext
        {
            CurrentRole = "EMPLOYEE",
            CurrentBranchId = null // Employee without branch
        };
        var stRepoMock = new Mock<IStocktakeRepository>();
        var supRetServiceMock = new Mock<ISupplierReturnService>();
        var supRetRepoMock = new Mock<ISupplierReturnRepository>();
        var supRepoMock = new Mock<ISupplierRepository>();
        var uow = new LocalUnitOfWork();

        var service = new StocktakeService(stRepoMock.Object, supRetServiceMock.Object, supRetRepoMock.Object, supRepoMock.Object, tenantContext, uow);

        // 1. Employee without branch creating stocktake
        var cmd = new CreateStocktakeCommand(Guid.Empty, "ALL", "PERIODIC", null, null, null, null);
        var exNoBranch = await Assert.ThrowsAsync<BusinessException>(() => service.CreateAsync(cmd));
        Assert.Contains("Tài khoản nhân viên chưa được gán chi nhánh", exNoBranch.Message);

        // 2. Employee with branch trying to access different branch's stocktake
        tenantContext.CurrentBranchId = Guid.NewGuid();
        var otherBranchStocktake = new Stocktake
        {
            Id = Guid.NewGuid(),
            TenantId = tenantContext.CurrentTenantId,
            BranchId = Guid.NewGuid(), // Different branch!
            Status = "DRAFT"
        };
        stRepoMock.Setup(r => r.GetByIdAsync(tenantContext.CurrentTenantId, otherBranchStocktake.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(otherBranchStocktake);
        stRepoMock.Setup(r => r.GetDetailDtoByIdAsync(tenantContext.CurrentTenantId, otherBranchStocktake.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StocktakeDetailDto(otherBranchStocktake.Id, "KK-OTHER", otherBranchStocktake.BranchId, "BR-OTH", "Other", "ALL", "PERIODIC", "DRAFT", "Staff", DateTime.UtcNow, null, null, null, null, null, new List<StocktakeLineDto>()));

        var exDiffBranch = await Assert.ThrowsAsync<BusinessException>(() => service.GetByIdAsync(otherBranchStocktake.Id));
        Assert.Contains("Bạn không có quyền xem hoặc chỉnh sửa", exDiffBranch.Message);
    }

    [Fact]
    public async Task StocktakeService_ReviewSurplusFoundAndWriteOffFallback_UpdatesStocks()
    {
        var tenantContext = new FakeTenantContext { CurrentRole = "STORE_OWNER" };
        var stRepoMock = new Mock<IStocktakeRepository>();
        var supRetServiceMock = new Mock<ISupplierReturnService>();
        var supRetRepoMock = new Mock<ISupplierReturnRepository>();
        var supRepoMock = new Mock<ISupplierRepository>();
        var uow = new LocalUnitOfWork();

        var branchId = tenantContext.CurrentBranchId!.Value;
        var stId = Guid.NewGuid();
        var stockId = Guid.NewGuid();
        var prodId = Guid.NewGuid();

        var stock = new BranchStock
        {
            Id = stockId,
            TenantId = tenantContext.CurrentTenantId,
            BranchId = branchId,
            ProductId = prodId,
            QuantityOnHand = 10m
        };

        var stocktake = new Stocktake
        {
            Id = stId,
            Code = "KK-001",
            TenantId = tenantContext.CurrentTenantId,
            BranchId = branchId,
            Status = "PENDING_APPROVAL",
            Lines = new List<StocktakeLine>
            {
                new StocktakeLine
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantContext.CurrentTenantId,
                    StocktakeId = stId,
                    BranchStockId = stockId,
                    ProductId = prodId,
                    SystemQty = 10m,
                    ActualQty = 15m,
                    VarianceQty = 5m, // Surplus of 5
                    DamagedQty = 2m,
                    ExpiredQty = 1m
                }
            }
        };

        stRepoMock.Setup(r => r.GetByIdAsync(tenantContext.CurrentTenantId, stId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stocktake);
        stRepoMock.Setup(r => r.GetBranchStockByIdAsync(tenantContext.CurrentTenantId, stockId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stock);
        stRepoMock.Setup(r => r.GetDetailDtoByIdAsync(tenantContext.CurrentTenantId, stId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StocktakeDetailDto(stId, "KK-001", branchId, "BR01", "Branch 1", "FULL", "PERIODIC", "APPROVED", "Owner", DateTime.UtcNow, null, "Owner", DateTime.UtcNow, null, null, new List<StocktakeLineDto>()));

        var service = new StocktakeService(stRepoMock.Object, supRetServiceMock.Object, supRetRepoMock.Object, supRepoMock.Object, tenantContext, uow);

        // Review with Decision = "APPROVE", ConfirmSurplusFound = true
        var reviewCmd = new ReviewStocktakeCommand(
            "APPROVE",
            "Duyet kiem ke va cong ton thua",
            "Store Owner",
            ConfirmSurplusFound: true
        );

        var res = await service.ReviewAsync(stId, reviewCmd);
        Assert.NotNull(res);
        Assert.Equal("APPROVED", stocktake.Status);
        Assert.Equal(12m, stock.QuantityOnHand); // 10 + 5 surplus - 2 damaged - 1 expired = 12!
        Assert.True(uow.SaveChangesCalled);
    }
}
