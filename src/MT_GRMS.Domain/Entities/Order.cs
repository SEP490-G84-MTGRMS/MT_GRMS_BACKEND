using MT_GRMS.Domain.Common;
using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Domain.Entities;

public class Order : BaseTenantEntity
{
    public string OrderCode { get; set; } = string.Empty;
    public Guid StoreId { get; set; }
    public decimal SubTotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Paid;
    public string? Note { get; set; }

    public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
}

public class OrderItem : BaseTenantEntity
{
    public Guid OrderId { get; set; }
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public decimal Quantity { get; set; }
    public decimal TotalLineAmount { get; set; }
    public decimal AppliedVatRate { get; set; } = 0.01m;
    public decimal VatAmount { get; set; } = 0m;
    public decimal VatDiscountAmount { get; set; } = 0m;

    public Order? Order { get; set; }
}
