using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Inventory;
using MT_GRMS.Domain.Entities.Inventory;
using MT_GRMS.Application.PurchaseOrders;
using MT_GRMS.Application.PurchaseOrders.Dispatch;
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
    private readonly ISupplierOrderEmailSender? _emailSender;
    private readonly IRepository<Package>? _packages;
    private readonly IRepository<BranchStock>? _stocks;

    public SupplierReturnService(
        ISupplierReturnRepository repository,
        IPurchaseOrderRepository purchaseOrderRepository,
        ISupplierRepository supplierRepository,
        ITenantContext tenantContext,
        IUnitOfWork unitOfWork,
        ISupplierOrderEmailSender? emailSender = null,
        IRepository<Package>? packages = null,
        IRepository<BranchStock>? stocks = null)
    {
        _repository = repository;
        _purchaseOrderRepository = purchaseOrderRepository;
        _supplierRepository = supplierRepository;
        _tenantContext = tenantContext;
        _unitOfWork = unitOfWork;
        _emailSender = emailSender;
        _packages = packages;
        _stocks = stocks;
    }

    public SupplierReturnService(
        ISupplierReturnRepository repository,
        ITenantContext tenantContext,
        IUnitOfWork unitOfWork)
        : this(repository, null!, null!, tenantContext, unitOfWork)
    {
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
                BranchStockId = sourceLine.BranchStockId,
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
        var resolutionType = SupplierReturnPolicyRules.NormalizeResolutionType(command.ResolutionType);
        var returnReasonType = SupplierReturnPolicyRules.NormalizeReturnReasonType(
            command.ReturnReasonType,
            command.Reason);

        var policy = _supplierRepository != null
            ? await _supplierRepository.GetActiveReturnPolicyAsync(grnInfo.SupplierId, cancellationToken)
            : null;

        // Phiếu từ kiểm kê đã bắt buộc ảnh từng dòng; phiếu trả thủ công kiểm theo policy.
        var hasEvidence = !string.IsNullOrWhiteSpace(command.AttachmentUrl) || command.StocktakeId.HasValue;
        SupplierReturnPolicyRules.EnsureReturnAllowed(
            policy,
            resolutionType,
            returnReasonType,
            grnInfo.ReceivedDate,
            requireEvidence: false,
            hasEvidence: hasEvidence);

        var supplierReturn = new SupplierReturn
        {
            TenantId = tenantId,
            Code = code,
            GoodsReceiptId = command.GoodsReceiptId,
            SupplierId = grnInfo.SupplierId,
            BranchId = grnInfo.BranchId,
            StocktakeId = command.StocktakeId,
            Status = "DRAFT",
            Reason = command.Reason,
            ReasonNotes = string.IsNullOrWhiteSpace(command.ReasonNotes)
                ? $"[REASON:{returnReasonType}]"
                : (command.ReasonNotes!.Contains("[REASON:", StringComparison.OrdinalIgnoreCase)
                    ? command.ReasonNotes
                    : $"[REASON:{returnReasonType}] {command.ReasonNotes}"),
            AttachmentUrl = command.AttachmentUrl,
            ResolutionType = resolutionType,
            SettlementStatus = "UNSETTLED",
            ReplacementStatus = "NOT_REQUIRED",
            TotalRequestedQty = totalQty,
            TotalApprovedQty = 0,
            TotalHandedOverQty = 0,
            TotalEstimatedAmount = totalAmount,
            Lines = linesToCreate
        };

        await _repository.AddAsync(supplierReturn, cancellationToken);

        if (command.AutoSubmit)
        {
            await ReserveReturnStockAsync(supplierReturn, releaseQuarantine: true, cancellationToken);
            await AutoConfirmByPolicyAsync(supplierReturn, cancellationToken);
            await TryNotifySupplierEmailAsync(supplierReturn, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (command.AutoSubmit)
            await TryAutoCreateExchangePurchaseOrderAsync(supplierReturn.Id, cancellationToken);

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

        entity.SubmittedAt = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(notes))
        {
            entity.ReasonNotes = string.IsNullOrWhiteSpace(entity.ReasonNotes) ? notes : $"{entity.ReasonNotes} | {notes}";
        }

        // Khóa tồn kho sang trạng thái chờ trả / đổi
        await ReserveReturnStockAsync(entity, releaseQuarantine: false, cancellationToken);

        // Không chờ NCC duyệt: tự xác nhận theo chính sách + gửi email thông báo.
        await AutoConfirmByPolicyAsync(entity, cancellationToken);
        await TryNotifySupplierEmailAsync(entity, cancellationToken);

        await _repository.UpdateAsync(entity, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // EXCHANGE: sau xác nhận + email → tự tạo PO đổi hàng (hiện trên danh sách PO).
        await TryAutoCreateExchangePurchaseOrderAsync(entity.Id, cancellationToken);

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
                        var stock = await GetReturnStockAsync(tenantId, entity.BranchId, line, cancellationToken);
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
                var stock = await GetReturnStockAsync(tenantId, entity.BranchId, line, cancellationToken);
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

            // Hàng đã rời chi nhánh phải giảm tồn ngay, kể cả phiếu đổi hàng.
            // GRN đổi chỉ ghi giảm phần chưa được bàn giao (ApprovedQty - HandedOverQty).
            if (currentBatchHandoverQty > 0)
            {
                var stock = await GetReturnStockAsync(tenantId, entity.BranchId, line, cancellationToken);
                if (stock == null || stock.QuantityOnHand < currentBatchHandoverQty)
                    throw new BusinessException($"Lô {line.BatchCode} không đủ tồn để bàn giao hàng trả nhà cung cấp.");

                var packageWeight = await GetActivePackageWeightAsync(entity.TenantId, stock.Id, cancellationToken);
                if (stock.QuantityOnHand - currentBatchHandoverQty < packageWeight + stock.QuantityQuarantined + Math.Max(0m, stock.QuantityReserved - currentBatchHandoverQty))
                    throw new BusinessException("RTV_COMMITTED_STOCK", "Bàn giao sẽ lấy vào lượng khay, cách ly hoặc giữ cho chứng từ khác.");

                stock.QuantityReserved = Math.Max(0, stock.QuantityReserved - currentBatchHandoverQty);
                stock.QuantityOnHand -= currentBatchHandoverQty;
            }
        }

        entity.HandedOverAt = DateTime.UtcNow;
        entity.HandedOverBy = command.HandedOverBy ?? "Nhân viên kho";
        entity.HandoverNote = command.Notes;
        entity.TotalHandedOverQty = totalHandedOver;

        if (IsExchangeResolution(entity.ResolutionType))
        {
            // Giữ APPROVED — bàn giao hoàn tất (COMPLETED) chỉ khi nhập đủ hàng đổi.
            entity.Status = "APPROVED";
            var canCreatePo = entity.Lines.Any(l =>
            {
                var eligible = l.SupplierAcceptedQty > 0 ? l.SupplierAcceptedQty : l.ApprovedQty;
                return eligible > l.ExchangePoOrderedQty;
            });
            entity.ReplacementStatus = canCreatePo ? "WAITING_PURCHASE_ORDER" : entity.ReplacementStatus;
            RecalculateExchangeAmounts(entity);
        }
        else
        {
            entity.Status = totalHandedOver >= entity.TotalApprovedQty ? "COMPLETED" : "PARTIALLY_HANDED_OVER";
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
                    var stock = await GetReturnStockAsync(tenantId, entity.BranchId, line, cancellationToken);
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
            throw new BusinessException("INVALID_STATUS", "Phiếu phải đã xác nhận theo chính sách trước khi tạo PO đổi hàng.");

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

        // Không phụ thuộc bàn giao vật lý — trừ tồn khi nhập GRN đổi thành công.
        var linesToCover = entity.Lines
            .Select(l =>
            {
                var eligible = l.SupplierAcceptedQty > 0 ? l.SupplierAcceptedQty : l.ApprovedQty;
                var remaining = Math.Max(0, eligible - l.ExchangePoOrderedQty);
                return (Line: l, Qty: remaining);
            })
            .Where(x => x.Qty > 0)
            .ToList();

        if (linesToCover.Count == 0)
            throw new BusinessException("NO_QTY", "Không còn số lượng đủ điều kiện để tạo PO đổi hàng.");

        var branch = await _purchaseOrderRepository.GetBranchByIdAsync(entity.BranchId, cancellationToken);
        var supplier = await _purchaseOrderRepository.GetSupplierByIdAsync(entity.SupplierId, cancellationToken);
        var deliveryDate = ResolveNextSupplierDeliveryDate(supplier);
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

        if (IsExchangeResolution(entity.ResolutionType)
            && entity.SettlementStatus is "PAID" or "NOT_REQUIRED"
            && entity.ReplacementStatus is "WAITING_PAYMENT" or "WAITING_HANDOVER" or "WAITING_PURCHASE_ORDER" or null or "")
        {
            entity.ReplacementStatus = "WAITING_PURCHASE_ORDER";
        }

        await _repository.UpdateAsync(entity, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (entity.SettlementStatus is "PAID" or "NOT_REQUIRED")
            await TryAutoCreateExchangePurchaseOrderAsync(entity.Id, cancellationToken);

        return await GetByIdAsync(entity.Id, cancellationToken);
    }

    /// <summary>
    /// Tự tạo PO đổi hàng sau khi xác nhận/email (hoặc sau khi CH thanh toán đủ phần cost-share).
    /// Không ném lỗi nghiệp vụ ra ngoài — nếu chưa đủ điều kiện thì giữ trạng thái chờ.
    /// </summary>
    private async Task TryAutoCreateExchangePurchaseOrderAsync(
        Guid supplierReturnId,
        CancellationToken cancellationToken)
    {
        if (_purchaseOrderRepository == null)
            return;

        try
        {
            await CreateExchangePurchaseOrdersAsync(supplierReturnId, cancellationToken);
        }
        catch (BusinessException)
        {
            // COST_SHARE_UNPAID / NO_QTY / NOT_EXCHANGE / INVALID_STATUS → giữ luồng thủ công.
        }
        catch (NotFoundException)
        {
        }
    }

    private async Task ReserveReturnStockAsync(
        SupplierReturn entity,
        bool releaseQuarantine,
        CancellationToken cancellationToken)
    {
        foreach (var line in entity.Lines)
        {
            var stock = await GetReturnStockAsync(entity.TenantId, entity.BranchId, line, cancellationToken)
                ?? throw new BusinessException("RTV_STOCK_NOT_FOUND", "Không tìm thấy đúng dòng tồn của hàng trả nhà cung cấp.");

            if (releaseQuarantine && stock.QuantityQuarantined > 0)
            {
                var release = Math.Min(stock.QuantityQuarantined, line.RequestedQty);
                stock.QuantityQuarantined -= release;
            }

            var packageWeight = await GetActivePackageWeightAsync(entity.TenantId, stock.Id, cancellationToken);
            if (BranchStockAvailability.GetRawSellableKg(stock, packageWeight) < line.RequestedQty)
                throw new BusinessException("RTV_INSUFFICIENT_RAW", "Không đủ hàng rời trong lô để giữ cho phiếu trả nhà cung cấp.");
            stock.QuantityReserved += line.RequestedQty;
        }
    }

    private async Task<decimal> GetActivePackageWeightAsync(string tenantId, Guid branchStockId, CancellationToken ct)
        => _packages == null ? 0m : (await _packages.FindAsync(p => p.TenantId == tenantId && p.BranchStockId == branchStockId
            && !p.IsDeleted && (p.Status == "AVAILABLE" || p.Status == "QUARANTINED"), ct)).Sum(p => p.NetWeight);

    private async Task<BranchStock?> GetReturnStockAsync(string tenantId, Guid branchId, SupplierReturnLine line, CancellationToken ct)
    {
        BranchStock? stock = line.BranchStockId.HasValue && _stocks != null
            ? await _stocks.GetByIdAsync(line.BranchStockId.Value, ct)
            : await _repository.GetBranchStockAsync(tenantId, branchId, line.ProductId, line.BatchCode, ct);
        if (stock == null || stock.IsDeleted || stock.TenantId != tenantId || stock.BranchId != branchId || stock.ProductId != line.ProductId
            || (line.BranchStockId.HasValue && stock.Id != line.BranchStockId.Value))
            return null;
        return stock;
    }

    private async Task AutoConfirmByPolicyAsync(SupplierReturn entity, CancellationToken cancellationToken)
    {
        foreach (var line in entity.Lines)
        {
            line.SupplierAcceptedQty = line.RequestedQty;
            line.ApprovedQty = line.RequestedQty;
            line.RejectedQty = 0;
        }

        entity.TotalApprovedQty = entity.TotalRequestedQty;
        entity.Status = "APPROVED";
        entity.SubmittedAt ??= DateTime.UtcNow;
        entity.RespondedAt = DateTime.UtcNow;
        entity.SupplierConfirmedBy = _tenantContext.CurrentUserId != Guid.Empty
            ? _tenantContext.CurrentUserId.ToString()
            : null;
        entity.SupplierResponseNote =
            "Tự động xác nhận theo chính sách NCC. Đã gửi email thông báo (không chờ NCC duyệt).";

        await ApplyExchangePolicySnapshotAsync(entity, cancellationToken);
    }

    private async Task TryNotifySupplierEmailAsync(SupplierReturn entity, CancellationToken cancellationToken)
    {
        if (_emailSender == null || _purchaseOrderRepository == null)
            return;

        try
        {
            var supplier = await _purchaseOrderRepository.GetSupplierByIdAsync(entity.SupplierId, cancellationToken);
            var to = supplier?.Email?.Trim();
            if (string.IsNullOrWhiteSpace(to))
                return;

            var sb = new StringBuilder();
            sb.AppendLine($"Kính gửi {supplier!.Name},");
            sb.AppendLine();
            sb.AppendLine($"Cửa hàng đã lập phiếu trả/đổi hàng {entity.Code} theo chính sách đã thỏa thuận.");
            sb.AppendLine($"Lý do: {entity.Reason}");
            sb.AppendLine($"Hình thức: {entity.ResolutionType}");
            sb.AppendLine($"Tổng SL: {entity.TotalApprovedQty:0.###}");
            if (IsExchangeResolution(entity.ResolutionType))
            {
                sb.AppendLine($"Giá trị thay thế ước tính: {entity.ReplacementValue:N0} đ");
                sb.AppendLine($"Phần cửa hàng chịu: {entity.StorePayableAmount:N0} đ");
                sb.AppendLine($"Phần NCC chịu: {entity.SupplierBearAmount:N0} đ");
                sb.AppendLine("Hàng đổi sẽ được đặt theo lịch giao gần nhất của quý công ty.");
            }

            sb.AppendLine();
            sb.AppendLine("Chi tiết dòng hàng:");
            foreach (var line in entity.Lines)
            {
                var name = line.Product?.Name ?? line.ProductId.ToString();
                sb.AppendLine(
                    $"- {name} | Lô {line.BatchCode ?? "—"} | SL {line.ApprovedQty:0.###} | Đơn giá {line.UnitCost:N0}");
            }

            sb.AppendLine();
            sb.AppendLine("Email này chỉ mang tính thông báo — không yêu cầu duyệt trên hệ thống.");
            sb.AppendLine("Trân trọng.");

            await _emailSender.SendSupplierOrderAsync(
                new SupplierOrderEmailMessage
                {
                    To = to,
                    Subject = $"[MT-GRMS] Thông báo trả/đổi hàng {entity.Code}",
                    BodyText = sb.ToString(),
                },
                cancellationToken);
        }
        catch
        {
            // Không chặn luồng nghiệp vụ nếu SMTP lỗi / chưa cấu hình.
        }
    }

    private static DateTime ResolveNextSupplierDeliveryDate(Supplier? supplier)
    {
        var now = DateTime.UtcNow;
        if (supplier == null)
            return DateTime.SpecifyKind(now.Date.AddDays(1), DateTimeKind.Utc);

        var mask = supplier.DeliveryWeekdaysMask > 0 ? supplier.DeliveryWeekdaysMask : 127;
        var cutoff = string.IsNullOrWhiteSpace(supplier.CutoffTime) ? "17:00" : supplier.CutoffTime;
        var earliest = DeliveryScheduleCalculator.CalculateEarliestDeliveryDate(
            now,
            cutoff,
            mask,
            supplier.BlackoutDates);

        return DateTime.SpecifyKind(earliest.Date, DateTimeKind.Utc);
    }

    private static bool IsExchangeResolution(string? resolutionType) =>
        SupplierReturnPolicyRules.IsExchangeResolution(resolutionType);

    private async Task ApplyExchangePolicySnapshotAsync(SupplierReturn entity, CancellationToken cancellationToken)
    {
        if (!IsExchangeResolution(entity.ResolutionType))
        {
            entity.ReplacementStatus = "NOT_REQUIRED";
            foreach (var line in entity.Lines)
            {
                line.StoreCostSharePercentSnapshot = null;
                line.SupplierCostSharePercentSnapshot = null;
                line.LineReplacementValue = 0;
                line.StorePayableAmount = 0;
                line.SupplierBearAmount = 0;
            }
            return;
        }

        var policy = _supplierRepository != null
            ? await _supplierRepository.GetActiveReturnPolicyAsync(entity.SupplierId, cancellationToken)
            : null;
        if (policy != null)
        {
            SupplierReturnPolicyRules.EnsureReturnAllowed(
                policy,
                "EXCHANGE",
                SupplierReturnPolicyRules.NormalizeReturnReasonType(null, entity.Reason),
                receiptDateUtc: null,
                requireEvidence: false,
                hasEvidence: true);

            entity.PolicyId = policy.Id;
            // Header % = mặc định NCC (fallback). Tổng tiền tính theo từng dòng.
            entity.StoreCostSharePercentSnapshot = policy.CostShareEnabled ? (policy.StoreCostSharePercent ?? 0m) : 0m;
            entity.SupplierCostSharePercentSnapshot = policy.CostShareEnabled
                ? (policy.SupplierCostSharePercent ?? (100m - (policy.StoreCostSharePercent ?? 0m)))
                : 100m;
            entity.PolicySnapshotJson = JsonSerializer.Serialize(new
            {
                policy.AllowReturn,
                policy.CostShareEnabled,
                policy.CostShareByCategoryEnabled,
                policy.StoreCostSharePercent,
                policy.SupplierCostSharePercent,
                policy.RequireStorePaymentBeforeReplacement,
                policy.DefaultResolutionType,
                CategoryCostShares = (policy.CategoryCostShares ?? [])
                    .Where(c => !c.IsDeleted)
                    .Select(c => new
                    {
                        c.CategoryId,
                        CategoryName = c.Category?.Name,
                        c.StoreCostSharePercent,
                        c.SupplierCostSharePercent
                    })
            });
        }
        else
        {
            entity.StoreCostSharePercentSnapshot = 0m;
            entity.SupplierCostSharePercentSnapshot = 100m;
        }

        RecalculateExchangeAmounts(entity, policy);
        entity.SettlementStatus = (entity.StorePayableAmount > 0) ? "UNPAID" : "NOT_REQUIRED";
        entity.ReplacementStatus = entity.StorePayableAmount > 0
            ? "WAITING_PAYMENT"
            : "WAITING_PURCHASE_ORDER";
    }

    /// <summary>
    /// Hướng A: tính StorePayable theo từng dòng (Category → % riêng, fallback % mặc định NCC).
    /// </summary>
    private static void RecalculateExchangeAmounts(SupplierReturn entity, SupplierReturnPolicy? policy = null)
    {
        var defaultStorePct = entity.StoreCostSharePercentSnapshot ?? 0m;
        var byCategory = policy?.CostShareEnabled == true && policy.CostShareByCategoryEnabled;
        var categoryMap = (policy?.CategoryCostShares ?? [])
            .Where(c => !c.IsDeleted)
            .GroupBy(c => c.CategoryId)
            .ToDictionary(g => g.Key, g => g.First());

        decimal replacementValue = 0;
        decimal storePayable = 0;
        foreach (var line in entity.Lines)
        {
            var qty = line.SupplierAcceptedQty > 0 ? line.SupplierAcceptedQty : line.ApprovedQty;
            if (line.ReplacementUnitPrice <= 0)
                line.ReplacementUnitPrice = line.UnitCost;

            var lineValue = Math.Round(qty * line.ReplacementUnitPrice, 2);
            line.LineReplacementValue = lineValue;
            replacementValue += lineValue;

            var categoryId = line.Product?.CategoryId ?? line.CategoryId;
            var categoryName = line.Product?.Category?.Name ?? line.CategoryName;
            line.CategoryId = categoryId;
            line.CategoryName = categoryName;

            decimal storePct;
            if (policy != null)
            {
                storePct = defaultStorePct;
                if (!policy.CostShareEnabled)
                    storePct = 0m;
                else if (byCategory && categoryId.HasValue && categoryMap.TryGetValue(categoryId.Value, out var catShare))
                    storePct = catShare.StoreCostSharePercent;
            }
            else
            {
                // Tính lại sau khi đã snapshot: giữ % theo dòng đã chốt.
                storePct = line.StoreCostSharePercentSnapshot ?? defaultStorePct;
            }

            var supplierPct = 100m - storePct;
            line.StoreCostSharePercentSnapshot = storePct;
            line.SupplierCostSharePercentSnapshot = supplierPct;
            line.StorePayableAmount = Math.Round(lineValue * storePct / 100m, 2);
            line.SupplierBearAmount = lineValue - line.StorePayableAmount;
            storePayable += line.StorePayableAmount;
        }

        entity.ReplacementValue = Math.Round(replacementValue, 2);
        entity.StorePayableAmount = Math.Round(storePayable, 2);
        entity.SupplierBearAmount = entity.ReplacementValue - entity.StorePayableAmount;
    }

    private static List<string> ParseJsonList(string? json) =>
        SupplierReturnPolicyRules.ParseJsonList(json);

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
                    stockOnHandQty,
                    l.CategoryId,
                    l.CategoryName ?? l.Product?.Category?.Name,
                    l.StoreCostSharePercentSnapshot,
                    l.SupplierCostSharePercentSnapshot,
                    l.LineReplacementValue,
                    l.StorePayableAmount,
                    l.SupplierBearAmount,
                    l.ReplacementUnitPrice
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

    private static string NormalizeResolutionType(string? resolutionType) =>
        SupplierReturnPolicyRules.NormalizeResolutionType(resolutionType);
}
