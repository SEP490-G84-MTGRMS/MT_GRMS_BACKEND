using MT_GRMS.Domain.Common;
using MT_GRMS.Domain.Entities.SaaS;

namespace MT_GRMS.Domain.Entities.Finance;

/// <summary>Gán tài khoản nhận thanh toán cho chi nhánh.</summary>
public class BranchPaymentAccount : BaseTenantEntity
{
    public Guid BranchId { get; set; }
    public Guid PaymentAccountId { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;

    public Branch? Branch { get; set; }
    public PaymentAccount? PaymentAccount { get; set; }
}
