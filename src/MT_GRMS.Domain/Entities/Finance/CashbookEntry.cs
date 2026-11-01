using MT_GRMS.Domain.Common;
using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Domain.Entities.Finance;

/// <summary>
/// Immutable ledger entry in Cashbook (Append-Only).
/// </summary>
public class CashbookEntry : BaseTenantEntity
{
    public string EntryCode { get; set; } = string.Empty;
    public Guid AccountId { get; set; }
    public FinanceAccountType AccountType { get; set; }
    public Guid? BranchId { get; set; }
    public CashbookEntryType EntryType { get; set; }
    public decimal Amount { get; set; }
    public decimal BalanceAfter { get; set; }
    public string SourceType { get; set; } = string.Empty; // POS, EXPENSE, DEBT, CASH_DROP, BANK_DEPOSIT, SALARY, EQUITY, ADJUSTMENT...
    public Guid? SourceId { get; set; }
    public string ReferenceCode { get; set; } = string.Empty;
    public Guid? ReversedEntryId { get; set; }
    public string AuditHash { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
}