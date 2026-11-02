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
    
    // Quản lý công nợ khách hàng (Đã hợp nhất từ CustomerDebtProfiles)
    public bool AllowDebt { get; set; } = false;
    public decimal TotalDebt { get; set; } = 0.00m;
    public decimal DebtLimit { get; set; } = 5000000.00m;
    public decimal PrepaymentBalance { get; set; } = 0.00m;
    public int MaxDebtDays { get; set; } = 30;
    public CustomerDebtStatus DebtStatus { get; set; } = CustomerDebtStatus.GoodStanding;
    public bool IsBlockedForDebt { get; set; } = false;
    public DateTime? LastDebtIncurredAtUtc { get; set; }
    public DateTime? LastRepaymentAtUtc { get; set; }
    public string? Notes { get; set; }
    public byte[]? RowVersion { get; set; }

    // Navigations
    public ICollection<Order> Orders { get; set; } = new List<Order>();
}
