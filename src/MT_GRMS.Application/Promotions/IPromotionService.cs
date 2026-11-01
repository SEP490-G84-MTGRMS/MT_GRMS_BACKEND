using MT_GRMS.Application.Common;
using MT_GRMS.Application.Promotions.Dtos;

namespace MT_GRMS.Application.Promotions;

public interface IPromotionService
{
    // 1. Promotions CRUD & Cart Evaluation
    Task<PagedResult<PromotionResponse>> GetPagedPromotionsAsync(PromotionFilterQuery query, CancellationToken cancellationToken = default);
    Task<PromotionResponse> GetPromotionByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PromotionResponse> CreatePromotionAsync(PromotionCreateRequest request, CancellationToken cancellationToken = default);
    Task<PromotionResponse> UpdatePromotionAsync(Guid id, PromotionUpdateRequest request, CancellationToken cancellationToken = default);
    Task<bool> TogglePromotionStatusAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> DeletePromotionAsync(Guid id, CancellationToken cancellationToken = default);
    Task<EvaluateCartResponse> EvaluateCartAsync(EvaluateCartRequest request, CancellationToken cancellationToken = default);

    // 2. Voucher Batches & Coupon Codes
    Task<PagedResult<VoucherBatchResponse>> GetPagedVoucherBatchesAsync(int pageIndex, int pageSize, string? searchTerm = null, string? status = null, CancellationToken cancellationToken = default);
    Task<VoucherBatchResponse> GetVoucherBatchByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<VoucherBatchResponse> CreateVoucherBatchAsync(VoucherBatchCreateRequest request, CancellationToken cancellationToken = default);
    Task<int> GenerateVoucherCodesAsync(Guid batchId, GenerateVoucherCodesRequest request, CancellationToken cancellationToken = default);
    Task<PagedResult<VoucherCodeDto>> GetVoucherCodesAsync(Guid batchId, int pageIndex, int pageSize, string? status = null, CancellationToken cancellationToken = default);
    Task<ValidateVoucherResponse> ValidateVoucherAsync(ValidateVoucherRequest request, CancellationToken cancellationToken = default);
    Task<RedeemVoucherResponse> RedeemVoucherAsync(RedeemVoucherRequest request, CancellationToken cancellationToken = default);
}

