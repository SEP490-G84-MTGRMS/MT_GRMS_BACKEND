using System;
using System.Linq;
using MT_GRMS.Domain.Entities.Catalog;

namespace MT_GRMS.Application.Inventory;

/// <summary>
/// SP cân kg = BaseUnit là kg và đơn vị nhập là chính đơn vị kg gốc.
/// </summary>
public static class WeightProductHelper
{
    public static string NormalizeUnit(string? unit)
        => (unit ?? string.Empty).Trim().ToLowerInvariant();

    public static bool IsKgUnit(string? unit)
        => NormalizeUnit(unit) == "kg";

    public static bool IsWeightProduct(Product product)
    {
        ArgumentNullException.ThrowIfNull(product);
        if (!IsKgUnit(product.BaseUnit))
        {
            return false;
        }

        var units = product.Units?.Where(u => !u.IsDeleted).ToList() ?? new();
        if (units.Count == 0)
        {
            // Chưa có ProductUnit: coi BaseUnit kg là đủ.
            return true;
        }

        var purchase = units.FirstOrDefault(u => u.IsPurchaseUnit);
        if (purchase == null)
        {
            var baseUnit = units.FirstOrDefault(u => u.IsBaseUnit);
            return baseUnit == null || (IsKgUnit(baseUnit.UnitName) && baseUnit.ConversionRate == 1m);
        }

        return purchase.IsBaseUnit
            && IsKgUnit(purchase.UnitName)
            && purchase.ConversionRate == 1m;
    }

    /// <summary>Làm tròn giá khay về 100đ.</summary>
    public static decimal RoundPackagePrice(decimal price)
        => Math.Round(price / 100m, MidpointRounding.AwayFromZero) * 100m;
}
