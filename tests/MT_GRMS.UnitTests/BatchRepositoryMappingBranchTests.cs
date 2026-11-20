using System.Reflection;
using MT_GRMS.Application.Batches.Dtos;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Entities.Inventory;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Domain.Entities.Supplier;
using MT_GRMS.Infrastructure.Repositories;

namespace MT_GRMS.UnitTests;

public class BatchRepositoryMappingBranchTests
{
    private static decimal ResolveReturnRate(Product? product, string? deliveryUnit, string? snapshot)
    {
        var method = typeof(SupplierReturnRepository).GetMethod(
            "ResolvePurchaseToStockRate", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return Assert.IsType<decimal>(method.Invoke(null, new object?[] { product, deliveryUnit, snapshot }));
    }

    [Fact]
    public void SupplierReturnRate_CoversUnitResolutionFallbacks()
    {
        Assert.Equal(1m, ResolveReturnRate(null, null, null));
        var product = new Product { Units = new List<ProductUnit>() };
        Assert.Equal(1m, ResolveReturnRate(product, null, null));

        product.Units = new List<ProductUnit>
        {
            new ProductUnit { UnitName = "Deleted", ConversionRate = 9, IsDeleted = true },
            new ProductUnit { UnitName = "Carton", ConversionRate = 12, IsPurchaseUnit = true },
            new ProductUnit { UnitName = "Piece", ConversionRate = 1, IsBaseUnit = true },
            new ProductUnit { UnitName = "Invalid", ConversionRate = 0 }
        };
        Assert.Equal(12m, ResolveReturnRate(product, " carton ", null));
        Assert.Equal(1m, ResolveReturnRate(product, "missing", " piece "));
        Assert.Equal(12m, ResolveReturnRate(product, "missing", "also missing"));

        product.Units.Single(x => x.UnitName == "Carton").IsPurchaseUnit = false;
        product.Units.Single(x => x.UnitName == "Piece").IsBaseUnit = false;
        Assert.Equal(1m, ResolveReturnRate(product, null, null));
        Assert.Equal(1m, ResolveReturnRate(product, "Invalid", null));
    }

    private static BatchListItemDto Map(
        BranchStock stock,
        IReadOnlyDictionary<Guid, Product> products,
        IReadOnlyDictionary<Guid, Branch> branches,
        IReadOnlyDictionary<Guid, Supplier> suppliers,
        DateTime now,
        bool? isBatchManaged = null,
        decimal? initialQty = null,
        decimal packagedQty = 0,
        int packageCount = 0,
        IReadOnlyDictionary<(Guid SupplierId, Guid ProductId), string>? deliveryUnits = null)
    {
        var method = typeof(BatchRepository).GetMethod(
            "MapBranchStockToListItem",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return Assert.IsType<BatchListItemDto>(method.Invoke(null, new object?[]
        {
            stock, products, branches, suppliers, now, "GRN-TEST", isBatchManaged,
            Guid.NewGuid(), initialQty, packagedQty, packageCount, deliveryUnits
        }));
    }

    [Fact]
    public void MapBranchStock_CoversMissingReferencesAndNonBatchFallbacks()
    {
        var now = new DateTime(2026, 11, 18);
        var stock = new BranchStock
        {
            Id = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            BranchId = Guid.NewGuid(),
            QuantityOnHand = 5,
            QuantityReserved = 10,
            QuantityQuarantined = 2,
            LandedCostPerUnit = 0,
            BatchCode = null
        };

        var result = Map(stock,
            new Dictionary<Guid, Product>(),
            new Dictionary<Guid, Branch>(),
            new Dictionary<Guid, Supplier>(),
            now,
            isBatchManaged: false);

        Assert.Equal(9999, result.DaysLeft);
        Assert.Equal("Normal", result.Status);
        Assert.Equal(0, result.QtyAvailable);
        Assert.Equal(5, result.QtyInitial);
        Assert.False(result.IsWeightProduct);
        Assert.Equal(0, result.PackageCount);
    }

    [Fact]
    public void MapBranchStock_CoversExpiredAndFutureBatchSupplierAndUnitBranches()
    {
        var now = new DateTime(2026, 11, 18);
        var productId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var supplierId = Guid.NewGuid();
        var product = new Product
        {
            Id = productId,
            ProductCode = "SKU-BATCH",
            Name = "Batch product",
            BaseUnit = "Piece",
            HasBatches = true,
            CostPrice = 12,
            Units = new List<ProductUnit>
            {
                new() { UnitName = "Carton", ConversionRate = 0, IsPurchaseUnit = true },
                new() { UnitName = "Piece", ConversionRate = 1, IsBaseUnit = true, IsDeleted = true }
            }
        };
        var products = new Dictionary<Guid, Product> { [productId] = product };
        var branches = new Dictionary<Guid, Branch>
        {
            [branchId] = new() { Id = branchId, Name = "Branch mapped" }
        };
        var suppliers = new Dictionary<Guid, Supplier>
        {
            [supplierId] = new() { Id = supplierId, Name = "Supplier mapped" }
        };
        var units = new Dictionary<(Guid, Guid), string> { [(supplierId, productId)] = "Unknown unit" };
        var stock = new BranchStock
        {
            Id = Guid.NewGuid(), ProductId = productId, BranchId = branchId, SupplierId = supplierId,
            BatchCode = "B-01", ManufactureDate = now.AddDays(-30), ExpiryDate = now.AddDays(-1),
            QuantityOnHand = 8, QuantityReserved = 1, QuantityQuarantined = 1,
            LandedCostPerUnit = 0
        };

        var expired = Map(stock, products, branches, suppliers, now, true, 20, 4, 3, units);
        Assert.Equal("Expired", expired.Status);
        Assert.Equal(0, expired.QtyAvailable);
        Assert.Equal(20, expired.QtyInitial);
        Assert.Equal("Supplier mapped", expired.SupplierName);
        Assert.Equal("Unknown unit", expired.Unit);
        Assert.Equal(1, expired.PurchaseToStockRate);

        stock.ExpiryDate = now.AddDays(100);
        stock.LandedCostPerUnit = 15;
        var future = Map(stock, products, branches, suppliers, now, null, 0, deliveryUnits: null);
        Assert.Equal("Normal", future.Status);
        Assert.Equal(6, future.QtyAvailable);
        Assert.Equal(8, future.QtyInitial);
        Assert.Equal(15, future.CostPrice);
        Assert.Equal("Carton", future.Unit);
    }

    [Fact]
    public void MapBranchStock_CoversWeightProductPackagingBranches()
    {
        var now = new DateTime(2026, 11, 18);
        var productId = Guid.NewGuid();
        var product = new Product
        {
            Id = productId,
            ProductCode = "WEIGHT",
            Name = "Weight product",
            BaseUnit = "kg",
            HasBatches = false,
            AllowRepack = false,
            Units = new List<ProductUnit>()
        };
        var stock = new BranchStock
        {
            Id = Guid.NewGuid(), ProductId = productId, BranchId = Guid.NewGuid(),
            QuantityOnHand = 10, QuantityReserved = 1, QuantityQuarantined = 2
        };

        var result = Map(stock,
            new Dictionary<Guid, Product> { [productId] = product },
            new Dictionary<Guid, Branch>(),
            new Dictionary<Guid, Supplier>(),
            now,
            isBatchManaged: false,
            packagedQty: -5,
            packageCount: 4);

        Assert.True(result.IsWeightProduct);
        Assert.True(result.AllowRepack);
        Assert.Equal(0, result.PackagedQty);
        Assert.Equal(4, result.PackageCount);
    }
}
