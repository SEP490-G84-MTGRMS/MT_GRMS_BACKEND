using MT_GRMS.Domain.Exceptions;

namespace MT_GRMS.Application.Suppliers;

/// <summary>
/// Giá nhập hiệu lực: AgreedWholesalePrice nếu &gt; 0, nếu không thì Product.CostPrice.
/// Không tự gán 0 khi không có giá fallback hợp lệ.
/// </summary>
public static class SupplierProductPricing
{
    public static decimal ResolveEffectivePurchasePrice(decimal agreedWholesalePrice, decimal? productCostPrice)
    {
        if (agreedWholesalePrice > 0m)
        {
            return agreedWholesalePrice;
        }

        if (productCostPrice.HasValue)
        {
            return productCostPrice.Value;
        }

        throw new BusinessException(
            "INVALID_PURCHASE_PRICE",
            "Không xác định được giá nhập hợp lệ. Vui lòng nhập giá thỏa thuận hoặc đảm bảo sản phẩm đã có giá vốn.");
    }

    public static decimal GetEffectivePurchasePriceOrZero(decimal agreedWholesalePrice, decimal? productCostPrice)
    {
        if (agreedWholesalePrice > 0m)
        {
            return agreedWholesalePrice;
        }

        return productCostPrice ?? 0m;
    }
}
