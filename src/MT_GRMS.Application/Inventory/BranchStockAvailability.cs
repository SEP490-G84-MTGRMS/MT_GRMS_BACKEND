using System;
using MT_GRMS.Domain.Entities.Inventory;

namespace MT_GRMS.Application.Inventory;

/// <summary>
/// RULE-01: công thức khả dụng bán thống nhất toàn hệ thống.
/// SellableAvailable = max(0, OnHand - Reserved - Quarantined - PackagedWeight); hết hạn = 0.
/// </summary>
public static class BranchStockAvailability
{
    public static decimal GetSellableAvailable(
        BranchStock stock,
        DateTime? asOfUtcDate = null,
        decimal packagedWeight = 0m)
    {
        ArgumentNullException.ThrowIfNull(stock);

        var today = (asOfUtcDate ?? DateTime.UtcNow).Date;
        if (stock.ExpiryDate.HasValue && stock.ExpiryDate.Value.Date < today)
        {
            return 0m;
        }

        var packaged = Math.Max(0m, packagedWeight);
        return Math.Max(0m, stock.QuantityOnHand - stock.QuantityReserved - stock.QuantityQuarantined - packaged);
    }

    public static decimal GetRawSellableKg(
        BranchStock stock,
        decimal activePackageWeight,
        DateTime? asOfUtcDate = null)
        => GetSellableAvailable(stock, asOfUtcDate, activePackageWeight);

    public static bool MeetsPackageInvariant(decimal quantityOnHand, decimal activePackageWeight)
        => quantityOnHand >= Math.Max(0m, activePackageWeight) - 0.0005m;

    public static decimal SumSellableAvailable(
        System.Collections.Generic.IEnumerable<BranchStock> stocks,
        DateTime? asOfUtcDate = null)
    {
        var total = 0m;
        foreach (var stock in stocks)
        {
            total += GetSellableAvailable(stock, asOfUtcDate);
        }

        return total;
    }
}
