using MT_GRMS.Domain.Common;

namespace MT_GRMS.Domain.Entities.Finance;

/// <summary>
/// Cash drawer at a branch checkout till.
/// </summary>
public class BranchCashTill : BaseTenantEntity
{
    public Guid BranchId { get; set; }
    public string TillName { get; set; } = string.Empty; // e.g., 'Két Thu Ngân #1'
    public decimal CurrentBalance { get; set; }
    public Guid? ActiveShiftId { get; set; }
    public bool IsActive { get; set; } = true;
    public byte[]? RowVersion { get; set; }
}