using System.ComponentModel.DataAnnotations.Schema;
using MT_GRMS.Domain.Common;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Domain.Entities.Sales;

/// <summary>
/// Bảng Orders (Hóa đơn bán lẻ POS)
/// </summary>
public class Order : BaseTenantEntity
{
    public Guid BranchId { get; set; }
    public Guid? CustomerId { get; set; }
    public string OrderCode { get; set; } = string.Empty;
    public DateTime OrderDate { get; set; } = DateTime.UtcNow;
    public decimal TotalAmount { get; set; } = 0.00m;
    public decimal DiscountAmount { get; set; } = 0.00m;
    public decimal FinalAmount { get; set; } = 0.00m;
    public decimal DepositAmount { get; set; } = 0.00m;
    public string PaymentMethod { get; set; } = "CASH";
    public string PaymentStatus { get; set; } = "PAID";
    public string Status { get; set; } = "Completed";

    // Backward compatibility aliases
    [NotMapped]
    public Guid StoreId { get => BranchId; set => BranchId = value; }
    [NotMapped]
    public decimal SubTotal { get => TotalAmount; set => TotalAmount = value; }
    [NotMapped]
    public PaymentMethod PaymentMethodEnum
    {
        get => Enum.TryParse<PaymentMethod>(PaymentMethod, true, out var m) ? m : Enums.PaymentMethod.Cash;
        set => PaymentMethod = value.ToString().ToUpper();
    }
    [NotMapped]
    public PaymentStatus PaymentStatusEnum
    {
        get => Enum.TryParse<PaymentStatus>(PaymentStatus, true, out var s) ? s : Enums.PaymentStatus.Paid;
        set => PaymentStatus = value.ToString().ToUpper();
    }
    [NotMapped]
    public string? Note { get; set; }
    [NotMapped]
    public ICollection<OrderDetail> Items { get => Details; set => Details = value; }

    // Navigations
    public Branch? Branch { get; set; }
    public Customer? Customer { get; set; }
    public ICollection<OrderDetail> Details { get; set; } = new List<OrderDetail>();
}
