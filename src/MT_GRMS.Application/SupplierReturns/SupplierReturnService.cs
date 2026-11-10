using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.SupplierReturns.Dtos;
using MT_GRMS.Domain.Entities.Supplier;
using MT_GRMS.Domain.Exceptions;

namespace MT_GRMS.Application.SupplierReturns;

public class SupplierReturnService : ISupplierReturnService
{
    private readonly ISupplierReturnRepository _repository;
    private readonly ITenantContext _tenantContext;
    private readonly IUnitOfWork _unitOfWork;

    public SupplierReturnService(
        ISupplierReturnRepository repository,
        ITenantContext tenantContext,
        IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _tenantContext = tenantContext;
        _unitOfWork = unitOfWork;
    }

    public async Task<PagedResult<SupplierReturnListItemDto>> GetPagedAsync(
        SupplierReturnFilterQuery query,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.CurrentTenantId ?? string.Empty;
        return await _repository.GetPagedAsync(tenantId, query, cancellationToken);
    }

    public async Task<SupplierReturnDetailDto> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.CurrentTenantId ?? string.Empty;
        var entity = await _repository.GetByIdAsync(tenantId, id, cancellationToken);
        if (entity == null)
            throw new NotFoundException($"Không tìm thấy phiếu trả hàng nhà cung cấp với ID {id}.");

        List<GrnReturnableLineDto>? returnableLines = null;
        try
        {
            returnableLines = await _repository.GetGrnReturnableLinesAsync(tenantId, entity.GoodsReceiptId, cancellationToken);
        }
        catch { }

        return MapToDetailDto(entity, returnableLines);
    }

    public async Task<List<EligibleGrnLookupDto>> GetEligibleGrnsAsync(
        Guid? branchId,
        Guid? supplierId,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.CurrentTenantId ?? string.Empty;
        return await _repository.GetEligibleGrnsAsync(tenantId, branchId, supplierId, cancellationToken);
    }

    public async Task<List<GrnReturnableLineDto>> GetGrnReturnableLinesAsync(
        Guid grnId,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.CurrentTenantId ?? string.Empty;
        return await _repository.GetGrnReturnableLinesAsync(tenantId, grnId, cancellationToken);
    }

    public async Task<SupplierReturnDetailDto> CreateAsync(
        CreateSupplierReturnCommand command,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.CurrentTenantId ?? string.Empty;

        // 1. Kiểm tra phiếu nhập kho gốc
        var returnableLines = await _repository.GetGrnReturnableLinesAsync(tenantId, command.GoodsReceiptId, cancellationToken);
        if (!returnableLines.Any())
            throw new BusinessException("Phiếu nhập kho gốc không tồn tại, chưa hoàn tất hoặc không có sản phẩm nào đủ điều kiện trả hàng.");

        var eligibleGrns = await _repository.GetEligibleGrnsAsync(tenantId, null, null, cancellationToken);
        var grnInfo = eligibleGrns.FirstOrDefault(g => g.Id == command.GoodsReceiptId);
        if (grnInfo == null)
            throw new BusinessException("Chỉ được phép trả hàng từ các phiếu nhập kho đã hoàn tất (COMPLETED).");

        if (command.Lines == null || !command.Lines.Any())
            throw new BusinessException("Phiếu trả hàng bắt buộc phải có ít nhất 1 dòng sản phẩm.");

        // 2. Validate số lượng còn có thể trả của từng dòng
        var linesToCreate = new List<SupplierReturnLine>();
        foreach (var reqLine in command.Lines)
        {
            if (reqLine.RequestedQty <= 0)
                throw new BusinessException($"Số lượng yêu cầu trả phải lớn hơn 0.");

            var sourceLine = returnableLines.FirstOrDefault(l =>
                (reqLine.GoodsReceiptLineId.HasValue && l.GoodsReceiptLineId == reqLine.GoodsReceiptLineId.Value) ||
                (l.ProductId == reqLine.ProductId && l.BatchCode == reqLine.BatchCode));

            if (sourceLine == null)
                throw new BusinessException($"Sản phẩm với ID {reqLine.ProductId} không nằm trong phiếu nhập kho gốc.");

            if (reqLine.RequestedQty > sourceLine.AvailableReturnQty)
            {
                throw new BusinessException(
                    $"Số lượng yêu cầu trả ({reqLine.RequestedQty}) của sản phẩm '{sourceLine.ProductName}' vượt quá số lượng còn được phép trả ({sourceLine.AvailableReturnQty}).");
            }

            linesToCreate.Add(new SupplierReturnLine
            {
                TenantId = tenantId,
                ProductId = reqLine.ProductId,
                GoodsReceiptLineId = reqLine.GoodsReceiptLineId,
                StocktakeLineId = reqLine.StocktakeLineId,
                BatchCode = reqLine.BatchCode,
                ExpiryDate = reqLine.ExpiryDate ?? sourceLine.ExpiryDate,
                RequestedQty = reqLine.RequestedQty,
                SupplierAcceptedQty = 0,
                ApprovedQty = 0,
                RejectedQty = 0,
                HandedOverQty = 0,
                UnitCost = reqLine.UnitCost > 0 ? reqLine.UnitCost : sourceLine.UnitCost,
                Notes = reqLine.Notes
            });
        }

        var code = await _repository.GenerateCodeAsync(tenantId, cancellationToken);
        var totalQty = linesToCreate.Sum(l => l.RequestedQty);
        var totalAmount = linesToCreate.Sum(l => l.LineTotal);

        var supplierReturn = new SupplierReturn
        {
            TenantId = tenantId,
            Code = code,
            GoodsReceiptId = command.GoodsReceiptId,
            SupplierId = grnInfo.SupplierId,
            BranchId = grnInfo.BranchId,
            StocktakeId = command.StocktakeId,
            Status = command.AutoSubmit ? "PENDING_SUPPLIER" : "DRAFT",
            Reason = command.Reason,
            ReasonNotes = command.ReasonNotes,
            AttachmentUrl = command.AttachmentUrl,
            ResolutionType = command.ResolutionType ?? "REDUCE_PAYABLE",
            SettlementStatus = "UNSETTLED",
            TotalRequestedQty = totalQty,
            TotalApprovedQty = 0,
            TotalHandedOverQty = 0,
            TotalEstimatedAmount = totalAmount,
            SubmittedAt = command.AutoSubmit ? DateTime.UtcNow : null,
            Lines = linesToCreate
        };

        await _repository.AddAsync(supplierReturn, cancellationToken);

        if (command.AutoSubmit)
        {
            foreach (var line in linesToCreate)
            {
                var stock = await _repository.GetBranchStockAsync(tenantId, grnInfo.BranchId, line.ProductId, line.BatchCode, cancellationToken);
                if (stock != null)
                {
                    if (stock.QuantityQuarantined > 0)
                    {
                        var releaseQuarantine = Math.Min(stock.QuantityQuarantined, line.RequestedQty);
                        stock.QuantityQuarantined -= releaseQuarantine;
                    }
                    stock.QuantityReserved += line.RequestedQty;
                }
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(supplierReturn.Id, cancellationToken);
    }

    public async Task<SupplierReturnDetailDto> SubmitAsync(
        Guid id,
        string? notes = null,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.CurrentTenantId ?? string.Empty;
        var entity = await _repository.GetByIdAsync(tenantId, id, cancellationToken);
        if (entity == null)
            throw new NotFoundException($"Không tìm thấy phiếu trả hàng với ID {id}.");

        if (entity.Status != "DRAFT")
            throw new BusinessException($"Chỉ có thể gửi yêu cầu khi phiếu ở trạng thái DRAFT (Trạng thái hiện tại: {entity.Status}).");

        entity.Status = "PENDING_SUPPLIER";
        entity.SubmittedAt = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(notes))
        {
            entity.ReasonNotes = string.IsNullOrWhiteSpace(entity.ReasonNotes) ? notes : $"{entity.ReasonNotes} | {notes}";
        }

        // Khóa tồn kho sang trạng thái chờ trả
        foreach (var line in entity.Lines)
        {
            var stock = await _repository.GetBranchStockAsync(tenantId, entity.BranchId, line.ProductId, line.BatchCode, cancellationToken);
            if (stock != null)
            {
                stock.QuantityReserved += line.RequestedQty;
            }
        }

        await _repository.UpdateAsync(entity, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(entity.Id, cancellationToken);
    }

    public async Task<SupplierReturnDetailDto> RecordSupplierResponseAsync(
        Guid id,
        RecordSupplierResponseCommand command,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.CurrentTenantId ?? string.Empty;
        var entity = await _repository.GetByIdAsync(tenantId, id, cancellationToken);
        if (entity == null)
            throw new NotFoundException($"Không tìm thấy phiếu trả hàng với ID {id}.");

        if (entity.Status != "PENDING_SUPPLIER")
            throw new BusinessException($"Chỉ có thể ghi nhận phản hồi NCC khi phiếu ở trạng thái PENDING_SUPPLIER (Trạng thái hiện tại: {entity.Status}).");

        entity.SupplierResponseNote = command.SupplierResponseNote;
        entity.RespondedAt = DateTime.UtcNow;

        if (command.ResultType.Equals("APPROVED_FULL", StringComparison.OrdinalIgnoreCase))
        {
            entity.Status = "APPROVED";
            foreach (var line in entity.Lines)
            {
                line.SupplierAcceptedQty = line.RequestedQty;
                line.ApprovedQty = line.RequestedQty;
                line.RejectedQty = 0;
            }
            entity.TotalApprovedQty = entity.TotalRequestedQty;
        }
        else if (command.ResultType.Equals("APPROVED_PARTIAL", StringComparison.OrdinalIgnoreCase))
        {
            entity.Status = "APPROVED";
            decimal totalApproved = 0;

            foreach (var line in entity.Lines)
            {
                var resp = command.LineResponses?.FirstOrDefault(r => r.LineId == line.Id);
                if (resp != null)
                {
                    if (resp.ApprovedQty < 0 || resp.ApprovedQty > line.RequestedQty)
                        throw new BusinessException($"Số lượng chấp nhận không hợp lệ cho dòng hàng.");

                    line.SupplierAcceptedQty = resp.ApprovedQty;
                    line.ApprovedQty = resp.ApprovedQty;
                    line.RejectedQty = Math.Max(0, line.RequestedQty - resp.ApprovedQty);
                    line.RejectionReason = resp.RejectionReason;

                    // Giảm bớt số lượng khóa đối với phần bị từ chối
                    if (line.RejectedQty > 0)
                    {
                        var stock = await _repository.GetBranchStockAsync(tenantId, entity.BranchId, line.ProductId, line.BatchCode, cancellationToken);
                        if (stock != null)
                        {
                            stock.QuantityReserved = Math.Max(0, stock.QuantityReserved - line.RejectedQty);
                        }
                    }
                }
                else
                {
                    line.SupplierAcceptedQty = line.RequestedQty;
                    line.ApprovedQty = line.RequestedQty;
                    line.RejectedQty = 0;
                }
                totalApproved += line.ApprovedQty;
            }
            entity.TotalApprovedQty = totalApproved;
        }
        else if (command.ResultType.Equals("REJECTED", StringComparison.OrdinalIgnoreCase))
        {
            entity.Status = "REJECTED";
            foreach (var line in entity.Lines)
            {
                line.ApprovedQty = 0;
                line.RejectedQty = line.RequestedQty;

                // Giải phóng toàn bộ số lượng đã khóa
                var stock = await _repository.GetBranchStockAsync(tenantId, entity.BranchId, line.ProductId, line.BatchCode, cancellationToken);
                if (stock != null)
                {
                    stock.QuantityReserved = Math.Max(0, stock.QuantityReserved - line.RequestedQty);
                }
            }
            entity.TotalApprovedQty = 0;
        }
        else
        {
            throw new BusinessException($"Loại kết quả phản hồi '{command.ResultType}' không hợp lệ.");
        }

        await _repository.UpdateAsync(entity, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(entity.Id, cancellationToken);
    }

    public async Task<SupplierReturnDetailDto> CompleteHandoverAsync(
        Guid id,
        CompleteHandoverCommand command,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.CurrentTenantId ?? string.Empty;
        var entity = await _repository.GetByIdAsync(tenantId, id, cancellationToken);
        if (entity == null)
            throw new NotFoundException($"Không tìm thấy phiếu trả hàng với ID {id}.");

        if (entity.Status != "APPROVED" && entity.Status != "PARTIALLY_HANDED_OVER")
            throw new BusinessException($"Chỉ có thể bàn giao khi phiếu ở trạng thái APPROVED hoặc PARTIALLY_HANDED_OVER (Trạng thái hiện tại: {entity.Status}).");

        decimal totalHandedOver = 0;

        foreach (var line in entity.Lines)
        {
            var lineCommand = command.Lines?.FirstOrDefault(l => l.LineId == line.Id);
            var remainingToHandover = Math.Max(0, line.ApprovedQty - line.HandedOverQty);
            var currentBatchHandoverQty = lineCommand?.HandedOverQty ?? remainingToHandover;

            if (currentBatchHandoverQty < 0 || line.HandedOverQty + currentBatchHandoverQty > line.ApprovedQty)
            {
                throw new BusinessException($"Số lượng bàn giao cộng dồn ({line.HandedOverQty + currentBatchHandoverQty}) không được vượt quá số lượng đã duyệt ({line.ApprovedQty}).");
            }

            line.HandedOverQty += currentBatchHandoverQty;
            totalHandedOver += line.HandedOverQty;

            // Giảm tồn kho chi nhánh đúng bằng số lượng thực tế bàn giao đợt này và giải phóng tồn đã khóa tương ứng
            if (currentBatchHandoverQty > 0)
            {
                var stock = await _repository.GetBranchStockAsync(tenantId, entity.BranchId, line.ProductId, line.BatchCode, cancellationToken);
                if (stock != null)
                {
                    stock.QuantityReserved = Math.Max(0, stock.QuantityReserved - currentBatchHandoverQty);
                    stock.QuantityOnHand = Math.Max(0, stock.QuantityOnHand - currentBatchHandoverQty);
                }
            }
        }

        entity.Status = totalHandedOver >= entity.TotalApprovedQty ? "COMPLETED" : "PARTIALLY_HANDED_OVER";
        entity.HandedOverAt = DateTime.UtcNow;
        entity.HandedOverBy = command.HandedOverBy ?? "Nhân viên kho";
        entity.TotalHandedOverQty = totalHandedOver;

        await _repository.UpdateAsync(entity, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(entity.Id, cancellationToken);
    }

    public async Task<SupplierReturnDetailDto> CancelAsync(
        Guid id,
        CancelSupplierReturnCommand command,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.CurrentTenantId ?? string.Empty;
        var entity = await _repository.GetByIdAsync(tenantId, id, cancellationToken);
        if (entity == null)
            throw new NotFoundException($"Không tìm thấy phiếu trả hàng với ID {id}.");

        if (entity.Status == "COMPLETED")
            throw new BusinessException("Không thể hủy phiếu trả hàng đã hoàn tất bàn giao.");

        if (string.IsNullOrWhiteSpace(command.Reason))
            throw new BusinessException("Bắt buộc phải nhập lý do khi hủy phiếu trả hàng.");

        // Giải phóng tồn kho đã khóa nếu phiếu đang chờ NCC hoặc đã duyệt
        if (entity.Status is "PENDING_SUPPLIER" or "APPROVED")
        {
            foreach (var line in entity.Lines)
            {
                var unreserveQty = entity.Status == "APPROVED" ? line.ApprovedQty : line.RequestedQty;
                if (unreserveQty > 0)
                {
                    var stock = await _repository.GetBranchStockAsync(tenantId, entity.BranchId, line.ProductId, line.BatchCode, cancellationToken);
                    if (stock != null)
                    {
                        stock.QuantityReserved = Math.Max(0, stock.QuantityReserved - unreserveQty);
                    }
                }
            }
        }

        entity.Status = "CANCELLED";
        entity.CancellationReason = command.Reason.Trim();

        await _repository.UpdateAsync(entity, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(entity.Id, cancellationToken);
    }

    private static SupplierReturnDetailDto MapToDetailDto(
        SupplierReturn entity,
        List<GrnReturnableLineDto>? returnableLines = null)
    {
        return new SupplierReturnDetailDto(
            entity.Id,
            entity.Code,
            entity.GoodsReceiptId,
            entity.GoodsReceipt != null ? entity.GoodsReceipt.Code : string.Empty,
            entity.SupplierId,
            entity.Supplier != null ? entity.Supplier.Code : string.Empty,
            entity.Supplier != null ? entity.Supplier.Name : string.Empty,
            entity.BranchId,
            entity.Branch != null ? entity.Branch.BranchCode : string.Empty,
            entity.Branch != null ? entity.Branch.Name : string.Empty,
            entity.Status,
            entity.Reason,
            entity.ReasonNotes,
            entity.SupplierResponseNote,
            entity.CancellationReason,
            entity.AttachmentUrl,
            entity.TotalRequestedQty,
            entity.TotalApprovedQty,
            entity.TotalHandedOverQty,
            entity.TotalEstimatedAmount,
            entity.CreatedAt,
            entity.SubmittedAt,
            entity.RespondedAt,
            entity.HandedOverAt,
            entity.HandedOverBy,
            entity.Lines.Select(l =>
            {
                var source = returnableLines?.FirstOrDefault(rl =>
                    (l.GoodsReceiptLineId.HasValue && rl.GoodsReceiptLineId == l.GoodsReceiptLineId.Value) ||
                    (rl.ProductId == l.ProductId && rl.BatchCode == l.BatchCode));

                var receivedQty = source?.ReceivedQty ?? l.RequestedQty;
                var returnedQty = source?.ReturnedQty ?? 0;
                var pendingReturnQty = source?.PendingReturnQty ?? 0;
                var availableReturnQty = source?.AvailableReturnQty ?? 0;
                var stockOnHandQty = source?.StockOnHandQty ?? receivedQty;

                return new SupplierReturnLineDto(
                    l.Id,
                    l.ProductId,
                    l.Product != null ? l.Product.ProductCode : string.Empty,
                    l.Product != null ? l.Product.Name : "Sản phẩm",
                    l.Product != null ? l.Product.BaseUnit : "Cái",
                    l.GoodsReceiptLineId,
                    l.BatchCode,
                    l.ExpiryDate,
                    receivedQty,
                    returnedQty,
                    pendingReturnQty,
                    availableReturnQty,
                    l.RequestedQty,
                    l.ApprovedQty,
                    l.RejectedQty,
                    l.HandedOverQty,
                    l.UnitCost,
                    l.LineTotal,
                    l.RejectionReason,
                    l.Notes,
                    l.StocktakeLineId,
                    l.SupplierAcceptedQty > 0 ? l.SupplierAcceptedQty : l.ApprovedQty,
                    stockOnHandQty
                );
            }).ToList(),
            entity.StocktakeId,
            entity.StocktakeCode,
            entity.ResolutionType,
            entity.SettlementStatus
        );
    }
}
