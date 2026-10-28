using MT_GRMS.Domain.Common;
using MT_GRMS.Domain.Entities.SaaS;

namespace MT_GRMS.Domain.Entities.Finance;

/// <summary>
/// Bảng ServiceExpenses (Chi phí dịch vụ vận hành cửa hàng)
/// </summary>
public class ServiceExpense : BaseTenantEntity
{
    public Guid BranchId { get; set; }
    public string ExpenseCode { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public decimal Amount { get; set; } = 0.00m;
    public DateTime ExpenseDate { get; set; }
    public DateTime? DueDate { get; set; }
    public string? VendorName { get; set; }
    public string Status { get; set; } = "Paid";
    public string? InvoiceNumber { get; set; }
    public string? Note { get; set; }

    // Navigations
    public Branch? Branch { get; set; }
}
