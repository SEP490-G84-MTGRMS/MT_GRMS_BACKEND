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
    public bool IsGift { get; set; } = false;

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
    public Promotion? Promotion { get; set; }
}
