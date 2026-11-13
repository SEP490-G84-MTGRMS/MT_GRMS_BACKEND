using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.PurchaseOrders;
using MT_GRMS.Application.Suppliers;
using MT_GRMS.Application.SupplierReturns.Dtos;
using MT_GRMS.Domain.Entities.Supplier;
using MT_GRMS.Domain.Exceptions;

namespace MT_GRMS.Application.SupplierReturns;

public class SupplierReturnService : ISupplierReturnService
{
    private readonly ISupplierReturnRepository _repository;
    private readonly IPurchaseOrderRepository _purchaseOrderRepository;
    private readonly ISupplierRepository _supplierRepository;
    private readonly ITenantContext _tenantContext;
    private readonly IUnitOfWork _unitOfWork;

    public SupplierReturnService(
        ISupplierReturnRepository repository,
        IPurchaseOrderRepository purchaseOrderRepository,
        ISupplierRepository supplierRepository,
        ITenantContext tenantContext,
        IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _purchaseOrderRepository = purchaseOrderRepository;
        _supplierRepository = supplierRepository;
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
            ResolutionType = NormalizeResolutionType(command.ResolutionType),
            SettlementStatus = "UNSETTLED",
            ReplacementStatus = "NOT_REQUIRED",
            TotalRequestedQty = totalQty,
            TotalApprovedQty = 0,
            TotalHandedOverQty = 0,
            TotalEstimatedAmount = totalAmount,
            SubmittedAt = command.AutoSubmit ? DateTime.UtcNow : null,
            Lines = linesToCreate
        };

        if (IsExchangeResolution(supplierReturn.ResolutionType))
            await ValidateExchangeAllowedByPolicyAsync(grnInfo.SupplierId, cancellationToken);

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

        if (entity.Status is "APPROVED" or "SUPPLIER_CONFIRMED")
        {
            entity.SupplierConfirmedBy = _tenantContext.CurrentUserId != Guid.Empty
                ? _tenantContext.CurrentUserId.ToString()
                : null;
            await ApplyExchangePolicySnapshotAsync(entity, cancellationToken);
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
        entity.HandoverNote = command.Notes;
        entity.TotalHandedOverQty = totalHandedOver;

        if (IsExchangeResolution(entity.ResolutionType))
        {
            // Exchange: không đóng Return khi mới bàn giao — chờ nhận hàng đổi đủ.
            if (entity.Status == "COMPLETED")
                entity.Status = "APPROVED";

            var canCreatePo = entity.Lines.Any(l =>
            {
                var eligible = Math.Min(l.SupplierAcceptedQty > 0 ? l.SupplierAcceptedQty : l.ApprovedQty, l.HandedOverQty);
                return eligible > l.ExchangePoOrderedQty;
            });
            entity.ReplacementStatus = canCreatePo ? "WAITING_PURCHASE_ORDER" : entity.ReplacementStatus;
            RecalculateExchangeAmounts(entity);
        }

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

    public async Task<IReadOnlyList<ExchangePurchaseOrderSummaryDto>> CreateExchangePurchaseOrdersAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.CurrentTenantId ?? string.Empty;
        var entity = await _repository.GetByIdAsync(tenantId, id, cancellationToken)
            ?? throw new NotFoundException($"Không tìm thấy phiếu trả hàng với ID {id}.");

        if (!IsExchangeResolution(entity.ResolutionType))
            throw new BusinessException("NOT_EXCHANGE", "Chỉ tạo PO đổi hàng khi ResolutionType là EXCHANGE.");

        if (entity.Status is not ("APPROVED" or "PARTIALLY_HANDED_OVER" or "SUPPLIER_CONFIRMED"))
            throw new BusinessException("INVALID_STATUS", "Phiếu phải đã được NCC xác nhận trước khi tạo PO đổi hàng.");

        if (entity.StorePayableAmount > 0
            && entity.StorePaidAmount < entity.StorePayableAmount
            && string.Equals(entity.SettlementStatus, "UNPAID", StringComparison.OrdinalIgnoreCase))
        {
            var policy = await _supplierRepository.GetActiveReturnPolicyAsync(entity.SupplierId, cancellationToken);
            if (policy?.RequireStorePaymentBeforeReplacement != false)
            {
                throw new BusinessException(
                    "COST_SHARE_UNPAID",
                    $"Cần thanh toán đủ phần cửa hàng ({entity.StorePayableAmount:N0}đ) trước khi tạo PO đổi hàng.");
            }
        }

        var linesToCover = entity.Lines
            .Select(l =>
            {
                var eligible = Math.Min(l.SupplierAcceptedQty > 0 ? l.SupplierAcceptedQty : l.ApprovedQty, l.HandedOverQty);
                var remaining = Math.Max(0, eligible - l.ExchangePoOrderedQty);
                return (Line: l, Qty: remaining);
            })
            .Where(x => x.Qty > 0)
            .ToList();

        if (linesToCover.Count == 0)
            throw new BusinessException("NO_QTY", "Không còn số lượng đủ điều kiện (đã bàn giao) để tạo PO đổi hàng.");

        var branch = await _purchaseOrderRepository.GetBranchByIdAsync(entity.BranchId, cancellationToken);
        var supplier = await _purchaseOrderRepository.GetSupplierByIdAsync(entity.SupplierId, cancellationToken);
        var deliveryDate = DateTime.SpecifyKind(DateTime.UtcNow.Date, DateTimeKind.Utc);
        var poCode = await _purchaseOrderRepository.GeneratePoCodeAsync(deliveryDate, cancellationToken);

        decimal totalQty = 0;
        decimal totalAmount = 0;
        var po = new PurchaseOrder
        {
            TenantId = tenantId,
            Code = poCode,
            BranchId = entity.BranchId,
            SupplierId = entity.SupplierId,
            DeliveryDate = deliveryDate,
            Status = "CONFIRMED",
            SourceType = "SUPPLIER_RETURN",
            SourceSupplierReturnId = entity.Id,
            Note = $"PO đổi hàng từ {entity.Code}",
            OrderedAtUtc = DateTime.UtcNow,
            CreatedByRole = _tenantContext.CurrentRole,
            SupplierCodeSnapshot = supplier?.Code,
            SupplierNameSnapshot = supplier?.Name,
            SupplierEmailSnapshot = supplier?.Email,
            StoreCodeSnapshot = branch?.BranchCode,
            StoreNameSnapshot = branch?.Name,
            StoreAddressSnapshot = branch?.Address,
            StorePhoneSnapshot = branch?.Phone,
            RevisionNumber = 1
        };

        foreach (var (line, qty) in linesToCover)
        {
            var unitPrice = line.ReplacementUnitPrice > 0 ? line.ReplacementUnitPrice : line.UnitCost;
            var lineAmount = Math.Round(qty * unitPrice, 2);
            totalQty += qty;
            totalAmount += lineAmount;
            line.ExchangePoOrderedQty += qty;

            po.Details.Add(new PurchaseOrderDetail
            {
                TenantId = tenantId,
                ProductId = line.ProductId,
                DeliveryDate = deliveryDate,
                OrderedQty = qty,
                FinalQty = qty,
                ReceivedQty = 0,
                UnitPrice = unitPrice,
                LineAmount = lineAmount,
                DeliveryUoM = line.Product?.BaseUnit ?? "Cái",
                ProductCodeSnapshot = line.Product?.ProductCode,
                ProductNameSnapshot = line.Product?.Name,
                PurchaseUnitNameSnapshot = line.Product?.BaseUnit,
                SourceSupplierReturnLineId = line.Id
            });
        }

        po.TotalQty = totalQty;
        po.TotalAmount = totalAmount;

        await _purchaseOrderRepository.AddPurchaseOrderAsync(po, cancellationToken);
        entity.ReplacementStatus = "WAITING_RECEIPT";
        await _repository.UpdateAsync(entity, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return
        [
            new ExchangePurchaseOrderSummaryDto(
                po.Id,
                po.Code,
                po.Status,
                po.TotalQty,
                po.TotalAmount,
                po.DeliveryDate,
                po.CreatedAt)
        ];
    }

    public async Task<IReadOnlyList<ExchangePurchaseOrderSummaryDto>> GetRelatedPurchaseOrdersAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.CurrentTenantId ?? string.Empty;
        _ = await _repository.GetByIdAsync(tenantId, id, cancellationToken)
            ?? throw new NotFoundException($"Không tìm thấy phiếu trả hàng với ID {id}.");

        // Reuse paged PO query via repository Find - scan committed by source
        var (items, _) = await _purchaseOrderRepository.GetPagedPurchaseOrdersAsync(
            null, null, null, null, null, 1, 200, null, cancellationToken);

        return items
            .Where(p => p.SourceSupplierReturnId == id && !p.IsDeleted)
            .Select(p => new ExchangePurchaseOrderSummaryDto(
                p.Id, p.Code, p.Status, p.TotalQty, p.TotalAmount, p.DeliveryDate, p.CreatedAt))
            .ToList();
    }

    public async Task<SupplierReturnDetailDto> RecordCostSharePaymentAsync(
        Guid id,
        RecordCostSharePaymentCommand command,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.CurrentTenantId ?? string.Empty;
        var entity = await _repository.GetByIdAsync(tenantId, id, cancellationToken)
            ?? throw new NotFoundException($"Không tìm thấy phiếu trả hàng với ID {id}.");

        if (command.Amount <= 0)
            throw new BusinessException("INVALID_AMOUNT", "Số tiền thanh toán phải lớn hơn 0.");

        entity.StorePaidAmount = Math.Round(entity.StorePaidAmount + command.Amount, 2);
        if (entity.StorePayableAmount <= 0)
            entity.SettlementStatus = "NOT_REQUIRED";
        else if (entity.StorePaidAmount >= entity.StorePayableAmount)
            entity.SettlementStatus = "PAID";
        else if (entity.StorePaidAmount > 0)
            entity.SettlementStatus = "PARTIALLY_PAID";
        else
            entity.SettlementStatus = "UNPAID";

        await _repository.UpdateAsync(entity, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return await GetByIdAsync(entity.Id, cancellationToken);
    }

    private static bool IsExchangeResolution(string? resolutionType) =>
        !string.IsNullOrWhiteSpace(resolutionType)
        && resolutionType.Contains("EXCHANGE", StringComparison.OrdinalIgnoreCase);

    private async Task ApplyExchangePolicySnapshotAsync(SupplierReturn entity, CancellationToken cancellationToken)
    {
        if (!IsExchangeResolution(entity.ResolutionType))
        {
            entity.ReplacementStatus = "NOT_REQUIRED";
            return;
        }

        var policy = await _supplierRepository.GetActiveReturnPolicyAsync(entity.SupplierId, cancellationToken);
        if (policy != null)
        {
            if (!policy.AllowReturn)
                throw new BusinessException("RETURN_NOT_ALLOWED", "NCC không cho phép trả/đổi hàng theo chính sách.");

            var allowed = ParseJsonList(policy.AllowedResolutionTypesJson);
            if (allowed.Count > 0 && !allowed.Any(a => a.Contains("EXCHANGE", StringComparison.OrdinalIgnoreCase)))
                throw new BusinessException("EXCHANGE_NOT_ALLOWED", "Chính sách NCC không cho phép đổi hàng.");

            entity.PolicyId = policy.Id;
            entity.StoreCostSharePercentSnapshot = policy.CostShareEnabled ? policy.StoreCostSharePercent : 0m;
            entity.SupplierCostSharePercentSnapshot = policy.CostShareEnabled
                ? (policy.SupplierCostSharePercent ?? (100m - (policy.StoreCostSharePercent ?? 0m)))
                : 100m;
            entity.PolicySnapshotJson = JsonSerializer.Serialize(new
            {
                policy.AllowReturn,
                policy.CostShareEnabled,
                policy.StoreCostSharePercent,
                policy.SupplierCostSharePercent,
                policy.RequireStorePaymentBeforeReplacement,
                policy.DefaultResolutionType
            });
        }
        else
        {
            entity.StoreCostSharePercentSnapshot = 0m;
            entity.SupplierCostSharePercentSnapshot = 100m;
        }

        RecalculateExchangeAmounts(entity);
        entity.ReplacementStatus = "WAITING_HANDOVER";
        entity.SettlementStatus = (entity.StorePayableAmount > 0) ? "UNPAID" : "NOT_REQUIRED";
    }

    private static void RecalculateExchangeAmounts(SupplierReturn entity)
    {
        decimal replacementValue = 0;
        foreach (var line in entity.Lines)
        {
            var qty = line.SupplierAcceptedQty > 0 ? line.SupplierAcceptedQty : line.ApprovedQty;
            if (line.ReplacementUnitPrice <= 0)
                line.ReplacementUnitPrice = line.UnitCost;
            replacementValue += qty * line.ReplacementUnitPrice;
        }

        entity.ReplacementValue = Math.Round(replacementValue, 2);
        var storePct = entity.StoreCostSharePercentSnapshot ?? 0m;
        entity.StorePayableAmount = Math.Round(entity.ReplacementValue * storePct / 100m, 2);
        entity.SupplierBearAmount = entity.ReplacementValue - entity.StorePayableAmount;
    }

    private static List<string> ParseJsonList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch
        {
            return [];
        }
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
            entity.SettlementStatus,
            entity.ReplacementStatus,
            entity.StoreCostSharePercentSnapshot,
            entity.SupplierCostSharePercentSnapshot,
            entity.ReplacementValue,
            entity.StorePayableAmount,
            entity.StorePaidAmount,
            entity.SupplierBearAmount
        );
    }

    private static string NormalizeResolutionType(string? resolutionType)
    {
        if (string.IsNullOrWhiteSpace(resolutionType))
            return "REDUCE_PAYABLE";

        var raw = resolutionType.Trim().ToUpperInvariant();
        return raw switch
        {
            "REPLACE_GOODS" or "EXCHANGE" => "EXCHANGE",
            "DEDUCT_INVOICE" or "REDUCE_PAYABLE" => "REDUCE_PAYABLE",
            "REFUND" => "REFUND",
            "SUPPLIER_CREDIT" or "OTHER" => "OTHER",
            _ => resolutionType.Trim()
        };
    }

    private async Task ValidateExchangeAllowedByPolicyAsync(Guid supplierId, CancellationToken cancellationToken)
    {
        var policy = await _supplierRepository.GetActiveReturnPolicyAsync(supplierId, cancellationToken);
        if (policy == null)
            return;

        if (!policy.AllowReturn)
            throw new BusinessException("RETURN_NOT_ALLOWED", "NCC không cho phép trả/đổi hàng theo chính sách.");

        var allowed = ParseJsonList(policy.AllowedResolutionTypesJson);
        if (allowed.Count > 0 && !allowed.Any(a => a.Contains("EXCHANGE", StringComparison.OrdinalIgnoreCase)
            || a.Contains("REPLACE", StringComparison.OrdinalIgnoreCase)))
        {
            throw new BusinessException("EXCHANGE_NOT_ALLOWED", "Chính sách NCC không cho phép đổi hàng.");
        }
    }
}
