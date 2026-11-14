using MT_GRMS.Domain.Exceptions;

namespace MT_GRMS.Application.Suppliers;

/// <summary>
/// RULE giá nguồn: AgreedWholesalePrice nullable (NULL / 0 = chưa thỏa thuận).
/// Giá hiệu lực khi đặt/PO = Agreed (nếu &gt; 0) ?? Master CostPrice.
/// Không ghi Master vào cột Agreed khi user để trống.
/// </summary>
public static class SupplierProductPricing
{
    /// <summary>
    /// Chuẩn hóa giá thỏa thuận để lưu DB: trống hoặc 0 → null; âm → lỗi.
    /// </summary>
    public static decimal? NormalizeAgreedPriceToPersist(decimal? agreedWholesalePrice)
    {
        if (!agreedWholesalePrice.HasValue)
        {
            return null;
        }

        if (agreedWholesalePrice.Value < 0m)
        {
            throw new BusinessException("INVALID_PURCHASE_PRICE", "Giá thỏa thuận NCC không được âm.");
        }

        // 0 = chưa thỏa thuận (không dùng 0 thay NULL)
        if (agreedWholesalePrice.Value == 0m)
        {
            return null;
        }

        return agreedWholesalePrice.Value;
    }

    public static decimal? NormalizeAgreedPriceToPersist(decimal agreedWholesalePrice)
        => NormalizeAgreedPriceToPersist((decimal?)agreedWholesalePrice);

    /// <summary>
    /// Giá dùng khi lập PO / Matrix / hiển thị hiệu lực.
    /// </summary>
    public static decimal ResolveEffectivePurchasePrice(decimal? agreedWholesalePrice, decimal? productCostPrice)
    {
        if (agreedWholesalePrice.HasValue && agreedWholesalePrice.Value > 0m)
        {
            return agreedWholesalePrice.Value;
        }

        return productCostPrice ?? 0m;
    }

    public static decimal ResolveEffectivePurchasePrice(decimal agreedWholesalePrice, decimal? productCostPrice)
        => ResolveEffectivePurchasePrice((decimal?)agreedWholesalePrice, productCostPrice);

    public static decimal GetEffectivePurchasePriceOrZero(decimal? agreedWholesalePrice, decimal? productCostPrice)
        => ResolveEffectivePurchasePrice(agreedWholesalePrice, productCostPrice);

    public static decimal GetEffectivePurchasePriceOrZero(decimal agreedWholesalePrice, decimal? productCostPrice)
        => ResolveEffectivePurchasePrice(agreedWholesalePrice, productCostPrice);

    /// <summary>Legacy name — chỉ còn dùng chỗ bắt buộc có giá &gt; 0.</summary>
    public static decimal ResolvePurchasePriceToPersist(decimal agreedWholesalePrice, decimal? productCostPrice)
    {
        var effective = ResolveEffectivePurchasePrice(agreedWholesalePrice, productCostPrice);
        if (effective <= 0m)
        {
            throw new BusinessException(
                "INVALID_PURCHASE_PRICE",
                "Không xác định được giá nhập hợp lệ. Vui lòng nhập giá thỏa thuận hoặc đảm bảo sản phẩm Master đã có giá vốn.");
        }

        return effective;
    }
}
