using MT_GRMS.Domain.Common;
using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Domain.Entities.Finance;

public class CustomerDebtProfile : BaseTenantEntity
{
    public Guid CustomerId { get; set; }
    public decimal CreditLimit { get; set; }
    public int MaxDebtDays { get; set; } = 30;
    public decimal CurrentDebtBalance { get; set; }
    public decimal PrepaymentBalance { get; set; }
    public CustomerDebtStatus DebtStatus { get; set; } = CustomerDebtStatus.GoodStanding;
    public DateTime? LastDebtIncurredAtUtc { get; set; }
    public DateTime? LastRepaymentAtUtc { get; set; }
    public string? Notes { get; set; }
    public byte[]? RowVersion { get; set; }
}
