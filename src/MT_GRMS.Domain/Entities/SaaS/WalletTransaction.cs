using MT_GRMS.Domain.Common;

namespace MT_GRMS.Domain.Entities.SaaS;

/// <summary>
/// Sổ cái lịch sử biến động số dư ví của Gian hàng (Tenant Wallet Ledger)
/// </summary>
public class WalletTransaction : BaseEntity<string>
{
    public WalletTransaction()
    {
        Id = Uuid7.NewString();
    }

    public string TenantId { get; set; } = string.Empty;
    public string TransactionCode { get; set; } = string.Empty;
    public string Type { get; set; } = "TOPUP"; // TOPUP, PURCHASE_PLAN, PURCHASE_ADDON, REFUND
    public decimal Amount { get; set; }
    public decimal BalanceBefore { get; set; }
    public decimal BalanceAfter { get; set; }
    public string Description { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = "VietQR"; // VietQR, WALLET, BANK_TRANSFER
    public string Status { get; set; } = "SUCCESS"; // SUCCESS, PENDING, FAILED
    public string? ReferenceCode { get; set; } // Mã tham chiếu từ ngân hàng hoặc SePay
    public string? Metadata { get; set; }

    // Navigation
    public Tenant? Tenant { get; set; }
}

