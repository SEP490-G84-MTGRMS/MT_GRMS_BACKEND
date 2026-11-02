using MT_GRMS.Domain.Common;
using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Domain.Entities.Finance;

/// <summary>
/// Consolidated financial account for cash drawers (tills), branch safes, and bank accounts.
/// </summary>
public class FinanceAccount : BaseTenantEntity
{
    public Guid? BranchId { get; set; }
    public FinanceAccountType AccountType { get; set; }
    public string AccountName { get; set; } = string.Empty;
    public decimal CurrentBalance { get; set; }
    public string? BankName { get; set; }
    public string? AccountNumber { get; set; }
    public string? AccountHolderName { get; set; }
    public Guid? ActiveShiftId { get; set; }
    public Guid? ManagerUserId { get; set; }
    public bool IsActive { get; set; } = true;
    public byte[]? RowVersion { get; set; }
}
