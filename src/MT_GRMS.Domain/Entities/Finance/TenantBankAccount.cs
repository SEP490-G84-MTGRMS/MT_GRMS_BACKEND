using MT_GRMS.Domain.Common;

namespace MT_GRMS.Domain.Entities.Finance;

/// <summary>
/// Tenant bank account used for VietQR and central corporate cash flow.
/// </summary>
public class TenantBankAccount : BaseTenantEntity
{
    public string BankName { get; set; } = string.Empty; // e.g. Vietcombank, MB
    public string AccountNumber { get; set; } = string.Empty;
    public string AccountHolderName { get; set; } = string.Empty;
    public decimal CurrentBalance { get; set; }
    public bool IsActive { get; set; } = true;
    public byte[]? RowVersion { get; set; }
}