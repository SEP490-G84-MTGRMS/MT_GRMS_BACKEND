using System;
using System.Collections.Generic;
using MT_GRMS.Application.Inventory;
using MT_GRMS.Application.Suppliers;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Exceptions;
using Xunit;

namespace MT_GRMS.UnitTests.Inventory;

public class WeightAndPricingHelperTests
{
    [Fact]
    public void WeightProductHelper_NormalizeUnit_And_IsKgUnit_ShouldWork()
    {
        Assert.Equal("", WeightProductHelper.NormalizeUnit(null));
        Assert.Equal("kg", WeightProductHelper.NormalizeUnit("  KG  "));
        Assert.Equal("gram", WeightProductHelper.NormalizeUnit("Gram"));

        Assert.True(WeightProductHelper.IsKgUnit("kg"));
        Assert.True(WeightProductHelper.IsKgUnit("KG"));
        Assert.True(WeightProductHelper.IsKgUnit("  Kg  "));
        Assert.False(WeightProductHelper.IsKgUnit(null));
        Assert.False(WeightProductHelper.IsKgUnit("g"));
        Assert.False(WeightProductHelper.IsKgUnit("lon"));
    }

    [Fact]
    public void WeightProductHelper_IsWeightProduct_AllBranches()
    {
        // 1. Null product throws
        Assert.Throws<ArgumentNullException>(() => WeightProductHelper.IsWeightProduct(null!));

        // 2. BaseUnit is not kg -> false
        var nonKgProduct = new Product
        {
            Id = Guid.NewGuid(),
            BaseUnit = "Chai",
            Name = "Nuoc Tuong"
        };
        Assert.False(WeightProductHelper.IsWeightProduct(nonKgProduct));

        // 3. BaseUnit is kg, Units collection is null -> true
        var kgProductNoUnits = new Product
        {
            Id = Guid.NewGuid(),
            BaseUnit = "kg",
            Name = "Thit Heo",
            Units = null!
        };
        Assert.True(WeightProductHelper.IsWeightProduct(kgProductNoUnits));

        // 4. BaseUnit is kg, Units collection is empty -> true
        kgProductNoUnits.Units = new List<ProductUnit>();
        Assert.True(WeightProductHelper.IsWeightProduct(kgProductNoUnits));

        // 5. Purchase unit is null:
        // 5a. baseUnit unit in list is null -> true
        var kgProductWithUnitsNoBase = new Product
        {
            Id = Guid.NewGuid(),
            BaseUnit = "kg",
            Name = "Thit Heo",
            Units = new List<ProductUnit>
            {
                new ProductUnit { Id = Guid.NewGuid(), UnitName = "Thung", IsPurchaseUnit = false, IsBaseUnit = false, ConversionRate = 10m }
            }
        };
        Assert.True(WeightProductHelper.IsWeightProduct(kgProductWithUnitsNoBase));

        // 5b. baseUnit unit in list is kg and rate == 1m -> true
        kgProductWithUnitsNoBase.Units.Add(new ProductUnit
        {
            Id = Guid.NewGuid(),
            UnitName = "kg",
            IsPurchaseUnit = false,
            IsBaseUnit = true,
            ConversionRate = 1m
        });
        Assert.True(WeightProductHelper.IsWeightProduct(kgProductWithUnitsNoBase));

        // 5c. baseUnit unit in list is not kg or rate != 1m -> false
        var kgProductBadBase = new Product
        {
            Id = Guid.NewGuid(),
            BaseUnit = "kg",
            Name = "Thit Heo",
            Units = new List<ProductUnit>
            {
                new ProductUnit { Id = Guid.NewGuid(), UnitName = "g", IsPurchaseUnit = false, IsBaseUnit = true, ConversionRate = 0.001m }
            }
        };
        Assert.False(WeightProductHelper.IsWeightProduct(kgProductBadBase));

        // 6. Purchase unit is present:
        // 6a. isBaseUnit && isKg && rate == 1m -> true
        var kgProductValidPurchase = new Product
        {
            Id = Guid.NewGuid(),
            BaseUnit = "kg",
            Name = "Rau Muong",
            Units = new List<ProductUnit>
            {
                new ProductUnit { Id = Guid.NewGuid(), UnitName = "kg", IsPurchaseUnit = true, IsBaseUnit = true, ConversionRate = 1m }
            }
        };
        Assert.True(WeightProductHelper.IsWeightProduct(kgProductValidPurchase));

        // 6b. isPurchaseUnit but not baseUnit (e.g. Purchase by Thung) -> false
        var kgProductThungPurchase = new Product
        {
            Id = Guid.NewGuid(),
            BaseUnit = "kg",
            Name = "Rau Muong",
            Units = new List<ProductUnit>
            {
                new ProductUnit { Id = Guid.NewGuid(), UnitName = "Thung", IsPurchaseUnit = true, IsBaseUnit = false, ConversionRate = 20m }
            }
        };
        Assert.False(WeightProductHelper.IsWeightProduct(kgProductThungPurchase));
    }

    [Fact]
    public void WeightProductHelper_RoundPackagePrice_ShouldRoundTo100Vnd()
    {
        Assert.Equal(12300m, WeightProductHelper.RoundPackagePrice(12349m));
        Assert.Equal(12400m, WeightProductHelper.RoundPackagePrice(12350m));
        Assert.Equal(12400m, WeightProductHelper.RoundPackagePrice(12399m));
        Assert.Equal(0m, WeightProductHelper.RoundPackagePrice(0m));
    }

    [Fact]
    public void SupplierProductPricing_NormalizeAgreedPriceToPersist_AllBranches()
    {
        Assert.Null(SupplierProductPricing.NormalizeAgreedPriceToPersist(null));
        Assert.Null(SupplierProductPricing.NormalizeAgreedPriceToPersist(0m));

        var ex = Assert.Throws<BusinessException>(() =>
            SupplierProductPricing.NormalizeAgreedPriceToPersist(-5000m));
        Assert.Equal("INVALID_PURCHASE_PRICE", ex.ErrorCode);

        Assert.Equal(50000m, SupplierProductPricing.NormalizeAgreedPriceToPersist(50000m));
        Assert.Equal(75000m, SupplierProductPricing.NormalizeAgreedPriceToPersist((decimal?)75000m));
    }

    [Fact]
    public void SupplierProductPricing_ResolveEffectivePurchasePrice_AllBranches()
    {
        // When agreed > 0 -> returns agreed
        Assert.Equal(45000m, SupplierProductPricing.ResolveEffectivePurchasePrice(45000m, 50000m));
        Assert.Equal(45000m, SupplierProductPricing.ResolveEffectivePurchasePrice((decimal?)45000m, 50000m));
        Assert.Equal(45000m, SupplierProductPricing.GetEffectivePurchasePriceOrZero(45000m, 50000m));
        Assert.Equal(45000m, SupplierProductPricing.GetEffectivePurchasePriceOrZero((decimal?)45000m, 50000m));

        // When agreed is null or 0 -> falls back to productCostPrice ?? 0m
        Assert.Equal(50000m, SupplierProductPricing.ResolveEffectivePurchasePrice(null, 50000m));
        Assert.Equal(50000m, SupplierProductPricing.ResolveEffectivePurchasePrice(0m, 50000m));
        Assert.Equal(0m, SupplierProductPricing.ResolveEffectivePurchasePrice(null, null));
        Assert.Equal(0m, SupplierProductPricing.GetEffectivePurchasePriceOrZero(null, null));
    }

    [Fact]
    public void SupplierProductPricing_ResolvePurchasePriceToPersist_AllBranches()
    {
        // Positive returns effective
        Assert.Equal(40000m, SupplierProductPricing.ResolvePurchasePriceToPersist(40000m, 35000m));
        Assert.Equal(35000m, SupplierProductPricing.ResolvePurchasePriceToPersist(0m, 35000m));

        // <= 0 throws
        var ex1 = Assert.Throws<BusinessException>(() =>
            SupplierProductPricing.ResolvePurchasePriceToPersist(0m, 0m));
        Assert.Equal("INVALID_PURCHASE_PRICE", ex1.ErrorCode);

        var ex2 = Assert.Throws<BusinessException>(() =>
            SupplierProductPricing.ResolvePurchasePriceToPersist(0m, null));
        Assert.Equal("INVALID_PURCHASE_PRICE", ex2.ErrorCode);
    }
}
