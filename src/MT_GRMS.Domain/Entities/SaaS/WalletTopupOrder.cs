using MT_GRMS.Domain.Common;

namespace MT_GRMS.Domain.Entities.SaaS;

/// <summary>
/// Lệnh yêu cầu nạp tiền vào ví gian hàng qua VietQR SePay
/// </summary>
public class WalletTopupOrder : BaseEntity<string>
{
    public WalletTopupOrder()
    {
        Id = Uuid7.NewString();
    }

    public string TenantId { get; set; } = string.Empty;
    public string OrderCode { get; set; } = string.Empty; // e.g. WAL 8392 hoặc WAL8392
    public decimal Amount { get; set; }
    public string TransferContent { get; set; } = string.Empty;
    public string BankCode { get; set; } = string.Empty;
    public string BankName { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string QrUrl { get; set; } = string.Empty;
    public string Status { get; set; } = "PENDING"; // PENDING, COMPLETED, EXPIRED, CANCELLED
    public DateTime ExpiresAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ReferenceCode { get; set; }

    // Navigation
    public Tenant? Tenant { get; set; }
}

