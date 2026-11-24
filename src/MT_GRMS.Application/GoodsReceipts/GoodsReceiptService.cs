using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.GoodsReceipts.Dtos;
using MT_GRMS.Application.SupplierReturns;
using MT_GRMS.Domain.Entities.Catalog;
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
        var effectiveBranchId = BranchScopeGuard.ResolveEffectiveBranchId(_tenantContext, query.BranchId);
        var scopedQuery = new GoodsReceiptFilterQuery
        {
            BranchId = effectiveBranchId,
            SupplierId = query.SupplierId,
            Status = query.Status,
            Keyword = query.Keyword,
            FromDate = query.FromDate,
            ToDate = query.ToDate,
            IsTodayDelivery = query.IsTodayDelivery,
            HasMismatch = query.HasMismatch,
            PageIndex = query.PageIndex,
            PageSize = query.PageSize
        };
        return await _repository.GetPagedAsync(tenantId, scopedQuery, cancellationToken);
    }

    public async Task<GoodsReceiptDto> GetGoodsReceiptByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.CurrentTenantId ?? string.Empty;
        var receipt = await _repository.GetByIdAsync(tenantId, id, cancellationToken);
        if (receipt == null || receipt.IsDeleted)
        {
            throw new NotFoundException("Phiếu nhập kho", id);
        }

        BranchScopeGuard.EnsureCanAccessBranch(_tenantContext, receipt.BranchId);
        return MapToDto(receipt);
    }

    public async Task<List<PurchaseOrderLookupDto>> GetPoLookupAsync(Guid? branchId, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.CurrentTenantId ?? string.Empty;
        var effectiveBranchId = BranchScopeGuard.ResolveEffectiveBranchId(_tenantContext, branchId);
        var pos = await _repository.GetEligiblePurchaseOrdersAsync(tenantId, effectiveBranchId, cancellationToken);

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

        BranchScopeGuard.EnsureCanAccessBranch(_tenantContext, po.BranchId);
        EnsureDeliveryDateReachedForReceipt(po);

        // PO thường: chặn nếu đã có GRN active. PO đổi hàng: chỉ chặn khi còn phiếu nháp.
        var existingReceipt = await _repository.GetActiveReceiptByPoIdAsync(tenantId, po.Id, cancellationToken);
        if (existingReceipt != null)
        {
            var isExchangePoDetails = string.Equals(po.SourceType, "SUPPLIER_RETURN", StringComparison.OrdinalIgnoreCase);
            if (!isExchangePoDetails
                || string.Equals(existingReceipt.Status, "DRAFT", StringComparison.OrdinalIgnoreCase))
            {
                throw new BusinessException(
                    "PO_HAS_ACTIVE_RECEIPT",
                    $"Đơn hàng {po.Code} đã có phiếu nhập kho {existingReceipt.Code} ở trạng thái {existingReceipt.Status}. Không thể tạo thêm phiếu nhập mới.");
            }
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
            SourceType = po.SourceType,
            SourceSupplierReturnId = po.SourceSupplierReturnId,
            Items = po.Details.Select(d => new PoReceiptLineItemDto
            {
                PurchaseOrderDetailId = d.Id,
                ProductId = d.ProductId,
                ProductCode = d.Product?.ProductCode ?? string.Empty,
                ProductName = d.Product?.Name ?? string.Empty,
                Unit = ResolveInboundUnit(d.DeliveryUoM, d.PurchaseUnitNameSnapshot, d.Product),
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

        // Kiểm tra quyền chi nhánh theo PO gốc và đối chiếu BranchId/SupplierId (GRN-02)
        BranchScopeGuard.EnsureCanAccessBranch(_tenantContext, po.BranchId);
        if (command.BranchId != Guid.Empty && command.BranchId != po.BranchId)
        {
            throw new BusinessException("PO_BRANCH_MISMATCH", "Chi nhánh trên phiếu nhập không khớp với chi nhánh của đơn đặt hàng PO.");
        }
        if (command.SupplierId != Guid.Empty && command.SupplierId != po.SupplierId)
        {
            throw new BusinessException("PO_SUPPLIER_MISMATCH", "Nhà cung cấp trên phiếu nhập không khớp với nhà cung cấp của đơn đặt hàng PO.");
        }

        EnsureDeliveryDateReachedForReceipt(po);

        // 2. Ràng buộc GRN active theo loại PO
        var activeReceipt = await _repository.GetActiveReceiptByPoIdAsync(tenantId, command.PurchaseOrderId, cancellationToken);
        var isExchangePo = string.Equals(po.SourceType, "SUPPLIER_RETURN", StringComparison.OrdinalIgnoreCase);
        if (activeReceipt != null)
        {
            // PO đổi hàng: cho phép nhiều GRN COMPLETED; chỉ chặn khi còn DRAFT.
            if (isExchangePo)
            {
                if (string.Equals(activeReceipt.Status, "DRAFT", StringComparison.OrdinalIgnoreCase))
                {
                    throw new BusinessException(
                        "PO_ALREADY_HAS_RECEIPT",
                        $"PO đổi hàng {po.Code} đang có phiếu nhập nháp {activeReceipt.Code}. Hãy hoàn tất hoặc hủy phiếu nháp trước.");
                }
            }
            else
            {
                throw new BusinessException(
                    "PO_ALREADY_HAS_RECEIPT",
                    $"Đơn đặt hàng {po.Code} đã có phiếu nhập {activeReceipt.Code} ({activeReceipt.Status}). Mỗi đơn PO chỉ có tối đa một phiếu nhập chưa bị hủy.");
            }
        }

        var code = await _repository.GenerateCodeAsync(tenantId, cancellationToken);
        var actorUserId = await ResolveValidActorUserIdAsync(cancellationToken);

        var receipt = new GoodsReceipt
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Code = code,
            PurchaseOrderId = command.PurchaseOrderId,
            SupplierId = po.SupplierId,
            BranchId = po.BranchId,
            InvoiceNo = command.InvoiceNo,
            ReceivedAtUtc = command.ReceivedAtUtc ?? DateTime.UtcNow,
            ReceiverUserId = actorUserId,
            DiscountAmount = command.DiscountAmount,
            VatPercent = command.VatPercent,
            PaymentMethod = command.PaymentMethod,
            PaymentDueDate = command.PaymentDueDate,
            Note = command.Note,
            CreatedAt = DateTime.UtcNow
        };

        // 3. Tính toán và xác thực từng dòng sản phẩm (GRN-03: giá & SL đặt lấy từ PO DB, chặn giá/chiết khấu/VAT/HSD bất hợp lệ)
        if (command.Lines == null || command.Lines.Count == 0)
        {
            throw new BusinessException("LINES_REQUIRED", "Phiếu nhập kho bắt buộc phải có ít nhất 1 dòng sản phẩm.");
        }
        if (command.DiscountAmount < 0)
        {
            throw new BusinessException("INVALID_DISCOUNT", "Chiết khấu không được âm.");
        }
        if (command.VatPercent < 0 || command.VatPercent > 100)
        {
            throw new BusinessException("INVALID_VAT", "Thuế suất VAT phải nằm trong khoảng 0% đến 100%.");
        }

        decimal subTotal = 0;
        foreach (var l in command.Lines)
        {
            var poDetail = po.Details.FirstOrDefault(d =>
                (l.PurchaseOrderDetailId.HasValue && d.Id == l.PurchaseOrderDetailId.Value)
                || d.ProductId == l.ProductId);
            if (poDetail != null)
            {
                l.PurchaseOrderDetailId ??= poDetail.Id;
                l.OrderedQty = poDetail.OrderedQty ?? poDetail.FinalQty ?? l.OrderedQty;
                l.UnitPrice = poDetail.UnitPrice;
            }
            if (l.UnitPrice < 0)
            {
                throw new BusinessException("INVALID_UNIT_PRICE", "Đơn giá sản phẩm không được âm.");
            }
            if (l.RejectedQty < 0)
            {
                throw new BusinessException("INVALID_QTY", "Số lượng từ chối không được âm.");
            }
            var lineAmount = l.AcceptedQty * l.UnitPrice;
            subTotal += lineAmount;
        }

        if (command.DiscountAmount > subTotal && subTotal > 0)
        {
            throw new BusinessException("INVALID_DISCOUNT", "Chiết khấu không được vượt quá tổng tiền hàng.");
        }

        receipt.SubTotal = subTotal;
        var baseAmount = subTotal - command.DiscountAmount;
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

            if (l.ManufactureDate.HasValue && l.ExpiryDate.HasValue
                && l.ExpiryDate.Value.Date <= l.ManufactureDate.Value.Date)
            {
                throw new BusinessException("INVALID_EXPIRY_DATE", "Hạn sử dụng (EXP) phải lớn hơn ngày sản xuất (MFG).");
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

            // Giá vốn nhập theo giá mua đã chốt; VAT/discount chỉ phục vụ tổng tiền chứng từ.
            var lineAmount = l.AcceptedQty * l.UnitPrice;
            var weight = subTotal > 0 ? (lineAmount / subTotal) : 0;
            var discountAlloc = Math.Round(command.DiscountAmount * weight, 2);
            var purchaseSubtotal = lineAmount - discountAlloc;
            var vatAlloc = Math.Round(purchaseSubtotal * (command.VatPercent / 100m), 2);
            var purchaseUnitCost = l.UnitPrice;

            var shouldManageBatch = l.ManageBatch || !string.IsNullOrWhiteSpace(l.BatchCode) || l.ExpiryDate.HasValue || l.ManufactureDate.HasValue;

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
                BatchCode = shouldManageBatch && !string.IsNullOrWhiteSpace(l.BatchCode) ? l.BatchCode.Trim() : null,
                ManufactureDate = shouldManageBatch ? l.ManufactureDate : null,
                ExpiryDate = shouldManageBatch ? l.ExpiryDate : null,
                DiscountAllocated = discountAlloc,
                VatAllocated = vatAlloc,
                PurchaseUnitCost = purchaseUnitCost,
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

        BranchScopeGuard.EnsureCanAccessBranch(_tenantContext, receipt.BranchId);

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

        if (command.Lines == null || command.Lines.Count == 0)
        {
            throw new BusinessException("LINES_REQUIRED", "Phiếu nhập kho bắt buộc phải có ít nhất 1 dòng sản phẩm.");
        }
        if (command.DiscountAmount < 0)
        {
            throw new BusinessException("INVALID_DISCOUNT", "Chiết khấu không được âm.");
        }
        if (command.VatPercent < 0 || command.VatPercent > 100)
        {
            throw new BusinessException("INVALID_VAT", "Thuế suất VAT phải nằm trong khoảng 0% đến 100%.");
        }

        decimal subTotal = 0;
        foreach (var l in command.Lines)
        {
            if (po != null)
            {
                var poDetail = po.Details.FirstOrDefault(d =>
                    (l.PurchaseOrderDetailId.HasValue && d.Id == l.PurchaseOrderDetailId.Value)
                    || d.ProductId == l.ProductId);
                if (poDetail != null)
                {
                    l.PurchaseOrderDetailId ??= poDetail.Id;
                    l.OrderedQty = poDetail.OrderedQty ?? poDetail.FinalQty ?? l.OrderedQty;
                    l.UnitPrice = poDetail.UnitPrice;
                }
            }
            if (l.UnitPrice < 0)
            {
                throw new BusinessException("INVALID_UNIT_PRICE", "Đơn giá sản phẩm không được âm.");
            }
            if (l.RejectedQty < 0)
            {
                throw new BusinessException("INVALID_QTY", "Số lượng từ chối không được âm.");
            }
            if (l.ManufactureDate.HasValue && l.ExpiryDate.HasValue
                && l.ExpiryDate.Value.Date <= l.ManufactureDate.Value.Date)
            {
                throw new BusinessException("INVALID_EXPIRY_DATE", "Hạn sử dụng (EXP) phải lớn hơn ngày sản xuất (MFG).");
            }
            subTotal += l.AcceptedQty * l.UnitPrice;
        }

        if (command.DiscountAmount > subTotal && subTotal > 0)
        {
            throw new BusinessException("INVALID_DISCOUNT", "Chiết khấu không được vượt quá tổng tiền hàng.");
        }

        receipt.SubTotal = subTotal;
        var baseAmount = subTotal - command.DiscountAmount;
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
            var discountAlloc = Math.Round(command.DiscountAmount * weight, 2);
            var purchaseSubtotal = lineAmount - discountAlloc;
            var vatAlloc = Math.Round(purchaseSubtotal * (command.VatPercent / 100m), 2);
            var purchaseUnitCost = l.UnitPrice;

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
                DiscountAllocated = discountAlloc,
                VatAllocated = vatAlloc,
                PurchaseUnitCost = purchaseUnitCost,
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

        BranchScopeGuard.EnsureCanAccessBranch(_tenantContext, receipt.BranchId);

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

        BranchScopeGuard.EnsureCanAccessBranch(_tenantContext, receipt.BranchId);

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
    /// TEMP (test/UAT): bỏ chặn ngày giao — đặt lại false khi ship thật.
    /// </summary>
    private static readonly bool AllowReceiveBeforeDeliveryDate = true;

    /// <summary>Đơn vị nhập: DeliveryUoM / snapshot PO → ProductUnit IsPurchaseUnit → BaseUnit.</summary>
    private static string ResolveInboundUnit(string? deliveryUoM, string? purchaseUnitSnapshot, Product? product)
    {
        if (!string.IsNullOrWhiteSpace(deliveryUoM))
        {
            return deliveryUoM.Trim();
        }

        if (!string.IsNullOrWhiteSpace(purchaseUnitSnapshot))
        {
            return purchaseUnitSnapshot.Trim();
        }

        if (product?.Units != null)
        {
            var active = product.Units.Where(u => !u.IsDeleted).ToList();
            var purchase = active.FirstOrDefault(u => u.IsPurchaseUnit)
                           ?? active.FirstOrDefault(u => u.IsBaseUnit);
            if (purchase != null && !string.IsNullOrWhiteSpace(purchase.UnitName))
            {
                return purchase.UnitName.Trim();
            }
        }

        return string.IsNullOrWhiteSpace(product?.BaseUnit) ? "Cái" : product!.BaseUnit.Trim();
    }

    /// <summary>
    /// 1 đơn vị nhập = bao nhiêu đơn vị tồn (base). BranchStock.QuantityOnHand luôn theo base (giống POS).
    /// </summary>
    private static decimal ResolvePurchaseToStockRate(
        Product? product,
        string? deliveryUoM,
        string? purchaseUnitSnapshot)
    {
        if (product?.Units == null)
        {
            return 1m;
        }

        var active = product.Units.Where(u => !u.IsDeleted).ToList();
        if (active.Count == 0)
        {
            return 1m;
        }

        ProductUnit? matched = null;
        if (!string.IsNullOrWhiteSpace(deliveryUoM))
        {
            matched = active.FirstOrDefault(u =>
                string.Equals(u.UnitName, deliveryUoM.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        if (matched == null && !string.IsNullOrWhiteSpace(purchaseUnitSnapshot))
        {
            matched = active.FirstOrDefault(u =>
                string.Equals(u.UnitName, purchaseUnitSnapshot.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        matched ??= active.FirstOrDefault(u => u.IsPurchaseUnit)
                    ?? active.FirstOrDefault(u => u.IsBaseUnit);

        if (matched == null || matched.ConversionRate <= 0)
        {
            return 1m;
        }

        return matched.ConversionRate;
    }

    private static void EnsureDeliveryDateReachedForReceipt(PurchaseOrder po)
    {
        if (AllowReceiveBeforeDeliveryDate)
        {
            return;
        }

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
        var isExchangePo = po != null
            && string.Equals(po.SourceType, "SUPPLIER_RETURN", StringComparison.OrdinalIgnoreCase);

        // PO đổi hàng: lô nhập phải khác lô hàng trả — tránh cộng/trừ cùng một BranchStock (net = 0).
        if (isExchangePo)
            await EnsureExchangeIncomingBatchesAreDistinctAsync(tenantId, po!, receipt, productFlags, cancellationToken);

        // 1. Cộng tồn BranchStocks theo SL nhận.
        // Không cập nhật Products.CostPrice — giá nhập cố định ở quan hệ sản phẩm–NCC, không thuộc nhiệm vụ phiếu nhập kho.
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

            var product = line.Product ?? await _repository.GetProductByIdAsync(tenantId, line.ProductId, cancellationToken);
            var pod = line.PurchaseOrderDetailId.HasValue && po != null
                ? po.Details.FirstOrDefault(d => d.Id == line.PurchaseOrderDetailId.Value)
                : line.PurchaseOrderDetail;

            // AcceptedQty trên GRN/PO theo đơn vị nhập; BranchStock theo đơn vị tồn (base) — giống POS.
            var purchaseToStockRate = ResolvePurchaseToStockRate(
                product,
                pod?.DeliveryUoM,
                pod?.PurchaseUnitNameSnapshot);
            var stockQtyDelta = Math.Round(line.AcceptedQty * purchaseToStockRate, 3);
            var purchaseCostPerStockUnit = purchaseToStockRate > 0
                ? Math.Round(line.PurchaseUnitCost / purchaseToStockRate, 4)
                : line.PurchaseUnitCost;

            var stock = await _repository.GetBranchStockAsync(
                tenantId,
                receipt.BranchId,
                line.ProductId,
                hasBatches ? line.BatchCode : null,
                cancellationToken);

            if (stock != null)
            {
                var prevQty = Math.Max(0, stock.QuantityOnHand);
                var nextQty = prevQty + stockQtyDelta;
                stock.LandedCostPerUnit = nextQty > 0
                    ? Math.Round(((prevQty * stock.LandedCostPerUnit) + (stockQtyDelta * purchaseCostPerStockUnit)) / nextQty, 4)
                    : purchaseCostPerStockUnit;
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
                    QuantityOnHand = stockQtyDelta,
                    QuantityReserved = 0,
                    LandedCostPerUnit = purchaseCostPerStockUnit,
                    CreatedAt = DateTime.UtcNow
                };
                await _repository.AddBranchStockAsync(newStock, cancellationToken);
                line.BranchStockId = newStock.Id;
            }

            // Tồn live chỉ ghi BranchStocks. Không dual-write bảng Batches (legacy snapshot).
        }

        // 2. Cập nhật PO nguồn
        if (po != null)
        {
            foreach (var line in receipt.Lines)
            {
                if (line.PurchaseOrderDetailId.HasValue)
                {
                    var pod = po.Details.FirstOrDefault(d => d.Id == line.PurchaseOrderDetailId.Value);
                    if (pod != null)
                    {
                        var ordered = pod.OrderedQty ?? pod.FinalQty ?? 0m;
                        if (isExchangePo && pod.ReceivedQty + line.AcceptedQty > ordered + 0.0001m)
                        {
                            throw new BusinessException(
                                "EXCHANGE_EXCEEDS_ORDERED",
                                $"PO đổi hàng: nhận vượt số lượng đặt (đã nhận {pod.ReceivedQty:0.##} + {line.AcceptedQty:0.##} > {ordered:0.##}).");
                        }

                        pod.ReceivedQty += line.AcceptedQty;
                        pod.UpdatedAt = DateTime.UtcNow;
                    }
                }
            }

            var orderedTotal = po.Details.Sum(d => d.OrderedQty ?? d.FinalQty ?? 0m);
            var receivedTotal = po.Details.Sum(d => d.ReceivedQty);

            if (isExchangePo)
            {
                // PO đổi hàng: bắt buộc nhận đủ — không đóng thiếu; có thể nhận nhiều đợt.
                // Trừ tồn hàng trả + cộng tồn GRN (đã cộng ở bước trên) khi nhập thành công từng đợt.
                if (receivedTotal + 0.0001m < orderedTotal)
                {
                    po.Status = "PARTIALLY_RECEIVED";
                    var remainNote =
                        $"[EXCHANGE] Đã nhận {receivedTotal:0.##}/{orderedTotal:0.##}, còn thiếu {orderedTotal - receivedTotal:0.##}. Chờ NCC giao đủ trên cùng PO.";
                    po.Note = string.IsNullOrWhiteSpace(po.Note) ? remainNote : $"{po.Note} | {remainNote}";
                    await ApplyExchangeReceiptToSupplierReturnAsync(
                        tenantId,
                        po,
                        receipt,
                        "PARTIALLY_RECEIVED",
                        completeReturn: false,
                        cancellationToken);
                }
                else
                {
                    po.Status = "COMPLETED";
                    await ApplyExchangeReceiptToSupplierReturnAsync(
                        tenantId,
                        po,
                        receipt,
                        "COMPLETED",
                        completeReturn: true,
                        cancellationToken);
                }
            }
            else
            {
                // RULE-02 P-SHORT-A: PO mua thường — 1 lần nhập → đóng kể cả thiếu.
                if (receivedTotal + 0.0001m < orderedTotal)
                {
                    var shortNote =
                        $"[P-SHORT-A] Đóng PO sau 1 lần nhập thiếu (nhận {receivedTotal:0.##}/{orderedTotal:0.##}). Phần còn lại cần đặt bù đơn mới.";
                    po.Note = string.IsNullOrWhiteSpace(po.Note) ? shortNote : $"{po.Note} | {shortNote}";
                }

                po.Status = "COMPLETED";
            }

            po.UpdatedAt = DateTime.UtcNow;
        }

        // 3. Sinh công nợ AP khi CreateApOnConfirm = true và GrandTotal > 0.
        // PO đổi hàng (SUPPLIER_RETURN): không ghi PurchaseInvoice full — tránh đếm nợ trùng.
        // CASH/BANK: ghi cặp PurchaseInvoice (+X) + PaymentVoucher (−X), dư nợ không đổi.
        if (receipt.CreateApOnConfirm && receipt.GrandTotal > 0 && !isExchangePo)
        {
            var supplier = await _repository.GetSupplierByIdAsync(tenantId, receipt.SupplierId, cancellationToken);
            if (supplier != null)
            {
                var isImmediatePaid = string.Equals(receipt.PaymentMethod, "CASH", StringComparison.OrdinalIgnoreCase)
                                   || string.Equals(receipt.PaymentMethod, "BANK", StringComparison.OrdinalIgnoreCase);

                var txDate = receipt.ConfirmedAtUtc ?? DateTime.UtcNow;
                var actor = receipt.ConfirmedByUserId ?? "Hệ thống";
                var poNote = po != null ? $" (PO: {po.Code})" : string.Empty;
                var debtAfterInvoice = supplier.CurrentDebt + receipt.GrandTotal;

                var apEntry = new SupplierAPLedger
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    SupplierId = supplier.Id,
                    TransactionDate = txDate,
                    DocType = APDocType.PurchaseInvoice,
                    DocNo = receipt.Code,
                    Description = $"Nhập kho theo phiếu {receipt.Code}{poNote}",
                    Amount = receipt.GrandTotal,
                    Direction = 1,
                    BalanceAfter = debtAfterInvoice,
                    CreatedByName = actor,
                    IdempotencyKey = $"GRN-CONFIRM-{receipt.Id}",
                    CreatedAt = DateTime.UtcNow
                };

                await _repository.AddSupplierLedgerAsync(apEntry, cancellationToken);

                if (isImmediatePaid)
                {
                    var payMethod = string.Equals(receipt.PaymentMethod, "CASH", StringComparison.OrdinalIgnoreCase)
                        ? "CASH"
                        : "BANK_TRANSFER";

                    var payEntry = new SupplierAPLedger
                    {
                        Id = Guid.NewGuid(),
                        TenantId = tenantId,
                        SupplierId = supplier.Id,
                        TransactionDate = txDate,
                        DocType = APDocType.PaymentVoucher,
                        DocNo = $"PAY-{receipt.Code}",
                        Description = $"Thanh toán ngay khi nhập kho {receipt.Code}{poNote}",
                        Amount = receipt.GrandTotal,
                        Direction = -1,
                        BalanceAfter = supplier.CurrentDebt,
                        CreatedByName = actor,
                        IdempotencyKey = $"GRN-CONFIRM-{receipt.Id}-PAY",
                        PaymentMethod = payMethod,
                        CreatedAt = DateTime.UtcNow
                    };

                    await _repository.AddSupplierLedgerAsync(payEntry, cancellationToken);
                    // CurrentDebt không đổi
                }
                else
                {
                    if (!receipt.PaymentDueDate.HasValue)
                    {
                        receipt.PaymentDueDate = txDate.Date.AddDays(Math.Max(0, supplier.PaymentTermDays));
                    }

                    supplier.CurrentDebt = debtAfterInvoice;
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
                Unit = ResolveInboundUnit(
                    l.PurchaseOrderDetail?.DeliveryUoM,
                    l.PurchaseOrderDetail?.PurchaseUnitNameSnapshot,
                    l.Product),
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
                DiscountAllocated = l.DiscountAllocated,
                VatAllocated = l.VatAllocated,
                PurchaseUnitCost = l.PurchaseUnitCost,
                QualityStatus = l.QualityStatus,
                RejectionReason = l.RejectionReason,
                Note = l.Note
            }).ToList()
        };
    }

    /// <summary>
    /// Đảm bảo mã lô hàng đổi khác mã lô hàng trả — tránh cộng rồi trừ cùng một BranchStock.
    /// </summary>
    private async Task EnsureExchangeIncomingBatchesAreDistinctAsync(
        string tenantId,
        PurchaseOrder po,
        GoodsReceipt receipt,
        IReadOnlyDictionary<Guid, (bool HasBatches, string Name)>? productFlags,
        CancellationToken cancellationToken)
    {
        Domain.Entities.Supplier.SupplierReturn? ret = null;
        if (po.SourceSupplierReturnId.HasValue && _supplierReturnRepository != null)
        {
            ret = await _supplierReturnRepository.GetByIdAsync(
                tenantId, po.SourceSupplierReturnId.Value, cancellationToken);
        }

        var usedCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var returnBatchByProduct = new Dictionary<Guid, HashSet<string>>();

        if (ret != null)
        {
            foreach (var rl in ret.Lines)
            {
                if (string.IsNullOrWhiteSpace(rl.BatchCode))
                    continue;
                if (!returnBatchByProduct.TryGetValue(rl.ProductId, out var set))
                {
                    set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    returnBatchByProduct[rl.ProductId] = set;
                }

                set.Add(rl.BatchCode.Trim());
            }
        }

        foreach (var line in receipt.Lines.Where(l => l.AcceptedQty > 0))
        {
            var hasBatches = productFlags != null && productFlags.TryGetValue(line.ProductId, out var flag)
                ? flag.HasBatches
                : line.Product?.HasBatches == true;
            if (!hasBatches)
                continue;

            string? linkedReturnBatch = null;
            if (line.PurchaseOrderDetailId.HasValue)
            {
                var pod = po.Details.FirstOrDefault(d => d.Id == line.PurchaseOrderDetailId.Value);
                if (pod?.SourceSupplierReturnLineId != null && ret != null)
                {
                    linkedReturnBatch = ret.Lines
                        .FirstOrDefault(l => l.Id == pod.SourceSupplierReturnLineId.Value)
                        ?.BatchCode;
                }
            }

            var current = string.IsNullOrWhiteSpace(line.BatchCode) ? null : line.BatchCode.Trim();
            var conflictsReturn = !string.IsNullOrWhiteSpace(current)
                && (
                    (!string.IsNullOrWhiteSpace(linkedReturnBatch)
                     && string.Equals(current, linkedReturnBatch.Trim(), StringComparison.OrdinalIgnoreCase))
                    || (returnBatchByProduct.TryGetValue(line.ProductId, out var retBatches)
                        && retBatches.Contains(current))
                );

            var needsNew = string.IsNullOrWhiteSpace(current) || conflictsReturn
                || (current != null && usedCodes.Contains(current));

            if (needsNew)
            {
                var sku = !string.IsNullOrWhiteSpace(line.Product?.ProductCode)
                    ? line.Product!.ProductCode!
                    : line.ProductId.ToString("N")[..8].ToUpperInvariant();
                sku = sku.Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant();
                var ymd = receipt.ReceivedAtUtc.ToString("yyyyMMdd");
                var baseCode = $"BAT-EX-{sku}-{ymd}";
                var candidate = baseCode;
                var n = 1;
                while (usedCodes.Contains(candidate)
                       || (returnBatchByProduct.TryGetValue(line.ProductId, out var rb) && rb.Contains(candidate)))
                {
                    n++;
                    candidate = $"{baseCode}-{n}";
                }

                line.BatchCode = candidate;
                usedCodes.Add(candidate);
            }
            else if (current != null)
            {
                usedCodes.Add(current);
            }
        }
    }

    /// <summary>
    /// Khi nhập GRN từ PO đổi hàng: trừ tồn lô trả (giải phóng Reserved + giảm OnHand)
    /// và ghi nhận HandedOverQty. Nhập đủ → Status = COMPLETED (bàn giao hoàn tất).
    /// </summary>
    private async Task ApplyExchangeReceiptToSupplierReturnAsync(
        string tenantId,
        PurchaseOrder po,
        GoodsReceipt receipt,
        string replacementStatus,
        bool completeReturn,
        CancellationToken cancellationToken)
    {
        if (!po.SourceSupplierReturnId.HasValue || _supplierReturnRepository == null)
            return;

        var ret = await _supplierReturnRepository.GetByIdAsync(
            tenantId, po.SourceSupplierReturnId.Value, cancellationToken);
        if (ret == null)
            return;

        foreach (var grLine in receipt.Lines.Where(l => l.AcceptedQty > 0))
        {
            if (!grLine.PurchaseOrderDetailId.HasValue)
                continue;

            var pod = po.Details.FirstOrDefault(d => d.Id == grLine.PurchaseOrderDetailId.Value);
            if (pod?.SourceSupplierReturnLineId == null)
                continue;

            var retLine = ret.Lines.FirstOrDefault(l => l.Id == pod.SourceSupplierReturnLineId.Value);
            if (retLine == null)
                continue;

            var approved = retLine.SupplierAcceptedQty > 0 ? retLine.SupplierAcceptedQty : retLine.ApprovedQty;
            var remaining = Math.Max(0, approved - retLine.HandedOverQty);
            if (remaining <= 0)
                continue;

            // PO đổi / GRN exchange dùng ĐVT tồn (base) giống dòng phiếu trả.
            var deduct = Math.Min(grLine.AcceptedQty, remaining);
            if (deduct <= 0)
                continue;

            retLine.HandedOverQty += deduct;

            var stock = retLine.BranchStockId.HasValue
                ? await _repository.GetBranchStockByIdAsync(tenantId, ret.BranchId, retLine.ProductId, retLine.BranchStockId.Value, cancellationToken)
                : await _repository.GetBranchStockAsync(tenantId, ret.BranchId, retLine.ProductId, retLine.BatchCode, cancellationToken);
            if (stock == null || stock.QuantityOnHand < deduct)
                throw new BusinessException("RTV_EXCHANGE_STOCK_MISSING", "Không tìm thấy đủ tồn lô cũ để ghi giảm khi nhận hàng đổi.");
            var packageWeight = await _repository.GetActivePackageWeightAsync(tenantId, stock.Id, cancellationToken);
            if (stock.QuantityOnHand - deduct < packageWeight + stock.QuantityQuarantined + Math.Max(0m, stock.QuantityReserved - deduct))
                throw new BusinessException("RTV_EXCHANGE_COMMITTED_STOCK", "Nhận hàng đổi sẽ lấy vào lượng khay, cách ly hoặc giữ chỗ khác của lô cũ.");
            stock.QuantityReserved = Math.Max(0, stock.QuantityReserved - deduct);
            stock.QuantityOnHand -= deduct;
            stock.UpdatedAt = DateTime.UtcNow;
        }

        ret.TotalHandedOverQty = ret.Lines.Sum(l => l.HandedOverQty);
        ret.ReplacementStatus = replacementStatus;
        ret.HandedOverAt ??= DateTime.UtcNow;
        ret.HandedOverBy ??= "Hệ thống (nhập GRN đổi hàng)";

        if (completeReturn)
        {
            // Nhập đủ hàng đổi → bàn giao hoàn tất.
            ret.Status = "COMPLETED";
        }

        await _supplierReturnRepository.UpdateAsync(ret, cancellationToken);
    }
}
