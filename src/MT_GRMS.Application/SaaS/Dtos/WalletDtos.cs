using System.Text.Json.Serialization;

namespace MT_GRMS.Application.SaaS.Dtos;

public record CreateTopupOrderRequest(decimal Amount);

public record TopupOrderResponse(
    string OrderCode,
    decimal Amount,
    string BankCode,
    string BankName,
    string AccountNumber,
    string AccountName,
    string TransferContent,
    string QrUrl,
    DateTime ExpiresAt
);

public record TopupOrderStatusResponse(
    string OrderCode,
    string Status,
    decimal Amount,
    decimal? NewBalance,
    DateTime? CompletedAt
);

public record WalletBalanceResponse(
    decimal WalletBalance,
    decimal TotalTopup,
    decimal TotalSpent
);

public record WalletTransactionDto(
    string Id,
    string TransactionCode,
    string Type,
    decimal Amount,
    decimal BalanceBefore,
    decimal BalanceAfter,
    string Description,
    string PaymentMethod,
    string Status,
    string? ReferenceCode,
    DateTime CreatedAt
);

public class SepayWebhookPayload
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("gateway")]
    public string? Gateway { get; set; }

    [JsonPropertyName("transactionDate")]
    public string? TransactionDate { get; set; }

    [JsonPropertyName("accountNumber")]
    public string? AccountNumber { get; set; }

    [JsonPropertyName("subAccount")]
    public string? SubAccount { get; set; }

    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("transferType")]
    public string? TransferType { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("transferAmount")]
    public decimal TransferAmount { get; set; }

    [JsonPropertyName("referenceCode")]
    public string? ReferenceCode { get; set; }

    [JsonPropertyName("accumulated")]
    public decimal? Accumulated { get; set; }
}

public record SepayWebhookResult(
    bool Success,
    string Message,
    string? OrderCode = null,
    string? TenantId = null,
    decimal Amount = 0
);

