using MT_GRMS.Domain.Common;
using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Domain.Entities.Sales;

/// <summary>
/// Bảng Customers (Khách hàng thành viên & Công nợ hợp nhất - Single Source of Truth)
/// </summary>
public class Customer : BaseTenantEntity
{
    public string CustomerCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string MembershipTier { get; set; } = "Standard";
    public int RewardPoints { get; set; } = 0;
    public decimal TotalSpending { get; set; } = 0.00m;
    
    // Tổng dư nợ khách hàng. Chỉ StoreOwner được phép phát sinh đơn bán nợ.
    public decimal TotalDebt { get; set; } = 0.00m;
    public DateTime? LastDebtIncurredAtUtc { get; set; }
    public DateTime? LastRepaymentAtUtc { get; set; }
    public string? Notes { get; set; }
    public byte[]? RowVersion { get; set; }

    // Navigations
    public ICollection<Order> Orders { get; set; } = new List<Order>();
}
