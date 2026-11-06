using MT_GRMS.Domain.Common;

namespace MT_GRMS.Domain.Entities.Finance;

/// <summary>
/// Cấu hình tài khoản ngân hàng và cổng thanh toán SePay theo từng Tenant (Cửa hàng)
/// </summary>
public class StorePaymentConfig : BaseTenantEntity
{
    public string BankCode { get; set; } = "MB"; // MB, VCB, ACB, TPB, TCB, VPB...
    public string BankName { get; set; } = "MBBank - Ngân hàng Quân Đội";
    public string BankAccountNumber { get; set; } = string.Empty;
    public string BankAccountName { get; set; } = string.Empty;
    public string SepayApiKeyEncrypted { get; set; } = string.Empty; // Mã hóa AES-256
    public string SepayWebhookSecret { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

