using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using MT_GRMS.Application.Batches;
using MT_GRMS.Application.Batches.Dtos;
using MT_GRMS.Application.Branches;
using MT_GRMS.Application.Branches.Dtos;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.GoodsReceipts;
using MT_GRMS.Application.PurchaseOrders;
using MT_GRMS.Application.PurchaseOrders.Dtos;
using MT_GRMS.Application.Repacks;
using MT_GRMS.Application.Stocktakes;
using MT_GRMS.Application.Stocktakes.Dtos;
using MT_GRMS.Application.Suppliers;
using MT_GRMS.Application.Suppliers.Dtos;
using MT_GRMS.Application.Suppliers.Validators;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Entities.Inventory;
using MT_GRMS.Domain.Entities.Supplier;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Domain.Exceptions;
using Xunit;

namespace MT_GRMS.UnitTests;

public class DucProcurementCoverageTests
{
    private static object? InvokePrivateStatic(Type type, string methodName, params object?[] args)
    {
        var mi = type.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(mi);
        try
        {
            return mi!.Invoke(null, args);
        }
        catch (TargetInvocationException tie) when (tie.InnerException != null)
        {
            throw tie.InnerException;
        }
    }

    private sealed class FakeTenantContext : ITenantContext
    {
        public string CurrentTenantId { get; set; } = "tenant-duc-cov";
        public Guid CurrentUserId { get; set; } = Guid.Empty;
        public string CurrentRawUserId { get; set; } = "demo-owner-pro";
        public Guid? CurrentBranchId { get; set; }
        public string CurrentRole { get; set; } = "STORE_OWNER";
        public void SetContext(string tenantId, Guid userId, Guid? branchId)
        {
            CurrentTenantId = tenantId;
            CurrentUserId = userId;
            CurrentBranchId = branchId;
        }
        public void SetRawUserId(string? rawUserId) => CurrentRawUserId = rawUserId ?? string.Empty;
        public void SetRole(string? role) => CurrentRole = role ?? string.Empty;
    }

    // =========================================================================
    // 1. PurchaseOrderService.EnsureSuggestionsMeetSupplierOrderMoq (40 lines)
    // =========================================================================
    [Fact]
    public void PurchaseOrder_EnsureSuggestionsMeetSupplierOrderMoq_CoversAllBranches()
    {
        var supA = Guid.NewGuid();
        var supB = Guid.NewGuid();
        var supNoMoq = Guid.NewGuid();
        var day1 = new DateTime(2026, 11, 26);

        // Empty lists -> early return
        InvokePrivateStatic(
            typeof(PurchaseOrderService),
            "EnsureSuggestionsMeetSupplierOrderMoq",
            new List<OrderMatrixSuggestionDto>(),
            new List<OrderMatrixSupplierDeliveryConfigDto>());

        // Configs with MinOrderQuantity == 0 -> early return
        var zeroMoqConfigs = new List<OrderMatrixSupplierDeliveryConfigDto>
        {
            new() { SupplierId = supNoMoq, MinOrderQuantity = 0 }
        };
        var singleItem = new List<OrderMatrixSuggestionDto>
        {
            new() { SupplierId = supNoMoq, ProductId = Guid.NewGuid(), DeliveryDate = day1, SuggestedQty = 1m }
        };
        InvokePrivateStatic(typeof(PurchaseOrderService), "EnsureSuggestionsMeetSupplierOrderMoq", singleItem, zeroMoqConfigs);
        Assert.Equal(1m, singleItem[0].SuggestedQty);

        // Mixed suppliers: supA deficit needs top-up with OrderMultiple, supB already >= MOQ, supNoMoq skipped
        var configs = new List<OrderMatrixSupplierDeliveryConfigDto>
        {
            new() { SupplierId = supA, MinOrderQuantity = 10 },
            new() { SupplierId = supB, MinOrderQuantity = 5 },
            new() { SupplierId = supNoMoq, MinOrderQuantity = 0 }
        };
        var fastProd = Guid.NewGuid();
        var slowProd = Guid.NewGuid();
        var results = new List<OrderMatrixSuggestionDto>
        {
            new() { SupplierId = supA, ProductId = fastProd, DeliveryDate = day1, SalesPerDay = 8m, SuggestedQty = 2m, OrderMultiple = 2 },
            new() { SupplierId = supA, ProductId = slowProd, DeliveryDate = day1, SalesPerDay = 1m, SuggestedQty = 1m, OrderMultiple = 0 },
            new() { SupplierId = supB, ProductId = Guid.NewGuid(), DeliveryDate = day1, SalesPerDay = 3m, SuggestedQty = 6m, OrderMultiple = 1 },
            new() { SupplierId = supNoMoq, ProductId = Guid.NewGuid(), DeliveryDate = day1, SalesPerDay = 2m, SuggestedQty = 1m, OrderMultiple = 1 },
        };

        InvokePrivateStatic(typeof(PurchaseOrderService), "EnsureSuggestionsMeetSupplierOrderMoq", results, configs);

        Assert.True(results.Where(r => r.SupplierId == supA).Sum(r => r.SuggestedQty) >= 10m);
        Assert.Equal(6m, results.Single(r => r.SupplierId == supB).SuggestedQty);
        Assert.Equal(1m, results.Single(r => r.SupplierId == supNoMoq).SuggestedQty);
    }

    // =========================================================================
    // 2. GoodsReceiptService helpers & string CurrentRawUserId fix (BUG-SC-33-01)
    // =========================================================================
    [Fact]
    public void GoodsReceipt_ResolveInboundUnit_CoversAllFallbackBranches()
    {
        var prodWithPurchase = new Product
        {
            BaseUnit = "Chai",
            Units = new List<ProductUnit>
            {
                new() { UnitName = "Thùng", IsPurchaseUnit = true, IsBaseUnit = false, ConversionRate = 24m },
                new() { UnitName = "Chai", IsPurchaseUnit = false, IsBaseUnit = true, ConversionRate = 1m }
            }
        };
        var prodBaseOnly = new Product
        {
            BaseUnit = "Hộp",
            Units = new List<ProductUnit>
            {
                new() { UnitName = "Hộp", IsPurchaseUnit = false, IsBaseUnit = true, ConversionRate = 1m }
            }
        };

        Assert.Equal("Két", InvokePrivateStatic(typeof(GoodsReceiptService), "ResolveInboundUnit", " Két ", "Thùng", prodWithPurchase));
        Assert.Equal("Lốc", InvokePrivateStatic(typeof(GoodsReceiptService), "ResolveInboundUnit", "", " Lốc ", prodWithPurchase));
        Assert.Equal("Thùng", InvokePrivateStatic(typeof(GoodsReceiptService), "ResolveInboundUnit", null, null, prodWithPurchase));
        Assert.Equal("Hộp", InvokePrivateStatic(typeof(GoodsReceiptService), "ResolveInboundUnit", null, null, prodBaseOnly));
        Assert.Equal("Kg", InvokePrivateStatic(typeof(GoodsReceiptService), "ResolveInboundUnit", null, null, new Product { BaseUnit = " Kg ", Units = new List<ProductUnit>() }));
        Assert.Equal("Cái", InvokePrivateStatic(typeof(GoodsReceiptService), "ResolveInboundUnit", null, null, null));
    }

    [Fact]
    public void GoodsReceipt_ResolvePurchaseToStockRate_CoversAllBranches()
    {
        var prod = new Product
        {
            Units = new List<ProductUnit>
            {
                new() { UnitName = "Thùng", IsPurchaseUnit = true, ConversionRate = 24m },
                new() { UnitName = "Lốc", IsPurchaseUnit = false, ConversionRate = 6m },
                new() { UnitName = "Chai", IsBaseUnit = true, ConversionRate = 1m },
                new() { UnitName = "Lỗi", IsBaseUnit = false, ConversionRate = 0m }
            }
        };

        Assert.Equal(1m, InvokePrivateStatic(typeof(GoodsReceiptService), "ResolvePurchaseToStockRate", null, "Thùng", null));
        Assert.Equal(1m, InvokePrivateStatic(typeof(GoodsReceiptService), "ResolvePurchaseToStockRate", new Product { Units = new List<ProductUnit>() }, "Thùng", null));
        Assert.Equal(6m, InvokePrivateStatic(typeof(GoodsReceiptService), "ResolvePurchaseToStockRate", prod, "lốc", null));
        Assert.Equal(6m, InvokePrivateStatic(typeof(GoodsReceiptService), "ResolvePurchaseToStockRate", prod, "KhôngKhớp", "Lốc"));
        Assert.Equal(24m, InvokePrivateStatic(typeof(GoodsReceiptService), "ResolvePurchaseToStockRate", prod, "KhôngKhớp", "KhôngKhớp2"));
        Assert.Equal(1m, InvokePrivateStatic(typeof(GoodsReceiptService), "ResolvePurchaseToStockRate", prod, "Lỗi", null));
    }

    [Fact]
    public void GoodsReceipt_EnsureDeliveryDateReachedForReceipt_CoversNullPastAndFuture()
    {
        // Null DeliveryDate -> allowed
        InvokePrivateStatic(typeof(GoodsReceiptService), "EnsureDeliveryDateReachedForReceipt", new PurchaseOrder { DeliveryDate = null });

        // Past/today -> allowed
        InvokePrivateStatic(typeof(GoodsReceiptService), "EnsureDeliveryDateReachedForReceipt", new PurchaseOrder { DeliveryDate = DateTime.UtcNow.AddDays(-1) });

        // Future -> throws DELIVERY_DATE_NOT_REACHED
        var ex = Assert.Throws<BusinessException>(() =>
            InvokePrivateStatic(typeof(GoodsReceiptService), "EnsureDeliveryDateReachedForReceipt", new PurchaseOrder { DeliveryDate = DateTime.UtcNow.AddDays(5) }));
        Assert.Equal("DELIVERY_DATE_NOT_REACHED", ex.ErrorCode);
    }

    [Fact]
    public async Task GoodsReceipt_ResolveValidActorUserIdAsync_UsesCurrentRawUserIdForStringAccounts()
    {
        var repo = new Mock<IGoodsReceiptRepository>();
        repo.Setup(r => r.UserExistsAsync("demo-owner-pro", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var uow = new Mock<IUnitOfWork>();
        var tenant = new FakeTenantContext { CurrentUserId = Guid.Empty, CurrentRawUserId = "demo-owner-pro" };

        var service = new GoodsReceiptService(repo.Object, uow.Object, tenant);
        var mi = typeof(GoodsReceiptService).GetMethod("ResolveValidActorUserIdAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(mi);
        var resolved = await (Task<string>)mi!.Invoke(service, new object[] { CancellationToken.None })!;
        Assert.Equal("demo-owner-pro", resolved);
    }

    // =========================================================================
    // 3. RepackService helpers & NotFoundException fix (BUG-SC-16-01)
    // =========================================================================
    [Fact]
    public void Repack_BuildPackageName_And_ResolveProcessedPricePerKg_CoverAllBranches()
    {
        Assert.Equal("Thịt ba rọi 500 G", InvokePrivateStatic(typeof(RepackService), "BuildPackageName", " Thịt ba rọi ", 0.5m));
        Assert.Equal("Khay 1.25 KG", InvokePrivateStatic(typeof(RepackService), "BuildPackageName", "   ", 1.25m));
        var longName = new string('A', 220);
        var truncated = (string)InvokePrivateStatic(typeof(RepackService), "BuildPackageName", longName, 2m)!;
        Assert.Equal(200, truncated.Length);

        var p1 = new Product { SellingPrice = 50000m, ProcessedSellingPricePerKg = 65000m };
        var p2 = new Product { SellingPrice = 50000m, ProcessedSellingPricePerKg = null };
        var stockWithRetail = new BranchStock { RetailPrice = 58000m };
        var stockNoRetail = new BranchStock { RetailPrice = null };

        Assert.Equal(65000m, InvokePrivateStatic(typeof(RepackService), "ResolveProcessedPricePerKg", p1, stockWithRetail));
        Assert.Equal(58000m, InvokePrivateStatic(typeof(RepackService), "ResolveProcessedPricePerKg", p2, stockWithRetail));
        Assert.Equal(50000m, InvokePrivateStatic(typeof(RepackService), "ResolveProcessedPricePerKg", p2, stockNoRetail));
    }

    [Fact]
    public async Task Repack_GetBranchStockPackagesAsync_NotFoundThrows404NotFoundException()
    {
        var repo = new Mock<IRepackRepository>();
        var uow = new Mock<IUnitOfWork>();
        var tenant = new FakeTenantContext();
        var service = new RepackService(repo.Object, tenant, uow.Object);

        await Assert.ThrowsAsync<NotFoundException>(() => service.GetBranchStockPackagesAsync(Guid.NewGuid()));
    }

    // =========================================================================
    // 4. StocktakeService helpers (ResolveReturnReason & ApplyLineDispositionActions)
    // =========================================================================
    [Fact]
    public void Stocktake_ResolveReturnReason_CoversAllSixBranches()
    {
        var r1 = ((string ReasonText, string ReasonType))InvokePrivateStatic(typeof(StocktakeService), "ResolveReturnReason",
            new[] { new StocktakeLine { DefectiveReasonCode = "RECALLED" } })!;
        Assert.Equal("RECALL", r1.ReasonType);

        var r2 = ((string ReasonText, string ReasonType))InvokePrivateStatic(typeof(StocktakeService), "ResolveReturnReason",
            new[] { new StocktakeLine { DefectiveReasonCode = "WRONG_SPEC_DISCOVERED" } })!;
        Assert.Equal("WRONG_ITEM", r2.ReasonType);

        var r3 = ((string ReasonText, string ReasonType))InvokePrivateStatic(typeof(StocktakeService), "ResolveReturnReason",
            new[] { new StocktakeLine { ExpiredAction = "RETURN_SUPPLIER", ExpiredQty = 2m } })!;
        Assert.Equal("EXPIRED", r3.ReasonType);

        var r4 = ((string ReasonText, string ReasonType))InvokePrivateStatic(typeof(StocktakeService), "ResolveReturnReason",
            new[] { new StocktakeLine { NearExpiryAction = "NEAR_EXPIRY_RETURN", NearExpiryQty = 3m } })!;
        Assert.Equal("NEAR_EXPIRY", r4.ReasonType);

        var r5 = ((string ReasonText, string ReasonType))InvokePrivateStatic(typeof(StocktakeService), "ResolveReturnReason",
            new[] { new StocktakeLine { DamagedAction = "RETURN_SUPPLIER", DamagedQty = 1m } })!;
        Assert.Equal("QUALITY_DEFECT", r5.ReasonType);

        var r6 = ((string ReasonText, string ReasonType))InvokePrivateStatic(typeof(StocktakeService), "ResolveReturnReason",
            new[] { new StocktakeLine() })!;
        Assert.Equal("QUALITY_DEFECT", r6.ReasonType);
    }

    [Fact]
    public void Stocktake_ApplyLineDispositionActions_CoversNullAndExplicitActions()
    {
        var l1 = new StocktakeLine { Id = Guid.NewGuid(), DamagedQty = 2m, ExpiredQty = 1m };
        var l2 = new StocktakeLine { Id = Guid.NewGuid(), DamagedQty = 3m, ExpiredQty = 0m };
        var st1 = new Stocktake { Lines = new List<StocktakeLine> { l1 } };

        // Null actions -> defaults to WRITE_OFF
        InvokePrivateStatic(typeof(StocktakeService), "ApplyLineDispositionActions", st1, null);
        Assert.Equal("WRITE_OFF", l1.DamagedAction);
        Assert.Equal("WRITE_OFF", l1.ExpiredAction);

        // Explicit action for l1, unmatched l2 falls back to WRITE_OFF
        var l3 = new StocktakeLine { Id = Guid.NewGuid(), DamagedQty = 2m, ExpiredQty = 1m };
        var st2 = new Stocktake { Lines = new List<StocktakeLine> { l3, l2 } };
        var actions = new List<ReviewStocktakeLineActionDto>
        {
            new(l3.Id, "RETURN_SUPPLIER", "WRITE_OFF")
        };
        InvokePrivateStatic(typeof(StocktakeService), "ApplyLineDispositionActions", st2, actions);
        Assert.Equal("RETURN_SUPPLIER", l3.DamagedAction);
        Assert.Equal("WRITE_OFF", l3.ExpiredAction);
        Assert.Equal("WRITE_OFF", l2.DamagedAction);
    }

    // =========================================================================
    // 5. BranchProductService.ResolveBranchRetailPrice + Batch/Supplier Round 1 fixes
    // =========================================================================
    [Fact]
    public void BranchProduct_ResolveBranchRetailPrice_CoversAllBranches()
    {
        var cmdWithBase = new UpdateBranchProductCommand
        {
            RetailPrice = 10000m,
            UnitPrices = new List<BranchProductUnitPriceDto>
            {
                new() { IsBaseUnit = false, ConversionRate = 12m, Price = 120000m },
                new() { IsBaseUnit = true, ConversionRate = 1m, Price = 11000m }
            }
        };
        Assert.Equal(11000m, InvokePrivateStatic(typeof(BranchProductService), "ResolveBranchRetailPrice", cmdWithBase, 9000m));

        var cmdNoBase = new UpdateBranchProductCommand
        {
            RetailPrice = 10000m,
            UnitPrices = new List<BranchProductUnitPriceDto>
            {
                new() { IsBaseUnit = false, ConversionRate = 12m, Price = 120000m },
                new() { IsBaseUnit = false, ConversionRate = 0m, Price = 10500m }
            }
        };
        Assert.Equal(10500m, InvokePrivateStatic(typeof(BranchProductService), "ResolveBranchRetailPrice", cmdNoBase, 9000m));

        Assert.Equal(10000m, InvokePrivateStatic(typeof(BranchProductService), "ResolveBranchRetailPrice", new UpdateBranchProductCommand { RetailPrice = 10000m }, 9000m));
        Assert.Equal(9000m, InvokePrivateStatic(typeof(BranchProductService), "ResolveBranchRetailPrice", new UpdateBranchProductCommand { RetailPrice = 0m }, 9000m));
        Assert.Equal(0m, InvokePrivateStatic(typeof(BranchProductService), "ResolveBranchRetailPrice", new UpdateBranchProductCommand { RetailPrice = 0m }, 0m));
    }

    [Fact]
    public async Task BatchService_GetBatchById_MissingThrowsNotFoundException()
    {
        var repo = new Mock<IBatchRepository>();
        var uow = new Mock<IUnitOfWork>();
        var tenant = new FakeTenantContext();
        var service = new BatchService(repo.Object, tenant, uow.Object);

        await Assert.ThrowsAsync<NotFoundException>(() => service.GetBatchByIdAsync(Guid.NewGuid()));
        await Assert.ThrowsAsync<NotFoundException>(() => service.ApplyFefoDiscountAsync(Guid.NewGuid(), new ApplyFefoDiscountRequest(15m, true, 5)));
    }

    [Fact]
    public void UpdateBasicSupplierCommandValidator_RejectsInvalidPhoneAndTaxCode()
    {
        var validator = new UpdateBasicSupplierCommandValidator();
        var result = validator.Validate(new UpdateBasicSupplierCommand
        {
            Name = "NCC Test",
            Phone = "abc",
            TaxCode = "123",
            Status = SupplierStatus.Active
        });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Phone");
        Assert.Contains(result.Errors, e => e.PropertyName == "TaxCode");
    }
}
