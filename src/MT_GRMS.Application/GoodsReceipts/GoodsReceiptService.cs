using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.GoodsReceipts.Dtos;
using MT_GRMS.Application.SupplierReturns;
using MT_GRMS.Domain.Entities.Inventory;
using MT_GRMS.Domain.Entities.Supplier;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Domain.Exceptions;

namespace MT_GRMS.Application.GoodsReceipts;

public class GoodsReceiptService : IGoodsReceiptService
{
    private readonly IGoodsReceiptRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITenantContext _tenantContext;
    private readonly ISupplierReturnRepository? _supplierReturnRepository;

    public GoodsReceiptService(
        IGoodsReceiptRepository repository,
        IUnitOfWork unitOfWork,
        ITenantContext tenantContext,
        ISupplierReturnRepository? supplierReturnRepository = null)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _tenantContext = tenantContext;
        _supplierReturnRepository = supplierReturnRepository;
    }

    public async Task<PagedResult<GoodsReceiptDto>> GetGoodsReceiptsAsync(
        GoodsReceiptFilterQuery query,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.CurrentTenantId ?? string.Empty;
        return await _repository.GetPagedAsync(tenantId, query, cancellationToken);
    }

    public async Task<GoodsReceiptDto> GetGoodsReceiptByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.CurrentTenantId ?? string.Empty;
        var receipt = await _repository.GetByIdAsync(tenantId, id, cancellationToken);
        if (receipt == null || receipt.IsDeleted)
        {
            throw new NotFoundException("Phiếu nhập kho", id);
        }

        return MapToDto(receipt);
    }

    public async Task<List<PurchaseOrderLookupDto>> GetPoLookupAsync(Guid? branchId, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.CurrentTenantId ?? string.Empty;
        var pos = await _repository.GetEligiblePurchaseOrdersAsync(tenantId, branchId, cancellationToken);

        return pos.Select(po => new PurchaseOrderLookupDto
        {
            Id = po.Id,
            Code = po.Code,
            SupplierId = po.SupplierId,
            SupplierCode = po.Supplier?.Code ?? string.Empty,
            SupplierName = po.Supplier?.Name ?? string.Empty,
            BranchId = po.BranchId,
            BranchCode = po.Branch?.BranchCode ?? string.Empty,
            BranchName = po.Branch?.Name ?? string.Empty,
            DeliveryDate = po.DeliveryDate,
            TotalQty = po.TotalQty,
            TotalAmount = po.TotalAmount,
            Status = po.Status
        }).ToList();
    }

    public async Task<PoReceiptDetailsDto> GetPoReceiptDetailsAsync(string poIdOrCode, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.CurrentTenantId ?? string.Empty;
        PurchaseOrder? po = null;
        if (Guid.TryParse(poIdOrCode, out var poGuid))
        {
            po = await _repository.GetPurchaseOrderWithDetailsAsync(tenantId, poGuid, cancellationToken);
        }
        else
        {
            po = await _repository.GetPurchaseOrderByCodeWithDetailsAsync(tenantId, poIdOrCode, cancellationToken);
        }

        if (po == null || po.IsDeleted)
        {
            throw new NotFoundException("Đơn đặt hàng PO", poIdOrCode);
        }

        EnsureDeliveryDateReachedForReceipt(po);

        // Kiểm tra xem PO đã có phiếu nhập kho DRAFT hoặc COMPLETED chưa (Mục 4)
        var existingReceipt = await _repository.GetActiveReceiptByPoIdAsync(tenantId, po.Id, cancellationToken);
        if (existingReceipt != null)
        {
            throw new BusinessException(
                "PO_HAS_ACTIVE_RECEIPT",
                $"Đơn hàng {po.Code} đã có phiếu nhập kho {existingReceipt.Code} ở trạng thái {existingReceipt.Status}. Không thể tạo thêm phiếu nhập mới.");
        }

        return new PoReceiptDetailsDto
        {
            Id = po.Id,
            Code = po.Code,
            SupplierId = po.SupplierId,
            SupplierCode = po.Supplier?.Code ?? string.Empty,
            SupplierName = po.Supplier?.Name ?? string.Empty,
            BranchId = po.BranchId,
            BranchCode = po.Branch?.BranchCode ?? string.Empty,
            BranchName = po.Branch?.Name ?? string.Empty,
            DeliveryDate = po.DeliveryDate,
            TotalQty = po.TotalQty,
            TotalAmount = po.TotalAmount,
            Items = po.Details.Select(d => new PoReceiptLineItemDto
            {
                PurchaseOrderDetailId = d.Id,
                ProductId = d.ProductId,
                ProductCode = d.Product?.ProductCode ?? string.Empty,
                ProductName = d.Product?.Name ?? string.Empty,
                Unit = d.Product?.BaseUnit ?? "Cái",
                HasBatches = d.Product?.HasBatches ?? false,
                OrderedQty = d.OrderedQty ?? d.FinalQty ?? 0,
                UnitPrice = d.UnitPrice,
                LineAmount = d.LineAmount
            }).ToList()
        };
    }

    public async Task<GoodsReceiptDto> CreateGoodsReceiptAsync(
        CreateGoodsReceiptCommand command,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.CurrentTenantId ?? string.Empty;

        // 1. Kiểm tra đơn PO gốc
        var po = await _repository.GetPurchaseOrderWithDetailsAsync(tenantId, command.PurchaseOrderId, cancellationToken);
        if (po == null || po.IsDeleted)
        {
            throw new NotFoundException("Đơn đặt hàng PO", command.PurchaseOrderId);
        }

        EnsureDeliveryDateReachedForReceipt(po);

        // 2. Ràng buộc: Một PO chỉ có tối đa một phiếu nhập chưa bị hủy (Mục 4)
        var activeReceipt = await _repository.GetActiveReceiptByPoIdAsync(tenantId, command.PurchaseOrderId, cancellationToken);
        if (activeReceipt != null)
        {
            throw new BusinessException(
                "PO_ALREADY_HAS_RECEIPT",
                $"Đơn đặt hàng {po.Code} đã có phiếu nhập {activeReceipt.Code} ({activeReceipt.Status}). Mỗi đơn PO chỉ có tối đa một phiếu nhập chưa bị hủy.");
        }

        var code = await _repository.GenerateCodeAsync(tenantId, cancellationToken);
        var actorUserId = await ResolveValidActorUserIdAsync(cancellationToken);

        var receipt = new GoodsReceipt
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Code = code,
            PurchaseOrderId = command.PurchaseOrderId,
            SupplierId = command.SupplierId,
            BranchId = command.BranchId,
            InvoiceNo = command.InvoiceNo,
            ReceivedAtUtc = command.ReceivedAtUtc ?? DateTime.UtcNow,
            ReceiverUserId = actorUserId,
            FreightFee = command.FreightFee,
            DiscountAmount = command.DiscountAmount,
            VatPercent = command.VatPercent,
            PaymentMethod = command.PaymentMethod,
            PaymentDueDate = command.PaymentDueDate,
            Note = command.Note,
            CreatedAt = DateTime.UtcNow
        };

        // 3. Tính toán và xác thực từng dòng sản phẩm
        decimal subTotal = 0;
        foreach (var l in command.Lines)
        {
            var lineAmount = l.AcceptedQty * l.UnitPrice;
            subTotal += lineAmount;
        }

        receipt.SubTotal = subTotal;
        var baseAmount = subTotal - command.DiscountAmount + command.FreightFee;
        receipt.VatAmount = Math.Round(Math.Max(0, baseAmount) * (command.VatPercent / 100m), 2);
        receipt.GrandTotal = Math.Max(0, baseAmount) + receipt.VatAmount;

        // Nhiều dòng cùng SKU/PO detail (tách lô): tổng Delivered/Accepted ≤ Ordered
        foreach (var group in command.Lines.GroupBy(l => l.PurchaseOrderDetailId ?? l.ProductId))
        {
            var ordered = group.Max(x => x.OrderedQty);
            var sumDelivered = group.Sum(x => x.DeliveredQty);
            var sumAccepted = group.Sum(x => x.AcceptedQty);
            if (sumDelivered > ordered)
            {
                throw new BusinessException(
                    "EXCEEDS_ORDERED",
                    $"Tổng thực nhận ({sumDelivered}) vượt số lượng đặt PO ({ordered}). Không được nhận vượt khi tách lô.");
            }

            if (sumAccepted > ordered)
            {
                throw new BusinessException(
                    "ACCEPTED_EXCEEDS_ORDERED",
                    $"Tổng chấp nhận ({sumAccepted}) vượt số lượng đặt PO ({ordered}).");
            }

            // Nếu tổng thực nhận/chấp nhận < SL đặt và có lý do từ chối nhưng client gửi DeliveredQty == AcceptedQty,
            // chuẩn hóa RejectedQty và DeliveredQty cho dòng đầu tiên của nhóm để ghi nhận hàng từ chối trả lại tài xế.
            var existingRejected = group.Sum(x => Math.Max(x.RejectedQty, x.DeliveredQty - x.AcceptedQty));
            var groupReason = group.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.RejectionReason))?.RejectionReason;
            if (sumAccepted < ordered && existingRejected == 0 && !string.IsNullOrWhiteSpace(groupReason))
            {
                var firstLine = group.First();
                var diffRejected = ordered - sumAccepted;
                firstLine.RejectedQty = diffRejected;
                firstLine.DeliveredQty = firstLine.AcceptedQty + diffRejected;
                if (string.IsNullOrWhiteSpace(firstLine.RejectionReason))
                {
                    firstLine.RejectionReason = groupReason;
                }
            }
        }

        foreach (var l in command.Lines)
        {
            if (l.AcceptedQty < 0 || l.DeliveredQty < 0)
            {
                throw new BusinessException("INVALID_QTY", "Số lượng nhận và chấp nhận không được âm.");
            }

            if (l.AcceptedQty > l.DeliveredQty)
            {
                throw new BusinessException("ACCEPTED_EXCEEDS_RECEIVED", "Số lượng chấp nhận không được vượt quá số lượng thực nhận.");
            }

            var rejectedQty = l.RejectedQty > 0 ? l.RejectedQty : Math.Max(0, l.DeliveredQty - l.AcceptedQty);
            var deliveredQty = Math.Max(l.DeliveredQty, l.AcceptedQty + rejectedQty);
            if (rejectedQty > 0 && string.IsNullOrWhiteSpace(l.RejectionReason))
            {
                throw new BusinessException("REJECTION_REASON_REQUIRED", $"Dòng sản phẩm có {rejectedQty} hàng bị từ chối bắt buộc phải có lý do từ chối.");
            }

            if (l.ManageBatch && l.AcceptedQty > 0)
            {
                if (!l.ManufactureDate.HasValue)
                {
                    throw new BusinessException("MFG_REQUIRED", "Dòng đang quản lý batch bắt buộc phải nhập ngày sản xuất (MFG).");
                }

                if (!l.ExpiryDate.HasValue)
                {
                    throw new BusinessException("EXPIRY_DATE_REQUIRED", "Dòng đang quản lý batch bắt buộc phải nhập hạn sử dụng (EXP).");
                }

                if (l.ExpiryDate.Value.Date < (command.ReceivedAtUtc ?? DateTime.UtcNow).Date)
                {
                    throw new BusinessException("EXPIRY_PAST", "Hạn sử dụng không được trước ngày nhập hàng.");
                }
            }

            // Phân bổ chi phí Landed cost
            var lineAmount = l.AcceptedQty * l.UnitPrice;
            var weight = subTotal > 0 ? (lineAmount / subTotal) : 0;
            var freightAlloc = Math.Round(command.FreightFee * weight, 2);
            var discountAlloc = Math.Round(command.DiscountAmount * weight, 2);
            var landedSubtotal = lineAmount - discountAlloc + freightAlloc;
            var vatAlloc = Math.Round(landedSubtotal * (command.VatPercent / 100m), 2);
            var landedTotal = landedSubtotal + vatAlloc;
            var landedUnitCost = l.AcceptedQty > 0 ? Math.Round(landedTotal / l.AcceptedQty, 2) : l.UnitPrice;

            var line = new GoodsReceiptLine
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                GoodsReceiptId = receipt.Id,
                ProductId = l.ProductId,
                PurchaseOrderDetailId = l.PurchaseOrderDetailId,
                OrderedQty = l.OrderedQty,
                DeliveredQty = deliveredQty,
                AcceptedQty = l.AcceptedQty,
                RejectedQty = rejectedQty,
                UnitPrice = l.UnitPrice,
                LineAmount = lineAmount,
                BatchCode = l.ManageBatch && !string.IsNullOrWhiteSpace(l.BatchCode) ? l.BatchCode.Trim() : null,
                ManufactureDate = l.ManageBatch ? l.ManufactureDate : null,
                ExpiryDate = l.ManageBatch ? l.ExpiryDate : null,
                FreightAllocated = freightAlloc,
                DiscountAllocated = discountAlloc,
                VatAllocated = vatAlloc,
                LandedUnitCost = landedUnitCost,
                QualityStatus = rejectedQty > 0 ? "PARTIAL_REJECT" : "ACCEPTED",
                RejectionReason = l.RejectionReason,
                Note = string.IsNullOrWhiteSpace(l.RejectionNote) ? l.Note : $"{l.RejectionNote} | {l.Note}",
                CreatedAt = DateTime.UtcNow
            };

            receipt.Lines.Add(line);
        }

        if (command.AutoComplete)
        {
            // Không gán navigation Product vào line (tránh EF tracking conflict khi trùng ProductId / PO đã track Product)
            var productFlags = await LoadProductValidationFlagsAsync(receipt, cancellationToken);
            ValidateForCompletion(receipt, productFlags);
            receipt.Status = "COMPLETED";
            receipt.ConfirmedByUserId = actorUserId;
            receipt.ConfirmedAtUtc = DateTime.UtcNow;

            await ApplyStockAndPoUpdatesAsync(tenantId, receipt, po, productFlags, cancellationToken);
        }
        else
        {
            receipt.Status = "DRAFT";
        }

        await _repository.AddAsync(receipt, cancellationToken);

        SupplierReturn? createdReturn = null;
        if (receipt.Status == "COMPLETED")
        {
            createdReturn = await CreateRejectedGoodsReturnIfAnyAsync(tenantId, receipt, po, actorUserId, cancellationToken);
        }

        var resultDto = await GetGoodsReceiptByIdAsync(receipt.Id, cancellationToken);
        if (createdReturn != null)
        {
            resultDto.CreatedReturnId = createdReturn.Id;
            resultDto.CreatedReturnCode = createdReturn.Code;
        }
        return resultDto;
    }

    public async Task<GoodsReceiptDto> UpdateDraftGoodsReceiptAsync(
        Guid id,
        CreateGoodsReceiptCommand command,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.CurrentTenantId ?? string.Empty;
        var receipt = await _repository.GetByIdAsync(tenantId, id, cancellationToken);
        if (receipt == null || receipt.IsDeleted)
        {
            throw new NotFoundException("Phiếu nhập kho", id);
        }

        if (!string.Equals(receipt.Status, "DRAFT", StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessException(
                "GRN_NOT_DRAFT",
                $"Chỉ được sửa phiếu nhập ở trạng thái nháp. Phiếu {receipt.Code} đang {receipt.Status}.");
        }

        var po = receipt.PurchaseOrderId.HasValue
            ? await _repository.GetPurchaseOrderWithDetailsAsync(tenantId, receipt.PurchaseOrderId.Value, cancellationToken)
            : null;
        if (po != null)
        {
            EnsureDeliveryDateReachedForReceipt(po);
        }

        receipt.InvoiceNo = command.InvoiceNo;
        receipt.FreightFee = command.FreightFee;
        receipt.DiscountAmount = command.DiscountAmount;
        receipt.VatPercent = command.VatPercent;
        receipt.PaymentMethod = command.PaymentMethod;
        receipt.PaymentDueDate = command.PaymentDueDate;
        receipt.Note = command.Note;
        receipt.UpdatedAt = DateTime.UtcNow;

        foreach (var existing in receipt.Lines.ToList())
        {
            receipt.Lines.Remove(existing);
        }

        decimal subTotal = 0;
        foreach (var l in command.Lines)
        {
            subTotal += l.AcceptedQty * l.UnitPrice;
        }

        receipt.SubTotal = subTotal;
        var baseAmount = subTotal - command.DiscountAmount + command.FreightFee;
        receipt.VatAmount = Math.Round(Math.Max(0, baseAmount) * (command.VatPercent / 100m), 2);
        receipt.GrandTotal = Math.Max(0, baseAmount) + receipt.VatAmount;

        foreach (var group in command.Lines.GroupBy(l => l.PurchaseOrderDetailId ?? l.ProductId))
        {
            var ordered = group.Max(x => x.OrderedQty);
            var sumDelivered = group.Sum(x => x.DeliveredQty);
            var sumAccepted = group.Sum(x => x.AcceptedQty);
            if (sumDelivered > ordered)
            {
                throw new BusinessException(
                    "EXCEEDS_ORDERED",
                    $"Tổng thực nhận ({sumDelivered}) vượt số lượng đặt PO ({ordered}). Không được nhận vượt khi tách lô.");
            }

            if (sumAccepted > ordered)
            {
                throw new BusinessException(
                    "ACCEPTED_EXCEEDS_ORDERED",
                    $"Tổng chấp nhận ({sumAccepted}) vượt số lượng đặt PO ({ordered}).");
            }
        }

        foreach (var l in command.Lines)
        {
            if (l.AcceptedQty < 0 || l.DeliveredQty < 0)
            {
                throw new BusinessException("INVALID_QTY", "Số lượng nhận và chấp nhận không được âm.");
            }

            if (l.AcceptedQty > l.DeliveredQty)
            {
                throw new BusinessException("ACCEPTED_EXCEEDS_RECEIVED", "Số lượng chấp nhận không được vượt quá số lượng thực nhận.");
            }

            var rejectedQty = l.RejectedQty > 0 ? l.RejectedQty : Math.Max(0, l.DeliveredQty - l.AcceptedQty);
            var deliveredQty = Math.Max(l.DeliveredQty, l.AcceptedQty + rejectedQty);
            if (rejectedQty > 0 && string.IsNullOrWhiteSpace(l.RejectionReason))
            {
                throw new BusinessException("REJECTION_REASON_REQUIRED", $"Dòng sản phẩm có {rejectedQty} hàng bị từ chối bắt buộc phải có lý do từ chối.");
            }

            var lineAmount = l.AcceptedQty * l.UnitPrice;
            var weight = subTotal > 0 ? (lineAmount / subTotal) : 0;
            var freightAlloc = Math.Round(command.FreightFee * weight, 2);
            var discountAlloc = Math.Round(command.DiscountAmount * weight, 2);
            var landedSubtotal = lineAmount - discountAlloc + freightAlloc;
            var vatAlloc = Math.Round(landedSubtotal * (command.VatPercent / 100m), 2);
            var landedTotal = landedSubtotal + vatAlloc;
            var landedUnitCost = l.AcceptedQty > 0 ? Math.Round(landedTotal / l.AcceptedQty, 2) : l.UnitPrice;

            receipt.Lines.Add(new GoodsReceiptLine
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                GoodsReceiptId = receipt.Id,
                ProductId = l.ProductId,
                PurchaseOrderDetailId = l.PurchaseOrderDetailId,
                OrderedQty = l.OrderedQty,
                DeliveredQty = deliveredQty,
                AcceptedQty = l.AcceptedQty,
                RejectedQty = rejectedQty,
                UnitPrice = l.UnitPrice,
                LineAmount = lineAmount,
                BatchCode = l.ManageBatch && !string.IsNullOrWhiteSpace(l.BatchCode) ? l.BatchCode.Trim() : null,
                ManufactureDate = l.ManageBatch ? l.ManufactureDate : null,
                ExpiryDate = l.ManageBatch ? l.ExpiryDate : null,
                FreightAllocated = freightAlloc,
                DiscountAllocated = discountAlloc,
                VatAllocated = vatAlloc,
                LandedUnitCost = landedUnitCost,
                QualityStatus = rejectedQty > 0 ? "PARTIAL_REJECT" : "ACCEPTED",
                RejectionReason = l.RejectionReason,
                Note = string.IsNullOrWhiteSpace(l.RejectionNote) ? l.Note : $"{l.RejectionNote} | {l.Note}",
                CreatedAt = DateTime.UtcNow
            });
        }

        await _repository.UpdateAsync(receipt, cancellationToken);
        return await GetGoodsReceiptByIdAsync(receipt.Id, cancellationToken);
    }

    public async Task<GoodsReceiptDto> CompleteGoodsReceiptAsync(
        Guid id,
        CompleteGoodsReceiptCommand command,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.CurrentTenantId ?? string.Empty;
        var receipt = await _repository.GetByIdAsync(tenantId, id, cancellationToken);
        if (receipt == null || receipt.IsDeleted)
        {
            throw new NotFoundException("Phiếu nhập kho", id);
        }

        // Idempotency (Mục 7.10)
        if (receipt.Status == "COMPLETED")
        {
            throw new BusinessException("ALREADY_COMPLETED", "Phiếu nhập kho này đã được hoàn tất trước đó.");
        }

        if (receipt.Status == "CANCELLED")
        {
            throw new BusinessException("CANNOT_COMPLETE_CANCELLED", "Không thể hoàn tất phiếu nhập kho đã bị hủy.");
        }

        var productFlags = await LoadProductValidationFlagsAsync(receipt, cancellationToken);
        ValidateForCompletion(receipt, productFlags);

        PurchaseOrder? po = null;
        if (receipt.PurchaseOrderId.HasValue)
        {
            po = await _repository.GetPurchaseOrderWithDetailsAsync(tenantId, receipt.PurchaseOrderId.Value, cancellationToken);
            if (po != null)
            {
                EnsureDeliveryDateReachedForReceipt(po);
            }
        }

        var actorUserId = await ResolveValidActorUserIdAsync(cancellationToken);
        receipt.Status = "COMPLETED";
        receipt.ConfirmedByUserId = actorUserId;
        receipt.ConfirmedAtUtc = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(command.Note))
        {
            receipt.Note = string.IsNullOrWhiteSpace(receipt.Note) ? command.Note : $"{receipt.Note} | {command.Note}";
        }
        receipt.UpdatedAt = DateTime.UtcNow;

        await ApplyStockAndPoUpdatesAsync(tenantId, receipt, po, productFlags, cancellationToken);

        await _repository.UpdateAsync(receipt, cancellationToken);

        var createdReturn = await CreateRejectedGoodsReturnIfAnyAsync(tenantId, receipt, po, actorUserId, cancellationToken);

        var resultDto = await GetGoodsReceiptByIdAsync(receipt.Id, cancellationToken);
        if (createdReturn != null)
        {
            resultDto.CreatedReturnId = createdReturn.Id;
            resultDto.CreatedReturnCode = createdReturn.Code;
        }
        return resultDto;
    }

    public async Task<GoodsReceiptDto> CancelGoodsReceiptAsync(
        Guid id,
        CancelGoodsReceiptCommand command,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.CurrentTenantId ?? string.Empty;
        var receipt = await _repository.GetByIdAsync(tenantId, id, cancellationToken);
        if (receipt == null || receipt.IsDeleted)
        {
            throw new NotFoundException("Phiếu nhập kho", id);
        }

        // Quy tắc Mục 8.1 & 11: Chỉ phiếu DRAFT mới được hủy, không cho hủy COMPLETED
        if (receipt.Status == "COMPLETED")
        {
            throw new BusinessException("CANNOT_CANCEL_COMPLETED", "Không cho phép hủy phiếu nhập kho đã ở trạng thái COMPLETED.");
        }

        if (receipt.Status == "CANCELLED")
        {
            throw new BusinessException("ALREADY_CANCELLED", "Phiếu nhập kho này đã bị hủy trước đó.");
        }

        if (string.IsNullOrWhiteSpace(command.Reason))
        {
            throw new BusinessException("REASON_REQUIRED", "Bắt buộc phải ghi nhận lý do khi hủy phiếu nhập kho.");
        }

        receipt.Status = "CANCELLED";
        receipt.RejectedByUserId = await ResolveValidActorUserIdAsync(cancellationToken);
        receipt.RejectedAtUtc = DateTime.UtcNow;
        receipt.RejectionReason = command.Reason;
        receipt.UpdatedAt = DateTime.UtcNow;

        await _repository.UpdateAsync(receipt, cancellationToken);

        return await GetGoodsReceiptByIdAsync(receipt.Id, cancellationToken);
    }

    /// <summary>
    /// User.Id trong DB là string (usr_store_owner / user-owner-001), không phải Guid JWT.
    /// Chỉ trả ID có thật trong bảng Users — tránh FK ConfirmedByUserId / ReceiverUserId.
    /// </summary>
    private async Task<string> ResolveValidActorUserIdAsync(CancellationToken cancellationToken = default)
    {
        if (_tenantContext.CurrentUserId != Guid.Empty)
        {
            var strId = _tenantContext.CurrentUserId.ToString();
            if (await _repository.UserExistsAsync(strId, cancellationToken))
            {
                return strId;
            }
        }

        var role = (_tenantContext.CurrentRole ?? string.Empty).Trim().ToUpperInvariant();
        var isStaff = role is "EMPLOYEE" or "STAFF" or "CASHIER";
        var candidates = isStaff
            ? new[] { "usr_cashier", "user-cashier-001", "user-employee-001", "usr_store_owner", "user-owner-001" }
            : new[] { "usr_store_owner", "user-owner-001", "usr_cashier", "user-cashier-001" };

        foreach (var id in candidates)
        {
            if (await _repository.UserExistsAsync(id, cancellationToken))
            {
                return id;
            }
        }

        throw new BusinessException(
            "ACTOR_USER_MISSING",
            "Không xác định được người dùng hợp lệ để ghi nhận phiếu nhập kho. Vui lòng đăng nhập lại.");
    }

    /// <summary>
    /// Chỉ cho phép nhập kho từ ngày giao PO trở đi (so theo lịch VN UTC+7).
    /// </summary>
    private static void EnsureDeliveryDateReachedForReceipt(PurchaseOrder po)
    {
        if (!po.DeliveryDate.HasValue)
        {
            return;
        }

        var deliveryDate = DateOnly.FromDateTime(po.DeliveryDate.Value);
        var todayVn = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        if (deliveryDate > todayVn)
        {
            throw new BusinessException(
                "DELIVERY_DATE_NOT_REACHED",
                $"Chỉ được nhập hàng từ ngày giao {deliveryDate:dd/MM/yyyy}. Hôm nay chưa đến ngày giao.");
        }
    }

    private async Task<IReadOnlyDictionary<Guid, (bool HasBatches, string Name)>> LoadProductValidationFlagsAsync(
        GoodsReceipt receipt,
        CancellationToken cancellationToken)
    {
        var flags = new Dictionary<Guid, (bool HasBatches, string Name)>();

        foreach (var line in receipt.Lines)
        {
            if (line.Product != null && !flags.ContainsKey(line.ProductId))
            {
                flags[line.ProductId] = (line.Product.HasBatches, line.Product.Name ?? "Sản phẩm");
            }
        }

        var missingIds = receipt.Lines
            .Select(l => l.ProductId)
            .Distinct()
            .Where(id => !flags.ContainsKey(id))
            .ToList();

        if (missingIds.Count > 0)
        {
            var products = await _repository.GetProductsByIdsAsync(missingIds, cancellationToken);
            foreach (var (id, product) in products)
            {
                flags[id] = (product.HasBatches, product.Name ?? "Sản phẩm");
            }
        }

        return flags;
    }

    private static void ValidateForCompletion(
        GoodsReceipt receipt,
        IReadOnlyDictionary<Guid, (bool HasBatches, string Name)>? productFlags = null)
    {
        foreach (var line in receipt.Lines)
        {
            var hasBatches = productFlags != null && productFlags.TryGetValue(line.ProductId, out var flag)
                ? flag.HasBatches
                : line.Product?.HasBatches == true;
            var productName = productFlags != null && productFlags.TryGetValue(line.ProductId, out var named)
                ? named.Name
                : (line.Product?.Name ?? "Sản phẩm");

            if (line.AcceptedQty < 0 || line.DeliveredQty < 0)
            {
                throw new BusinessException("INVALID_QTY", $"Số lượng không hợp lệ tại dòng {productName}.");
            }

            if (line.AcceptedQty > line.DeliveredQty)
            {
                throw new BusinessException("ACCEPTED_EXCEEDS_RECEIVED", $"Số lượng chấp nhận không được vượt quá số lượng thực nhận tại dòng {productName}.");
            }

            if (line.RejectedQty > 0 && string.IsNullOrWhiteSpace(line.RejectionReason))
            {
                throw new BusinessException("REJECTION_REASON_REQUIRED", $"Dòng {productName} có hàng bị từ chối, bắt buộc phải chọn lý do từ chối.");
            }

            // Quản lý batch: có MFG/EXP/BatchCode hoặc Product.HasBatches
            var manageBatch = line.ManufactureDate.HasValue
                              || line.ExpiryDate.HasValue
                              || !string.IsNullOrWhiteSpace(line.BatchCode)
                              || hasBatches;

            if (manageBatch && line.AcceptedQty > 0)
            {
                if (line.ExpiryDate.HasValue && line.ExpiryDate.Value.Date < receipt.ReceivedAtUtc.Date)
                {
                    throw new BusinessException("EXPIRY_PAST", $"Hạn sử dụng của lô {line.BatchCode} không được trước ngày nhập hàng.");
                }

                if (hasBatches)
                {
                    if (string.IsNullOrWhiteSpace(line.BatchCode))
                    {
                        throw new BusinessException("BATCH_CODE_REQUIRED", $"Sản phẩm {productName} quản lý HSD bắt buộc phải nhập mã lô.");
                    }

                    if (!line.ExpiryDate.HasValue)
                    {
                        throw new BusinessException("EXPIRY_DATE_REQUIRED", $"Sản phẩm {productName} quản lý HSD bắt buộc phải nhập hạn sử dụng.");
                    }
                }
                else if (line.ManufactureDate.HasValue && !line.ExpiryDate.HasValue)
                {
                    throw new BusinessException(
                        "BATCH_DATES_REQUIRED",
                        $"Dòng {productName} có Ngày SX thì bắt buộc có Hạn SD.");
                }
            }
        }
    }

    private async Task ApplyStockAndPoUpdatesAsync(
        string tenantId,
        GoodsReceipt receipt,
        PurchaseOrder? po,
        IReadOnlyDictionary<Guid, (bool HasBatches, string Name)>? productFlags,
        CancellationToken cancellationToken)
    {
        // 1. Cập nhật tồn kho chi nhánh BranchStocks, tạo lô FEFO (ProductBatch) và tính giá vốn bình quân gia quyền (MovingAverageCost)
        foreach (var line in receipt.Lines.Where(l => l.AcceptedQty > 0))
        {
            var hasBatches = productFlags != null && productFlags.TryGetValue(line.ProductId, out var flag)
                ? flag.HasBatches
                : line.Product?.HasBatches == true;

            if (!hasBatches)
            {
                line.BatchCode = null;
                line.ManufactureDate = null;
                line.ExpiryDate = null;
            }
            else
            {
                line.BatchCode = string.IsNullOrWhiteSpace(line.BatchCode) ? null : line.BatchCode.Trim();
            }

            var existingTotalStock = await _repository.GetTotalProductStockAsync(tenantId, line.ProductId, cancellationToken);
            var product = line.Product ?? await _repository.GetProductByIdAsync(tenantId, line.ProductId, cancellationToken);

            // Tính MovingAverageCost cho sản phẩm
            if (product != null)
            {
                var oldCost = product.CostPrice ?? line.UnitPrice;
                var totalNewQty = existingTotalStock + line.AcceptedQty;
                if (totalNewQty > 0)
                {
                    product.CostPrice = Math.Round(
                        ((existingTotalStock * oldCost) + (line.AcceptedQty * line.LandedUnitCost)) / totalNewQty,
                        2);
                }
                else
                {
                    product.CostPrice = line.LandedUnitCost;
                }
                product.UpdatedAt = DateTime.UtcNow;
            }

            var stock = await _repository.GetBranchStockAsync(
                tenantId,
                receipt.BranchId,
                line.ProductId,
                hasBatches ? line.BatchCode : null,
                cancellationToken);

            if (stock != null)
            {
                var prevQty = Math.Max(0, stock.QuantityOnHand);
                var nextQty = prevQty + line.AcceptedQty;
                stock.LandedCostPerUnit = nextQty > 0
                    ? Math.Round(((prevQty * stock.LandedCostPerUnit) + (line.AcceptedQty * line.LandedUnitCost)) / nextQty, 2)
                    : line.LandedUnitCost;
                stock.QuantityOnHand = nextQty;
                if (!hasBatches)
                {
                    stock.BatchCode = null;
                    stock.ManufactureDate = null;
                    stock.ExpiryDate = null;
                }
                else
                {
                    if (line.ExpiryDate.HasValue) stock.ExpiryDate = line.ExpiryDate;
                    if (line.ManufactureDate.HasValue) stock.ManufactureDate = line.ManufactureDate;
                }
                stock.UpdatedAt = DateTime.UtcNow;
                line.BranchStockId = stock.Id;
            }
            else
            {
                var newStock = new BranchStock
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    BranchId = receipt.BranchId,
                    ProductId = line.ProductId,
                    SupplierId = receipt.SupplierId,
                    BatchCode = hasBatches ? line.BatchCode : null,
                    ManufactureDate = hasBatches ? line.ManufactureDate : null,
                    ExpiryDate = hasBatches ? line.ExpiryDate : null,
                    QuantityOnHand = line.AcceptedQty,
                    QuantityReserved = 0,
                    LandedCostPerUnit = line.LandedUnitCost,
                    CreatedAt = DateTime.UtcNow
                };
                await _repository.AddBranchStockAsync(newStock, cancellationToken);
                line.BranchStockId = newStock.Id;
            }

            // Tạo bản ghi lô hàng FEFO (Batch / ProductBatch)
            if (!string.IsNullOrWhiteSpace(line.BatchCode) || line.ExpiryDate.HasValue)
            {
                var newBatch = new Batch
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    BranchId = receipt.BranchId,
                    ProductId = line.ProductId,
                    BatchCode = !string.IsNullOrWhiteSpace(line.BatchCode) ? line.BatchCode : $"LOT-{receipt.Code}",
                    ManufactureDate = line.ManufactureDate ?? receipt.ReceivedAtUtc.Date,
                    ExpiryDate = line.ExpiryDate ?? receipt.ReceivedAtUtc.Date.AddMonths(6),
                    InitialQuantity = line.AcceptedQty,
                    LandedCostPerUnit = line.LandedUnitCost,
                    CreatedAt = DateTime.UtcNow
                };
                await _repository.AddBatchAsync(newBatch, cancellationToken);
            }
        }

        // 2. Cập nhật đơn PO nguồn sang COMPLETED (Mục 8.2: "Khi Goods Receipt được hoàn tất, PO chuyển sang COMPLETED, kể cả trường hợp nhà cung cấp giao thiếu.")
        if (po != null)
        {
            foreach (var line in receipt.Lines)
            {
                if (line.PurchaseOrderDetailId.HasValue)
                {
                    var pod = po.Details.FirstOrDefault(d => d.Id == line.PurchaseOrderDetailId.Value);
                    if (pod != null)
                    {
                        pod.ReceivedQty += line.AcceptedQty;
                        pod.UpdatedAt = DateTime.UtcNow;
                    }
                }
            }

            // RULE-02 P-SHORT-A: 1 lần nhập → đóng PO kể cả giao thiếu; phần thiếu đặt bù PO mới.
            var orderedTotal = po.Details.Sum(d => d.OrderedQty ?? d.FinalQty ?? 0m);
            var receivedTotal = po.Details.Sum(d => d.ReceivedQty);
            if (receivedTotal + 0.0001m < orderedTotal)
            {
                var shortNote =
                    $"[P-SHORT-A] Đóng PO sau 1 lần nhập thiếu (nhận {receivedTotal:0.##}/{orderedTotal:0.##}). Phần còn lại cần đặt bù đơn mới.";
                po.Note = string.IsNullOrWhiteSpace(po.Note) ? shortNote : $"{po.Note} | {shortNote}";
            }

            po.Status = "COMPLETED";
            po.UpdatedAt = DateTime.UtcNow;
        }

        // 3. Sinh công nợ phải trả nhà cung cấp (SupplierAPLedger) nếu CreateApOnConfirm = true và GrandTotal > 0
        if (receipt.CreateApOnConfirm && receipt.GrandTotal > 0)
        {
            var supplier = await _repository.GetSupplierByIdAsync(tenantId, receipt.SupplierId, cancellationToken);
            if (supplier != null)
            {
                var isImmediatePaid = string.Equals(receipt.PaymentMethod, "CASH", StringComparison.OrdinalIgnoreCase)
                                   || string.Equals(receipt.PaymentMethod, "BANK", StringComparison.OrdinalIgnoreCase);

                var newDebt = isImmediatePaid ? supplier.CurrentDebt : supplier.CurrentDebt + receipt.GrandTotal;
                var apEntry = new SupplierAPLedger
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    SupplierId = supplier.Id,
                    TransactionDate = receipt.ConfirmedAtUtc ?? DateTime.UtcNow,
                    DocType = APDocType.PurchaseInvoice,
                    DocNo = receipt.Code,
                    Description = $"Nhập kho theo phiếu {receipt.Code}" + (po != null ? $" (PO: {po.Code})" : string.Empty),
                    Amount = receipt.GrandTotal,
                    Direction = 1,
                    BalanceAfter = newDebt,
                    CreatedByName = receipt.ConfirmedByUserId ?? "Hệ thống",
                    IdempotencyKey = $"GRN-CONFIRM-{receipt.Id}",
                    CreatedAt = DateTime.UtcNow
                };

                await _repository.AddSupplierLedgerAsync(apEntry, cancellationToken);
                if (!isImmediatePaid)
                {
                    supplier.CurrentDebt = newDebt;
                    supplier.UpdatedAt = DateTime.UtcNow;
                }
            }
        }
    }

    private async Task<SupplierReturn?> CreateRejectedGoodsReturnIfAnyAsync(
        string tenantId,
        GoodsReceipt receipt,
        PurchaseOrder? po,
        string actorUserId,
        CancellationToken cancellationToken)
    {
        if (_supplierReturnRepository == null)
        {
            return null;
        }

        var rejectedLines = receipt.Lines.Where(l => l.RejectedQty > 0).ToList();
        if (rejectedLines.Count == 0)
        {
            return null;
        }

        var now = DateTime.UtcNow;
        var returnCode = await _supplierReturnRepository.GenerateCodeAsync(tenantId, cancellationToken);
        var returnId = Guid.NewGuid();

        var returnLines = rejectedLines.Select(rLine => new SupplierReturnLine
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SupplierReturnId = returnId,
            ProductId = rLine.ProductId,
            GoodsReceiptLineId = rLine.Id,
            BatchCode = rLine.BatchCode,
            ExpiryDate = rLine.ExpiryDate,
            RequestedQty = rLine.RejectedQty,
            // GRN từ chối tại chỗ = NCC nhận lại ngay → SupplierAcceptedQty = số bị từ chối
            SupplierAcceptedQty = rLine.RejectedQty,
            ApprovedQty = rLine.RejectedQty,
            RejectedQty = 0,
            HandedOverQty = rLine.RejectedQty,
            UnitCost = rLine.UnitPrice,
            RejectionReason = rLine.RejectionReason,
            Notes = FormatRejectionReasonLabel(rLine.RejectionReason, rLine.Note),
            CreatedAt = now
        }).ToList();

        var totalRejectedQty = returnLines.Sum(l => l.RequestedQty);
        var totalEstimatedAmount = returnLines.Sum(l => l.RequestedQty * l.UnitCost);

        var poRef = po != null ? $" (Đơn PO: {po.Code})" : string.Empty;
        var supplierReturn = new SupplierReturn
        {
            Id = returnId,
            TenantId = tenantId,
            Code = returnCode,
            GoodsReceiptId = receipt.Id,
            SupplierId = receipt.SupplierId,
            BranchId = receipt.BranchId,
            Status = "COMPLETED",
            Reason = "Từ chối nhận hàng",
            ReasonNotes = $"Từ chối nhận hàng trực tiếp tại phiếu nhập kho {receipt.Code}{poRef} — Đã bàn giao tài xế chở về ngay.",
            SupplierResponseNote = "Đã bàn giao trực tiếp cho tài xế giao hàng chở về ngay lúc kiểm nhận GRN.",
            TotalRequestedQty = totalRejectedQty,
            TotalApprovedQty = totalRejectedQty,
            TotalHandedOverQty = totalRejectedQty,
            TotalEstimatedAmount = totalEstimatedAmount,
            SubmittedAt = now,
            RespondedAt = now,
            HandedOverAt = now,
            HandedOverBy = actorUserId,
            CreatedAt = now,
            Lines = returnLines
        };

        await _supplierReturnRepository.AddAsync(supplierReturn, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return supplierReturn;
    }

    private static string FormatRejectionReasonLabel(string? code, string? note)
    {
        var label = code switch
        {
            "DAMAGE" => "Hàng hư hỏng / móp méo",
            "EXPIRED" => "Hàng đã hết hạn sử dụng",
            "NEAR_EXPIRY" => "Hàng cận hạn không đạt chuẩn",
            "WRONG_SPEC" => "Sai quy cách đóng gói",
            "WRONG_ITEM" => "Giao sai sản phẩm",
            "QUALITY_ISSUE" => "Chất lượng không đạt",
            "SHORT_DELIVERY" => "Từ chối nhận / Giao thiếu so với PO",
            "OTHER" => "Lý do khác",
            _ => string.IsNullOrWhiteSpace(code) ? "Từ chối nhận hàng" : code
        };

        return string.IsNullOrWhiteSpace(note) ? label : $"{label} ({note})";
    }

    private static GoodsReceiptDto MapToDto(GoodsReceipt gr)
    {
        return new GoodsReceiptDto
        {
            Id = gr.Id,
            Code = gr.Code,
            PurchaseOrderId = gr.PurchaseOrderId,
            PurchaseOrderCode = gr.PurchaseOrder?.Code,
            SupplierId = gr.SupplierId,
            SupplierCode = gr.Supplier?.Code ?? string.Empty,
            SupplierName = gr.Supplier?.Name ?? string.Empty,
            BranchId = gr.BranchId,
            BranchCode = gr.Branch?.BranchCode ?? string.Empty,
            BranchName = gr.Branch?.Name ?? string.Empty,
            Status = gr.Status,
            ReceivedAtUtc = gr.ReceivedAtUtc,
            InvoiceNo = gr.InvoiceNo,
            ReceiverUserId = gr.ReceiverUserId,
            ConfirmedByUserId = gr.ConfirmedByUserId,
            ConfirmedAtUtc = gr.ConfirmedAtUtc,
            CancelledByUserId = gr.RejectedByUserId,
            CancelledAtUtc = gr.RejectedAtUtc,
            CancellationReason = gr.RejectionReason,
            SubTotal = gr.SubTotal,
            FreightFee = gr.FreightFee,
            DiscountAmount = gr.DiscountAmount,
            VatPercent = gr.VatPercent,
            VatAmount = gr.VatAmount,
            GrandTotal = gr.GrandTotal,
            PaymentMethod = gr.PaymentMethod,
            PaymentDueDate = gr.PaymentDueDate,
            TotalItems = gr.Lines.Count,
            TotalOrderedQty = gr.Lines.Sum(l => l.OrderedQty),
            TotalReceivedQty = gr.Lines.Sum(l => l.DeliveredQty),
            TotalAcceptedQty = gr.Lines.Sum(l => l.AcceptedQty),
            TotalRejectedQty = gr.Lines.Sum(l => l.RejectedQty),
            HasMismatch = gr.Lines.Any(l => l.AcceptedQty != l.OrderedQty || l.RejectedQty > 0),
            Note = gr.Note,
            Lines = gr.Lines.Select(l => new GoodsReceiptLineDto
            {
                Id = l.Id,
                ProductId = l.ProductId,
                ProductCode = l.Product?.ProductCode ?? string.Empty,
                ProductName = l.Product?.Name ?? string.Empty,
                Unit = l.Product?.BaseUnit ?? "Cái",
                HasBatches = l.Product?.HasBatches ?? false,
                PurchaseOrderDetailId = l.PurchaseOrderDetailId,
                OrderedQty = l.OrderedQty,
                DeliveredQty = l.DeliveredQty,
                AcceptedQty = l.AcceptedQty,
                RejectedQty = l.RejectedQty,
                UnitPrice = l.UnitPrice,
                LineAmount = l.LineAmount,
                BatchCode = l.BatchCode,
                ManufactureDate = l.ManufactureDate,
                ExpiryDate = l.ExpiryDate,
                FreightAllocated = l.FreightAllocated,
                DiscountAllocated = l.DiscountAllocated,
                VatAllocated = l.VatAllocated,
                LandedUnitCost = l.LandedUnitCost,
                QualityStatus = l.QualityStatus,
                RejectionReason = l.RejectionReason,
                Note = l.Note
            }).ToList()
        };
    }
}
