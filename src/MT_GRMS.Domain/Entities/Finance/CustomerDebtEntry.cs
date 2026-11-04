using MT_GRMS.Domain.Common;
using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Domain.Entities.Finance;

/// <summary>
/// Immutable customer debt ledger entry (Append-Only).
/// </summary>
public class CustomerDebtEntry : BaseTenantEntity
{
    public string EntryCode { get; set; } = string.Empty;
    public Guid CustomerId { get; set; }
    public Guid? BranchId { get; set; }
    public CustomerDebtType EntryType { get; set; }
    public decimal Amount { get; set; }
    public decimal BalanceAfter { get; set; }
    public decimal PrepaymentAfter { get; set; }
    public string ReferenceCode { get; set; } = string.Empty;
    public Guid? SourceId { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
    public string Description { get; set; } = string.Empty;
}
