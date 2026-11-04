using MT_GRMS.Domain.Exceptions;

namespace MT_GRMS.Application.Suppliers;

/// <summary>
/// Giá nhập NCC: nếu client gửi giá &gt; 0 thì dùng giá đó;
/// nếu 0/chưa nhập thì mặc định lấy Product.CostPrice (Master) để lưu.
/// </summary>
public static class SupplierProductPricing
{
    /// <summary>
    /// Giá sẽ ghi vào SupplierProducts.AgreedWholesalePrice.
    /// </summary>
    public static decimal ResolvePurchasePriceToPersist(decimal agreedWholesalePrice, decimal? productCostPrice)
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
            "Không xác định được giá nhập hợp lệ. Vui lòng nhập giá nhập hoặc đảm bảo sản phẩm Master đã có giá vốn.");
    }

    public static decimal ResolveEffectivePurchasePrice(decimal agreedWholesalePrice, decimal? productCostPrice)
        => ResolvePurchasePriceToPersist(agreedWholesalePrice, productCostPrice);

    public static decimal GetEffectivePurchasePriceOrZero(decimal agreedWholesalePrice, decimal? productCostPrice)
    {
        if (agreedWholesalePrice > 0m)
        {
            return agreedWholesalePrice;
        }

        return productCostPrice ?? 0m;
    }
}
