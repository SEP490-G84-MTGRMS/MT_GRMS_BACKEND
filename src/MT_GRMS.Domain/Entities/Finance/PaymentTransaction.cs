using System;
using MT_GRMS.Domain.Common;

namespace MT_GRMS.Domain.Entities.Finance;

/// <summary>
/// Lưu vết giao dịch thanh toán ngân hàng qua SePay / VietQR để chống tấn công lặp lại (Idempotency) và đối soát
/// </summary>
public class PaymentTransaction : BaseTenantEntity
{
    public Guid? OrderId { get; set; }
    public string BankTransactionId { get; set; } = string.Empty; // ID giao dịch SePay / Số GD Ngân hàng (Chỉ mục Unique)
    public string Gateway { get; set; } = string.Empty; // MBBank, Vietcombank, VietQR...
    public decimal AmountIn { get; set; }
    public decimal? Accumulated { get; set; }
    public string TransactionContent { get; set; } = string.Empty;
    public string? ReferenceNumber { get; set; }
    public DateTime TransactionDate { get; set; }
    public bool VerifiedBySepayApi { get; set; } = false;
    public string? RawPayloadJson { get; set; }
}

