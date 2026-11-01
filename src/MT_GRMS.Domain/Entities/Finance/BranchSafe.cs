using MT_GRMS.Domain.Common;

namespace MT_GRMS.Domain.Entities.Finance;

/// <summary>
/// Branch safe vault holding surplus cash managed by branch manager.
/// </summary>
public class BranchSafe : BaseTenantEntity
{
    public Guid BranchId { get; set; }
    public string SafeName { get; set; } = string.Empty; // e.g., 'Két Sắt Chi Nhánh #1'
    public decimal CurrentBalance { get; set; }
    public Guid? ManagerUserId { get; set; }
    public byte[]? RowVersion { get; set; }
}