using MT_GRMS.Domain.Common;

namespace MT_GRMS.Domain.Entities.Sales;

/// <summary>
/// Bảng Customers (Khách hàng thành viên & Công nợ)
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
    public decimal TotalDebt { get; set; } = 0.00m;
    public decimal DebtLimit { get; set; } = 5000000.00m;
    public bool IsBlockedForDebt { get; set; } = false;
    public bool AllowDebt { get; set; } = false;

    // Navigations
    public ICollection<Order> Orders { get; set; } = new List<Order>();
}
