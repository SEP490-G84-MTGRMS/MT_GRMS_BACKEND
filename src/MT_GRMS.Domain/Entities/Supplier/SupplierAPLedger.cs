using MT_GRMS.Domain.Common;
using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Domain.Entities.Supplier;

public class SupplierAPLedger : BaseTenantEntity
{
    public Guid SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    public DateTime TransactionDate { get; set; } = DateTime.UtcNow;
    public APDocType DocType { get; set; }
    public string DocNo { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public int Direction { get; set; } = 1; // 1: increase debt, -1: decrease debt
    public decimal BalanceAfter { get; set; }
    public string CreatedByName { get; set; } = string.Empty;
    public string? IdempotencyKey { get; set; }
}
