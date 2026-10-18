using MT_GRMS.Domain.Common;
using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Domain.Entities.Supplier;

public class Supplier : BaseTenantEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? TradeName { get; set; }
    public SupplierType SupplierType { get; set; } = SupplierType.Goods;

    public Guid? CategoryId { get; set; }
    public SupplierCategory? Category { get; set; }
    public string CategoryCode { get; set; } = "FRESH";

    public string ContactPerson { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? ContactPhone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? Ward { get; set; }
    public string? District { get; set; }
    public string? Province { get; set; }

    public int PaymentTermDays { get; set; } = 0;
    public decimal CreditLimit { get; set; } = 0;
    public decimal CurrentDebt { get; set; } = 0;
    public decimal OverdueDebt { get; set; } = 0;
    public decimal Rating { get; set; } = 5.0m;
    public SupplierStatus Status { get; set; } = SupplierStatus.Active;

    public string? LinkedCustomerId { get; set; }
    public string? TaxCode { get; set; }
    public string? CompanyName { get; set; }
    public string? InvoiceAddress { get; set; }
    public string? BankName { get; set; }
    public string? BankAccount { get; set; }
    public string? BankAccountNumber { get => BankAccount; set => BankAccount = value; }
    public string? AccountHolder { get; set; }
    public string? BankAccountHolder { get => AccountHolder; set => AccountHolder = value; }
    public string? Note { get; set; }
    public string? Notes { get => Note; set => Note = value; }

    public SupplierDeliveryConfig? DeliveryConfig { get; set; }
    public ICollection<SupplierAPLedger> APLedgers { get; set; } = new List<SupplierAPLedger>();
    public ICollection<SupplierBlackoutDate> BlackoutDates { get; set; } = new List<SupplierBlackoutDate>();
}
