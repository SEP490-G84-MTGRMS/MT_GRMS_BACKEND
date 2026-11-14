using System.ComponentModel.DataAnnotations.Schema;
using MT_GRMS.Domain.Common;
using MT_GRMS.Domain.Entities.Catalog;

namespace MT_GRMS.Domain.Entities.Sales;

/// <summary>
/// Bảng OrderDetails (Chi tiết mặt hàng trên hóa đơn bán lẻ POS)
/// </summary>
public class OrderDetail : BaseEntity
{
    public Guid OrderId { get; set; }
    public Guid ProductId { get; set; }
    public Guid? PromotionId { get; set; }
    public int LineNumber { get; set; } = 1;
    public decimal Qty { get; set; } = 1.00m;
    public decimal UnitPrice { get; set; } = 0.00m;
    public decimal DiscountAmount { get; set; } = 0.00m;
    public decimal ActualPrice { get; set; } = 0.00m;
    public decimal CostPriceAtSale { get; set; } = 0.00m; // Giá vốn chốt cứng tại thời điểm xuất bán (Chống méo mó hồi tố)
    public bool IsGift { get; set; } = false;

    // Đơn vị tính quy đổi trên đơn hàng
    public Guid? ProductUnitId { get; set; }
    public string? UnitName { get; set; }
    public decimal ConversionRate { get; set; } = 1.00m;

    /// <summary>Có giá trị khi dòng hóa đơn là bán khay.</summary>
    public Guid? PackageId { get; set; }

    // Thuế GTGT từng dòng hàng theo TT 152/2025/TT-BTC
    public decimal AppliedVatRate { get; set; } = 0.01m;
    public decimal VatAmount { get; set; } = 0m;
    public decimal VatDiscountAmount { get; set; } = 0m;

    // Backward compatibility aliases
    [NotMapped]
    public decimal Quantity { get => Qty; set => Qty = value; }
    [NotMapped]
    public decimal TotalLineAmount { get => ActualPrice * Qty; set => ActualPrice = Qty > 0 ? value / Qty : value; }
    [NotMapped]
    public string ProductName { get; set; } = string.Empty;

    // Navigations
    public Order? Order { get; set; }
    public Product? Product { get; set; }
    public ProductUnit? ProductUnit { get; set; }
    public Promotion? Promotion { get; set; }
}
