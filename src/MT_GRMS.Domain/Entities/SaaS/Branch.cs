using MT_GRMS.Domain.Common;

namespace MT_GRMS.Domain.Entities.SaaS;

public class Branch : BaseTenantEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? ManagerName { get; set; }
    public string Status { get; set; } = "Active";
    public int PendingOrdersCount { get; set; } = 0;
    public DateTime? LastOrderDate { get; set; }
}
