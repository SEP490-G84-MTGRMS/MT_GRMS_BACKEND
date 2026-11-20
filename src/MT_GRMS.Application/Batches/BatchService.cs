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
        if (request.DiscountPercent < 0m || request.DiscountPercent > 90m)
            throw new ArgumentOutOfRangeException(nameof(request.DiscountPercent), "Mức giảm FEFO phải từ 0% đến 90%.");

        var tenantId = _tenantContext.CurrentTenantId ?? string.Empty;
        var stock = await _repository.GetBranchStockForUpdateAsync(tenantId, id, cancellationToken)
            ?? throw new KeyNotFoundException($"Không tìm thấy lô hàng với ID {id}.");

        if (stock.ExpiryDate.HasValue && stock.ExpiryDate.Value.Date < DateTime.UtcNow.Date)
            throw new InvalidOperationException("Lô đã hết hạn không được phép áp dụng giảm giá để bán.");

        stock.FefoDiscountPercent = request.IsActive ? request.DiscountPercent : 0m;
        stock.IsFefoDiscountActive = request.IsActive && request.DiscountPercent > 0m;
        stock.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return new FefoDiscountResponse(stock.Id, stock.FefoDiscountPercent, stock.IsFefoDiscountActive);
    }
}


