using MT_GRMS.Application.Common;
using MT_GRMS.Application.Promotions.Dtos;
using MT_GRMS.Domain.Entities.Sales;

namespace MT_GRMS.Application.Promotions;

public interface IPromotionRepository : IRepository<Promotion>
{
    Task<Promotion?> GetByIdWithConditionsAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> ExistsByCodeAsync(string code, CancellationToken cancellationToken = default);
    Task<(IReadOnlyList<Promotion> Items, int TotalCount)> GetPagedListAsync(PromotionFilterQuery query, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Promotion>> GetActivePromotionsAsync(DateTime atUtc, string? branchId = null, CancellationToken cancellationToken = default);
    
    Task ReplaceConditionsAsync(Guid promotionId, IEnumerable<PromotionCondition> conditions, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<Promotion> Batches, int TotalCount)> GetPagedVoucherBatchesAsync(int pageIndex, int pageSize, string? searchTerm = null, string? status = null, CancellationToken cancellationToken = default);
    Task AddVoucherCodesAsync(IEnumerable<VoucherCode> codes, CancellationToken cancellationToken = default);
    Task<bool> VoucherCodeExistsAsync(string code, CancellationToken cancellationToken = default);
    Task<VoucherCode?> GetVoucherCodeAsync(string code, CancellationToken cancellationToken = default);
    Task<(IReadOnlyList<VoucherCode> Codes, int TotalCount)> GetPagedVoucherCodesAsync(Guid promotionId, int pageIndex, int pageSize, string? status = null, CancellationToken cancellationToken = default);
    Task<bool> RedeemVoucherCodeAtomicAsync(string code, Guid orderId, string orderCode, Guid? customerId, string? customerName, string? customerPhone, CancellationToken cancellationToken = default);
}

