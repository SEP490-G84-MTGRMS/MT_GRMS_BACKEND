using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MT_GRMS.Application.Batches.Dtos;
using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Entities.Inventory;

namespace MT_GRMS.Application.Batches;

/// <summary>
/// Service xử lý quản lý lô hàng & hạn sử dụng theo chuẩn FEFO (First Expired, First Out)
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

    public async Task<BatchResponse> CreateBatchAsync(
        BatchCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.CurrentTenantId ?? string.Empty;
        var batch = new Batch
        {
            TenantId = tenantId,
            ProductId = request.ProductId,
            BranchId = request.BranchId,
            BatchCode = request.BatchCode,
            ManufactureDate = request.ManufactureDate,
            ExpiryDate = request.ExpiryDate,
            InitialQuantity = request.InitialQuantity,
            LandedCostPerUnit = request.LandedCostPerUnit
        };

        await _repository.AddBatchAsync(batch, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new BatchResponse(
            Id: batch.Id,
            ProductId: batch.ProductId,
            BranchId: batch.BranchId,
            BatchCode: batch.BatchCode,
            ManufactureDate: batch.ManufactureDate,
            ExpiryDate: batch.ExpiryDate,
            InitialQuantity: batch.InitialQuantity,
            LandedCostPerUnit: batch.LandedCostPerUnit,
            DaysUntilExpiry: batch.DaysUntilExpiry,
            IsExpired: batch.IsExpired
        );
    }

    public async Task<IReadOnlyList<ExpiryAlertDto>> GetNearExpiryBatchesAsync(
        int daysThreshold = 30,
        Guid? branchId = null,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.CurrentTenantId ?? string.Empty;
        return await _repository.GetNearExpiryBatchesAsync(tenantId, daysThreshold, branchId, cancellationToken);
    }
}


