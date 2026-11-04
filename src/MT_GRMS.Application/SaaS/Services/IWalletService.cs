using MT_GRMS.Application.SaaS.Dtos;

namespace MT_GRMS.Application.SaaS.Services;

public interface IWalletService
{
    Task<TopupOrderResponse> CreateTopupOrderAsync(string tenantId, CreateTopupOrderRequest request, CancellationToken cancellationToken = default);
    Task<TopupOrderStatusResponse?> GetTopupOrderStatusAsync(string tenantId, string orderCode, CancellationToken cancellationToken = default);
    Task<WalletBalanceResponse> GetWalletBalanceAsync(string tenantId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WalletTransactionDto>> GetWalletTransactionsAsync(string tenantId, string? type = null, CancellationToken cancellationToken = default);
    Task<SepayWebhookResult> ProcessSepayWebhookAsync(SepayWebhookPayload payload, CancellationToken cancellationToken = default);
    Task DeductWalletAsync(string tenantId, decimal amount, string description, string referenceCode, CancellationToken cancellationToken = default);
}

