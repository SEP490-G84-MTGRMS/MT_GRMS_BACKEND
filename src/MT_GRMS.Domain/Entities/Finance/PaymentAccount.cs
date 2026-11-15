using MT_GRMS.Domain.Common;

namespace MT_GRMS.Domain.Entities.Finance;

/// <summary>
/// Tài khoản ngân hàng vật lý nhận thanh toán. Một tài khoản có thể được dùng tại nhiều chi nhánh.
/// Thông tin bí mật SePay vẫn được quản lý ở StorePaymentConfig theo tenant.
/// </summary>
public class PaymentAccount : BaseTenantEntity
{
    public string BankCode { get; set; } = "MB";
    public string BankName { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
    public string AccountHolderName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    public ICollection<BranchPaymentAccount> BranchAssignments { get; set; } = new List<BranchPaymentAccount>();
}
