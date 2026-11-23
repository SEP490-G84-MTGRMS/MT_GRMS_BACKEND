using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MT_GRMS.Application.Batches.Dtos;
using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Entities.Inventory;
using MT_GRMS.Domain.Exceptions;

namespace MT_GRMS.Application.Batches;

/// <summary>
/// Service xử lý quản lý lô hàng & hạn sử dụng theo chuẩn FEFO (First Expired, First Out).
/// Tồn live = BranchStocks. Bảng Batches/StockOnHands là legacy — không tạo tồn qua API này.
/// </summary>
public class BatchService : IBatchService
{
    private readonly IBatchRepository _repository;
    private readonly ITenantContext _tenantContext;
    private readonly IUnitOfWork _unitOfWork;

    public BatchService(
        IBatchRepository repository,
        ITenantContext tenantContext,
        IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _tenantContext = tenantContext;
        _unitOfWork = unitOfWork;
    }

    public async Task<BatchPagedResultDto> GetPagedBatchesAsync(
        BatchFilterQuery query,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.CurrentTenantId ?? string.Empty;
        return await _repository.GetPagedBatchesAsync(tenantId, query, cancellationToken);
    }

    public async Task<BatchListItemDto> GetBatchByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.CurrentTenantId ?? string.Empty;
        var batch = await _repository.GetBatchByIdAsync(tenantId, id, cancellationToken);
        if (batch == null)
            throw new KeyNotFoundException($"Không tìm thấy thông tin lô hàng với ID {id}.");

        return batch;
    }

    public Task<BatchResponse> CreateBatchAsync(
        BatchCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        // Legacy: trước đây tạo Batches + StockOnHands (tồn ma, POS không đọc).
        // Tồn live chỉ tăng qua GRN COMPLETED → BranchStocks.
        throw new BusinessException(
            "BATCH_CREATE_DEPRECATED",
            "Không còn hỗ trợ tạo lô/tồn thủ công. Hãy nhập kho (Goods Receipt) để tăng BranchStocks.");
    }

    public async Task<IReadOnlyList<ExpiryAlertDto>> GetNearExpiryBatchesAsync(
        int daysThreshold = 30,
        Guid? branchId = null,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.CurrentTenantId ?? string.Empty;
        return await _repository.GetNearExpiryBatchesAsync(tenantId, daysThreshold, branchId, cancellationToken);
    }

    public async Task<FefoDiscountResponse> ApplyFefoDiscountAsync(Guid id, ApplyFefoDiscountRequest request, CancellationToken cancellationToken = default)
    {
        TenantRoleGuard.RequireStoreOwner(_tenantContext, "lịch giảm giá FEFO");

        var tenantId = _tenantContext.CurrentTenantId ?? string.Empty;
        var stock = await _repository.GetBranchStockForUpdateAsync(tenantId, id, cancellationToken)
            ?? throw new KeyNotFoundException($"Không tìm thấy lô hàng với ID {id}.");

        var localNow = BusinessDateResolver.ToLocal(DateTime.UtcNow);
        if (stock.ExpiryDate.HasValue && stock.ExpiryDate.Value.Date < localNow.Date)
            throw new InvalidOperationException("Lô đã hết hạn không được phép áp dụng giảm giá để bán.");

        if (request.DiscountPercent < 0m || request.DiscountPercent > 90m)
            throw new ArgumentOutOfRangeException(nameof(request.DiscountPercent), "Mức giảm FEFO phải từ 0% đến 90%.");

        if (!request.IsActive || request.DiscountPercent == 0m)
        {
            stock.FefoDiscountPercent = 0m;
            stock.IsFefoDiscountActive = false;
            stock.FefoDiscountStartAtUtc = null;
            stock.FefoDiscountEndAtUtc = null;
        }
        else
        {
            if (request.DiscountPercent < 10m)
                throw new ArgumentOutOfRangeException(nameof(request.DiscountPercent), "Mức giảm FEFO phải từ 10% đến 90%.");
            if (!stock.ExpiryDate.HasValue)
                throw new InvalidOperationException("Lô không có hạn sử dụng nên không thể lập lịch giảm giá cận hạn.");
            if (stock.QuantityOnHand - stock.QuantityReserved - stock.QuantityQuarantined <= 0m)
                throw new InvalidOperationException("Lô không còn tồn khả dụng để áp dụng giảm giá.");
            var nearExpiryThreshold = stock.Product?.NearExpiryThresholdDays is > 0
                ? stock.Product.NearExpiryThresholdDays.Value
                : 30;
            if (request.StartDaysBeforeExpiry < 0 || request.StartDaysBeforeExpiry > nearExpiryThreshold)
                throw new ArgumentOutOfRangeException(nameof(request.StartDaysBeforeExpiry),
                    $"Mốc bắt đầu phải nằm trong cửa sổ cận hạn 0–{nearExpiryThreshold} ngày của sản phẩm.");

            var startTime = request.StartTime ?? TimeSpan.Zero;
            if (startTime < TimeSpan.Zero || startTime >= TimeSpan.FromDays(1))
                throw new ArgumentOutOfRangeException(nameof(request.StartTime), "Giờ bắt đầu phải nằm trong một ngày.");

            var expiryLocalDate = stock.ExpiryDate.Value.Date;
            var startLocal = expiryLocalDate.AddDays(-request.StartDaysBeforeExpiry).Add(startTime);
            var endLocal = expiryLocalDate.AddDays(1);
            if (startLocal >= endLocal)
                throw new InvalidOperationException("Mốc bắt đầu phải trước thời điểm kết thúc ngày HSD.");

            stock.FefoDiscountPercent = request.DiscountPercent;
            stock.IsFefoDiscountActive = true;
            stock.FefoDiscountStartAtUtc = BusinessDateResolver.ToUtc(startLocal);
            stock.FefoDiscountEndAtUtc = BusinessDateResolver.ToUtc(endLocal);
        }
        stock.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var utcNow = DateTime.UtcNow;
        return new FefoDiscountResponse(
            stock.Id,
            stock.FefoDiscountPercent,
            stock.IsFefoDiscountActive,
            stock.FefoDiscountStartAtUtc,
            stock.FefoDiscountEndAtUtc,
            FefoDiscountWindow.ResolveStatus(stock, utcNow));
    }
}


