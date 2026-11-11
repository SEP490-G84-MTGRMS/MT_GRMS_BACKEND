using System;
using MT_GRMS.Domain.Entities.Inventory;

namespace MT_GRMS.Application.Inventory;

/// <summary>
/// RULE-01: công thức khả dụng bán thống nhất toàn hệ thống.
/// SellableAvailable = max(0, OnHand - Reserved - Quarantined); hết hạn = 0.
/// </summary>
public static class BranchStockAvailability
{
    public static decimal GetSellableAvailable(BranchStock stock, DateTime? asOfUtcDate = null)
    {
        ArgumentNullException.ThrowIfNull(stock);

        var today = (asOfUtcDate ?? DateTime.UtcNow).Date;
        if (stock.ExpiryDate.HasValue && stock.ExpiryDate.Value.Date < today)
        {
            return 0m;
        }

        return Math.Max(0m, stock.QuantityOnHand - stock.QuantityReserved - stock.QuantityQuarantined);
    }

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
