using MT_GRMS.Domain.Common;
using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Domain.Entities.Finance;

public class CustomerDebtInvoice : BaseTenantEntity
{
    public Guid CustomerId { get; set; }
    public Guid OrderId { get; set; }
    public string OrderCode { get; set; } = string.Empty;
    public Guid BranchId { get; set; }
    public decimal TotalDebtAmount { get; set; }
    public decimal RemainingDebtAmount { get; set; }
    public DateTime DueDate { get; set; }
    public DebtInvoiceStatus Status { get; set; } = DebtInvoiceStatus.Unpaid;
    public DateTime? FullyPaidAtUtc { get; set; }
    public string? Notes { get; set; }
}
