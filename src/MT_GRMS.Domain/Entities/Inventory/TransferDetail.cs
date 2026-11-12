using System.ComponentModel.DataAnnotations.Schema;
using MT_GRMS.Domain.Common;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Domain.Entities.Inventory;

/// <summary>
/// Bảng TransferDetails (Chi tiết mặt hàng điều chuyển kho)
/// </summary>
public class TransferDetail : BaseTenantEntity
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
    public TransferLineStatus LineStatus { get; set; } = TransferLineStatus.Pending;
    public string? DiscrepancyReason { get; set; }
    public string? Note { get; set; }

    // Backward compatibility aliases & computed status
    [NotMapped]
    public TransferLineStatus Status { get => LineStatus; set => LineStatus = value; }

    // Navigations
    public Transfer? Transfer { get; set; }
    public Product? Product { get; set; }
}
