using MT_GRMS.Domain.Common;
using MT_GRMS.Domain.Entities.Catalog;

namespace MT_GRMS.Domain.Entities.Inventory;

/// <summary>
/// Bảng TransferDetails (Chi tiết mặt hàng điều chuyển kho)
/// </summary>
public class TransferDetail : BaseEntity
{
    public Guid TransferId { get; set; }
    public Guid ProductId { get; set; }
    public decimal SentQuantity { get; set; } = 0.00m;
    public decimal ReceivedQuantity { get; set; } = 0.00m;
    public decimal DamagedQuantity { get; set; } = 0.00m;
    public string PackagingUnit { get; set; } = string.Empty;
    public string? BatchNumber { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public decimal UnitPrice { get; set; } = 0.00m;
    public decimal Amount { get; set; } = 0.00m;
    public string? DiscrepancyReason { get; set; }
    public string? Note { get; set; }

    // Navigations
    public Transfer? Transfer { get; set; }
    public Product? Product { get; set; }
}
